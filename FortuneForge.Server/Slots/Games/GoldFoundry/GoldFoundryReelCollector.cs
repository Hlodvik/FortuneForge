namespace FortuneForge.Server.Slots.Games.GoldFoundry;

internal static class GoldFoundryReelCollector
{
    internal const string EngineVersion = "gold-foundry-reel-collector-v1";

    internal static (int CollectorCount, long PayoutPoints) Calculate(
        IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints)
    {
        var collectorCount = 0;
        var ingotMultiplier = 0m;
        foreach (var symbol in reels.SelectMany(reel => reel))
        {
            if (symbol == "PAW")
                collectorCount++;
            else
                ingotMultiplier += IngotMultiplier(symbol);
        }

        var payoutPoints = collectorCount == 0 ? 0 :
            checked((long)Math.Round(wagerPoints * ingotMultiplier * Math.Min(collectorCount, 2),
                MidpointRounding.AwayFromZero));
        return (collectorCount, payoutPoints);
    }

    internal static decimal IngotMultiplier(string symbolId) => symbolId switch
    {
        "SEAL_ROWS" => 0.5m,
        "SEAL_SYNC" => 1m,
        "SEAL_PAW" => 1.5m,
        "SEAL_RAND" => 2m,
        _ => 0m
    };
}
