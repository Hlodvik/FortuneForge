using System.Text.Json;

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
    public bool IsNeonDiscoPlay { get; init; }
    public int? NeonDiscoMultiplier { get; init; }
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

    public static void ValidateRequest(PaidSlotOutcomeRequest request, string expectedGameId)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AdmissionId == Guid.Empty || !string.Equals(request.GameId, expectedGameId, StringComparison.Ordinal))
            throw new ArgumentException("A matching game and accepted admission are required.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PlayerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.WagerPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(request.EnergyBalance);
        ArgumentOutOfRangeException.ThrowIfNegative(request.PityMissesBefore);
        ArgumentOutOfRangeException.ThrowIfNegative(request.FreeSpinsRemaining);
    }
}
