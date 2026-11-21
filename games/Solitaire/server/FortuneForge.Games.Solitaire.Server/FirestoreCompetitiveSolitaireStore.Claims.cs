using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
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
        if (await ReplayFinancialAsync(userId, idempotencyKey, "claim", matchId, string.Empty, cancellationToken) is { } replay)
            return replay;
        await RecoverFinancialOperationAsync(cancellationToken);
        await AdvanceMatchAsync(matchId, nowUtc, cancellationToken);
        var actionReference = ActionDocument(userId, idempotencyKey);
        var matchReference = MatchDocument(matchId);
        var playerReference = PlayerDocument(matchId, userId);
        var resultReference = CardGameResultDocument(matchId, userId);
        var sessionReference = SessionDocument(userId);
        var auditReference = database.Collection(RoundAuditDocuments.Summaries)
            .Document(CreateLookupKey($"solitaire\n{matchId}"));

        return await RunFinancialAsync(userId, idempotencyKey, "claim", matchId, string.Empty, nowUtc,
            async (transaction, writes, availableBalance) =>
            {
                var snapshots = await Task.WhenAll(
                    transaction.GetSnapshotAsync(actionReference, cancellationToken),
                    transaction.GetSnapshotAsync(matchReference, cancellationToken),
                    transaction.GetSnapshotAsync(playerReference, cancellationToken),
                    transaction.GetSnapshotAsync(resultReference, cancellationToken),
                    transaction.GetSnapshotAsync(sessionReference, cancellationToken),
                    transaction.GetSnapshotAsync(auditReference, cancellationToken),
                    transaction.GetSnapshotAsync(database.Collection(RoundAuditDocuments.Evidence).Document(auditReference.Id), cancellationToken));
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
                    !player.IsAccountBacked ||
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

                var otherPlayerSnapshots = await Task.WhenAll(match.PlayerIds.Where(id => id != userId).Select(id =>
                    transaction.GetSnapshotAsync(PlayerDocument(matchId, id), cancellationToken)));
                // Terminal claim evidence keeps every final board; prior accepted-move observations
                // already own the undo journal, so do not duplicate it into the wallet plan.
                var claimedPlayers = otherPlayerSnapshots.Select(snapshot => ReadPlayer(snapshot, match))
                    .Append(player with { Acknowledged = true }).Select(saved => saved with { UndoHistory = [] }).ToArray();
                var completionJson = ReadString(snapshots[6], "payloadJson");
                var completionSha = ReadString(snapshots[6], "sha256");
                if (!snapshots[6].Exists && ReadString(snapshots[3], "backendOwner") == BackendOwner)
                    throw new SolitaireConflictException("The original Solitaire completion evidence is unavailable.");
                if (snapshots[6].Exists && (GameSecurityEventDocuments.Hash(completionJson) != completionSha ||
                    ReadString(snapshots[5], "evidenceSha256") != completionSha))
                    throw new SolitaireConflictException("The original Solitaire completion evidence is unavailable.");
                if (snapshots[6].Exists)
                {
                    using var completion = JsonDocument.Parse(completionJson);
                    var originalMatch = completion.RootElement.GetProperty("match");
                    var originalPlayers = completion.RootElement.GetProperty("players").EnumerateArray()
                        .Where(original => original.GetProperty("userId").GetString() == userId &&
                            original.GetProperty("isAccountBacked").GetBoolean()).ToArray();
                    if (originalMatch.GetProperty("matchId").GetString() != matchId || originalPlayers.Length != 1 ||
                        originalPlayers[0].GetProperty("payoutCents").GetInt64() != player.PayoutCents ||
                        ReadLong(snapshots[3], "payoutCents") != player.PayoutCents)
                        throw new SolitaireConflictException("The Solitaire claim differs from its original completed entitlement.");
                }
                // Older claimable results may predate protected completion captures; retain their entitlement
                // and explicitly record the unavailable historical anchor in the new immutable claim event.
                writes.AnchorClaim(auditReference.Id, snapshots[6].Exists ? completionSha : null);
                writes.Journal(match, claimedPlayers, nowUtc, "payout-claimed");
                if (player.PayoutCents > 0) writes.Credit(player.PayoutCents);

                writes.Update(resultReference, new Dictionary<string, object>
                {
                    ["claimStatus"] = SolitaireClaimStatuses.Completed,
                    ["claimedAt"] = Timestamp.FromDateTime(nowUtc),
                    ["claimIdempotencyKey"] = idempotencyKey
                });
                writes.Update(playerReference, new Dictionary<string, object>
                {
                    ["acknowledged"] = true,
                    ["acknowledgedAt"] = Timestamp.FromDateTime(nowUtc)
                });
                if (ReadString(snapshots[4], "matchId") == matchId)
                    writes.Set(
                        sessionReference,
                        SessionData(userId, SolitaireSessionKinds.Idle, null, null, nowUtc),
                        SetOptions.MergeAll);
                writes.Create(
                    actionReference,
                    ActionData(userId, "claim", matchId, string.Empty, nowUtc));
                if (snapshots[5].Exists) writes.InvalidateAudit(auditReference, nowUtc);
                return true;
            },
            token: cancellationToken);

    }
}
