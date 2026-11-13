using System.Text.Json;
using System.Text.Json.Serialization;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

public sealed record NeonCarRespinOutcome(Guid OriginalSpinId, int ReelIndex, int StopIndex,
    IReadOnlyList<IReadOnlyList<string>> InitialReels, IReadOnlyList<IReadOnlyList<string>> Reels,
    SpinPayout Payout, long OriginalPayoutPoints, long ImprovementPoints)
{
    public string RuleVersion { get; init; } = NeonCarRespin.RuleVersion;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NeonNitroWildOutcome? NeonNitroWild { get; init; }
}

public sealed class NeonCarRespin(IGameRandomSource random)
{
    public const string RuleVersion = "neon-car-respin-v1";

    public static IReadOnlyList<int> OfferReels(NeonSpinOutcome original) =>
        original.Payout.TotalPoints == 0 ? EligibleReels(original) : [];

    public static IReadOnlyList<int> EligibleReels(NeonSpinOutcome original)
    {
        if (!IsSupportedCarPlay(original)) return [];

        var contributingReels = original.Payout.Paylines.SelectMany(line => line.Matches)
            .Where(match => match.AmountPoints > 0)
            .SelectMany(match => match.Match.Positions)
            .Select(position => position.Reel)
            .ToHashSet();
        if (original.NeonArcade.PaylineBoostPoints > 0)
        {
            var raw = original.EngineAudit.RawGeneratedReels;
            for (var reel = 0; reel < raw.Count; reel++)
                if (raw[reel].Contains(NeonArcadeBonus.BoostSymbolId)) contributingReels.Add(reel);
        }
        return Enumerable.Range(0, original.Reels.Count).Where(reel => !contributingReels.Contains(reel)).ToArray();
    }

    public NeonCarRespinOutcome Resolve(NeonSpinOutcome original, int reelIndex)
    {
        var strip = GetEligibleStrip(original, reelIndex);
        return ResolveAtStop(original, reelIndex, strip, random.Next(strip.Count));
    }

    public static NeonCarRespinOutcome ResolveChoice(NeonSpinOutcome original, NeonCarRespinOutcome pending, int selectedReel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(selectedReel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(selectedReel, original.Reels.Count);
        if (pending.NeonNitroWild?.RequiresChoice != true || Check(original, pending).Count != 0)
            throw new InvalidOperationException("The Nitro reel choice does not match a pending Car respin.");

        var strip = GetEligibleStrip(original, pending.ReelIndex);
        return ResolveAtStop(original, pending.ReelIndex, strip, pending.StopIndex, selectedReel);
    }

    public static IReadOnlyList<NeonReplayFinding> Check(NeonSpinOutcome original, NeonCarRespinOutcome respin)
    {
        if (respin.RuleVersion != RuleVersion || respin.OriginalSpinId != original.SpinId)
            return [new("slot-neon-car-respin-identity-mismatch", "The Car respin differs from its original spin or rule version.")];

        try
        {
            var strip = GetEligibleStrip(original, respin.ReelIndex);
            var expected = ResolveAtStop(original, respin.ReelIndex, strip, respin.StopIndex, respin.NeonNitroWild?.ExpandedReel);
            return JsonSerializer.Serialize(expected) == JsonSerializer.Serialize(respin) ? [] :
                [new("slot-neon-car-respin-mismatch", "The Car respin does not match its selected stop and banked payout.")];
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        {
            return [new("slot-neon-car-respin-replay-failed", "The Car respin could not replay from the captured original spin.")];
        }
    }

    private static bool IsSupportedCarPlay(NeonSpinOutcome original) =>
        NeonArcadeSlotDefinitions.SupportsNitroWild(original.EngineAudit.EngineVersion) &&
        NeonNitroWild.IsCarPlay(original.IsFreeSpin, original.EngineAudit.FreeSpinFeatureMode) &&
        original.NeonNitroWild?.RequiresChoice != true &&
        original.ReelSetId == NeonArcadeSlotDefinitions.CarReelSetId;

    private static IReadOnlyList<string> GetEligibleStrip(NeonSpinOutcome original, int reelIndex)
    {
        if (!EligibleReels(original).Contains(reelIndex))
            throw new ArgumentOutOfRangeException(nameof(reelIndex), "Select a Car reel that did not contribute to a win.");
        if (NeonOutcomeReplay.Check(original, original.Payout).Count != 0)
            throw new InvalidOperationException("The original Car spin could not be replayed.");

        var audit = original.EngineAudit;
        return NeonReelGenerator.GetPlayableStrip(audit.EffectiveGame, audit.ReelSet.Reels[reelIndex]);
    }

    private static NeonCarRespinOutcome ResolveAtStop(NeonSpinOutcome original, int reelIndex,
        IReadOnlyList<string> strip, int stopIndex, int? selectedWildReel = null)
    {
        if (stopIndex < 0 || stopIndex >= strip.Count)
            throw new ArgumentOutOfRangeException(nameof(stopIndex));

        var audit = original.EngineAudit;
        var game = audit.EffectiveGame;
        var selectedReel = Enumerable.Range(0, game.Layout.VisibleRows)
            .Select(row => strip[(stopIndex + row) % strip.Count]).ToArray();
        var landedReels = audit.RawGeneratedReels.ToArray();
        landedReels[reelIndex] = selectedReel;

        var initialReels = original.Reels.ToArray();
        initialReels[reelIndex] = selectedReel;
        var (reels, nitro) = original.NeonNitroWild?.EligibleReel == reelIndex &&
            (!NeonArcadeSlotDefinitions.SupportsNitroChoice(audit.EngineVersion) || original.NeonNitroWild.ExpandedReel is null) &&
            selectedReel.Contains(NeonNitroWild.SymbolId)
            ? NeonNitroWild.Resolve(initialReels, game.Symbols.WildSymbolId, audit.EngineVersion, selectedWildReel)
            : (initialReels, null);

        var evaluations = new NeonCombinationEvaluator().Evaluate(reels, game, audit.SymbolSet, audit.EngineVersion);
        var linePayout = new NeonPayoutCalculator().Calculate(evaluations, game, audit.Paytable, original.WagerPoints);
        var (payout, _, _) = NeonNightsEngine.CalculatePayout(
            linePayout, landedReels, original.WagerPoints, true, audit.EngineVersion);
        var originalPoints = original.Payout.TotalPoints;
        if (nitro?.RequiresChoice == true) payout = new(0, []);
        return new(original.SpinId, reelIndex, stopIndex, initialReels, reels, payout, originalPoints,
            Math.Max(0, checked(payout.TotalPoints - originalPoints)))
        {
            NeonNitroWild = nitro
        };
    }
}
