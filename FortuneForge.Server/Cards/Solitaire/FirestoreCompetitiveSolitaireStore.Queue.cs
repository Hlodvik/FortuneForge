using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed partial class FirestoreCompetitiveSolitaireStore
{
    public async Task<SolitaireStoreSession> JoinAsync(
        string userId,
        string displayName,
        int playerCount,
        long buyInCents,
        int drawCount,
        string idempotencyKey,
        uint dealSeed,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var partitionKey = PartitionKey(playerCount, buyInCents, drawCount);
        var ticketId = CreateLookupKey($"{userId}\n{idempotencyKey}");
        var ticketReference = TicketDocument(ticketId);
        var partitionReference = PartitionDocument(partitionKey);
        var sessionReference = SessionDocument(userId);
        var actionReference = ActionDocument(userId, idempotencyKey);
        var balanceReference = BalanceDocument(userId);
        var buyInReference = BalanceTransactionDocument($"{ticketId}-buyin");
        var detail = $"{playerCount}:{buyInCents}:{drawCount}";

        // A player may start another game as soon as their own ten-minute run
        // ends, even while its match remains open for later competitors.
        var currentSession = await sessionReference.GetSnapshotAsync(cancellationToken);
        var currentMatchId = ReadString(currentSession, "matchId");
        if (ReadString(currentSession, "kind") == SolitaireSessionKinds.Match &&
            !string.IsNullOrEmpty(currentMatchId))
            await AdvanceMatchAsync(currentMatchId, nowUtc, cancellationToken);

        await RunTransactionAsync(async transaction =>
        {
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(actionReference, cancellationToken),
                transaction.GetSnapshotAsync(sessionReference, cancellationToken),
                transaction.GetSnapshotAsync(partitionReference, cancellationToken),
                transaction.GetSnapshotAsync(balanceReference, cancellationToken));
            if (snapshots[0].Exists)
            {
                VerifyAction(snapshots[0], "join", ticketId, detail);
                return false;
            }
            var session = snapshots[1];
            var sessionKind = ReadString(session, "kind");
            if (sessionKind == SolitaireSessionKinds.Queued)
                throw new SolitaireConflictException(
                    "Cancel the current Solitaire queue ticket before starting another match.");
            if (sessionKind is SolitaireSessionKinds.Match or SolitaireSessionKinds.Result)
            {
                var previousMatchId = ReadString(session, "matchId");
                if (string.IsNullOrEmpty(previousMatchId))
                    throw new SolitaireConflictException("The current Solitaire session is invalid.");
                var previousPlayer = await transaction.GetSnapshotAsync(
                    PlayerDocument(previousMatchId, userId), cancellationToken);
                if (!previousPlayer.Exists ||
                    ReadString(previousPlayer, "status") is not (
                        SolitairePlayerStatuses.Finished or
                        SolitairePlayerStatuses.Forfeited or
                        SolitairePlayerStatuses.IntegrityFailed))
                {
                    throw new SolitaireConflictException(
                        "Finish the current Solitaire run before starting another match.");
                }
            }
            else if (!string.IsNullOrEmpty(sessionKind) && sessionKind != SolitaireSessionKinds.Idle)
                throw new SolitaireConflictException("The current Solitaire session is invalid.");

            var availableCents = ReadBalanceCents(snapshots[3]);
            if (availableCents < buyInCents)
                throw new SolitaireInsufficientCreditsException(availableCents, buyInCents);

            // This partition document is the admission index and the transaction
            // contention point. Two joiners cannot both take the final seat, and
            // concurrent first joiners cannot each create a separate empty table.
            var indexedIds = ReadStringArray(snapshots[2], "openMatchIds")
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var matchSnapshots = await Task.WhenAll(indexedIds.Select(id =>
                transaction.GetSnapshotAsync(MatchDocument(id), cancellationToken)));
            var openMatches = matchSnapshots
                .Where(snapshot => snapshot.Exists &&
                    ReadString(snapshot, "status") == PlayingMatchStatus &&
                    ReadString(snapshot, "financialModel") == HouseFundedPoolModel &&
                    ReadString(snapshot, "partitionKey") == partitionKey)
                .Select(ReadMatch)
                .Where(match => match.AdmissionClosesAtUtc > nowUtc &&
                    match.PlayerIds.Count < match.PlayerCount)
                .ToArray();
            var selectedMatch = openMatches.FirstOrDefault(match =>
                !match.PlayerIds.Contains(userId, StringComparer.Ordinal));
            var matchId = selectedMatch?.MatchId ?? CreateLookupKey($"solitaire-match\n{partitionKey}\n{ticketId}");
            var seat = selectedMatch is null ? 1 : selectedMatch.PlayerIds.Count + 1;
            var seed = selectedMatch?.DealSeed ?? dealSeed;
            var existingPlayerSnapshots = selectedMatch is null ? [] : await Task.WhenAll(selectedMatch.PlayerIds.Select(id =>
                transaction.GetSnapshotAsync(PlayerDocument(matchId, id), cancellationToken)));
            var existingPlayers = selectedMatch is null ? [] : existingPlayerSnapshots.Select(snapshot => ReadPlayer(snapshot, selectedMatch)).ToArray();
            var ticket = new SolitaireTicket(
                ticketId, userId, displayName, playerCount, buyInCents,
                partitionKey, MatchedStatus, nowUtc, matchId) { DrawCount = drawCount };
            var ticketData = TicketData(ticket);
            ticketData["dealSeed"] = (long)seed;
            transaction.Create(ticketReference, ticketData);
            var game = SolitaireEngine.CreateGame(seed, drawCount);
            var joinedPlayer = new SolitairePlayerState(
                matchId, userId, displayName, seat, SolitairePlayerStatuses.Playing,
                game, 1, null, null, 0, false)
            {
                StartedAtUtc = nowUtc,
                DeadlineAtUtc = nowUtc.Add(SolitaireCompetitionRules.MatchDuration)
            };
            transaction.Create(PlayerDocument(matchId, userId), PlayerData(joinedPlayer));

            if (selectedMatch is null)
            {
                var payout = checked(buyInCents * 90 / 100);
                var match = new SolitaireMatch(
                    matchId, playerCount, buyInCents, buyInCents, payout,
                    checked(buyInCents - payout), seed,
                    nowUtc, nowUtc.Add(SolitaireCompetitionRules.MatchDuration),
                    PlayingMatchStatus, [userId], [displayName], [ticketId], [nowUtc], null, null)
                {
                    PartitionKey = partitionKey,
                    DrawCount = drawCount,
                    FinancialModel = HouseFundedPoolModel,
                    HumanEntryCents = buyInCents,
                    HouseFundedEntryCents = 0,
                    SeatsFilled = false,
                    SeatFillEligibleAtUtc = nowUtc.AddHours(1),
                    AdmissionClosesAtUtc = nowUtc.AddHours(playerCount)
                };
                var matchData = MatchData(match);
                matchData["nextSweepAt"] = Timestamp.FromDateTime(
                    nowUtc.Add(SolitaireCompetitionRules.MatchDuration));
                transaction.Create(MatchDocument(matchId), matchData);
                WriteSecurityState(transaction, match, [joinedPlayer], nowUtc, "human-entry");
                transaction.Create(HouseFundingDocument(matchId), new Dictionary<string, object>
                {
                    ["matchId"] = matchId,
                    ["currencyId"] = SlotsCreditsCurrencyId,
                    ["financialClassification"] = HouseFundedPoolModel,
                    ["humanEntryCents"] = buyInCents,
                    ["houseFundedEntryCents"] = 0L,
                    ["humanPlayerCount"] = 1L,
                    ["managedPlayerCount"] = 0L,
                    ["fundedAt"] = Timestamp.FromDateTime(nowUtc),
                    ["schemaVersion"] = 1L
                });
            }
            else
            {
                var pool = checked(selectedMatch.PrizePoolCents + buyInCents);
                var payout = checked(pool * 90 / 100);
                var previousSweepAt = ReadOptionalTimestamp(
                    matchSnapshots.First(snapshot => snapshot.Id == matchId), "nextSweepAt");
                var playerDeadline = nowUtc.Add(SolitaireCompetitionRules.MatchDuration);
                WriteSecurityState(transaction, selectedMatch with
                {
                    PlayerIds = selectedMatch.PlayerIds.Append(userId).ToArray(),
                    DisplayNames = selectedMatch.DisplayNames.Append(displayName).ToArray(),
                    TicketIds = selectedMatch.TicketIds.Append(ticketId).ToArray(),
                    JoinedAtUtc = selectedMatch.JoinedAtUtc.Append(nowUtc).ToArray(),
                    PrizePoolCents = pool, WinnerPayoutCents = payout, PlatformFeeCents = checked(pool - payout),
                    HumanEntryCents = checked(selectedMatch.HumanEntryCents + buyInCents), SeatsFilled = seat == playerCount
                }, existingPlayers.Append(joinedPlayer).ToArray(), nowUtc, "human-entry");
                transaction.Update(MatchDocument(matchId), new Dictionary<string, object>
                {
                    ["playerIds"] = selectedMatch.PlayerIds.Append(userId).ToArray(),
                    ["displayNames"] = selectedMatch.DisplayNames.Append(displayName).ToArray(),
                    ["ticketIds"] = selectedMatch.TicketIds.Append(ticketId).ToArray(),
                    ["joinedAt"] = selectedMatch.JoinedAtUtc.Append(nowUtc)
                        .Select(Timestamp.FromDateTime).ToArray(),
                    ["prizePoolCents"] = pool,
                    ["winnerPayoutCents"] = payout,
                    ["platformFeeCents"] = checked(pool - payout),
                    ["humanEntryCents"] = checked(selectedMatch.HumanEntryCents + buyInCents),
                    ["botsFilled"] = seat == playerCount,
                    ["nextSweepAt"] = Timestamp.FromDateTime(previousSweepAt is { } dueAt && dueAt < playerDeadline
                        ? dueAt : playerDeadline)
                });
                transaction.Set(HouseFundingDocument(matchId), new Dictionary<string, object>
                {
                    ["humanEntryCents"] = checked(selectedMatch.HumanEntryCents + buyInCents),
                    ["humanPlayerCount"] = checked(selectedMatch.HumanEntryCents / buyInCents + 1)
                }, SetOptions.MergeAll);
            }

            transaction.Set(balanceReference,
                BalanceUpdate(checked(availableCents - buyInCents), nowUtc), SetOptions.MergeAll);
            transaction.Create(buyInReference, BalanceTransactionData(
                buyInReference.Id, userId, -buyInCents, checked(availableCents - buyInCents),
                "solitaire-buyin", idempotencyKey, nowUtc));
            transaction.Create(actionReference, ActionData(userId, "join", ticketId, detail, nowUtc));
            transaction.Set(sessionReference,
                SessionData(userId, SolitaireSessionKinds.Match, null, matchId, nowUtc),
                SetOptions.MergeAll);
            var openIds = openMatches
                .Where(match => match.MatchId != matchId || seat < playerCount)
                .Select(match => match.MatchId)
                .ToList();
            if (selectedMatch is null) openIds.Add(matchId);
            transaction.Set(partitionReference, new Dictionary<string, object>
            {
                ["partitionKey"] = partitionKey,
                ["playerCount"] = playerCount,
                ["buyInCents"] = buyInCents,
                ["drawCount"] = drawCount,
                ["openMatchIds"] = openIds.ToArray(),
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
            }, SetOptions.MergeAll);
            return true;
        }, cancellationToken);

        return await GetSessionAsync(userId, nowUtc, cancellationToken);
    }
    public async Task<SolitaireStoreSession> CancelAsync(
        string userId,
        string ticketId,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var ticketReference = TicketDocument(ticketId);
        var sessionReference = SessionDocument(userId);
        var actionReference = ActionDocument(userId, idempotencyKey);
        var balanceReference = BalanceDocument(userId);
        var refundReference = BalanceTransactionDocument($"{ticketId}-refund");

        var cancelledPartitionKey = await database.RunTransactionAsync(
            async transaction =>
            {
                var initial = await Task.WhenAll(
                    transaction.GetSnapshotAsync(actionReference, cancellationToken),
                    transaction.GetSnapshotAsync(ticketReference, cancellationToken),
                    transaction.GetSnapshotAsync(sessionReference, cancellationToken),
                    transaction.GetSnapshotAsync(balanceReference, cancellationToken));
                if (initial[0].Exists)
                {
                    VerifyAction(initial[0], "cancel", ticketId, string.Empty);
                    return ReadString(initial[1], "partitionKey");
                }
                if (!initial[1].Exists)
                {
                    throw new SolitaireNotFoundException("The Solitaire queue ticket was not found.");
                }
                var ticket = ReadTicket(initial[1]);
                if (!string.Equals(ticket.UserId, userId, StringComparison.Ordinal))
                {
                    throw new SolitaireNotFoundException("The Solitaire queue ticket was not found.");
                }
                if (ticket.Status != QueueStatus ||
                    ReadString(initial[2], "kind") != SolitaireSessionKinds.Queued ||
                    ReadString(initial[2], "ticketId") != ticketId)
                {
                    throw new SolitaireConflictException(
                        "This ticket has already matched or been cancelled and cannot be refunded again.");
                }

                var partitionReference = PartitionDocument(ticket.PartitionKey);
                var partitionSnapshot = await transaction.GetSnapshotAsync(
                    partitionReference,
                    cancellationToken);
                var availableCents = ReadBalanceCents(initial[3]);
                var refundedBalance = checked(availableCents + ticket.BuyInCents);
                var remainingIds = ReadStringArray(partitionSnapshot, "ticketIds")
                    .Where(id => id != ticketId).ToArray();

                transaction.Update(ticketReference, new Dictionary<string, object>
                {
                    ["status"] = CancelledStatus,
                    ["cancelledAt"] = Timestamp.FromDateTime(nowUtc)
                });
                transaction.Set(partitionReference, new Dictionary<string, object>
                {
                    ["ticketIds"] = remainingIds,
                    ["hasQueuedPlayers"] = remainingIds.Length > 0 ||
                        ReadQueuedManagedSeats(partitionSnapshot).Count > 0,
                    ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
                }, SetOptions.MergeAll);
                transaction.Set(
                    sessionReference,
                    SessionData(userId, SolitaireSessionKinds.Idle, null, null, nowUtc),
                    SetOptions.MergeAll);
                transaction.Set(
                    balanceReference,
                    BalanceUpdate(refundedBalance, nowUtc),
                    SetOptions.MergeAll);
                transaction.Create(
                    refundReference,
                    BalanceTransactionData(
                        refundReference.Id,
                        userId,
                        ticket.BuyInCents,
                        refundedBalance,
                        "solitaire-refund",
                        idempotencyKey,
                        nowUtc));
                transaction.Create(
                    actionReference,
                    ActionData(userId, "cancel", ticketId, string.Empty, nowUtc));
                return ticket.PartitionKey;
            },
            cancellationToken: cancellationToken);

        if (!string.IsNullOrEmpty(cancelledPartitionKey))
        {
            try
            {
                await TryMatchPartitionAsync(cancelledPartitionKey, nowUtc, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The cancellation and refund are already committed. The queue
                // worker will retry this partition, including releasing any
                // staged managed profiles, without turning a success into an
                // error shown to the player.
                logger?.LogWarning(exception,
                    "Unable to refresh Solitaire queue after cancelling ticket {TicketId}.", ticketId);
            }
        }
        return await GetSessionAsync(userId, nowUtc, cancellationToken);
    }
}
