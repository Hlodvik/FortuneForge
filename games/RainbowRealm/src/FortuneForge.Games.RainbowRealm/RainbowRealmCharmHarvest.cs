using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RainbowRealm;

public static class RainbowRealmCharmHarvest
{
    public static decimal WagerMultiplier(string symbol) => symbol switch
    {
        "SEAL_SYNC" => 1.5m, "SEAL_ROWS" => 2m,
        "SEAL_PAW" => 2.5m, "SEAL_RAND" => 3m, _ => 0m
    };

    public static ClassicFeaturePayout Calculate(
        IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints,
        decimal payoutMultiplier)
    {
        var symbols = reels.SelectMany(reel => reel).ToArray();
        var baskets = symbols.Count(symbol => symbol == "PAW");
        var harvest = baskets == 0 ? 0 : ClassicSlotEngine.Scale(wagerPoints,
            symbols.Sum(WagerMultiplier) * Math.Min(baskets, 2));
        var bananas = ClassicSlotEngine.CalculatePatternPayouts(
            reels, wagerPoints, payoutMultiplier);
        return new(baskets, harvest, bananas.Sum(value => value.AmountPoints),
            bananas, harvest);
    }
}
