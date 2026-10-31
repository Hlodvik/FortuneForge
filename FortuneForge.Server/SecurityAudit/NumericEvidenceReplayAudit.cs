using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Keno;
using FortuneForge.Games.SicBo;
using FortuneForge.Server.Dice.SicBo;
using FortuneForge.Server.Numbers.Keno;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Rechecks stored Keno and Sic Bo outcomes from private picks, draws, bets and dice.</summary>
internal static class NumericEvidenceReplayAudit
{
    internal static async Task<IReadOnlyList<RoundAuditFinding>> InspectFinancialAsync(
        FirestoreDb database, RoundAuditSummary summary, CancellationToken ct)
    {
        var keno = summary.GameId == "keno" && summary.AuditorVersion == KenoOutcomeAudit.Version;
        var sicBo = summary.GameId == "sic-bo" && summary.AuditorVersion == SicBoOutcomeAudit.Version;
        if (!keno && !sicBo) return [];
        if (summary.Participants.Count != 1)
            return Error("numeric-ledger-participants-invalid", "The draw game needs one account participant for wallet reconciliation.");
        var name = keno ? "keno" : "sic-bo";
        var sourceCollection = keno ? "kenoRounds" : "sicBoRounds";
        var eventCollection = keno ? "kenoRoundEvents" : "sicBoRoundEvents";
        var prefix = $"{name}-{summary.RoundId}";
        var sourceTask = database.Collection(sourceCollection).Document(summary.RoundId).GetSnapshotAsync(ct);
        var eventTask = database.Collection(eventCollection).Document(summary.RoundId).GetSnapshotAsync(ct);
        var wagerTask = database.Collection("balanceTransactions").Document($"{prefix}-wager").GetSnapshotAsync(ct);
        var payoutTask = database.Collection("balanceTransactions").Document($"{prefix}-payout").GetSnapshotAsync(ct);
        var ledgersTask = database.Collection("balanceTransactions").OrderBy(FieldPath.DocumentId)
            .StartAt(prefix + "-").EndAt(prefix + "-\uf8ff").Limit(4).GetSnapshotAsync(ct);
        await Task.WhenAll(sourceTask, eventTask, wagerTask, payoutTask, ledgersTask);
        var source = await sourceTask;
        var settled = await eventTask;
        var wager = await wagerTask;
        var payout = await payoutTask;
        var ledgers = await ledgersTask;
        var participant = summary.Participants[0];
        var sourceWager = keno ? "wagerCents" : "totalStakeCents";
        var sourcePayout = keno ? "payoutCents" : "totalReturnCents";
        if (!source.Exists || Text(source, "roundId") != summary.RoundId ||
            Text(source, "userId") != participant.ActorId ||
            Number(source, sourceWager) != participant.WagerCents ||
            Number(source, sourcePayout) != participant.PayoutCents)
            return Error("numeric-source-mismatch", "The stored draw game source round differs from the audited settlement.");
        var key = Text(source, "startIdempotencyKey");
        if (key.Length == 0 || !settled.Exists || Text(settled, "roundId") != summary.RoundId ||
            Text(settled, "userId") != participant.ActorId || Text(settled, "event") != "settled" ||
            Text(settled, "idempotencyKey") != key)
            return Error("numeric-settlement-event-mismatch", "The draw game settlement event is missing or disagrees with its source round.");
        if (!WalletEntry(wager, participant.ActorId, -participant.WagerCents, name + "-wager", key) ||
            participant.PayoutCents > 0 && !WalletEntry(payout, participant.ActorId, participant.PayoutCents,
                name + "-payout", key + "-payout") ||
            participant.PayoutCents == 0 && payout.Exists)
            return Error("numeric-ledger-mismatch", "The draw game wallet debit or payout credit differs from the audited settlement.");
        var expectedIds = participant.PayoutCents > 0
            ? new[] { $"{prefix}-wager", $"{prefix}-payout" }
            : [$"{prefix}-wager"];
        if (ledgers.Count != expectedIds.Length ||
            ledgers.Documents.Any(item => !expectedIds.Contains(item.Id, StringComparer.Ordinal)))
            return Error("numeric-ledger-extra", "The draw game has an unexpected round-prefixed wallet entry or too many entries for bounded review.");
        if (payout.Exists && wager.TryGetValue<double>("balanceAfter", out var afterWager) &&
            payout.TryGetValue<double>("balanceAfter", out var afterPayout) &&
            !SameCents(afterPayout - afterWager, participant.PayoutCents))
            return Error("numeric-ledger-balance-mismatch", "The draw game balance sequence differs from its payout credit.");
        return [];
    }

    internal static IReadOnlyList<RoundAuditFinding> Inspect(RoundAuditSummary summary, string payloadJson)
    {
        var keno = summary.GameId == "keno" && summary.AuditorVersion == KenoOutcomeAudit.Version;
        var sicBo = summary.GameId == "sic-bo" && summary.AuditorVersion == SicBoOutcomeAudit.Version;
        if (!keno && !sicBo) return [];
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var data = root.GetProperty("data");
            var expectedCollection = keno ? "kenoRounds" : "sicBoRounds";
            var expectedKind = keno ? "keno-ticket-and-draw-v1" : "sic-bo-bets-and-dice-v1";
            if (summary.EvidenceKind != expectedKind ||
                root.GetProperty("sourceCollection").GetString() != expectedCollection ||
                root.GetProperty("sourceDocumentId").GetString() != summary.RoundId ||
                summary.Participants.Count != 1 || !summary.Participants[0].IsAccountBacked)
                return Error("numeric-replay-classification-mismatch", "The draw game source, evidence kind, or participant classification differs from the saved round.");
            var participant = summary.Participants[0];
            var wager = root.GetProperty("wagerCents").GetInt64();
            var payout = root.GetProperty("payoutCents").GetInt64();
            if (participant.WagerCents != wager || participant.PayoutCents != payout ||
                participant.NetCents != checked(payout - wager) ||
                root.GetProperty("netCents").GetInt64() != checked(payout - wager))
                return Error("numeric-replay-money-mismatch", "The saved draw-game money differs from its participant summary.");
            if (keno)
            {
                var round = new KenoStoreRound(data.GetProperty("roundId").GetString() ?? "",
                    data.GetProperty("userId").GetString() ?? "",
                    new KenoTicket(Numbers(data.GetProperty("ticket").GetProperty("numbers"))),
                    new KenoDraw(Numbers(data.GetProperty("draw").GetProperty("numbers"))),
                    data.GetProperty("hitCount").GetInt32(), data.GetProperty("wagerCents").GetInt64(),
                    data.GetProperty("payoutCents").GetInt64(), data.GetProperty("paytableId").GetString() ?? "");
                if (round.RoundId != summary.RoundId || round.UserId != participant.ActorId ||
                    round.WagerCents != wager || round.PayoutCents != payout || !KenoOutcomeAudit.Verify(round))
                    return Error("keno-draw-replay-mismatch", "The saved ticket, draw, paytable or payout does not independently resolve to the recorded Keno result.");
            }
            else
            {
                var bets = JsonSerializer.Deserialize<List<SicBoStoredBet>>(
                    data.GetProperty("bets").GetRawText(), RoundAuditCapture.Json) ?? throw new JsonException();
                var round = new SicBoStoreRound(data.GetProperty("roundId").GetString() ?? "",
                    data.GetProperty("userId").GetString() ?? "", bets,
                    new SicBoRoll(Numbers(data.GetProperty("roll").GetProperty("values"))));
                if (round.RoundId != summary.RoundId || round.UserId != participant.ActorId ||
                    !SicBoOutcomeAudit.Verify(round, wager, payout))
                    return Error("sic-bo-dice-replay-mismatch", "The saved bets, dice or gross return do not independently resolve to the recorded Sic Bo result.");
            }
            return [];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or ArithmeticException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            return Error("numeric-replay-evidence-invalid", "The private draw-game evidence cannot be parsed for independent replay.");
        }
    }

    private static int[] Numbers(JsonElement array) => array.EnumerateArray().Select(value => value.GetInt32()).ToArray();

    private static bool WalletEntry(DocumentSnapshot item, string userId, long amountCents, string type, string key) =>
        item.Exists && Text(item, "transactionId") == item.Id && Text(item, "userId") == userId &&
        Text(item, "currencyId") == "slotsCredits" && Text(item, "type") == type &&
        Text(item, "idempotencyKey") == key &&
        item.TryGetValue<double>("amount", out var amount) && SameCents(amount, amountCents) &&
        item.TryGetValue<double>("balanceAfter", out var balanceAfter) && double.IsFinite(balanceAfter);

    private static bool SameCents(double value, long cents) =>
        double.IsFinite(value) && Math.Abs(value * 100d - cents) < 0.000001d;

    private static string Text(DocumentSnapshot item, string field) =>
        item.TryGetValue<string>(field, out var value) ? value : "";

    private static long Number(DocumentSnapshot item, string field) =>
        item.TryGetValue<long>(field, out var value) ? value : long.MinValue;

    private static IReadOnlyList<RoundAuditFinding> Error(string code, string message) => [new(code, "error", message)];
}
