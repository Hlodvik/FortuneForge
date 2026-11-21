using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;
using FortuneForge.Server.Cards.TexasHoldem.Credit;

namespace FortuneForge.Server.SecurityAudit;

internal static class HoldemFinancialAudit
{
    private const int PageSize = 50;
    private const int MaximumLedgerEntries = 2_000;

    internal static async Task<IReadOnlyList<RoundAuditFinding>> InspectAsync(FirestoreDb database,
        RoundAuditSummary summary, CancellationToken ct, string? roundPayload = null)
    {
        if (summary.GameId != "texas-holdem") return [];
        var findings = new List<RoundAuditFinding>();
        var revenue = await database.Collection(CreditHoldemBackend.MatchRevenue).Document(summary.RoundId).GetSnapshotAsync(ct);
        if (!revenue.Exists)
            return [new("holdem-revenue-missing", "error", "The completed Hold'em hand has no financial revenue record.")];
        if (!revenue.TryGetValue<long>("humanWagerCents", out var recordedWagers) ||
            !revenue.TryGetValue<long>("humanPayoutCents", out var recordedPayouts) ||
            !revenue.TryGetValue<string>("financialClassification", out var classification))
            return [new("holdem-revenue-invalid", "error", "The Hold'em financial revenue record is missing required fields.")];
        var humans = summary.Participants.Where(player => player.IsAccountBacked).ToArray();
        if (recordedWagers != humans.Sum(player => player.WagerCents) ||
            recordedPayouts != humans.Sum(player => player.PayoutCents))
            findings.Add(new("holdem-revenue-summary-mismatch", "error", "The Hold'em revenue record and private participant settlement disagree."));
        if (classification != "real-human-wager-v2")
        {
            if (classification == "escrow-house-backed-v1")
                findings.AddRange(await InspectEscrowAsync(database, summary, revenue, roundPayload, ct));
            else
                findings.Add(new("holdem-financial-classification-unknown", "error", "The Hold'em financial classification is unknown."));
            return findings;
        }

        var prefix = summary.RoundId + "-";
        var cursor = "";
        long charged = 0, returned = 0;
        var seen = 0;
        while (true)
        {
            Query query = database.Collection("balanceTransactions").OrderBy(FieldPath.DocumentId)
                .EndAt(prefix + "\uf8ff");
            query = cursor.Length > 0 ? query.StartAfter(cursor) : query.StartAt(prefix);
            var page = await query.Limit(PageSize).GetSnapshotAsync(ct);
            foreach (var entry in page.Documents)
            {
                seen++;
                if (!entry.TryGetValue<double>("amount", out var credits) || !double.IsFinite(credits) ||
                    !entry.TryGetValue<string>("type", out var type) ||
                    !entry.TryGetValue<string>("userId", out var userId) ||
                    !humans.Any(player => player.ActorId == userId))
                {
                    findings.Add(new("holdem-ledger-invalid", "error", "A hand-linked Hold'em balance entry is malformed or belongs to a nonparticipant."));
                    continue;
                }
                var cents = credits * 100d;
                if (Math.Abs(cents - Math.Round(cents)) > 0.000001d ||
                    cents < long.MinValue || cents > long.MaxValue)
                {
                    findings.Add(new("holdem-ledger-precision", "error", "A hand-linked Hold'em balance entry is not an exact cent amount."));
                    continue;
                }
                var amount = checked((long)Math.Round(cents));
                if (type == "texas-holdem-hand-payout" && amount > 0) returned = checked(returned + amount);
                else if (type.StartsWith("texas-holdem-", StringComparison.Ordinal) && amount < 0)
                    charged = checked(charged - amount);
                else findings.Add(new("holdem-ledger-type", "error", "A hand-linked Hold'em balance entry has an unexpected type or sign."));
            }
            if (page.Count == 0) break;
            cursor = page.Documents[^1].Id;
            if (seen >= MaximumLedgerEntries)
            {
                findings.Add(new("holdem-ledger-scan-limit", "error", "The hand ledger exceeded the bounded automatic audit; manual review is required."));
                return findings;
            }
            if (page.Count < PageSize) break;
        }
        var preparedEntries = await database.Collection(CreditHoldemBackend.WalletJournal).OrderBy(FieldPath.DocumentId)
            .StartAt(prefix).EndAt(prefix + "\uf8ff").Limit(MaximumLedgerEntries).GetSnapshotAsync(ct);
        foreach (var entry in preparedEntries.Documents)
        {
            if (!entry.TryGetValue<long>("deltaCents", out var cents) ||
                !humans.Any(player => player.ActorId == FirestoreRoundText(entry, "accountId")) ||
                !await VerifyPreparedEntryAsync(database, entry, ct))
            {
                findings.Add(new("holdem-prepared-journal-invalid", "error", "A hand-linked game journal entry has no matching signed wallet batch."));
                continue;
            }
            if (cents < 0) charged = checked(charged - cents);
            else if (FirestoreRoundText(entry, "reason") == "texas-holdem-hand-payout") returned = checked(returned + cents);
            else findings.Add(new("holdem-prepared-journal-type", "error", "A hand-linked game journal has an unexpected financial reason."));
        }
        if (preparedEntries.Count == MaximumLedgerEntries)
            findings.Add(new("holdem-prepared-journal-scan-limit", "error", "The game journal exceeds the bounded automatic audit."));
        if (charged != recordedWagers || returned != recordedPayouts)
            findings.Add(new("holdem-ledger-settlement-mismatch", "error", "Hand-linked wallet entries do not sum to the accepted wagers and payouts."));
        return findings;
    }

    private static async Task<IReadOnlyList<RoundAuditFinding>> InspectEscrowAsync(FirestoreDb database,
        RoundAuditSummary summary, DocumentSnapshot revenue, string? roundPayload, CancellationToken ct)
    {
        var findings = new List<RoundAuditFinding>();
        if (FirestoreRoundText(revenue, "matchId") != summary.TableId)
            findings.Add(new("holdem-escrow-match-mismatch", "error", "The escrow revenue record names a different table."));
        if (roundPayload is null)
        {
            var evidence = await database.Collection("gameRoundEvidence").Document(summary.Id).GetSnapshotAsync(ct);
            roundPayload = evidence.Exists ? FirestoreRoundText(evidence, "payloadJson") : null;
        }
        if (string.IsNullOrWhiteSpace(roundPayload))
            return [.. findings, new("holdem-escrow-snapshot-missing", "error", "The private escrow hand snapshot is unavailable for ticket and wallet reconciliation.")];
        try
        {
            using var document = JsonDocument.Parse(roundPayload);
            var root = document.RootElement;
            if (!root.GetProperty("usesBuyInEscrow").GetBoolean())
                findings.Add(new("holdem-escrow-mode-mismatch", "error", "The hand snapshot does not use the revenue record's escrow mode."));
            var cashedOut = root.GetProperty("cashedOutReservations").EnumerateArray()
                .Select(value => value.GetString()).Where(value => value is not null).ToHashSet(StringComparer.Ordinal);
            var outstanding = false;
            foreach (var player in root.GetProperty("players").EnumerateArray())
            {
                if (!player.GetProperty("isAccountBacked").GetBoolean()) continue;
                var userId = player.GetProperty("actorId").GetString() ?? "";
                var ticketId = player.GetProperty("escrowTicketId").GetString() ?? "";
                if (!CreditHoldemIds.IsValid(ticketId))
                {
                    findings.Add(new("holdem-escrow-ticket-invalid", "error", "An account-backed escrow seat has no valid reservation identifier."));
                    continue;
                }
                var ticket = await database.Collection(CreditHoldemBackend.Tickets).Document(ticketId).GetSnapshotAsync(ct);
                if (!ticket.Exists)
                {
                    findings.Add(new("holdem-escrow-ticket-missing", "error", "A hand participant's escrow reservation record is missing."));
                    continue;
                }
                using var ticketJson = JsonDocument.Parse(FirestoreRoundText(ticket, "ticketJson"));
                var ticketData = ticketJson.RootElement;
                var buyIn = ticketData.GetProperty("buyInCents").GetInt64();
                if (ticketData.GetProperty("userId").GetString() != userId || buyIn <= 0)
                    findings.Add(new("holdem-escrow-ticket-mismatch", "error", "The escrow reservation does not match its account or buy-in."));
                var buyInLedger = await ReadLedgerAsync(database, ticketId + "-buy-in", ct);
                if (!LedgerMatches(buyInLedger, userId, ticketId, "texas-holdem-buy-in", -buyIn))
                    findings.Add(new("holdem-escrow-buy-in-mismatch", "error", "The escrow buy-in has no matching wallet debit."));
                var cashOutLedger = await ReadLedgerAsync(database, ticketId + "-cash-out", ct);
                if (cashOutLedger.Exists)
                {
                    if (!LedgerMatches(cashOutLedger, userId, ticketId, "texas-holdem-cash-out", null))
                        findings.Add(new("holdem-escrow-cash-out-invalid", "error", "An escrow cash-out wallet entry is malformed or belongs to another account."));
                    else if (cashedOut.Contains(ticketId) &&
                        !LedgerMatches(cashOutLedger, userId, ticketId, "texas-holdem-cash-out", player.GetProperty("stack").GetInt64()))
                        findings.Add(new("holdem-escrow-cash-out-mismatch", "error", "The recorded cash-out differs from the seat's final stack."));
                }
                else if (cashedOut.Contains(ticketId))
                    findings.Add(new("holdem-escrow-cash-out-missing", "error", "A reservation marked cashed out has no wallet credit."));
                else outstanding = true;
                var status = FirestoreRoundText(ticket, "status");
                if (status is "cancelled" or "expired")
                {
                    var refund = await ReadLedgerAsync(database, ticketId + "-buy-in-refund", ct);
                    if (!LedgerMatches(refund, userId, ticketId, "texas-holdem-buy-in-refund", buyIn))
                        findings.Add(new("holdem-escrow-refund-mismatch", "error", "A cancelled escrow reservation has no matching wallet refund."));
                }
            }
            if (outstanding)
                findings.Add(new("holdem-escrow-lifecycle-open", "info", "Escrow buy-ins were checked, but one or more seats remain active; final wallet cash-out conservation can only be completed when those seats leave."));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            findings.Add(new("holdem-escrow-snapshot-invalid", "error", "The private escrow hand or reservation snapshot cannot be parsed for wallet reconciliation."));
        }
        return findings;
    }

    private static bool LedgerMatches(DocumentSnapshot ledger, string userId, string ticketId,
        string type, long? expectedCents)
    {
        if (ledger.Exists && ledger.TryGetValue<long>("deltaCents", out var delta))
            return FirestoreRoundText(ledger, "accountId") == userId && FirestoreRoundText(ledger, "sourceKey") == ticketId &&
                FirestoreRoundText(ledger, "reason") == type && (expectedCents is { } preparedExpected ? delta == preparedExpected : delta >= 0);
        if (!ledger.Exists || FirestoreRoundText(ledger, "userId") != userId ||
            FirestoreRoundText(ledger, "type") != type || FirestoreRoundText(ledger, "idempotencyKey") != ticketId ||
            !ledger.TryGetValue<double>("amount", out var credits) || !double.IsFinite(credits)) return false;
        var cents = credits * 100d;
        if (Math.Abs(cents - Math.Round(cents)) > 0.000001d || cents < long.MinValue || cents > long.MaxValue) return false;
        var actual = checked((long)Math.Round(cents));
        return expectedCents is { } expected ? actual == expected : actual >= 0;
    }

    private static async Task<DocumentSnapshot> ReadLedgerAsync(FirestoreDb database, string id, CancellationToken token)
    {
        var legacy = await database.Collection("balanceTransactions").Document(id).GetSnapshotAsync(token);
        if (legacy.Exists) return legacy;
        var prepared = await database.Collection(CreditHoldemBackend.WalletJournal).Document(id).GetSnapshotAsync(token);
        if (prepared.Exists && !await VerifyPreparedEntryAsync(database, prepared, token))
            throw new InvalidOperationException("The game financial journal has no matching signed wallet batch.");
        return prepared;
    }

    private static async Task<bool> VerifyPreparedEntryAsync(FirestoreDb database, DocumentSnapshot entry, CancellationToken token)
    {
        var id = FirestoreRoundText(entry, "mutationId");
        if (id.Length != 32) return false;
        var mutation = await database.Collection(CreditHoldemBackend.Mutations).Document(id).GetSnapshotAsync(token);
        if (!mutation.Exists || FirestoreRoundText(mutation, "status") != "applied") return false;
        var plan = CreditHoldemMutationJournal.Decode(mutation);
        var receipt = JsonSerializer.Deserialize<GameWalletBatchReceipt>(FirestoreRoundText(mutation, "walletJson"), CreditHoldemMutation.Json);
        if (receipt is null || receipt.BatchDeclined || plan.Id != id ||
            receipt.BatchId != FirestoreRoundText(entry, "walletBatchId") ||
            !GameWalletBatchVerifier.MatchesSettlement(receipt, CreditHoldemBackend.GameId, plan.ActorAccountId, plan.WalletRequest())) return false;
        var expected = plan.Wallet.SingleOrDefault(value => value.JournalId == entry.Id);
        return expected is not null && expected.AccountId == FirestoreRoundText(entry, "accountId") &&
            expected.Cents == entry.GetValue<long>("deltaCents") && expected.Reason == FirestoreRoundText(entry, "reason") &&
            expected.SourceKey == FirestoreRoundText(entry, "sourceKey");
    }

    private static string FirestoreRoundText(DocumentSnapshot snapshot, string key) =>
        snapshot.TryGetValue<string>(key, out var value) ? value : "";
}
