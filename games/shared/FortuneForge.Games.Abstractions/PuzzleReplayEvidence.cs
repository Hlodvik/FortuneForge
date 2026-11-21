using System.Text.Json;

namespace FortuneForge.Games.Abstractions.Auditing;

public sealed record PuzzleActionObservation(
    long Version, string Kind, DateTime OccurredAtUtc, JsonElement Payload, string? RequestJson);

public static class PuzzleReplayEvidence
{
    public static IReadOnlyList<RoundAuditFinding> Error(string code, string message) => [new(code, "error", message)];

    public static IReadOnlyList<RoundAuditFinding> CommandArgumentsUnavailable() =>
        [new("practice-command-arguments-unavailable", "warning",
            "One or more accepted puzzle actions lack a retained direction or column argument; the state transition replays, but the exact player input cannot be verified.")];

    public static uint NextRandom(ref uint value)
    {
        unchecked { value ^= value << 13; value ^= value >> 17; value ^= value << 5; }
        return value;
    }

    public static bool TryProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        value = default;
        return false;
    }
}
