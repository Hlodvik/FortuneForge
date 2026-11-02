using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Mines;
using FortuneForge.Games.Mines.Server;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Replays every persisted Mines transition from the initial board through terminal settlement.</summary>
internal static class MinesSequenceReplayAudit
{
    internal static async Task<IReadOnlyList<RoundAuditFinding>> InspectFinancialAsync(
        FirestoreDb database, RoundAuditSummary summary, string? terminalPayloadJson, CancellationToken ct)
    {
        if (summary.GameId != "mines" || summary.AuditorVersion != MinesOutcomeAudit.Version) return [];
        if (terminalPayloadJson is null || summary.Participants.Count != 1)
            return Error("mines-financial-evidence-missing", "The Mines financial pass lacks a private round or a single account participant.");
        try
        {
            using var terminal = JsonDocument.Parse(terminalPayloadJson);
            var settled = ReadRound(terminal.RootElement.GetProperty("data"));
            var sourceTask = database.Collection("minesRounds").Document(summary.RoundId).GetSnapshotAsync(ct);
            var resultTask = database.Collection("cardGameResults").Document($"mines-{summary.RoundId}").GetSnapshotAsync(ct);
            var ledgerTask = database.Collection("balanceTransactions").WhereEqualTo("roundId", summary.RoundId)
                .Limit(4).GetSnapshotAsync(ct);
            await Task.WhenAll(sourceTask, resultTask, ledgerTask);
            var source = await sourceTask;
            var result = await resultTask;
            var ledger = await ledgerTask;
            if (!source.Exists || !source.TryGetValue<string>("privateRoundJson", out var sourceJson) ||
                string.IsNullOrWhiteSpace(sourceJson))
                return Error("mines-source-round-missing", "The stored Mines source round is unavailable for financial reconciliation.");
            using var sourceDocument = JsonDocument.Parse(sourceJson);
            var sourceRound = ReadRound(sourceDocument.RootElement);
            if (sourceRound.RoundId != settled.RoundId || sourceRound.UserId != settled.UserId ||
                sourceRound.Version != settled.Version || sourceRound.CreatedAt != settled.CreatedAt ||
                sourceRound.UpdatedAt != settled.UpdatedAt || !SameState(sourceRound.State, settled.State))
                return Error("mines-source-round-mismatch", "The Mines source round differs from its private terminal evidence.");
            var participant = summary.Participants[0];
            if (!result.Exists || Text(result, "game") != "mines" || Text(result, "userId") != participant.ActorId ||
                Text(result, "matchId") != summary.RoundId || Text(result, "settlementStatus") != "paid" ||
                Number(result, "wagerCents") != participant.WagerCents ||
                Number(result, "payoutCents") != participant.PayoutCents ||
                Number(result, "netCents") != participant.NetCents)
                return Error("mines-result-mismatch", "The settled Mines result document is missing or differs from the private round.");
            var wager = ledger.Documents.Where(item => Text(item, "type") == "mines-wager").ToArray();
            var payout = ledger.Documents.Where(item => Text(item, "type") == "mines-payout").ToArray();
            if (ledger.Count != 1 + (participant.PayoutCents > 0 ? 1 : 0) || wager.Length != 1 ||
                payout.Length != (participant.PayoutCents > 0 ? 1 : 0) ||
                !Entry(wager[0], participant.ActorId, -participant.WagerCents) ||
                (payout.Length == 1 && !Entry(payout[0], participant.ActorId, participant.PayoutCents)))
                return Error("mines-ledger-mismatch", "The Mines wager debit or payout credit is missing or differs from the private settlement.");
            return [];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or ArithmeticException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            return Error("mines-financial-evidence-invalid", "The saved Mines source or financial records cannot be parsed for reconciliation.");
        }
    }

    internal static IReadOnlyList<RoundAuditFinding> Inspect(
        RoundAuditSummary summary, string? terminalPayloadJson, IReadOnlyList<string> eventPayloads)
    {
        if (summary.GameId != "mines" || summary.AuditorVersion != MinesOutcomeAudit.Version) return [];
        if (terminalPayloadJson is null) return Error("mines-terminal-evidence-missing", "The private terminal round is missing.");
        try
        {
            using var terminal = JsonDocument.Parse(terminalPayloadJson);
            var root = terminal.RootElement;
            var record = ReadRound(root.GetProperty("data"));
            if (summary.EvidenceKind != "mines-private-board-and-reveals-v1" ||
                root.GetProperty("sourceCollection").GetString() != "minesRounds" ||
                root.GetProperty("sourceDocumentId").GetString() != summary.RoundId ||
                record.RoundId != summary.RoundId || summary.Participants.Count != 1 ||
                record.UserId != summary.Participants[0].ActorId ||
                root.GetProperty("wagerCents").GetInt64() != record.State.WagerCents ||
                root.GetProperty("payoutCents").GetInt64() != record.State.PayoutCents ||
                root.GetProperty("netCents").GetInt64() != checked(record.State.PayoutCents - record.State.WagerCents) ||
                summary.Participants[0].WagerCents != record.State.WagerCents ||
                summary.Participants[0].PayoutCents != record.State.PayoutCents ||
                summary.Participants[0].NetCents != checked(record.State.PayoutCents - record.State.WagerCents) ||
                summary.StartedAtUtc != record.CreatedAt.UtcDateTime ||
                summary.CompletedAtUtc != record.UpdatedAt.UtcDateTime ||
                !MinesOutcomeAudit.Verify(record, summary.Participants[0].PayoutCents))
                return Error("mines-terminal-replay-mismatch", "The saved Mines round, independent payout, or audit summary disagree.");

            var events = new List<(long Version, DateTime OccurredAtUtc, MinesStoredRound Round)>();
            foreach (var payload in eventPayloads)
            {
                using var observation = JsonDocument.Parse(payload);
                var envelope = observation.RootElement;
                events.Add((envelope.GetProperty("version").GetInt64(),
                    envelope.GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime(),
                    ReadRound(envelope.GetProperty("state"))));
            }
            events.Sort((a, b) => a.Version.CompareTo(b.Version));
            if (events.Count != record.Version || events.Count == 0 ||
                events[0].Version != 1 || events[0].Round.State.Status != MinesRoundStatus.Active ||
                events[0].Round.State.RevealedTiles.Length != 0 || events[0].Round.State.PayoutCents != 0)
                return Error("mines-action-sequence-incomplete", "The saved Mines observation stream does not begin with the accepted empty board or has missing turns.");
            for (var i = 0; i < events.Count; i++)
            {
                var current = events[i];
                if (current.Version != i + 1 || current.Round.Version != current.Version ||
                    current.Round.RoundId != record.RoundId || current.Round.UserId != record.UserId ||
                    current.Round.CreatedAt != record.CreatedAt ||
                    current.Round.UpdatedAt.UtcDateTime != current.OccurredAtUtc ||
                    current.Round.UpdatedAt > record.UpdatedAt ||
                    !SameTermsAndBoard(current.Round.State, record.State))
                    return Error("mines-action-sequence-mismatch", "An accepted Mines observation changes the board, terms, identity, version, or server time.");
                if (i == 0) continue;
                var previous = events[i - 1].Round;
                if (previous.State.Status != MinesRoundStatus.Active ||
                    current.Round.UpdatedAt < previous.UpdatedAt ||
                    !ValidTransition(previous.State, current.Round.State))
                    return Error("mines-action-sequence-mismatch", "The saved Mines reveal or cash-out sequence is not a legal transition.");
            }
            if (!SameState(events[^1].Round.State, record.State) || events[^1].Round.UpdatedAt != record.UpdatedAt)
                return Error("mines-action-terminal-mismatch", "The final observed Mines state differs from the settled private round.");
            return [];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or ArithmeticException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            return Error("mines-replay-evidence-invalid", "The private Mines round or observation stream cannot be parsed for sequence replay.");
        }
    }

    private static MinesStoredRound ReadRound(JsonElement json) =>
        JsonSerializer.Deserialize<MinesStoredRound>(json.GetRawText(), RoundAuditCapture.Json) ??
        throw new JsonException("Missing Mines round.");

    private static bool Entry(DocumentSnapshot item, string userId, long cents) =>
        Text(item, "userId") == userId && Text(item, "currencyId") == "slotsCredits" &&
        Number(item, "amountCents") == cents && Text(item, "transactionId") == item.Id &&
        item.TryGetValue<double>("amount", out var amount) && SameCents(amount, cents) &&
        item.TryGetValue<long>("balanceAfterCents", out var afterCents) &&
        item.TryGetValue<double>("balanceAfter", out var after) && SameCents(after, afterCents);

    private static bool SameCents(double amount, long cents) =>
        double.IsFinite(amount) && Math.Abs(amount * 100d - cents) < 0.000001d;

    private static string Text(DocumentSnapshot item, string field) =>
        item.TryGetValue<string>(field, out var value) ? value : "";

    private static long Number(DocumentSnapshot item, string field) =>
        item.TryGetValue<long>(field, out var value) ? value : long.MinValue;

    private static bool SameTermsAndBoard(MinesRoundState a, MinesRoundState b) =>
        a.WagerCents == b.WagerCents && a.MineCount == b.MineCount &&
        a.HouseEdgeBasisPoints == b.HouseEdgeBasisPoints && a.PayoutPolicyVersion == b.PayoutPolicyVersion &&
        a.MinePositions.SequenceEqual(b.MinePositions);

    private static bool SameState(MinesRoundState a, MinesRoundState b) =>
        SameTermsAndBoard(a, b) && a.RevealedTiles.SequenceEqual(b.RevealedTiles) &&
        a.Status == b.Status && a.PayoutCents == b.PayoutCents;

    private static bool ValidTransition(MinesRoundState before, MinesRoundState after)
    {
        var oldTiles = before.RevealedTiles;
        var newTiles = after.RevealedTiles;
        if (newTiles.Length == oldTiles.Length)
            return oldTiles.Length > 0 && oldTiles.SequenceEqual(newTiles) && after.Status == MinesRoundStatus.CashedOut;
        if (newTiles.Length != oldTiles.Length + 1 || !newTiles.Take(oldTiles.Length).SequenceEqual(oldTiles)) return false;
        var hitMine = after.MinePositions.Contains(newTiles[^1]);
        var safeCount = newTiles.Count(tile => !after.MinePositions.Contains(tile));
        return hitMine ? after.Status is MinesRoundStatus.Lost or MinesRoundStatus.Consolation :
            safeCount == 25 - after.MineCount ? after.Status == MinesRoundStatus.Cleared :
            after.Status == MinesRoundStatus.Active;
    }

    private static IReadOnlyList<RoundAuditFinding> Error(string code, string message) => [new(code, "error", message)];
}
