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
            var sessionKind = ReadString(snapshots[1], "kind");
            if (!string.IsNullOrEmpty(sessionKind) && sessionKind != SolitaireSessionKinds.Idle)
                throw new SolitaireConflictException(
                    "Finish or cancel the current Solitaire session before joining another queue.");
            var availableCents = ReadBalanceCents(snapshots[3]);
            if (availableCents < buyInCents)
                throw new SolitaireInsufficientCreditsException(availableCents, buyInCents);
            var ticket = new SolitaireTicket(
                ticketId, userId, displayName, playerCount, buyInCents,
                partitionKey, QueueStatus, nowUtc, null) { DrawCount = drawCount };
            var ticketData = TicketData(ticket);
            ticketData["dealSeed"] = (long)dealSeed;
            transaction.Create(ticketReference, ticketData);
            transaction.Set(balanceReference,
                BalanceUpdate(checked(availableCents - buyInCents), nowUtc), SetOptions.MergeAll);
            transaction.Create(buyInReference, BalanceTransactionData(
                buyInReference.Id, userId, -buyInCents, checked(availableCents - buyInCents),
                "solitaire-buyin", idempotencyKey, nowUtc));
            transaction.Create(actionReference, ActionData(userId, "join", ticketId, detail, nowUtc));
            transaction.Set(sessionReference,
                SessionData(userId, SolitaireSessionKinds.Queued, ticketId, null, nowUtc),
                SetOptions.MergeAll);
            transaction.Set(partitionReference, new Dictionary<string, object>
            {
                ["partitionKey"] = partitionKey,
                ["playerCount"] = playerCount,
                ["buyInCents"] = buyInCents,
                ["drawCount"] = drawCount,
                ["ticketIds"] = ReadStringArray(snapshots[2], "ticketIds").Append(ticketId).ToArray(),
                ["hasQueuedPlayers"] = true,
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
                    ["hasQueuedPlayers"] = remainingIds.Length > 0,
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
