using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;
using static FortuneForge.Server.Cards.HouseHoldem.HouseHoldemRoundCodec;

namespace FortuneForge.Server.Cards.HouseHoldem;

internal sealed record HouseHoldemFinancialSnapshot(RoundAuditLedgerEntry[] Entries, RoundAuditWalletOperation[] Operations);

/// <summary>Retains exact accepted wallet evidence in the protected completion, independently of mutable round/ledger retention.</summary>
internal static class HouseHoldemFinancialProof
{
    internal const string Contract = "house-holdem-protected-wallet-evidence-v1";

    internal static async Task<HouseHoldemFinancialSnapshot> ReadPriorAsync(Transaction transaction, FirestoreDb database,
        HouseHoldemStoredRound? before, string account, string roundId, CancellationToken token)
    {
        var entries = new List<RoundAuditLedgerEntry>();
        var operations = new List<RoundAuditWalletOperation>();
        var saved = await transaction.GetSnapshotAsync(database.Collection(HouseHoldemBackend.CommandsCollection)
            .WhereEqualTo("roundId", roundId).Limit(2_000), token);
        if (saved.Count == 2_000) throw new GameWalletUnavailableException();
        foreach (var snapshot in saved.Documents)
        {
            var command = HouseHoldemPreparedStore.ReadCommand(snapshot);
            if (command.Prepared.AccountId != account || command.Prepared.RoundId != roundId)
                throw new GameWalletUnavailableException();
            if (command.Completion is not { LegacyAlreadyApplied: false, Wallet: { InsufficientFunds: false } receipt }) continue;
            HouseHoldemPreparedStore.ValidateWallet(command.Prepared, receipt);
            if (command.Prepared.WagerCents == 0 && command.Prepared.PayoutCents == 0) continue;
            entries.AddRange(Entries(command.Prepared, receipt));
            operations.Add(Operation(command.Prepared, receipt));
        }
        var prefix = $"{HouseHoldemBackend.GameId}-{roundId}-";
        var legacy = await transaction.GetSnapshotAsync(database.Collection("balanceTransactions").OrderBy(FieldPath.DocumentId)
            .StartAt(prefix).EndAt(prefix + "\uf8ff").Limit(2_000), token);
        if (legacy.Count == 2_000) throw new GameWalletUnavailableException();
        foreach (var snapshot in legacy.Documents)
        {
            var entry = FirestoreWalletEvidence.Legacy(snapshot, roundId);
            if (entry.UserId != account || !entry.Kind.StartsWith(HouseHoldemBackend.GameId + "-", StringComparison.Ordinal))
                throw new GameWalletUnavailableException();
            entries.Add(entry);
        }
        var recordedWager = -entries.Where(value => value.AmountCents < 0).Sum(value => value.AmountCents);
        if (before is not null && recordedWager < before.Round.TotalWagerCents)
            entries.Add(new(HouseHoldemBackend.RoundsCollection, roundId, account, "legacy-prepaid-stake", roundId,
                checked(recordedWager - before.Round.TotalWagerCents), RoundAuditFinancialProof.LegacyHostUnavailable, EvidenceAvailable: false));
        return new(entries.ToArray(), operations.ToArray());
    }

    internal static RoundAuditCapture Protect(RoundAuditCapture capture, HouseHoldemPreparedCommand command,
        GameWalletSettlementReceipt receipt, HouseHoldemFinancialSnapshot prior) =>
        RoundAuditFinancialProof.Attach(capture, HouseHoldemBackend.Owner, Contract,
            prior.Entries.Concat(Entries(command, receipt)).ToArray(), prior.Operations.Append(Operation(command, receipt)).ToArray());

    private static RoundAuditLedgerEntry[] Entries(HouseHoldemPreparedCommand command, GameWalletSettlementReceipt receipt)
    {
        var entries = new List<RoundAuditLedgerEntry>();
        if (command.WagerCents > 0)
            entries.Add(new("balanceTransactions", receipt.WagerTransactionId!, command.AccountId,
                command.Command == "start" ? "house-holdem-wager" : "house-holdem-play-wager", command.RoundId,
                -command.WagerCents, RoundAuditFinancialProof.SignedWallet, command.Id, receipt.SettlementId));
        if (command.PayoutCents > 0)
            entries.Add(new("balanceTransactions", receipt.PayoutTransactionId!, command.AccountId, "house-holdem-payout", command.RoundId,
                command.PayoutCents, RoundAuditFinancialProof.SignedWallet, command.Id, receipt.SettlementId));
        return entries.ToArray();
    }

    private static RoundAuditWalletOperation Operation(HouseHoldemPreparedCommand command, GameWalletSettlementReceipt receipt) =>
        new(HouseHoldemBackend.CommandsCollection, command.Id, command.Digest, Entries(command, receipt).Select(value => value.DocumentId).ToArray(),
            new Dictionary<string, GameWalletSettlementReceipt>(StringComparer.Ordinal) { [command.AccountId] = receipt with { WasAlreadySettled = false } });
}
