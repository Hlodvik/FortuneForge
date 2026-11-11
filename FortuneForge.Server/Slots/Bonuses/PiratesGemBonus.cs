namespace FortuneForge.Server.Slots.Bonuses;

/// <summary>
/// A Pirates gem can wake its feature once the pile has started to grow. The
/// chance rises on each gem and the twenty-fifth always awards the feature.
/// The spin ID makes the draw unpredictable before a spin and replayable later.
/// </summary>
internal static class PiratesGemBonus
{
    public const string RuleVersion =
        FortuneForge.Games.PiratesFortune.PiratesGemBonus.RuleVersion;
    public const int MaximumGems =
        FortuneForge.Games.PiratesFortune.PiratesGemBonus.MaximumGems;
    internal static readonly string[] Modes =
        FortuneForge.Games.PiratesFortune.PiratesGemBonus.Modes.ToArray();
    internal static readonly IReadOnlyDictionary<string, string> ModesBySymbol =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SEAL_SYNC"] = "sync", ["SEAL_ROWS"] = "rows",
            ["SEAL_PAW"] = "paw", ["SEAL_RAND"] = "rand"
        };

    internal static string AddFeatureMode(string? current, string mode) =>
        FortuneForge.Games.PiratesFortune.PiratesGemBonus.AddFeatureMode(current, mode);

    internal static PiratesGemProgress Advance(Guid spinId, string mode,
        int currentCount, long currentWagerTotal, int gemsAwarded, long wagerPerGem)
    {
        var progress = FortuneForge.Games.PiratesFortune.PiratesGemBonus.Advance(
            spinId, mode, currentCount, currentWagerTotal, gemsAwarded, wagerPerGem);
        return new(progress.Count, progress.WagerTotal,
            progress.Awards.Select(award =>
                new PiratesGemAward(award.GemCount, award.WagerTotal)).ToArray());
    }
}

internal sealed record PiratesGemProgress(
    int Count,
    long WagerTotal,
    IReadOnlyList<PiratesGemAward> Awards);

internal sealed record PiratesGemAward(int GemCount, long WagerTotal);

internal sealed record PiratesGemSettlementEvidence(
    string RuleVersion,
    Dictionary<string, long> CountsBefore,
    Dictionary<string, long> WagerCentsBefore,
    Dictionary<string, long> CountsAfter,
    Dictionary<string, long> WagerCentsAfter,
    int FreeSpinsAwarded,
    string? FeatureMode,
    long FreeSpinWagerPoints);
