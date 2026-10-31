using System.Text.Json;
using Google.Cloud.Firestore;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Solitaire;

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
                if (!player.IsAccountBacked)
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
                        var elapsedMilliseconds = won
                            ? ActiveElapsedMilliseconds(graph.Match, player, nowUtc)
                            : (long?)null;
                        if (elapsedMilliseconds is long winElapsedMilliseconds)
                        {
                            nextGame = nextGame with
                            {
                                Score = SolitaireScoreRules.FinalScore(nextGame, winElapsedMilliseconds)
                            };
                        }
                        updated = player with
                        {
                            Game = nextGame,
                            UndoHistory = player.UndoHistory
                                .TakeLast(9)
                                .Append(player.Game)
                                .ToArray(),
                            Version = checked(player.Version + 1),
                            Status = won ? SolitairePlayerStatuses.Finished : SolitairePlayerStatuses.Playing,
                            ElapsedMilliseconds = elapsedMilliseconds,
                            CompletedAtUtc = won ? nowUtc : null
                        };
                        terminal = won;
                        break;
                }
                var players = graph.Players.ToArray();
                players[playerIndex] = updated;
                var match = graph.Match;

                WriteSecurityState(transaction, match, players, nowUtc, "human-command");

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
                if (CanSettle(match, players, nowUtc))
                {
                    ApplySettlement(
                        transaction,
                        match,
                        players,
                        graph.BalanceSnapshots,
                        graph.SessionSnapshots,
                        nowUtc);
                }
                else
                {
                    transaction.Update(
                        PlayerDocument(matchId, userId),
                        PlayerData(updated));
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
                if (!player.IsAccountBacked)
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
                WriteSecurityState(transaction, match, players, nowUtc, "human-forfeit");
                transaction.Create(
                    actionReference,
                    ActionData(userId, "forfeit", matchId, detail, nowUtc));
                transaction.Set(
                    CardGameResultDocument(matchId, userId),
                    UnclaimedResultData(match, updated, nowUtc),
                    SetOptions.MergeAll);
                if (CanSettle(match, players, nowUtc))
                {
                    ApplySettlement(
                        transaction,
                        match,
                        players,
                        graph.BalanceSnapshots,
                        graph.SessionSnapshots,
                        nowUtc);
                }
                else
                {
                    transaction.Update(
                        PlayerDocument(matchId, userId),
                        PlayerData(updated));
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
                    transaction.GetSnapshotAsync(playerReference, cancellationToken),
                    transaction.GetSnapshotAsync(sessionReference, cancellationToken));
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
                if (!player.IsAccountBacked)
                {
                    throw new SolitaireNotFoundException("The Solitaire result was not found.");
                }
                var completedPlayerMayLeave = match.Status == PlayingMatchStatus && IsTerminal(player);
                if ((match.Status != SettledMatchStatus && !completedPlayerMayLeave) ||
                    player.MatchId != matchId)
                {
                    throw new SolitaireConflictException("The Solitaire game is not ready to close.");
                }

                var otherSnapshots = await Task.WhenAll(match.PlayerIds.Where(id => id != userId).Select(id =>
                    transaction.GetSnapshotAsync(PlayerDocument(matchId, id), cancellationToken)));
                var acknowledgedPlayers = otherSnapshots.Select(snapshot => ReadPlayer(snapshot, match))
                    .Append(player with { Acknowledged = true }).ToArray();
                WriteSecurityState(transaction, match, acknowledgedPlayers, nowUtc, "result-dismissed");

                transaction.Update(playerReference, new Dictionary<string, object>
                {
                    ["acknowledged"] = true,
                    ["acknowledgedAt"] = Timestamp.FromDateTime(nowUtc)
                });
                if (ReadString(snapshots[3], "matchId") == matchId)
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

    private async Task AdvanceMatchAsync(
        string matchId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var leaseState = await RunTransactionAsync(async transaction =>
        {
            var graph = await ReadMatchGraphAsync(transaction, matchId, cancellationToken);
            var match = graph.Match;
            var managed = graph.Players.FirstOrDefault(player => !player.IsAccountBacked);
            var assignmentId = managed?.HostMetadata.GetValueOrDefault(ManagedAssignmentMetadataKey)
                ?? ManagedAssignmentId(matchId);
            if (match.Status != PlayingMatchStatus)
                return new ManagedAssignmentState(assignmentId,
                    graph.ManagedPlayersReleasePending
                        ? graph.Players.Where(player => !player.IsAccountBacked).ToArray()
                        : [],
                    graph.ManagedPlayersReleasePending
                        ? graph.Players.Where(player => !player.IsAccountBacked && IsTerminal(player)).ToArray()
                        : [], true);
            var players = graph.Players.Select(player =>
            {
                if (player.Status != SolitairePlayerStatuses.Playing) return player;
                if (!player.IsAccountBacked)
                    return SolitaireManagedPlayerProgression.Advance(match, player, nowUtc);
                var advanced = AdvancePause(match, player, nowUtc);
                return advanced.PausedAtUtc is null && nowUtc >= PlayerDeadline(match, advanced)
                    ? ExpirePlayer(advanced, PlayerDeadline(match, advanced))
                    : advanced;
            }).ToArray();
            foreach (var player in players.Where(player => player.IsAccountBacked && IsTerminal(player) &&
                graph.Players.First(original => original.UserId == player.UserId).Status == SolitairePlayerStatuses.Playing))
            {
                transaction.Set(CardGameResultDocument(matchId, player.UserId),
                    UnclaimedResultData(match, player, player.CompletedAtUtc ?? nowUtc), SetOptions.MergeAll);
            }
            if (CanSettle(match, players, nowUtc))
            {
                ApplySettlement(transaction, match, players, graph.BalanceSnapshots,
                    graph.SessionSnapshots, nowUtc);
                return new ManagedAssignmentState(assignmentId,
                    players.Where(player => !player.IsAccountBacked).ToArray(),
                    players.Where(player => !player.IsAccountBacked).ToArray(), true);
            }
            for (var index = 0; index < players.Length; index++)
            {
                if (players[index] == graph.Players[index]) continue;
                transaction.Set(PlayerDocument(matchId, players[index].UserId),
                    PlayerData(players[index]), SetOptions.Overwrite);
            }
            var newlyFinished = players.Where((player, index) =>
                !player.IsAccountBacked && IsTerminal(player) &&
                !IsTerminal(graph.Players[index])).ToArray();
            var nextSweepAt = NextSweepAt(match, players, nowUtc);
            var matchChanges = new Dictionary<string, object>();
            if (graph.NextSweepAtUtc != nextSweepAt)
                matchChanges["nextSweepAt"] = Timestamp.FromDateTime(nextSweepAt);
            if (newlyFinished.Length > 0 && !graph.ManagedPlayersReleasePending)
                matchChanges["managedPlayersReleasePending"] = true;
            if (matchChanges.Count > 0 || !players.SequenceEqual(graph.Players))
                WriteSecurityState(transaction, match, players, nowUtc, "managed-or-timeout-progression");
            if (matchChanges.Count > 0)
                transaction.Update(MatchDocument(matchId), matchChanges);
            return new ManagedAssignmentState(assignmentId,
                players.Where(player => !player.IsAccountBacked).ToArray(),
                graph.ManagedPlayersReleasePending || newlyFinished.Length > 0
                    ? players.Where(player => !player.IsAccountBacked && IsTerminal(player)).ToArray()
                    : [], false);
        }, cancellationToken);
        if (leaseState.ManagedPlayers.Count == 0) return;
        foreach (var player in leaseState.ReleasePlayers)
            await matchmaking.ReleaseManagedPlayersAsync(
                leaseState.AssignmentId, [player.UserId],
                player.CompletedAtUtc ?? nowUtc, CancellationToken.None);
        if (leaseState.ReleasePlayers.Count > 0)
        {
            await RunTransactionAsync(async transaction =>
            {
                var graph = await ReadMatchGraphAsync(transaction, matchId, CancellationToken.None);
                if (!graph.ManagedPlayersReleasePending) return false;
                var released = leaseState.ReleasePlayers.Select(player => player.UserId)
                    .ToHashSet(StringComparer.Ordinal);
                if (graph.Players.Any(player => !player.IsAccountBacked &&
                    IsTerminal(player) && !released.Contains(player.UserId))) return false;
                FortuneForge.Server.SecurityAudit.GameSecurityJournal.Write(transaction, database, "solitaire", matchId,
                    graph.Players.Sum(player => (long)player.Version), nowUtc,
                    new { graph.Match, graph.Players, managedPlayersReleasePending = false },
                    graph.Players.Where(player => player.IsAccountBacked).Select(player => player.UserId), "managed-assignment-released");
                transaction.Update(MatchDocument(matchId), new Dictionary<string, object>
                {
                    ["managedPlayersReleasePending"] = false
                });
                return true;
            }, CancellationToken.None);
        }
        if (!leaseState.Settled)
        {
            var activeIds = leaseState.ManagedPlayers.Where(player => !IsTerminal(player))
                .Select(player => player.UserId).ToArray();
            if (activeIds.Length > 0 &&
                !await matchmaking.TryRenewManagedPlayersAsync(
                    ManagedPlayerGames.Solitaire, leaseState.AssignmentId,
                    activeIds, nowUtc, cancellationToken))
            {
                var unowned = new List<string>();
                foreach (var id in activeIds)
                    if (!await matchmaking.TryRenewManagedPlayersAsync(
                            ManagedPlayerGames.Solitaire, leaseState.AssignmentId,
                            [id], nowUtc, cancellationToken))
                        unowned.Add(id);
                if (unowned.Count > 0)
                    await RetireUnownedManagedPlayersAsync(matchId, unowned, nowUtc, cancellationToken);
            }
        }
    }

    private sealed record ManagedAssignmentState(
        string AssignmentId,
        IReadOnlyList<SolitairePlayerState> ManagedPlayers,
        IReadOnlyList<SolitairePlayerState> ReleasePlayers,
        bool Settled);

    private Task<bool> RetireUnownedManagedPlayersAsync(
        string matchId,
        IReadOnlyCollection<string> unownedIds,
        DateTime nowUtc,
        CancellationToken cancellationToken) => RunTransactionAsync(async transaction =>
        {
            var graph = await ReadMatchGraphAsync(transaction, matchId, cancellationToken);
            if (graph.Match.Status != PlayingMatchStatus) return false;
            var unowned = unownedIds.ToHashSet(StringComparer.Ordinal);
            var changed = false;
            var players = graph.Players.Select(player =>
            {
                if (player.IsAccountBacked || !unowned.Contains(player.UserId) || IsTerminal(player))
                    return player;
                changed = true;
                return player with
                {
                    Status = SolitairePlayerStatuses.Finished,
                    Version = checked(player.Version + 1),
                    CompletedAtUtc = nowUtc,
                    ElapsedMilliseconds = Math.Clamp(
                        (long)(nowUtc - PlayerStartedAt(graph.Match, player)).TotalMilliseconds,
                        0, (long)SolitaireCompetitionRules.MatchDuration.TotalMilliseconds),
                    Game = player.Game with { Message = "Run complete" }
                };
            }).ToArray();
            if (!changed) return false;
            WriteSecurityState(transaction, graph.Match, players, nowUtc, "managed-assignment-lost");
            if (CanSettle(graph.Match, players, nowUtc))
                ApplySettlement(transaction, graph.Match, players, graph.BalanceSnapshots,
                    graph.SessionSnapshots, nowUtc);
            else
            {
                foreach (var player in players.Where(player => unowned.Contains(player.UserId)))
                    transaction.Set(PlayerDocument(matchId, player.UserId), PlayerData(player),
                        SetOptions.Overwrite);
                transaction.Update(MatchDocument(matchId), new Dictionary<string, object>
                {
                    ["nextSweepAt"] = Timestamp.FromDateTime(NextSweepAt(graph.Match, players, nowUtc)),
                    ["managedPlayersReleasePending"] = true
                });
            }
            return true;
        }, cancellationToken);
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
        var graph = await ReadMatchGraphAsync(transaction, ReadMatch(snapshot), cancellationToken);
        return graph with
        {
            ManagedPlayersReleasePending = ReadBool(snapshot, "managedPlayersReleasePending"),
            NextSweepAtUtc = ReadOptionalTimestamp(snapshot, "nextSweepAt")
        };
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
            .Where(player => player.IsAccountBacked)
            .Select(player => player.UserId)
            .ToArray();
        var balanceSnapshots = await Task.WhenAll(realPlayerIds.Select(userId =>
            transaction.GetSnapshotAsync(BalanceDocument(userId), cancellationToken)));
        var sessionSnapshots = await Task.WhenAll(realPlayerIds.Select(userId =>
            transaction.GetSnapshotAsync(SessionDocument(userId), cancellationToken)));
        return new MatchGraph(
            match,
            playerSnapshots.Select(snapshot => ReadPlayer(snapshot, match)).ToArray(),
            realPlayerIds.Zip(balanceSnapshots).ToDictionary(
                pair => pair.First,
                pair => pair.Second,
                StringComparer.Ordinal))
        {
            SessionSnapshots = realPlayerIds.Zip(sessionSnapshots).ToDictionary(
                pair => pair.First, pair => pair.Second, StringComparer.Ordinal)
        };
    }

    private void ApplySettlement(
        Transaction transaction,
        SolitaireMatch match,
        IReadOnlyList<SolitairePlayerState> players,
        IReadOnlyDictionary<string, DocumentSnapshot> balanceSnapshots,
        IReadOnlyDictionary<string, DocumentSnapshot> sessionSnapshots,
        DateTime completedAtUtc)
    {
        _ = balanceSnapshots;
        // If no opponent arrived during the entire admission window, return
        // the only entrant's stake rather than collecting a competition fee.
        if (match.AdmissionClosesAtUtc is not null && players.Count == 1)
            match = match with
            {
                WinnerPayoutCents = match.PrizePoolCents,
                PlatformFeeCents = 0
            };
        var standings = SolitaireCompetitionRules.Rank(players);
        var realPlayers = standings.Where(player => player.IsAccountBacked).ToArray();
        var winner = (match.FinancialModel == HouseFundedPoolModel
                ? standings.FirstOrDefault()
                : realPlayers.FirstOrDefault())
            ?? throw new InvalidOperationException("A Solitaire match cannot settle without a real player.");

        foreach (var player in players)
        {
            var payout = player.UserId == winner.UserId ? match.WinnerPayoutCents : 0;
            var settledPlayer = player with { PayoutCents = payout };
            transaction.Set(
                PlayerDocument(match.MatchId, player.UserId),
                PlayerData(settledPlayer),
                SetOptions.Overwrite);
            if (player.IsAccountBacked && !player.Acknowledged &&
                sessionSnapshots.TryGetValue(player.UserId, out var currentSession) &&
                ReadString(currentSession, "matchId") == match.MatchId)
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
            if (player.IsAccountBacked)
            {
                transaction.Set(
                    CardGameResultDocument(match.MatchId, player.UserId),
                    ClaimableResultData(match, settledPlayer, payout, completedAtUtc),
                    SetOptions.MergeAll);
                var analytics = GameAnalyticsOutbox.Create(new GameAnalyticsSettlement(
                    "solitaire", match.MatchId, player.UserId, "paid", true, completedAtUtc,
                    player.StartedAtUtc.Kind == DateTimeKind.Utc ? player.StartedAtUtc : null,
                    match.BuyInCents, payout, "cardGameResults", CardGameResultDocument(match.MatchId, player.UserId).Id))!;
                transaction.Set(database.Collection(GameAnalyticsOutbox.CollectionName).Document(analytics.Id), analytics.Fields);
            }
            else
            {
                transaction.Set(
                    CardGameResultDocument(match.MatchId, player.UserId),
                    ManagedResultData(match, settledPlayer, completedAtUtc),
                    SetOptions.MergeAll);
            }
        }

        var settledMatch = match with
        {
            Status = SettledMatchStatus,
            CompletedAtUtc = completedAtUtc,
            WinnerUserId = winner.UserId
        };
        var settledPlayers = players.Select(player => player with
        {
            PayoutCents = player.UserId == winner.UserId ? match.WinnerPayoutCents : 0
        }).ToArray();
        var settledData = MatchData(settledMatch);
        WriteSecurityState(transaction, settledMatch, settledPlayers, completedAtUtc, "settlement");
        FortuneForge.Server.RoundAudit.RoundAuditFirestore.Write(transaction, database,
            CompetitiveSolitaireRoundAudit.Capture(settledMatch, settledPlayers, completedAtUtc));
        settledData["nextSweepAt"] = FieldValue.Delete;
        settledData["managedPlayersReleasePending"] = players.Any(player => !player.IsAccountBacked);
        var isHouseFunded = match.FinancialModel == HouseFundedPoolModel;
        var humanPayout = isHouseFunded && winner.IsAccountBacked ? match.WinnerPayoutCents : 0L;
        var botPrize = isHouseFunded && !winner.IsAccountBacked ? match.WinnerPayoutCents : 0L;
        var houseNet = isHouseFunded ? checked(match.HumanEntryCents - humanPayout) : 0L;
        if (isHouseFunded)
        {
            settledData["humanPayoutCents"] = humanPayout;
            settledData["botPrizeCents"] = botPrize;
            settledData["houseNetCents"] = houseNet;
        }
        transaction.Update(MatchDocument(match.MatchId), settledData);
        if (isHouseFunded)
        {
            var managedCount = players.Count(player => !player.IsAccountBacked);
            var expectedHumanEntry = checked(realPlayers.Length * match.BuyInCents);
            var expectedHouseEntry = checked(managedCount * match.BuyInCents);
            if (match.HumanEntryCents != expectedHumanEntry ||
                match.HouseFundedEntryCents != expectedHouseEntry ||
                match.PrizePoolCents != checked(expectedHumanEntry + expectedHouseEntry) ||
                match.PrizePoolCents - match.WinnerPayoutCents != match.PlatformFeeCents)
            {
                throw new InvalidOperationException("The Solitaire house-funded prize pool does not reconcile.");
            }
            if (houseNet != checked(match.PlatformFeeCents + botPrize - match.HouseFundedEntryCents))
                throw new InvalidOperationException("The Solitaire house-funded result does not reconcile.");
            transaction.Create(RevenueDocument(match.MatchId), new Dictionary<string, object>
            {
                ["matchId"] = match.MatchId,
                ["currencyId"] = SlotsCreditsCurrencyId,
                ["financialClassification"] = HouseFundedPoolModel,
                ["humanEntryCents"] = match.HumanEntryCents,
                ["houseFundedEntryCents"] = match.HouseFundedEntryCents,
                ["grossPoolCents"] = match.PrizePoolCents,
                ["winnerPayoutCents"] = match.WinnerPayoutCents,
                ["humanPayoutCents"] = humanPayout,
                ["botPrizeCents"] = botPrize,
                ["platformFeeCents"] = match.PlatformFeeCents,
                ["houseNetCents"] = houseNet,
                ["botFinancialContributionCents"] = 0L,
                ["humanPlayerCount"] = (long)realPlayers.Length,
                ["managedPlayerCount"] = (long)managedCount,
                ["winnerUserId"] = winner.UserId,
                ["recognizedAt"] = Timestamp.FromDateTime(completedAtUtc),
                ["schemaVersion"] = 2L
            });
            return;
        }
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

    private static bool CanSettle(
        SolitaireMatch match,
        IReadOnlyList<SolitairePlayerState> players,
        DateTime nowUtc) =>
        players.All(IsTerminal) &&
        (match.AdmissionClosesAtUtc is null ||
         match.PlayerIds.Count >= match.PlayerCount ||
         nowUtc >= match.AdmissionClosesAtUtc.Value);

    private DateTime NextSweepAt(
        SolitaireMatch match,
        IReadOnlyList<SolitairePlayerState> players,
        DateTime nowUtc)
    {
        var due = new List<DateTime>();
        foreach (var player in players.Where(player => player.Status == SolitairePlayerStatuses.Playing))
        {
            var deadline = PlayerDeadline(match, player);
            if (!player.IsAccountBacked)
            {
                due.Add(Min(deadline, SolitaireManagedPlayerProgression.NextActionAt(player)));
            }
            else if (player.PausedAtUtc is { } pausedAt)
            {
                var remainingPause = Math.Max(0,
                    (long)SolitaireCompetitionRules.PauseBudget.TotalMilliseconds -
                    player.PauseUsedMilliseconds);
                due.Add(pausedAt.AddMilliseconds(remainingPause));
            }
            else
            {
                due.Add(deadline);
            }
        }
        if (match.AdmissionClosesAtUtc is { } closesAt &&
            match.PlayerIds.Count < match.PlayerCount && nowUtc < closesAt)
        {
            due.Add(closesAt);
            if (options.AllowSingleHumanBotFill)
                due.Add(match.SeatFillEligibleAtUtc ?? match.StartedAtUtc.AddHours(1));
        }
        return due.Count == 0 ? nowUtc : due.Min();
    }

    private static DateTime Min(DateTime first, DateTime second) => first < second ? first : second;

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

    private sealed record MatchGraph(
        SolitaireMatch Match,
        IReadOnlyList<SolitairePlayerState> Players,
        IReadOnlyDictionary<string, DocumentSnapshot> BalanceSnapshots)
    {
        public bool ManagedPlayersReleasePending { get; init; }
        public DateTime? NextSweepAtUtc { get; init; }
        public IReadOnlyDictionary<string, DocumentSnapshot> SessionSnapshots { get; init; } =
            new Dictionary<string, DocumentSnapshot>(StringComparer.Ordinal);
    }
}
