using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Bonuses;

public sealed record OlympusTumbleAward(
    string Symbol,
    long AmountPoints,
    IReadOnlyList<GridPosition> Positions);

public sealed record OlympusTumbleStep(
    IReadOnlyList<OlympusTumbleAward> Awards,
    IReadOnlyList<GridPosition> Cleared,
    IReadOnlyList<IReadOnlyList<string>> Reels,
    IReadOnlyList<IReadOnlyList<int>> DropRows,
    long AmountPoints);

public sealed record OlympusTumbleOutcome(
    IReadOnlyList<IReadOnlyList<string>> InitialReels,
    IReadOnlyList<OlympusTumbleStep> Steps,
    long TotalPoints);

public static class OlympusTumbleDemo
{
    public const string LegacyEngineVersion =
        FortuneForge.Games.GodsOfOlympus.OlympusTumbleRules.LegacyEngineVersion;
    public const string EngineVersionV2 =
        FortuneForge.Games.GodsOfOlympus.OlympusTumbleRules.EngineVersionV2;
    public const string EngineVersion =
        FortuneForge.Games.GodsOfOlympus.OlympusTumbleRules.EngineVersion;

    public static bool SupportsEngineVersion(string? engineVersion) =>
        FortuneForge.Games.GodsOfOlympus.OlympusTumbleRules.SupportsEngineVersion(engineVersion);

    internal static bool SupportsPaidEngineVersion(string? engineVersion) =>
        FortuneForge.Games.GodsOfOlympus.OlympusTumbleRules.SupportsPaidEngineVersion(engineVersion);

    // Classic collection awards can lock an average between two cash wagers.
    internal static bool IsLegacyCarryWager(GameWageringDefinition wagering, long wagerPoints) =>
        wagerPoints >= wagering.MinimumWagerPoints &&
        (wagering.MaximumWagerPoints is not { } maximum || wagerPoints <= maximum);

    public static OlympusTumbleOutcome Resolve(long wagerPoints, bool showcase,
        Func<int, int> nextIndex) =>
        Resolve(EngineVersion, wagerPoints, showcase, nextIndex);

    public static OlympusTumbleOutcome Resolve(string engineVersion, long wagerPoints,
        bool showcase, Func<int, int> nextIndex)
    {
        var package = FortuneForge.Games.GodsOfOlympus.OlympusTumbleRules.Resolve(
            engineVersion, wagerPoints, showcase, nextIndex);
        return new OlympusTumbleOutcome(
            package.InitialReels,
            package.Steps.Select(step => new OlympusTumbleStep(
                step.Awards.Select(award => new OlympusTumbleAward(
                    award.Symbol, award.AmountPoints, award.Positions)).ToArray(),
                step.Cleared, step.Reels, step.DropRows, step.AmountPoints)).ToArray(),
            package.TotalPoints);
    }
}
