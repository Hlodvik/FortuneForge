using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed partial class FirestoreCompetitiveSolitaireStore
{
    public async Task<SolitaireStoreSession> ClaimAsync(
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
        var resultReference = CardGameResultDocument(matchId, userId);
        var balanceReference = BalanceDocument(userId);

        await database.RunTransactionAsync(
            async transaction =>
            {
                var snapshots = await Task.WhenAll(
                    transaction.GetSnapshotAsync(actionReference, cancellationToken),
                    transaction.GetSnapshotAsync(matchReference, cancellationToken),
                    transaction.GetSnapshotAsync(playerReference, cancellationToken),
                    transaction.GetSnapshotAsync(resultReference, cancellationToken),
                    transaction.GetSnapshotAsync(balanceReference, cancellationToken));
                if (snapshots[0].Exists)
                {
                    VerifyAction(snapshots[0], "claim", matchId, string.Empty);
                    return false;
                }
                if (!snapshots[1].Exists || !snapshots[2].Exists || !snapshots[3].Exists)
                {
                    throw new SolitaireNotFoundException("The Solitaire result was not found.");
                }

                var match = ReadMatch(snapshots[1]);
                var player = ReadPlayer(snapshots[2], match);
                if (match.Status != SettledMatchStatus ||
                    player.IsSynthetic ||
                    player.MatchId != matchId ||
                    ReadString(snapshots[3], "settlementStatus") != "claimable")
                {
                    throw new SolitaireConflictException(
                        "This Solitaire result is not ready to claim.");
                }
                if (ReadString(snapshots[3], "claimStatus") != SolitaireClaimStatuses.Unclaimed)
                {
                    throw new SolitaireConflictException(
                        "This Solitaire result has already been claimed.");
                }

                var finalBalance = ReadBalanceCents(snapshots[4]);
                if (player.PayoutCents > 0)
                {
                    finalBalance = checked(finalBalance + player.PayoutCents);
                    transaction.Set(
                        balanceReference,
                        BalanceUpdate(finalBalance, nowUtc),
                        SetOptions.MergeAll);
                    var payoutReference = BalanceTransactionDocument(
                        $"{matchId}-claim-{CreateLookupKey(userId)}");
                    transaction.Create(
                        payoutReference,
                        BalanceTransactionData(
                            payoutReference.Id,
                            userId,
                            player.PayoutCents,
                            finalBalance,
                            match.PrizePoolCents == match.BuyInCents
                                ? "solitaire-test-refund-claim"
                                : "solitaire-winner-payout-claim",
                            idempotencyKey,
                            nowUtc));
                }

                transaction.Update(resultReference, new Dictionary<string, object>
                {
                    ["claimStatus"] = SolitaireClaimStatuses.Completed,
                    ["claimedAt"] = Timestamp.FromDateTime(nowUtc),
                    ["claimIdempotencyKey"] = idempotencyKey
                });
                transaction.Update(playerReference, new Dictionary<string, object>
                {
                    ["acknowledged"] = true,
                    ["acknowledgedAt"] = Timestamp.FromDateTime(nowUtc)
                });
                transaction.Set(
                    SessionDocument(userId),
                    SessionData(userId, SolitaireSessionKinds.Idle, null, null, nowUtc),
                    SetOptions.MergeAll);
                transaction.Create(
                    actionReference,
                    ActionData(userId, "claim", matchId, string.Empty, nowUtc));
                return true;
            },
            cancellationToken: cancellationToken);

        return await GetSessionAsync(userId, nowUtc, cancellationToken);
    }
}
