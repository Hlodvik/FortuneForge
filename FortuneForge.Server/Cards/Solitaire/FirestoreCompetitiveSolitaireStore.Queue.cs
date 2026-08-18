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
        if (options.AllowSingleHumanBotFill)
        {
            return await JoinTestingMatchAsync(
                userId,
                displayName,
                playerCount,
                buyInCents,
                drawCount,
                idempotencyKey,
                dealSeed,
                nowUtc,
                cancellationToken);
        }

        var partitionKey = PartitionKey(playerCount, buyInCents, drawCount);
        var ticketId = CreateLookupKey($"{userId}\n{idempotencyKey}");
        var ticketReference = TicketDocument(ticketId);
        var partitionReference = PartitionDocument(partitionKey);
        var sessionReference = SessionDocument(userId);
        var actionReference = ActionDocument(userId, idempotencyKey);
        var balanceReference = BalanceDocument(userId);
        var buyInReference = BalanceTransactionDocument($"{ticketId}-buyin");
        var detail = $"{playerCount}:{buyInCents}:{drawCount}";

        await database.RunTransactionAsync(
            async transaction =>
            {
                var initial = await Task.WhenAll(
                    transaction.GetSnapshotAsync(actionReference, cancellationToken),
                    transaction.GetSnapshotAsync(sessionReference, cancellationToken),
                    transaction.GetSnapshotAsync(partitionReference, cancellationToken),
                    transaction.GetSnapshotAsync(balanceReference, cancellationToken));
                var actionSnapshot = initial[0];
                if (actionSnapshot.Exists)
                {
                    VerifyAction(actionSnapshot, "join", ticketId, detail);
                    return false;
                }

                var sessionKind = ReadString(initial[1], "kind");
                if (!string.IsNullOrEmpty(sessionKind) && sessionKind != SolitaireSessionKinds.Idle)
                {
                    throw new SolitaireConflictException(
                        "Finish or cancel the current Solitaire session before joining another queue.");
                }

                var partitionIds = ReadStringArray(initial[2], "ticketIds");
                var partitionTicketSnapshots = await Task.WhenAll(partitionIds
                    .Select(ticket => transaction.GetSnapshotAsync(
                        TicketDocument(ticket),
                        cancellationToken)));
                var queuedTickets = partitionTicketSnapshots
                    .Where(snapshot => snapshot.Exists)
                    .Select(ReadTicket)
                    .Where(ticket => ticket.Status == QueueStatus)
                    .ToList();

                var availableCents = ReadBalanceCents(initial[3]);
                if (availableCents < buyInCents)
                {
                    throw new SolitaireInsufficientCreditsException(availableCents, buyInCents);
                }

                var newTicket = new SolitaireTicket(
                    ticketId,
                    userId,
                    displayName,
                    playerCount,
                    buyInCents,
                    partitionKey,
                    QueueStatus,
                    nowUtc,
                    null)
                {
                    DrawCount = drawCount
                };
                queuedTickets.Add(newTicket);
                var createsMatch = queuedTickets.Count >= playerCount;
                SolitaireMatch? match = null;
                IReadOnlyList<SolitaireTicket> selected = [];
                if (createsMatch)
                {
                    selected = queuedTickets.Take(playerCount).ToArray();
                    var matchId = CreateLookupKey(
                        $"{partitionKey}\n{string.Join("\n", selected.Select(ticket => ticket.TicketId))}");
                    var poolCents = checked(playerCount * buyInCents);
                    var payoutCents = SolitaireMoney.WinnerPayout(playerCount, buyInCents);
                    match = new SolitaireMatch(
                        matchId,
                        playerCount,
                        buyInCents,
                        poolCents,
                        payoutCents,
                        checked(poolCents - payoutCents),
                        dealSeed,
                        nowUtc,
                        nowUtc.Add(SolitaireCompetitionRules.MatchDuration),
                        PlayingMatchStatus,
                        selected.Select(ticket => ticket.UserId).ToArray(),
                        selected.Select(ticket => ticket.DisplayName).ToArray(),
                        selected.Select(ticket => ticket.TicketId).ToArray(),
                        selected.Select(ticket => ticket.JoinedAtUtc).ToArray(),
                        null,
                        null)
                    {
                        PartitionKey = partitionKey,
                        DrawCount = drawCount
                    };
                }

                var currentFinalTicket = match is null || !selected.Any(ticket => ticket.TicketId == ticketId)
                    ? newTicket
                    : newTicket with { Status = MatchedStatus, MatchId = match.MatchId };
                transaction.Create(ticketReference, TicketData(currentFinalTicket));
                transaction.Set(
                    balanceReference,
                    BalanceUpdate(checked(availableCents - buyInCents), nowUtc),
                    SetOptions.MergeAll);
                transaction.Create(
                    buyInReference,
                    BalanceTransactionData(
                        buyInReference.Id,
                        userId,
                        -buyInCents,
                        checked(availableCents - buyInCents),
                        "solitaire-buyin",
                        idempotencyKey,
                        nowUtc));
                transaction.Create(
                    actionReference,
                    ActionData(userId, "join", ticketId, detail, nowUtc));

                if (match is null)
                {
                    transaction.Set(
                        sessionReference,
                        SessionData(
                            userId,
                            SolitaireSessionKinds.Queued,
                            ticketId,
                            null,
                            nowUtc),
                        SetOptions.MergeAll);
                    transaction.Set(partitionReference, new Dictionary<string, object>
                    {
                        ["partitionKey"] = partitionKey,
                        ["playerCount"] = playerCount,
                        ["buyInCents"] = buyInCents,
                        ["drawCount"] = drawCount,
                        ["ticketIds"] = queuedTickets.Select(ticket => ticket.TicketId).ToArray(),
                        ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
                    }, SetOptions.MergeAll);
                    return true;
                }

                var selectedIds = selected.Select(ticket => ticket.TicketId).ToHashSet(StringComparer.Ordinal);
                foreach (var selectedTicket in selected.Where(ticket => ticket.TicketId != ticketId))
                {
                    transaction.Update(TicketDocument(selectedTicket.TicketId), new Dictionary<string, object>
                    {
                        ["status"] = MatchedStatus,
                        ["matchId"] = match.MatchId
                    });
                }
                transaction.Create(MatchDocument(match.MatchId), MatchData(match));
                var initialGame = SolitaireEngine.CreateGame(match.DealSeed, match.DrawCount);
                for (var seat = 0; seat < selected.Count; seat++)
                {
                    var playerTicket = selected[seat];
                    transaction.Create(
                        PlayerDocument(match.MatchId, playerTicket.UserId),
                        PlayerData(new SolitairePlayerState(
                            match.MatchId,
                            playerTicket.UserId,
                            playerTicket.DisplayName,
                            seat + 1,
                            SolitairePlayerStatuses.Playing,
                            initialGame,
                            1,
                            null,
                            null,
                            0,
                            false)
                        {
                            StartedAtUtc = nowUtc,
                            DeadlineAtUtc = match.DeadlineAtUtc
                        }));
                    transaction.Set(
                        SessionDocument(playerTicket.UserId),
                        SessionData(
                            playerTicket.UserId,
                            SolitaireSessionKinds.Match,
                            null,
                            match.MatchId,
                            nowUtc),
                        SetOptions.MergeAll);
                }
                transaction.Set(partitionReference, new Dictionary<string, object>
                {
                    ["partitionKey"] = partitionKey,
                    ["playerCount"] = playerCount,
                    ["buyInCents"] = buyInCents,
                    ["drawCount"] = drawCount,
                    ["ticketIds"] = queuedTickets
                        .Where(ticket => !selectedIds.Contains(ticket.TicketId))
                        .Select(ticket => ticket.TicketId)
                        .ToArray(),
                    ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
                }, SetOptions.MergeAll);
                return true;
            },
            cancellationToken: cancellationToken);

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

        await database.RunTransactionAsync(
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
                    return false;
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

                transaction.Update(ticketReference, new Dictionary<string, object>
                {
                    ["status"] = CancelledStatus,
                    ["cancelledAt"] = Timestamp.FromDateTime(nowUtc)
                });
                transaction.Set(partitionReference, new Dictionary<string, object>
                {
                    ["ticketIds"] = ReadStringArray(partitionSnapshot, "ticketIds")
                        .Where(id => id != ticketId)
                        .ToArray(),
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
                return true;
            },
            cancellationToken: cancellationToken);

        return await GetSessionAsync(userId, nowUtc, cancellationToken);
    }
}
