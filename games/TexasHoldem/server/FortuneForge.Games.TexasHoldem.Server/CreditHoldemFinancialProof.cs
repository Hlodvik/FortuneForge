using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

internal sealed record CreditHoldemFinancialSnapshot(RoundAuditLedgerEntry[] Entries, RoundAuditWalletOperation[] Operations)
{
    internal static CreditHoldemFinancialSnapshot Empty => new([], []);
}

/// <summary>Game-selected historical and signed funding evidence, frozen before the next wallet effect.</summary>
internal static class CreditHoldemFinancialProof
{
    internal const string Contract = "texas-holdem-protected-wallet-evidence-v1";
    private const int MaximumEntries = 2_000;

    internal static async Task<CreditHoldemFinancialSnapshot> ReadPriorAsync(FirestoreDb database,
        Func<DocumentReference, Task<DocumentSnapshot>> readDocument, Func<Query, Task<QuerySnapshot>> readQuery,
        IReadOnlyList<CreditHoldemAuditIntent> audits, IReadOnlyList<CreditHoldemWalletDelta> current)
    {
        var entries = new List<RoundAuditLedgerEntry>();
        var operations = new Dictionary<string, RoundAuditWalletOperation>(StringComparer.Ordinal);
        foreach (var audit in audits)
        {
            var match = CreditHoldemMatchPersistence.Deserialize(audit.MatchJson);
            var roundId = RoundId(match);
            var humans = match.Players.Where(value => value.IsAccountBacked).ToArray();
            var accounts = humans.Select(value => value.ActorId).ToHashSet(StringComparer.Ordinal);
            if (match.UsesBuyInEscrow)
            {
                foreach (var player in humans)
                {
                    if (player.EscrowTicketId is not { Length: > 0 } ticketId)
                    {
                        entries.Add(Unavailable(CreditHoldemBackend.Matches, match.MatchId, player.ActorId,
                            "legacy-escrow-funding-unavailable", roundId, 0));
                        continue;
                    }
                    var ticketDocument = await readDocument(database.Collection(CreditHoldemBackend.Tickets).Document(ticketId));
                    var ticket = ticketDocument.Exists
                        ? JsonSerializer.Deserialize<CreditHoldemTicket>(ticketDocument.GetValue<string>("ticketJson"), CreditHoldemMutation.Json) : null;
                    if (ticket is not null && (ticket.UserId != player.ActorId || ticket.BuyInCents <= 0))
                        throw new GameWalletUnavailableException();
                    foreach (var suffix in new[] { "buy-in", "cash-out", "buy-in-refund" })
                    {
                        var id = $"{ticketId}-{suffix}";
                        if (current.Any(value => value.JournalId == id)) continue;
                        var modern = await readDocument(database.Collection(CreditHoldemBackend.WalletJournal).Document(id));
                        var legacy = await readDocument(database.Collection("balanceTransactions").Document(id));
                        if (modern.Exists && legacy.Exists) throw new GameWalletUnavailableException();
                        if (modern.Exists) await AddSignedAsync(database, modern, roundId, accounts, readDocument, entries, operations);
                        else if (legacy.Exists) AddLegacy(legacy, roundId, accounts, entries);
                    }
                    var fundingId = ticketId + "-buy-in";
                    if (!entries.Any(value => value.RoundReference == roundId && value.DocumentId == fundingId) &&
                        !current.Any(value => value.JournalId == fundingId))
                        entries.Add(Unavailable(CreditHoldemBackend.Tickets, ticketId, player.ActorId,
                            "legacy-escrow-funding-unavailable", roundId, -(long)(ticket?.BuyInCents ?? 0)));
                    if (match.EscrowCashedOutTicketIds.Contains(ticketId) &&
                        !entries.Any(value => value.RoundReference == roundId && value.DocumentId == ticketId + "-cash-out") &&
                        !current.Any(value => value.JournalId == ticketId + "-cash-out"))
                        entries.Add(Unavailable(CreditHoldemBackend.Matches, match.MatchId, player.ActorId,
                            "legacy-escrow-cashout-unavailable", roundId, player.Stack));
                }
            }
            else
            {
                var prefix = roundId + "-";
                var modern = await readQuery(database.Collection(CreditHoldemBackend.WalletJournal).OrderBy(FieldPath.DocumentId)
                    .StartAt(prefix).EndAt(prefix + "\uf8ff").Limit(MaximumEntries));
                var legacy = await readQuery(database.Collection("balanceTransactions").OrderBy(FieldPath.DocumentId)
                    .StartAt(prefix).EndAt(prefix + "\uf8ff").Limit(MaximumEntries));
                if (modern.Count == MaximumEntries || legacy.Count == MaximumEntries) throw new GameWalletUnavailableException();
                foreach (var snapshot in modern.Documents) await AddSignedAsync(database, snapshot, roundId, accounts, readDocument, entries, operations);
                foreach (var snapshot in legacy.Documents) AddLegacy(snapshot, roundId, accounts, entries);
                foreach (var player in humans)
                {
                    var recorded = -entries.Where(value => value.RoundReference == roundId && value.UserId == player.ActorId &&
                        value.AmountCents < 0).Sum(value => value.AmountCents);
                    recorded -= current.Where(value => value.AccountId == player.ActorId && value.Cents < 0 &&
                        value.JournalId.StartsWith(prefix, StringComparison.Ordinal)).Sum(value => value.Cents);
                    if (recorded < player.CommittedHand)
                        entries.Add(Unavailable(CreditHoldemBackend.Matches, match.MatchId, player.ActorId,
                            "legacy-prepaid-stake", roundId, checked(recorded - player.CommittedHand)));
                }
            }
        }
        return new(entries.ToArray(), operations.Values.ToArray());
    }

    internal static RoundAuditCapture Protect(RoundAuditCapture capture, CreditHoldemMatch match, CreditHoldemMutationPlan plan, GameWalletBatchReceipt? receipt,
        CreditHoldemFinancialSnapshot prior)
    {
        var roundId = capture.Summary.RoundId;
        var tickets = match.Players.Where(value => value.IsAccountBacked && value.EscrowTicketId is not null)
            .Select(value => value.EscrowTicketId!).ToHashSet(StringComparer.Ordinal);
        var current = plan.Wallet.Where(value => value.JournalId.StartsWith(roundId + "-", StringComparison.Ordinal) ||
            tickets.Any(ticket => value.JournalId.StartsWith(ticket + "-", StringComparison.Ordinal)))
            .Select(value => new RoundAuditLedgerEntry(CreditHoldemBackend.WalletJournal, value.JournalId, value.AccountId, value.Reason,
                roundId, value.Cents, RoundAuditFinancialProof.SignedWallet, plan.Id, receipt?.BatchId)).ToArray();
        var operations = receipt is null ? prior.Operations : prior.Operations.Append(Operation(plan, receipt)).ToArray();
        return RoundAuditFinancialProof.Attach(capture, CreditHoldemBackend.Owner, Contract,
            prior.Entries.Where(value => value.RoundReference == roundId).Concat(current).ToArray(), operations);
    }

    private static void AddLegacy(DocumentSnapshot snapshot, string roundId, HashSet<string> accounts, List<RoundAuditLedgerEntry> entries)
    {
        var entry = FirestoreWalletEvidence.Legacy(snapshot, roundId);
        if (!accounts.Contains(entry.UserId) || !entry.Kind.StartsWith(CreditHoldemBackend.GameId + "-", StringComparison.Ordinal))
            throw new GameWalletUnavailableException();
        entries.Add(entry);
    }

    private static async Task AddSignedAsync(FirestoreDb database, DocumentSnapshot snapshot, string roundId, HashSet<string> accounts,
        Func<DocumentReference, Task<DocumentSnapshot>> readDocument, List<RoundAuditLedgerEntry> entries,
        Dictionary<string, RoundAuditWalletOperation> operations)
    {
        var account = snapshot.GetValue<string>("accountId");
        if (!accounts.Contains(account)) throw new GameWalletUnavailableException();
        var id = snapshot.GetValue<string>("mutationId");
        var saved = await readDocument(database.Collection(CreditHoldemBackend.Mutations).Document(id));
        if (!saved.Exists || saved.GetValue<string>("status") != "applied") throw new GameWalletUnavailableException();
        var plan = CreditHoldemMutationJournal.Decode(saved);
        var receipt = JsonSerializer.Deserialize<GameWalletBatchReceipt>(saved.GetValue<string>("walletJson"), CreditHoldemMutation.Json)
            ?? throw new GameWalletUnavailableException();
        if (receipt.BatchDeclined || !GameWalletBatchVerifier.MatchesSettlement(receipt, CreditHoldemBackend.GameId, plan.ActorAccountId, plan.WalletRequest()))
            throw new GameWalletUnavailableException();
        var expected = plan.Wallet.SingleOrDefault(value => value.JournalId == snapshot.Id);
        if (expected is null || expected.AccountId != account || expected.Cents != snapshot.GetValue<long>("deltaCents") ||
            expected.Reason != snapshot.GetValue<string>("reason") || receipt.BatchId != snapshot.GetValue<string>("walletBatchId"))
            throw new GameWalletUnavailableException();
        operations.TryAdd(id, Operation(plan, receipt));
        entries.Add(new(CreditHoldemBackend.WalletJournal, snapshot.Id, account, expected.Reason, roundId, expected.Cents,
            RoundAuditFinancialProof.SignedWallet, id, receipt.BatchId));
    }

    private static RoundAuditWalletOperation Operation(CreditHoldemMutationPlan plan, GameWalletBatchReceipt receipt) =>
        new(CreditHoldemBackend.Mutations, plan.Id, plan.WalletRequest().OutcomeSha256, plan.Wallet.Select(value => value.JournalId).ToArray(),
            new Dictionary<string, GameWalletSettlementReceipt>(), BatchRequest: plan.WalletRequest(),
            BatchReceipt: receipt with { WasAlreadySettled = false });

    private static RoundAuditLedgerEntry Unavailable(string collection, string id, string account, string kind, string roundId, long amount) =>
        new(collection, id, account, kind, roundId, amount, RoundAuditFinancialProof.LegacyHostUnavailable, EvidenceAvailable: false);

    private static string RoundId(CreditHoldemMatch match) => $"{match.MatchId}-hand-{match.HandNumber}";
}
