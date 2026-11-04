using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Bonuses;

public sealed record NeonArcadeActivator(string Kind, string SymbolId, IReadOnlyList<GridPosition> Positions);

public sealed record NeonArcadeEvidence(
    string ModelId,
    bool Calibrated,
    int BonusSymbolCount,
    decimal BoostPercentPerSymbol,
    decimal PaylineBoostMultiplier,
    long UnboostedPaylinePoints,
    long PaylineBoostPoints,
    bool FeatureGamesEnabled,
    IReadOnlyList<NeonArcadeActivator> Activators);

internal sealed record NeonArcadeSettlement(
    SpinPayout Payout,
    NeonArcadeEvidence Evidence,
    int FreeSpinsAwarded);

internal static class NeonArcadeBonus
{
    internal const string PaidArcadeFeatureMode = "arcade-rows";

    private static readonly (string Kind, string Symbol)[] ActivatorRoles =
        [("car", "6"), ("disco-ball", "POWER"), ("arcade-cabinet", "SEAL_RAND")];

    internal static bool IsPaidArcadeMode(string? featureMode) =>
        string.Equals(featureMode, PaidArcadeFeatureMode, StringComparison.Ordinal);

    internal static string? AwardedFeatureMode(NeonArcadeEvidence? evidence) =>
        evidence?.Activators.FirstOrDefault()?.Kind == "arcade-cabinet"
            ? PaidArcadeFeatureMode : null;

    public static NeonArcadeSettlement Apply(
        SpinPayout paylinePayout,
        IReadOnlyList<IReadOnlyList<string>> reels,
        decimal boostPercentPerSymbol = NeonArcadeSlotDefinitions.BoostPercentPerBonusSymbol,
        bool isFreeSpin = false,
        string engineVersion = NeonArcadeSlotDefinitions.EngineVersion)
    {
        if (boostPercentPerSymbol < 0) throw new ArgumentOutOfRangeException(nameof(boostPercentPerSymbol));
        var positions = reels.SelectMany((reel, reelIndex) => reel.Select((symbol, rowIndex) =>
            (Symbol: symbol, Position: new GridPosition(reelIndex, rowIndex)))).ToArray();
        var bonusCount = positions.Count(cell => cell.Symbol == "FREE");
        var multiplier = checked(1m + bonusCount * boostPercentPerSymbol / 100m);
        var payout = bonusCount == 0 || paylinePayout.TotalPoints == 0
            ? paylinePayout : MultiplyCentPayout(paylinePayout, multiplier);
        var activators = ActivatorRoles.Select(role => new NeonArcadeActivator(role.Kind, role.Symbol,
            positions.Where(cell => cell.Symbol == role.Symbol).Select(cell => cell.Position).ToArray()))
            .Where(role => role.Positions.Count > 0).ToArray();
        var featureGamesEnabled = NeonArcadeSlotDefinitions.SupportsSpecialPlays(engineVersion);
        var canAward = featureGamesEnabled &&
            (!isFreeSpin || NeonArcadeSlotDefinitions.SupportsFreeSpinRetriggers(engineVersion));
        var award = canAward && activators.Length > 0
            ? NeonArcadeSlotDefinitions.SpecialPlaySpins : 0;
        return new(payout, new(NeonArcadeSlotDefinitions.ModelId, false, bonusCount,
            boostPercentPerSymbol, multiplier, paylinePayout.TotalPoints,
            checked(payout.TotalPoints - paylinePayout.TotalPoints), featureGamesEnabled, activators), award);
    }

    private static SpinPayout MultiplyCentPayout(SpinPayout payout, decimal multiplier)
    {
        var matches = payout.Paylines.SelectMany(line => line.Matches).ToArray();
        if (matches.Sum(match => match.AmountPoints) != payout.TotalPoints)
            throw new InvalidOperationException("Neon line awards must reconcile before applying the BONUS boost.");
        var exact = matches.Select(match => checked(match.AmountPoints * multiplier)).ToArray();
        var amounts = exact.Select(amount => checked((long)decimal.Floor(amount))).ToArray();
        // Neon v2 uses one-cent points. Round the spin's payline award once to
        // whole cents, then apportion fractional cents without cumulative drift.
        var target = checked((long)Math.Round(payout.TotalPoints * multiplier, MidpointRounding.AwayFromZero));
        var remaining = checked(target - amounts.Sum());
        foreach (var index in Enumerable.Range(0, amounts.Length)
                     .OrderByDescending(index => exact[index] - amounts[index]).ThenBy(index => index)
                     .Take(checked((int)remaining)))
            amounts[index] = checked(amounts[index] + 1);
        var cursor = 0;
        var paylines = payout.Paylines.Select(line =>
        {
            var paid = line.Matches.Select(match => match with { AmountPoints = amounts[cursor++] }).ToArray();
            return line with { AmountPoints = paid.Sum(match => match.AmountPoints), Matches = paid };
        }).ToArray();
        return new(target, paylines);
    }
}
