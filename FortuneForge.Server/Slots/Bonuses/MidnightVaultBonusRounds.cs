using FortuneForge.Server.Slots.Configuration;

namespace FortuneForge.Server.Slots.Bonuses;

public sealed record MidnightVaultBonusRound(
    string Id,
    int TotalSpins,
    int RemainingSpins,
    long WagerPoints,
    string? FeatureMode,
    string MathModelId);

public sealed record MidnightVaultBonusProgress(
    IReadOnlyList<MidnightVaultBonusRound> Rounds,
    IReadOnlyDictionary<string, long> CollectionCounts,
    IReadOnlyDictionary<string, long> CollectionWagerPoints,
    IReadOnlyList<MidnightVaultBonusRound> AwardedRounds);

public sealed record MidnightVaultBonusEvidence(
    MidnightVaultBonusRound? AdmittedRound,
    IReadOnlyList<MidnightVaultBonusRound> RoundsBeforeAdmission,
    IReadOnlyList<MidnightVaultBonusRound> RoundsAfterConsumption,
    IReadOnlyList<MidnightVaultBonusRound> RoundsAfterSettlement,
    IReadOnlyDictionary<string, long> CollectionCountsBefore,
    IReadOnlyDictionary<string, long> CollectionWagerPointsBefore,
    IReadOnlyDictionary<string, long> CollectionCountsAfter,
    IReadOnlyDictionary<string, long> CollectionWagerPointsAfter,
    IReadOnlyList<MidnightVaultBonusRound> AwardedRounds);

/// <summary>
/// Pure Midnight-only queue transitions. A caller selects the head entitlement
/// before consuming it, then appends newly earned rounds after settlement.
/// Collection totals retain the exact sum of the wagers attached to their gems.
/// </summary>
public static class MidnightVaultBonusRounds
{
    private static readonly CollectedBonusRules Rules = new(
        MidnightVaultSlotDefinitions.MinimumWagerPoints, MidnightVaultSlotDefinitions.MaximumWagerPoints,
        [
            new("sync", "SEAL_SYNC", 30, "rows-paw"), new("rows", "SEAL_ROWS", 30, "rows-paw"),
            new("paw", "SEAL_PAW", 30, "rows-paw"), new("rand", "SEAL_RAND", 30, "rows-paw")
        ],
        [
            new(MidnightVaultSlotDefinitions.LegacyPaytableId,
                MidnightVaultSlotDefinitions.LegacyScatterAwardedSpins, MidnightVaultSlotDefinitions.LegacyCollectionAwardedSpins,
                ["sync", "rows", "paw", "rand"], AllowFeatureModeCombinations: true, AllowLegacyCarryTotals: true),
            new(MidnightVaultSlotDefinitions.PreviousPaytableId, MidnightVaultSlotDefinitions.BonusRoundSpins,
                MidnightVaultSlotDefinitions.BonusRoundSpins, ["rows-paw"]),
            new(MidnightVaultSlotDefinitions.PaytableId, MidnightVaultSlotDefinitions.BonusRoundSpins,
                MidnightVaultSlotDefinitions.BonusRoundSpins, ["rows-paw"])
        ]);

    public static void Validate(IReadOnlyList<MidnightVaultBonusRound> rounds) =>
        SlotBonusRounds.Validate(Rules, ToGeneric(rounds));

    public static IReadOnlyList<MidnightVaultBonusRound> Consume(IReadOnlyList<MidnightVaultBonusRound> rounds) =>
        SlotBonusRounds.Consume(Rules, ToGeneric(rounds)).Select(FromGeneric).ToArray();

    public static MidnightVaultBonusProgress Advance(
        IReadOnlyList<MidnightVaultBonusRound> roundsAfterConsumption,
        IReadOnlyDictionary<string, long> counts,
        IReadOnlyDictionary<string, long> wagerPointTotals,
        IReadOnlyDictionary<string, int> awardedSeals,
        long spinWagerPoints,
        int scatterAwardedSpins,
        string? scatterMode,
        string mathModelId,
        Guid spinId)
    {
        var result = SlotBonusRounds.Advance(Rules, ToGeneric(roundsAfterConsumption), counts, wagerPointTotals,
            awardedSeals, spinWagerPoints, scatterAwardedSpins, scatterMode, mathModelId, spinId);
        return new(result.Rounds.Select(FromGeneric).ToArray(), result.CollectionCounts, result.CollectionWagerPoints,
            result.AwardedRounds.Select(FromGeneric).ToArray());
    }

    private static IReadOnlyList<SlotBonusRound> ToGeneric(IReadOnlyList<MidnightVaultBonusRound> rounds)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        return rounds.Select(round => round is null
            ? throw new InvalidOperationException("A bonus round is missing.")
            : new SlotBonusRound(round.Id, round.TotalSpins, round.RemainingSpins, round.WagerPoints,
                round.FeatureMode, round.MathModelId)).ToArray();
    }

    private static MidnightVaultBonusRound FromGeneric(SlotBonusRound round) =>
        new(round.Id, round.TotalSpins, round.RemainingSpins, round.WagerPoints, round.FeatureMode, round.MathModelId);
}
