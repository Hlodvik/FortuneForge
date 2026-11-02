using System.Text.Json;

namespace FortuneForge.Server.SecurityAudit;

internal sealed record InvestigationDecisionFeature(string ActorId, string? OpponentId, string Street,
    string Action, long AmountCents, long FacedWagerCents, int? StackAfter, int? CommitmentAfter,
    bool Timestamped, string Source, bool HeadsUp);
internal sealed record InvestigationStrategyFeatures(IReadOnlyList<InvestigationDecisionFeature> Decisions,
    string Coverage, IReadOnlyList<string> Limitations);

internal static class InvestigationStrategyExtractor
{
    internal const string Version = "descriptive-holdem-decisions-v1";
    internal static InvestigationStrategyFeatures Extract(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array || events.GetArrayLength() == 0)
            return new([], "timeline-unavailable", ["Ordered action summaries alone cannot reliably reconstruct faced wager or active opponent context."]);
        if (!root.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
            return new([], "participants-unavailable", ["Missing participant identities."]);
        var human = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in players.EnumerateArray())
        {
            var actor = Text(player, "actorId");
            if (actor is null) continue;
            active.Add(actor);
            if (player.TryGetProperty("isAccountBacked", out var backed) && backed.ValueKind == JsonValueKind.True) human.Add(actor);
        }
        var decisions = new List<InvestigationDecisionFeature>();
        var street = ""; long currentBet = 0;
        var commitments = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var entry in events.EnumerateArray())
        {
            var action = Text(entry, "action");
            if (action is null) continue;
            var entryStreet = Text(entry, "street") ?? "unknown";
            if (entryStreet != street) { street = entryStreet; currentBet = 0; commitments.Clear(); }
            var actor = Text(entry, "actorId");
            if (actor is null) continue;
            var amount = Number(entry, "amountCents") ?? 0;
            var committedAfter = Number(entry, "committedRoundAfter");
            var committedBefore = committedAfter is { } committed ? Math.Max(0, committed - amount) : commitments.GetValueOrDefault(actor);
            var faced = Math.Max(0, currentBet - committedBefore);
            var other = active.Where(value => value != actor).ToArray();
            // Managed opponents remain distinct; no comparison assumes that site automation is a human baseline.
            var headsUpHuman = other.Length == 1 && human.Contains(other[0]);
            if (human.Contains(actor) && action is "fold" or "check" or "call" or "raise")
            {
                decisions.Add(new(actor, headsUpHuman ? other[0] : null, entryStreet, action, amount, faced,
                    CheckedInt(Number(entry, "stackAfter")), CheckedInt(Number(entry, "committedHandAfter")),
                    Text(entry, "occurredAtUtc") is not null, Text(entry, "source") ?? "unknown", headsUpHuman));
            }
            if (action is "fold" or "left") active.Remove(actor);
            commitments[actor] = committedAfter ?? checked(committedBefore + amount);
            currentBet = Number(entry, "currentBetAfter") ?? Math.Max(currentBet, commitments[actor]);
        }
        return new(decisions, decisions.Count == 0 ? "no-supported-decisions" : "descriptive-only",
            ["Rates are descriptive and uncalibrated; no automatic case or strike.", "Position, hand strength, board, stake, stack depth and opponent skill are not fully matched.", "Faced wager is reconstructed from the recorded event timeline; incomplete timelines cannot establish a clean behavioral audit."]);
    }
    private static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var result) && result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    private static long? Number(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var result) && result.ValueKind == JsonValueKind.Number && result.TryGetInt64(out var number) ? number : null;
    private static int? CheckedInt(long? value) => value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
}
