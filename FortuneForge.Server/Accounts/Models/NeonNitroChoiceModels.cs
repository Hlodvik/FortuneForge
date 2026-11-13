using System.Text.Json.Serialization;

namespace FortuneForge.Server.Accounts.Models;

public sealed record NeonNitroChoiceOffer(
    Guid SpinId,
    string SourceKind,
    long WagerPoints,
    int FreeSpinsRemaining,
    IReadOnlyList<IReadOnlyList<string>> InitialReels,
    IReadOnlyList<int> EligibleReels,
    NeonNitroWildOutcome NeonNitroWild)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ReelIndex { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? RoundWinPoints { get; init; }
}

public sealed class NeonNitroChoiceConflictException(string message) : Exception(message);

public sealed record NeonNitroChoiceFinalization(
    FortuneForge.Server.Slots.Models.SpinResult Result,
    SlotSpinSettlement Settlement);
