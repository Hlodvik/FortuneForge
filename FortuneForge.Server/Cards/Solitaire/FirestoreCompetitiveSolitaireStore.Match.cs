using System.Text.Json;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed partial class FirestoreCompetitiveSolitaireStore
{
    public async Task<SolitaireStoreSession> CommandAsync(
        string userId,
        string matchId,
        SolitaireCommandRequest command,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await AdvanceMatchAsync(matchId, nowUtc, cancellationToken);
        var actionReference = ActionDocument(userId, idempotencyKey);
        var detail = JsonSerializer.Serialize(command, JsonOptions);

        await database.RunTransactionAsync(
            async transaction =>
            {
                var actionSnapshot = await transaction.GetSnapshotAsync(
                    actionReference,
                    cancellationToken);
                if (actionSnapshot.Exists)
                {
                    VerifyAction(actionSnapshot, "command", matchId, detail);
                    return false;
                }
                var graph = await ReadMatchGraphAsync(transaction, matchId, cancellationToken);
                if (graph.Match.Status != PlayingMatchStatus)
                {
                    throw new SolitaireConflictException("This Solitaire match has already finished.");
                }
                var playerIndex = IndexOfPlayer(graph.Match, userId);
                var player = graph.Players[playerIndex];
                if (player.IsSynthetic)
                {
                    throw new SolitaireNotFoundException("The Solitaire match was not found.");
                }
                var commandType = command.Type?.Trim().ToLowerInvariant() ?? string.Empty;
                var isBoardCommand = commandType is SolitaireCommandTypes.Draw
                    or SolitaireCommandTypes.Flip
                    or SolitaireCommandTypes.Move;
                if (nowUtc >= PlayerDeadline(graph.Match, player) && player.PausedAtUtc is null)
                {
                    throw new SolitaireConflictException(
                        "The ten-minute Solitaire deadline has passed. Reload the final result.");
                }
                if (player.Status != SolitairePlayerStatuses.Playing)
                {
                    throw new SolitaireConflictException("This player has already finished the match.");
                }
                if (commandType != SolitaireCommandTypes.AcknowledgeWarning
                    && player.IntegrityWarnings.LastOrDefault()?.AcknowledgedAtUtc is null
                    && player.IntegrityWarnings.Count > 0)
                {
                    throw new SolitaireConflictException(
                        "Acknowledge the board warning before continuing this game.");
                }
                if (commandType != SolitaireCommandTypes.IntegrityFailure &&
                    player.Version != command.ExpectedVersion && !isBoardCommand)
                {
                    throw new SolitaireConflictException(
                        "The Solitaire board changed. Reload it before sending another move.");
                }

                SolitairePlayerState updated;
                var terminal = false;
                if (isBoardCommand && player.Version != command.ExpectedVersion)
                {
                    updated = AddIntegrityWarning(
                        player,
                        matchId,
                        idempotencyKey,
                        nowUtc,
                        "That action was based on an older verified board position.");
                }
                else switch (commandType)
                {
                    case SolitaireCommandTypes.AcknowledgeWarning:
                        var warningIndex = player.IntegrityWarnings
                            .Select((warning, index) => new { Warning = warning, Index = index })
                            .LastOrDefault(value => value.Warning.AcknowledgedAtUtc is null)?.Index;
                        if (warningIndex is null)
                        {
                            throw new SolitaireConflictException("There is no board warning to acknowledge.");
                        }
                        var acknowledgedWarnings = player.IntegrityWarnings.ToArray();
                        acknowledgedWarnings[warningIndex.Value] = acknowledgedWarnings[warningIndex.Value] with
                        {
                            AcknowledgedAtUtc = nowUtc
                        };
                        updated = player with
                        {
                            Version = checked(player.Version + 1),
                            IntegrityWarnings = acknowledgedWarnings,
                            Game = player.Game with { Message = "Warning acknowledged" }
                        };
                        break;
                    case SolitaireCommandTypes.Pause:
                        if (player.PausedAtUtc is not null)
                        {
                            throw new SolitaireConflictException("This Solitaire game is already paused.");
                        }
                        if (PauseRemainingMilliseconds(player, nowUtc) <= 0)
                        {
                            throw new SolitaireConflictException("The ten-minute pause budget has been used.");
                        }
                        updated = player with
                        {
                            Version = checked(player.Version + 1),
                            PausedAtUtc = nowUtc,
                            Game = player.Game with { Message = "Game paused" }
                        };
                        break;
                    case SolitaireCommandTypes.Resume:
                        if (player.PausedAtUtc is null)
                        {
                            throw new SolitaireConflictException("This Solitaire game is not paused.");
                        }
                        updated = ResumePause(graph.Match, player, nowUtc) with
                        {
                            Version = checked(player.Version + 1),
                            Game = player.Game with { Message = "Game resumed" }
                        };
                        break;
                    case SolitaireCommandTypes.Undo:
                        if (player.PausedAtUtc is not null)
                        {
                            throw new SolitaireConflictException(
                                "Resume the Solitaire game before undoing a move.");
                        }
                        if (player.UndoHistory.Count == 0)
                        {
                            throw new SolitaireIllegalMoveException("There is no move to undo.");
                        }
                        updated = player with
                        {
                            Game = player.UndoHistory[^1] with { Message = "Move undone" },
                            UndoHistory = player.UndoHistory.Take(player.UndoHistory.Count - 1).ToArray(),
                            Version = checked(player.Version + 1)
                        };
                        break;
                    case SolitaireCommandTypes.Submit:
                        updated = CompletePlayer(
                            graph.Match,
                            player,
                            nowUtc,
                            SolitairePlayerStatuses.Finished,
                            "Game submitted");
                        terminal = true;
                        break;
                    case SolitaireCommandTypes.IntegrityFailure:
                        updated = CompletePlayer(
                            graph.Match,
                            player with { Game = player.Game with { Score = 0 } },
                            nowUtc,
                            SolitairePlayerStatuses.IntegrityFailed,
                            "Run ended · local and server rules did not agree");
                        terminal = true;
                        break;
                    default:
                        if (player.PausedAtUtc is not null)
                        {
                            throw new SolitaireConflictException(
                                "Resume the Solitaire game before making a move.");
                        }
                        SolitaireGameState nextGame;
                        try
                        {
                            nextGame = SolitaireEngine.Apply(player.Game, command);
                        }
                        catch (SolitaireIllegalMoveException)
                        {
                            updated = AddIntegrityWarning(
                                player,
                                matchId,
                                idempotencyKey,
                                nowUtc,
                                "That action was not legal from the last verified board position.");
                            break;
                        }
                        var won = SolitaireEngine.IsWon(nextGame);
                        updated = player with
                        {
                            Game = nextGame,
                            UndoHistory = player.UndoHistory
                                .TakeLast(9)
                                .Append(player.Game)
                                .ToArray(),
                            Version = checked(player.Version + 1),
                            Status = won ? SolitairePlayerStatuses.Finished : SolitairePlayerStatuses.Playing,
                            ElapsedMilliseconds = won
                                ? ActiveElapsedMilliseconds(graph.Match, player, nowUtc)
                                : null,
                            CompletedAtUtc = won ? nowUtc : null
                        };
                        terminal = won;
                        break;
                }
                var players = graph.Players.ToArray();
                players[playerIndex] = updated;
                var match = graph.Match;
                if (terminal && options.AllowSingleHumanBotFill && match.BotFillEligibleAtUtc is null)
                {
                    match = match with
                    {
                        BotFillEligibleAtUtc = nowUtc.Add(SolitaireCompetitionRules.LateHumanClaimWindow)
                    };
                }

                transaction.Create(
                    actionReference,
                    ActionData(userId, "command", matchId, detail, nowUtc));
                if (terminal)
                {
                    transaction.Set(
                        CardGameResultDocument(matchId, userId),
                        UnclaimedResultData(match, updated, updated.CompletedAtUtc ?? nowUtc),
                        SetOptions.MergeAll);
                }
                if (CanSettle(match, players))
                {
                    ApplySettlement(
                        transaction,
                        match,
                        players,
                        graph.BalanceSnapshots,
                        nowUtc);
                }
                else
                {
                    transaction.Update(
                        PlayerDocument(matchId, userId),
                        PlayerData(updated));
                    if (match != graph.Match)
                    {
                        transaction.Update(MatchDocument(matchId), MatchData(match));
                    }
                }
                return true;
            },
            cancellationToken: cancellationToken);

        return await GetSessionAsync(userId, nowUtc, cancellationToken);
    }

    private static SolitairePlayerState AddIntegrityWarning(
        SolitairePlayerState player,
        string matchId,
        string idempotencyKey,
        DateTime nowUtc,
        string reason)
    {
        var warning = new SolitaireIntegrityWarning(
            $"warning-{CreateLookupKey($"{matchId}\n{player.UserId}\n{idempotencyKey}")[..24]}",
            reason,
            "This warning protects fair competitive play. The board was restored to the last position accepted by the game server.",
            nowUtc,
            null);
        return player with
        {
            Version = checked(player.Version + 1),
            Game = player.Game with { Message = "Move reversed" },
            IntegrityWarnings = player.IntegrityWarnings
                .TakeLast(9)
                .Append(warning)
                .ToArray()
        };
    }

    public async Task<SolitaireStoreSession> ForfeitAsync(
        string userId,
        string matchId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await AdvanceMatchAsync(matchId, nowUtc, cancellationToken);
        var actionReference = ActionDocument(userId, idempotencyKey);
        var detail = expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

        await database.RunTransactionAsync(
            async transaction =>
            {
                var actionSnapshot = await transaction.GetSnapshotAsync(
                    actionReference,
                    cancellationToken);
                if (actionSnapshot.Exists)
                {
                    VerifyAction(actionSnapshot, "forfeit", matchId, detail);
                    return false;
                }
                var graph = await ReadMatchGraphAsync(transaction, matchId, cancellationToken);
                if (graph.Match.Status != PlayingMatchStatus)
                {
                    throw new SolitaireConflictException("This Solitaire match has already finished.");
                }
                var playerIndex = IndexOfPlayer(graph.Match, userId);
                var player = graph.Players[playerIndex];
                if (player.IsSynthetic)
                {
                    throw new SolitaireNotFoundException("The Solitaire match was not found.");
                }
                if (player.Status != SolitairePlayerStatuses.Playing)
                {
                    throw new SolitaireConflictException("This player has already finished the match.");
                }
                if (player.Version != expectedVersion)
                {
                    throw new SolitaireConflictException(
                        "The Solitaire board changed. Reload it before forfeiting.");
                }

                var updated = player with
                {
                    Status = SolitairePlayerStatuses.Forfeited,
                    Game = player.Game with { Score = 0, Message = "Match forfeited" },
                    Version = checked(player.Version + 1),
                    ElapsedMilliseconds = (long)SolitaireCompetitionRules.MatchDuration.TotalMilliseconds,
                    CompletedAtUtc = nowUtc,
                    PausedAtUtc = null
                };
                var players = graph.Players.ToArray();
                players[playerIndex] = updated;
                var match = graph.Match;
                if (options.AllowSingleHumanBotFill && match.BotFillEligibleAtUtc is null)
                {
                    match = match with
                    {
                        BotFillEligibleAtUtc = nowUtc.Add(SolitaireCompetitionRules.LateHumanClaimWindow)
                    };
                }
                transaction.Create(
                    actionReference,
                    ActionData(userId, "forfeit", matchId, detail, nowUtc));
                transaction.Set(
                    CardGameResultDocument(matchId, userId),
                    UnclaimedResultData(match, updated, nowUtc),
                    SetOptions.MergeAll);
                if (CanSettle(match, players))
                {
                    ApplySettlement(
                        transaction,
                        match,
                        players,
                        graph.BalanceSnapshots,
                        nowUtc);
                }
                else
                {
                    transaction.Update(
                        PlayerDocument(matchId, userId),
                        PlayerData(updated));
                    if (match != graph.Match)
                    {
                        transaction.Update(MatchDocument(matchId), MatchData(match));
                    }
                }
                return true;
            },
            cancellationToken: cancellationToken);

        return await GetSessionAsync(userId, nowUtc, cancellationToken);
    }

    public async Task<SolitaireStoreSession> DismissAsync(
        string userId,
        string matchId,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await AdvanceMatchAsync(matchId, nowUtc, cancellationToken);
        var actionReference = ActionDocument(userId, idempotencyKey);
        var matchReference = MatchDocument(matchId);
        var playerReference = PlayerDocument(matchId, userId);
        var sessionReference = SessionDocument(userId);

        await database.RunTransactionAsync(
            async transaction =>
            {
                var snapshots = await Task.WhenAll(
                    transaction.GetSnapshotAsync(actionReference, cancellationToken),
                    transaction.GetSnapshotAsync(matchReference, cancellationToken),
                    transaction.GetSnapshotAsync(playerReference, cancellationToken));
                if (snapshots[0].Exists)
                {
                    VerifyAction(snapshots[0], "dismiss", matchId, string.Empty);
                    if (!snapshots[1].Exists || !snapshots[2].Exists) return false;
                    return false;
                }
                if (!snapshots[1].Exists || !snapshots[2].Exists)
                {
                    throw new SolitaireNotFoundException("The Solitaire result was not found.");
                }
                var match = ReadMatch(snapshots[1]);
                var player = ReadPlayer(snapshots[2], match);
                if (player.IsSynthetic)
                {
                    throw new SolitaireNotFoundException("The Solitaire result was not found.");
                }
                var completedPlayerMayLeave = match.Status == PlayingMatchStatus && IsTerminal(player);
                if ((match.Status != SettledMatchStatus && !completedPlayerMayLeave) ||
                    player.MatchId != matchId)
                {
                    throw new SolitaireConflictException("The Solitaire game is not ready to close.");
                }

                transaction.Update(playerReference, new Dictionary<string, object>
                {
                    ["acknowledged"] = true,
                    ["acknowledgedAt"] = Timestamp.FromDateTime(nowUtc)
                });
                transaction.Set(
                    sessionReference,
                    SessionData(userId, SolitaireSessionKinds.Idle, null, null, nowUtc),
                    SetOptions.MergeAll);
                transaction.Create(
                    actionReference,
                    ActionData(userId, "dismiss", matchId, string.Empty, nowUtc));
                return false;
            },
            cancellationToken: cancellationToken);

        return await GetSessionAsync(userId, nowUtc, cancellationToken);
    }

    private async Task AdvancePartitionMatchAsync(
        string partitionKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var partition = await PartitionDocument(partitionKey).GetSnapshotAsync(cancellationToken);
        var matchId = EmptyToNull(ReadString(partition, "activeMatchId"));
        if (matchId is not null)
        {
            await AdvanceMatchAsync(matchId, nowUtc, cancellationToken);
        }
    }

    private async Task AdvanceMatchAsync(
        string matchId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await RunTransactionAsync(
            async transaction =>
            {
                var graph = await ReadMatchGraphAsync(transaction, matchId, cancellationToken);
                var match = graph.Match;
                if (match.Status != PlayingMatchStatus)
                {
                    return false;
                }

                var players = graph.Players.Select(player =>
                {
                    if (player.IsSynthetic || player.Status != SolitairePlayerStatuses.Playing)
                    {
                        return player;
                    }
                    var advanced = AdvancePause(match, player, nowUtc);
                    return advanced.PausedAtUtc is null && nowUtc >= PlayerDeadline(match, advanced)
                        ? ExpirePlayer(advanced, PlayerDeadline(match, advanced))
                        : advanced;
                }).ToList();

                foreach (var player in players.Where(player =>
                    !player.IsSynthetic &&
                    IsTerminal(player) &&
                    graph.Players.First(original => original.UserId == player.UserId).Status ==
                        SolitairePlayerStatuses.Playing))
                {
                    transaction.Set(
                        CardGameResultDocument(match.MatchId, player.UserId),
                        UnclaimedResultData(match, player, player.CompletedAtUtc ?? nowUtc),
                        SetOptions.MergeAll);
                }

                if (!options.AllowSingleHumanBotFill)
                {
                    var productionPlayers = players.OrderBy(player => player.Seat).ToArray();
                    if (productionPlayers.All(IsTerminal))
                    {
                        ApplySettlement(
                            transaction,
                            match,
                            productionPlayers,
                            graph.BalanceSnapshots,
                            nowUtc);
                        return true;
                    }
                    if (productionPlayers.SequenceEqual(graph.Players)) return false;
                    foreach (var player in productionPlayers)
                    {
                        transaction.Set(
                            PlayerDocument(match.MatchId, player.UserId),
                            PlayerData(player),
                            SetOptions.Overwrite);
                    }
                    return true;
                }
                var firstCompletion = players
                    .Where(player => !player.IsSynthetic && IsTerminal(player))
                    .Select(player => player.CompletedAtUtc)
                    .Where(value => value is not null)
                    .Min();
                if (match.BotFillEligibleAtUtc is null && firstCompletion is { } completed)
                {
                    match = match with
                    {
                        BotFillEligibleAtUtc = completed.Add(
                            SolitaireCompetitionRules.LateHumanClaimWindow)
                    };
                }

                if (!match.BotsFilled &&
                    match.BotFillEligibleAtUtc is { } eligibleAt &&
                    nowUtc >= eligibleAt)
                {
                    var occupiedSeats = players.Select(player => player.Seat).ToHashSet();
                    for (var seat = 1; seat <= match.PlayerCount; seat++)
                    {
                        if (occupiedSeats.Contains(seat)) continue;
                        var skill = CompetitiveSolitaireBotSimulation.Skill(seat);
                        var elapsed = CompetitiveSolitaireBotSimulation.ElapsedMilliseconds(
                            match.DealSeed,
                            seat,
                            skill);
                        var syntheticId = SyntheticPlayerId(match.MatchId, seat);
                        players.Add(new SolitairePlayerState(
                            match.MatchId,
                            syntheticId,
                            CompetitiveSolitaireBotSimulation.DisplayName(seat),
                            seat,
                            SolitairePlayerStatuses.Finished,
                            CompetitiveSolitaireBotSimulation.Play(
                                match.DealSeed,
                                match.DrawCount,
                                seat,
                                skill),
                            1,
                            elapsed,
                            eligibleAt,
                            0,
                            false)
                        {
                            StartedAtUtc = match.StartedAtUtc,
                            DeadlineAtUtc = eligibleAt,
                            IsSynthetic = true,
                            SyntheticSkill = skill
                        });
                    }
                    var ordered = players.OrderBy(player => player.Seat).ToArray();
                    match = match with
                    {
                        PlayerIds = ordered.Select(player => player.UserId).ToArray(),
                        DisplayNames = ordered.Select(player => player.DisplayName).ToArray(),
                        TicketIds = ordered.Select(player => player.IsSynthetic ? string.Empty :
                            match.TicketIds[PlayerIndex(match.PlayerIds, player.UserId)]).ToArray(),
                        JoinedAtUtc = ordered.Select(player => player.IsSynthetic ? eligibleAt :
                            match.JoinedAtUtc[PlayerIndex(match.PlayerIds, player.UserId)]).ToArray(),
                        BotsFilled = true
                    };
                    ClearActivePartition(transaction, graph.PartitionSnapshot, match, nowUtc);
                }

                var playerArray = players.OrderBy(player => player.Seat).ToArray();
                if (CanSettle(match, playerArray))
                {
                    ApplySettlement(
                        transaction,
                        match,
                        playerArray,
                        graph.BalanceSnapshots,
                        nowUtc);
                    return true;
                }

                if (match == graph.Match && playerArray.SequenceEqual(graph.Players))
                {
                    return false;
                }

                foreach (var player in playerArray)
                {
                    transaction.Set(
                        PlayerDocument(match.MatchId, player.UserId),
                        PlayerData(player),
                        SetOptions.Overwrite);
                }
                transaction.Update(MatchDocument(match.MatchId), MatchData(match));
                return true;
            },
            cancellationToken: cancellationToken);
    }

    private async Task<MatchGraph> ReadMatchGraphAsync(
        Transaction transaction,
        string matchId,
        CancellationToken cancellationToken)
    {
        var snapshot = await transaction.GetSnapshotAsync(
            MatchDocument(matchId),
            cancellationToken);
        if (!snapshot.Exists)
        {
            throw new SolitaireNotFoundException("The Solitaire match was not found.");
        }
        return await ReadMatchGraphAsync(transaction, ReadMatch(snapshot), cancellationToken);
    }

    private async Task<MatchGraph> ReadMatchGraphAsync(
        Transaction transaction,
        SolitaireMatch match,
        CancellationToken cancellationToken)
    {
        var playerSnapshots = await Task.WhenAll(match.PlayerIds.Select(userId =>
            transaction.GetSnapshotAsync(PlayerDocument(match.MatchId, userId), cancellationToken)));
        if (playerSnapshots.Any(snapshot => !snapshot.Exists))
        {
            throw new InvalidOperationException("A Solitaire match is missing a player state.");
        }
        var realPlayerIds = playerSnapshots
            .Select(snapshot => ReadPlayer(snapshot, match))
            .Where(player => !player.IsSynthetic)
            .Select(player => player.UserId)
            .ToArray();
        var balanceSnapshots = await Task.WhenAll(realPlayerIds.Select(userId =>
            transaction.GetSnapshotAsync(BalanceDocument(userId), cancellationToken)));
        var partitionSnapshot = string.IsNullOrEmpty(match.PartitionKey)
            ? null
            : await transaction.GetSnapshotAsync(
                PartitionDocument(match.PartitionKey),
                cancellationToken);
        return new MatchGraph(
            match,
            playerSnapshots.Select(snapshot => ReadPlayer(snapshot, match)).ToArray(),
            realPlayerIds.Zip(balanceSnapshots).ToDictionary(
                pair => pair.First,
                pair => pair.Second,
                StringComparer.Ordinal),
            partitionSnapshot);
    }

    private void ApplySettlement(
        Transaction transaction,
        SolitaireMatch match,
        IReadOnlyList<SolitairePlayerState> players,
        IReadOnlyDictionary<string, DocumentSnapshot> balanceSnapshots,
        DateTime completedAtUtc)
    {
        _ = balanceSnapshots;
        var standings = SolitaireCompetitionRules.Rank(players);
        var realPlayers = standings.Where(player => !player.IsSynthetic).ToArray();
        var winner = realPlayers.FirstOrDefault()
            ?? throw new InvalidOperationException("A Solitaire match cannot settle without a real player.");

        foreach (var player in players)
        {
            var payout = player.UserId == winner.UserId ? match.WinnerPayoutCents : 0;
            transaction.Set(
                PlayerDocument(match.MatchId, player.UserId),
                PlayerData(player with { PayoutCents = payout }),
                SetOptions.Overwrite);
            if (!player.IsSynthetic && !player.Acknowledged)
            {
                transaction.Set(
                    SessionDocument(player.UserId),
                    SessionData(
                        player.UserId,
                        SolitaireSessionKinds.Result,
                        null,
                        match.MatchId,
                        completedAtUtc),
                    SetOptions.MergeAll);
            }
            if (!player.IsSynthetic)
            {
                transaction.Set(
                    CardGameResultDocument(match.MatchId, player.UserId),
                    ClaimableResultData(match, player, payout, completedAtUtc),
                    SetOptions.MergeAll);
            }
        }

        transaction.Update(MatchDocument(match.MatchId), MatchData(match with
        {
            Status = SettledMatchStatus,
            CompletedAtUtc = completedAtUtc,
            WinnerUserId = winner.UserId
        }));
        var singleHuman = realPlayers.Length == 1;
        if (singleHuman)
        {
            transaction.Create(TestTraceDocument(match.MatchId), new Dictionary<string, object>
            {
                ["matchId"] = match.MatchId,
                ["classification"] = "single-human-bot-fill-test",
                ["completedAt"] = Timestamp.FromDateTime(completedAtUtc),
                ["schemaVersion"] = 1L
            });
        }
        else
        {
            if (match.PrizePoolCents - match.WinnerPayoutCents != match.PlatformFeeCents)
            {
                throw new InvalidOperationException(
                    "The Solitaire human pool, payout, and platform fee do not reconcile.");
            }
            transaction.Create(RevenueDocument(match.MatchId), new Dictionary<string, object>
            {
                ["matchId"] = match.MatchId,
                ["currencyId"] = SlotsCreditsCurrencyId,
                ["financialClassification"] = "real-human-pool-v1",
                ["grossPoolCents"] = match.PrizePoolCents,
                ["winnerPayoutCents"] = match.WinnerPayoutCents,
                ["platformFeeCents"] = match.PlatformFeeCents,
                ["botFinancialContributionCents"] = 0L,
                ["humanPlayerCount"] = (long)realPlayers.Length,
                ["winnerUserId"] = winner.UserId,
                ["recognizedAt"] = Timestamp.FromDateTime(completedAtUtc),
                ["schemaVersion"] = 1L
            });
        }
    }

    private static int IndexOfPlayer(SolitaireMatch match, string userId)
    {
        var index = PlayerIndex(match.PlayerIds, userId);
        if (index < 0)
        {
            throw new SolitaireNotFoundException("The Solitaire match was not found.");
        }
        return index;
    }

    private static int PlayerIndex(IReadOnlyList<string> playerIds, string userId)
    {
        for (var index = 0; index < playerIds.Count; index++)
        {
            if (string.Equals(playerIds[index], userId, StringComparison.Ordinal))
            {
                return index;
            }
        }
        return -1;
    }

    private static bool IsTerminal(SolitairePlayerState player) =>
        player.Status is SolitairePlayerStatuses.Finished or
            SolitairePlayerStatuses.Forfeited or
            SolitairePlayerStatuses.IntegrityFailed;

    private bool CanSettle(
        SolitaireMatch match,
        IReadOnlyList<SolitairePlayerState> players) =>
        (!options.AllowSingleHumanBotFill || match.BotsFilled) &&
        players.Where(player => !player.IsSynthetic).All(IsTerminal);

    private static DateTime PlayerDeadline(SolitaireMatch match, SolitairePlayerState player) =>
        player.DeadlineAtUtc == DateTime.UnixEpoch ? match.DeadlineAtUtc : player.DeadlineAtUtc;

    private static long ActiveElapsedMilliseconds(
        SolitaireMatch match,
        SolitairePlayerState player,
        DateTime nowUtc)
    {
        var currentPause = player.PausedAtUtc is { } pausedAt
            ? Math.Max(0, checked((long)(nowUtc - pausedAt).TotalMilliseconds))
            : 0;
        return Math.Clamp(
            checked((long)(nowUtc - PlayerStartedAt(match, player)).TotalMilliseconds) -
                player.PauseUsedMilliseconds -
                currentPause,
            0,
            (long)SolitaireCompetitionRules.MatchDuration.TotalMilliseconds);
    }

    private static long PauseRemainingMilliseconds(SolitairePlayerState player, DateTime nowUtc)
    {
        var currentPause = player.PausedAtUtc is { } pausedAt
            ? Math.Max(0, checked((long)(nowUtc - pausedAt).TotalMilliseconds))
            : 0;
        return Math.Max(
            0,
            (long)SolitaireCompetitionRules.PauseBudget.TotalMilliseconds -
                player.PauseUsedMilliseconds -
                currentPause);
    }

    private static SolitairePlayerState ResumePause(
        SolitaireMatch match,
        SolitairePlayerState player,
        DateTime nowUtc)
    {
        if (player.PausedAtUtc is not { } pausedAt) return player;
        var available = Math.Max(
            0,
            (long)SolitaireCompetitionRules.PauseBudget.TotalMilliseconds -
                player.PauseUsedMilliseconds);
        var duration = Math.Clamp(
            checked((long)(nowUtc - pausedAt).TotalMilliseconds),
            0,
            available);
        return player with
        {
            PauseUsedMilliseconds = checked(player.PauseUsedMilliseconds + duration),
            PausedAtUtc = null,
            StartedAtUtc = PlayerStartedAt(match, player),
            DeadlineAtUtc = PlayerDeadline(match, player).AddMilliseconds(duration)
        };
    }

    private static SolitairePlayerState AdvancePause(
        SolitaireMatch match,
        SolitairePlayerState player,
        DateTime nowUtc)
    {
        if (player.PausedAtUtc is null || PauseRemainingMilliseconds(player, nowUtc) > 0)
        {
            return player;
        }
        var resumed = ResumePause(match, player, nowUtc);
        return resumed with
        {
            Version = checked(player.Version + 1),
            Game = player.Game with { Message = "Pause budget used · game resumed" }
        };
    }

    private static DateTime PlayerStartedAt(SolitaireMatch match, SolitairePlayerState player) =>
        player.StartedAtUtc == DateTime.UnixEpoch ? match.StartedAtUtc : player.StartedAtUtc;

    private static SolitairePlayerState CompletePlayer(
        SolitaireMatch match,
        SolitairePlayerState player,
        DateTime nowUtc,
        string status,
        string message)
    {
        var elapsed = ActiveElapsedMilliseconds(match, player, nowUtc);
        var resumed = player.PausedAtUtc is null ? player : ResumePause(match, player, nowUtc);
        return resumed with
        {
            StartedAtUtc = PlayerStartedAt(match, resumed),
            DeadlineAtUtc = PlayerDeadline(match, resumed),
            Status = status,
            Game = resumed.Game with { Message = message },
            Version = checked(player.Version + 1),
            ElapsedMilliseconds = elapsed,
            CompletedAtUtc = nowUtc,
            PausedAtUtc = null
        };
    }

    private static long PlayRemainingMilliseconds(
        SolitaireMatch match,
        SolitairePlayerState player,
        DateTime nowUtc) =>
        Math.Clamp(
            checked((long)(PlayerDeadline(match, player) -
                (player.PausedAtUtc ?? nowUtc)).TotalMilliseconds),
            0,
            (long)SolitaireCompetitionRules.MatchDuration.TotalMilliseconds);

    private static SolitairePlayerState ExpirePlayer(
        SolitairePlayerState player,
        DateTime deadlineAtUtc) => player with
        {
            Status = SolitairePlayerStatuses.Finished,
            Version = checked(player.Version + 1),
            ElapsedMilliseconds = (long)SolitaireCompetitionRules.MatchDuration.TotalMilliseconds,
            CompletedAtUtc = deadlineAtUtc,
            Game = player.Game with { Message = "Time expired" }
        };

    private static string SyntheticPlayerId(string matchId, int seat) =>
        $"__solitaire_internal__:{matchId}:{seat}";

    private void ClearActivePartition(
        Transaction transaction,
        DocumentSnapshot? partitionSnapshot,
        SolitaireMatch match,
        DateTime nowUtc)
    {
        if (partitionSnapshot is null ||
            ReadString(partitionSnapshot, "activeMatchId") != match.MatchId)
        {
            return;
        }
        transaction.Set(PartitionDocument(match.PartitionKey), new Dictionary<string, object>
        {
            ["activeMatchId"] = string.Empty,
            ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
        }, SetOptions.MergeAll);
    }

    private sealed record MatchGraph(
        SolitaireMatch Match,
        IReadOnlyList<SolitairePlayerState> Players,
        IReadOnlyDictionary<string, DocumentSnapshot> BalanceSnapshots,
        DocumentSnapshot? PartitionSnapshot);
}
