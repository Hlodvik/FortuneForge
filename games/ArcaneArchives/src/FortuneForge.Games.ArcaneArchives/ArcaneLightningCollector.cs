using FortuneForge.Games.Slots;

namespace FortuneForge.Games.ArcaneArchives;

public sealed record ArcaneConstellationValue(int Reel, int Row, long AmountPoints);
public sealed record ArcaneLightningCollection(GridPosition Collector,
    IReadOnlyList<ArcaneConstellationValue> Cells, long TotalPoints);

public static class ArcaneLightningCollector
{
    public const string CollectorSymbolId = "BOLT";
    public const int PaylineId = 902;

    public static ArcaneLightningCollection? Calculate(
        IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints)
    {
        GridPosition? collector = null;
        var values = new List<ArcaneConstellationValue>();
        for (var reel = 0; reel < reels.Count; reel++)
        for (var row = 0; row < reels[reel].Count; row++)
        {
            var symbol = reels[reel][row];
            if (symbol == CollectorSymbolId) collector ??= new(reel, row);
            var multiplier = symbol switch
            {
                "7" => 0.5m, "POWER" => 1m, "SEAL_RAND" => 2m, _ => 0m
            };
            if (multiplier > 0) values.Add(new(reel, row, checked((long)Math.Round(
                wagerPoints * multiplier, MidpointRounding.AwayFromZero))));
        }
        return collector is { } position && values.Count > 0
            ? new(position, values, values.Sum(value => value.AmountPoints)) : null;
    }

    public static PaylinePayout CreatePayout(ArcaneLightningCollection collection,
        long wagerPoints)
    {
        var positions = new[] { collection.Collector }.Concat(collection.Cells
            .Select(value => new GridPosition(value.Reel, value.Row))).ToArray();
        var multiplier = checked((long)Math.Ceiling(collection.TotalPoints / (decimal)wagerPoints));
        return new(PaylineId, collection.TotalPoints,
            [new PaidMatch(new SymbolMatch(PaylineId, CollectorSymbolId, positions.Length,
                positions, []), multiplier, collection.TotalPoints)]);
    }
}
