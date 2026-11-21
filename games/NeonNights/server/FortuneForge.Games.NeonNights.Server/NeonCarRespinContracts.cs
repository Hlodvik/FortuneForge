using System.Text.Json.Serialization;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights.Server;

public sealed record NeonCarReelRequest(Guid SpinId, int? ReelIndex);

public sealed record NeonCarDismissRequest(Guid SpinId);

public sealed record NeonCarRespinResponse(
    Guid SourceSpinId, int ReelIndex, int StopIndex,
    IReadOnlyList<IReadOnlyList<string>> InitialReels, IReadOnlyList<IReadOnlyList<string>> Reels,
    SpinPayout Payout, long OriginalPayoutPoints, long ImprovementPoints,
    decimal SlotsCreditsBalance, bool AlreadyApplied)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NeonNitroWildOutcome? NeonNitroWild { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NeonNitroChoiceOffer? PendingNeonNitroChoice { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? RoundWinPoints { get; init; }

    internal static NeonCarRespinResponse From(NeonCarRespinOutcome outcome, long balanceCents,
        bool alreadyApplied, long? roundWinPoints, NeonNitroChoiceOffer? offer = null) =>
        new(outcome.OriginalSpinId, outcome.ReelIndex, outcome.StopIndex, outcome.InitialReels,
            outcome.Reels, outcome.Payout, outcome.OriginalPayoutPoints, outcome.ImprovementPoints,
            balanceCents / 100m, alreadyApplied)
        {
            NeonNitroWild = outcome.NeonNitroWild, RoundWinPoints = roundWinPoints, PendingNeonNitroChoice = offer
        };
}

public sealed class NeonCarActionConflictException(string message) : InvalidOperationException(message);
