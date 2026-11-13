using System.Text.Json.Serialization;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Accounts.Models;

public sealed record NeonCarRespinOffer(
    Guid SpinId,
    IReadOnlyList<int> EligibleReels,
    IReadOnlyList<IReadOnlyList<string>> Reels,
    long RoundWinPoints);

public sealed record NeonCarRespinSettlement(
    Guid SourceSpinId,
    int ReelIndex,
    int StopIndex,
    IReadOnlyList<IReadOnlyList<string>> InitialReels,
    IReadOnlyList<IReadOnlyList<string>> Reels,
    SpinPayout Payout,
    long OriginalPayoutPoints,
    long ImprovementPoints,
    decimal SlotsCreditsBalance,
    bool AlreadyApplied)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NeonNitroWildOutcome? NeonNitroWild { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NeonNitroChoiceOffer? PendingNeonNitroChoice { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? RoundWinPoints { get; init; }
}

public sealed class NeonCarRespinConflictException(string message) : Exception(message);
