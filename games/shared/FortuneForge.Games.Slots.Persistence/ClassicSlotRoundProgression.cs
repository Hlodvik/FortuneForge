using System.Text.Json.Serialization;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.Slots.Persistence;

public sealed record ClassicSlotActiveRoundProgress(ClassicSlotRoundState After, ClassicSlotEnergySettlement EnergySettlement,
    int CollectionFreeSpinsAwarded, string? AwardedFeatureMode, long CollectionFreeSpinWagerPoints)
{
    [JsonIgnore]
    public SpinPayout SettledPayout => EnergySettlement.Payout;
}

/// <summary>Combines conventional classic slot awards with the account's merged free-game bank.</summary>
public static class ClassicSlotRoundProgression
{
    public static bool SpendsLegacy(ClassicSlotRoundState before, bool freeSpin) => freeSpin && before.LegacyRemaining > 0;

    public static ClassicSlotRoundState AdvanceScatterOnly(ClassicSlotRoundState before, ClassicSlotSpinOutcome outcome,
        SlotProfile profile, int boostCost, bool spendsLegacy) =>
        AdvanceBank(before, outcome, boostCost, spendsLegacy, AwardedMode(outcome, profile, null), outcome.WagerPoints, 0)
            with { Energy = 0 };

    public static ClassicSlotActiveRoundProgress AdvanceEnergyAndCollections(ClassicSlotRoundState before, ClassicSlotSpinOutcome outcome,
        SlotProfile profile, int boostCost, long minimumWagerPoints, bool spendsLegacy) =>
        AdvanceEnergyAndCollections(before, outcome, profile, boostCost, minimumWagerPoints, spendsLegacy, null, null, false);

    public static ClassicSlotActiveRoundProgress AdvanceEnergyAndCollections(ClassicSlotRoundState before, ClassicSlotSpinOutcome outcome,
        SlotProfile profile, int boostCost, long minimumWagerPoints, bool spendsLegacy,
        ClassicSlotEnergySettlement? energySettlement, long? energyCompletionWagerPoints = null, bool roundFundedWagerDown = false)
    {
        var energy = energySettlement ?? ClassicSlotEnergy.Settle(before.Energy, outcome.EnergyAwarded, outcome.Payout);
        var current = ClassicSlotCollections.TrackIds.Select(mode => new SlotCollectionState(mode,
            checked((int)before.SealCounts.GetValueOrDefault(mode)), before.SealWagerPoints.GetValueOrDefault(mode))).ToArray();
        var collections = ClassicSlotCollections.Advance(profile, current, outcome.CollectionsAwarded,
            outcome.WagerPoints, energy.MultiplierApplied, energyCompletionWagerPoints, roundFundedWagerDown);
        var awardedMode = AwardedMode(outcome, profile, collections.FeatureMode);
        var awardedWager = collections.FreeSpinWagerPoints > 0
            ? Math.Max(minimumWagerPoints, collections.FreeSpinWagerPoints)
            : outcome.WagerPoints;
        var after = AdvanceBank(before, outcome, boostCost, spendsLegacy, awardedMode, awardedWager, collections.FreeSpinsAwarded) with
        {
            Energy = energy.FinalEnergyBalance,
            SealCounts = collections.Collections.ToDictionary(track => track.Mode, track => (long)track.Count, StringComparer.Ordinal),
            SealWagerPoints = collections.Collections.ToDictionary(track => track.Mode, track => track.WagerPointTotal, StringComparer.Ordinal)
        };
        return new(after, energy, collections.FreeSpinsAwarded, awardedMode, collections.FreeSpinWagerPoints);
    }

    private static string? AwardedMode(ClassicSlotSpinOutcome outcome, SlotProfile profile, string? collectionMode) =>
        (outcome.FreeSpinsAwarded > 0 ? profile.ScatterFeatureMode : null) ?? collectionMode;

    private static ClassicSlotRoundState AdvanceBank(ClassicSlotRoundState before, ClassicSlotSpinOutcome outcome, int boostCost,
        bool spendsLegacy, string? awardedMode, long lockedWagerPoints, int collectionAwardedSpins)
    {
        var legacy = checked(before.LegacyRemaining - (spendsLegacy ? 1 : 0));
        var native = checked(before.NativeRemaining - (outcome.IsFreeSpin && !spendsLegacy ? 1 : 0) +
            outcome.FreeSpinsAwarded + collectionAwardedSpins);
        if (legacy < 0 || native < 0 || spendsLegacy != SpendsLegacy(before, outcome.IsFreeSpin))
            throw new InvalidOperationException("The spin does not match its saved free-game bank.");

        var remaining = checked(legacy + native);
        return before with
        {
            NativeRemaining = native,
            LegacyRemaining = legacy,
            Carry = remaining == 0 ? null : new ClassicSlotRoundCarry(remaining, lockedWagerPoints, awardedMode ?? before.Carry?.Mode),
            SpecialPoints = checked(before.SpecialPoints - (outcome.SpecialBoostApplied ? boostCost : 0) + outcome.SpecialPointsAwarded),
            Pity = outcome.ConsecutiveFiveMisses
        };
    }
}
