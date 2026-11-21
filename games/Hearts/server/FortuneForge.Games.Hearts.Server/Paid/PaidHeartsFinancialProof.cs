using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Games.Hearts;

internal sealed record PaidHeartsFundingEvidence(string Path, string Fields);
internal sealed record PaidHeartsFinancialSnapshot(RoundAuditLedgerEntry[] Entries, RoundAuditWalletOperation[] Operations,
    PaidHeartsFundingEvidence[] Funding)
{
    internal static PaidHeartsFinancialSnapshot Empty => new([], [], []);
}

internal static class PaidHeartsFinancialProof
{
    private const int MaximumEntries = 1_000;
    internal static async Task<PaidHeartsFinancialSnapshot> ReadPriorAsync(FirestoreDb database, PaidHeartsMutation mutation, CancellationToken ct)
    {
        if (mutation.Settlements.Count == 0) return PaidHeartsFinancialSnapshot.Empty;
        var entries = new List<RoundAuditLedgerEntry>();
        var operations = new Dictionary<string, RoundAuditWalletOperation>(StringComparer.Ordinal);
        var funding = new List<PaidHeartsFundingEvidence>();
        foreach (var settlement in mutation.Settlements)
        {
            var match = JsonSerializer.Deserialize<PaidHeartsMatch>(settlement.MatchJson, PaidHeartsMutation.Json)!;
            var accounts = match.Players.Where(value => value.IsAccountBacked).Select(value => value.UserId).ToHashSet(StringComparer.Ordinal);
            var relevant = match.Players.Where(value => value.IsAccountBacked).SelectMany(value => new[]
            {
                $"{match.Id}-entry-{value.EntryId}", $"{match.Id}-payout-{PaidHeartsIds.Hash(value.UserId)}",
                $"{match.Id}-queue-refund-{value.EntryId}"
            }).ToHashSet(StringComparer.Ordinal);
            var prefix = match.Id + "-";
            var legacy = await mutation.GetSnapshotAsync(database.Collection("balanceTransactions").OrderBy(FieldPath.DocumentId)
                .StartAt(prefix).EndAt(prefix + "\uf8ff").Limit(MaximumEntries), ct);
            var modern = await mutation.GetSnapshotAsync(database.Collection(FirestorePaidHeartsStore.WalletJournalCollection).OrderBy(FieldPath.DocumentId)
                .StartAt(prefix).EndAt(prefix + "\uf8ff").Limit(MaximumEntries), ct);
            if (legacy.Count == MaximumEntries || modern.Count == MaximumEntries) throw new GameWalletUnavailableException();
            foreach (var snapshot in legacy.Documents)
            {
                if (!relevant.Contains(snapshot.Id)) continue;
                var entry = FirestoreWalletEvidence.Legacy(snapshot, match.Id);
                if (!accounts.Contains(entry.UserId) || !entry.Kind.StartsWith("hearts-", StringComparison.Ordinal)) throw new GameWalletUnavailableException();
                entries.Add(entry);
            }
            foreach (var snapshot in modern.Documents)
            {
                if (!relevant.Contains(snapshot.Id)) continue;
                var account = snapshot.GetValue<string>("accountId");
                if (!accounts.Contains(account)) throw new GameWalletUnavailableException();
                var source = await mutation.GetSnapshotAsync(database.Collection(FirestorePaidHeartsStore.PreparedCollection)
                    .Document(snapshot.GetValue<string>("mutationId")), ct);
                if (!source.Exists || source.GetValue<string>("status") != "applied") throw new GameWalletUnavailableException();
                var prior = FirestorePaidHeartsStore.Decode(source);
                var receipt = JsonSerializer.Deserialize<GameWalletBatchReceipt>(source.GetValue<string>("walletJson"), PaidHeartsMutation.Json)
                    ?? throw new GameWalletUnavailableException();
                var delta = prior.Money.SingleOrDefault(value => value.JournalId == snapshot.Id);
                if (delta is null || delta.AccountId != account || delta.Cents != snapshot.GetValue<long>("deltaCents") ||
                    delta.Kind != snapshot.GetValue<string>("kind") || receipt.BatchDeclined || receipt.BatchId != snapshot.GetValue<string>("walletBatchId") ||
                    !GameWalletBatchVerifier.MatchesSettlement(receipt, HeartsBackend.GameId, prior.ActorId, prior.Request())) throw new GameWalletUnavailableException();
                operations.TryAdd(prior.Id, Operation(prior, receipt));
                entries.Add(Entry(delta, match.Id, prior.Id, receipt.BatchId));
            }
            foreach (var player in match.Players.Where(value => value.IsAccountBacked))
            {
                var id = $"{match.Id}-entry-{player.EntryId}";
                if (!entries.Any(value => value.DocumentId == id && value.UserId == player.UserId && value.AmountCents == -match.EntryCents) &&
                    !mutation.Deltas.Any(value => value.JournalId == id && value.AccountId == player.UserId && value.Cents == -match.EntryCents))
                    entries.Add(new("heartsPaidMatches", match.Id, player.UserId, "legacy-hearts-entry-unavailable", match.Id,
                        -match.EntryCents, RoundAuditFinancialProof.LegacyHostUnavailable, EvidenceAvailable: false));
            }
            var fundingDocument = await mutation.GetSnapshotAsync(database.Collection("heartsPaidFunding").Document(match.Id), ct);
            if (fundingDocument.Exists) funding.Add(new(PaidHeartsMutation.Relative(fundingDocument.Reference), FirestoreFieldCodec.Serialize(fundingDocument.ToDictionary())));
            var transfers = await mutation.GetSnapshotAsync(database.Collection("heartsPaidFundingTransfers").OrderBy(FieldPath.DocumentId)
                .StartAt(prefix).EndAt(prefix + "\uf8ff").Limit(MaximumEntries), ct);
            if (transfers.Count == MaximumEntries) throw new GameWalletUnavailableException();
            funding.AddRange(transfers.Documents.Select(value => new PaidHeartsFundingEvidence(PaidHeartsMutation.Relative(value.Reference), FirestoreFieldCodec.Serialize(value.ToDictionary()))));
        }
        return new(entries.ToArray(), operations.Values.ToArray(), funding.ToArray());
    }

    internal static RoundAuditCapture Protect(RoundAuditCapture capture, PaidHeartsPlan plan, GameWalletBatchReceipt? receipt, string matchId)
    {
        var payload = JsonNode.Parse(capture.PayloadJson)!.AsObject();
        payload.Add("fundingEvidence", JsonSerializer.SerializeToNode(plan.Evidence.Funding, RoundAuditCapture.Json));
        var enriched = payload.ToJsonString(RoundAuditCapture.Json);
        capture = capture with { PayloadJson = enriched, Summary = capture.Summary with { EvidenceSha256 = GameSecurityEventDocuments.Hash(enriched) } };
        var entries = plan.Evidence.Entries.Where(value => value.RoundReference == matchId).Concat(plan.Money
            .Where(value => value.JournalId.StartsWith(matchId + "-", StringComparison.Ordinal))
            .Select(value => Entry(value, matchId, plan.Id, receipt?.BatchId ?? throw new GameWalletUnavailableException()))).ToArray();
        var operations = receipt is null ? plan.Evidence.Operations : plan.Evidence.Operations.Append(Operation(plan, receipt)).ToArray();
        return RoundAuditFinancialProof.Attach(capture, HeartsBackend.Owner, HeartsBackend.FinancialProofContract, entries, operations);
    }
    private static RoundAuditLedgerEntry Entry(PaidHeartsMoney value, string roundId, string operationId, string batchId) =>
        new(FirestorePaidHeartsStore.WalletJournalCollection, value.JournalId, value.AccountId, value.Kind, roundId, value.Cents,
            RoundAuditFinancialProof.SignedWallet, operationId, batchId);
    private static RoundAuditWalletOperation Operation(PaidHeartsPlan plan, GameWalletBatchReceipt receipt) =>
        new(FirestorePaidHeartsStore.PreparedCollection, plan.Id, plan.Request().OutcomeSha256, plan.Money.Select(value => value.JournalId).ToArray(),
            new Dictionary<string, GameWalletSettlementReceipt>(), plan.Request(), receipt with { WasAlreadySettled = false });
}
