using FortuneForge.Games.GoldFoundry;

namespace FortuneForge.Server.Slots.Games.GoldFoundry;

internal static class GoldFoundryReelCollector
{
    internal const string EngineVersion = FortuneForge.Games.GoldFoundry.GoldFoundryEngine.LegacyVersion;

    internal static (int CollectorCount, long PayoutPoints) Calculate(
        IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints)
        => GoldFoundryCollector.Calculate(reels, wagerPoints);

    internal static decimal IngotMultiplier(string symbolId) => GoldFoundryCollector.IngotMultiplier(symbolId);
}
