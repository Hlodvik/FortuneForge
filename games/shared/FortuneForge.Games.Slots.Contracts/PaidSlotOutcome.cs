// Edited: 2026-11-18T18:14:04-06:00 | Computer: Vinnabok
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FortuneForge.Games.Slots;

/// <summary>Server-owned inputs captured after a wallet admission.</summary>
public sealed record PaidSlotOutcomeRequest(
    Guid AdmissionId,
    string GameId,
    string PlayerId,
    long WagerPoints,
    bool IsFreeSpin,
    bool SpecialBoostApplied,
    long EnergyBalance,
    string? FreeSpinFeatureMode,
    int PityMissesBefore,
    string? MathModelId)
{
    public int FreeSpinsRemaining { get; init; }

    // Keep game-owned options at their existing top-level wire names.
    [JsonExtensionData]
    public Dictionary<string, JsonElement> GameOptions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A package's outcome and replay evidence, bound to the original admission.</summary>
public sealed record PaidSlotOutcomeResponse(Guid AdmissionId, string GameId, string OutcomeKind, JsonElement Outcome)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static PaidSlotOutcomeResponse Create<TOutcome>(
        PaidSlotOutcomeRequest request, string outcomeKind, TOutcome outcome) =>
        new(request.AdmissionId, request.GameId, outcomeKind, JsonSerializer.SerializeToElement(outcome, Json));
}

public static class PaidSlotOutcomeProtocol
{
    public const string Route = "/internal/v1/paid/spins";
    public const string ClassicSlotKind = "classic-slot";
    public const string OlympusTumbleKind = "olympus-tumble";

    public static void ValidateRequest(PaidSlotOutcomeRequest request, string expectedGameId, params string[] optionNames)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AdmissionId == Guid.Empty || !string.Equals(request.GameId, expectedGameId, StringComparison.Ordinal))
            throw new ArgumentException("A matching game and accepted admission are required.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PlayerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.WagerPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(request.EnergyBalance);
        ArgumentOutOfRangeException.ThrowIfNegative(request.PityMissesBefore);
        ArgumentOutOfRangeException.ThrowIfNegative(request.FreeSpinsRemaining);
        // Legacy hosts serialize absent game flags and nullable options as false or null.
        if (request.GameOptions.Any(option =>
            option.Value.ValueKind is not (JsonValueKind.False or JsonValueKind.Null) &&
            !optionNames.Contains(option.Key, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("The admission includes unsupported game options.", nameof(request));
    }
}
