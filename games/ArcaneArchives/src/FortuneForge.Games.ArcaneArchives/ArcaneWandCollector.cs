// Edited: 2026-11-18T18:00:51-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:30:05-06:00 | Computer: Vinnabok
using System.Text.Json.Serialization;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.ArcaneArchives;

public sealed record ArcaneWandConstellationAward(int Reel, int Row, string SymbolId, long Multiplier, long AmountPoints)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long PotionBonusPoints { get; init; }
}

public sealed record ArcaneWandCollection(GridPosition Wand, IReadOnlyList<ArcaneWandConstellationAward> Cells, long TotalPoints);

public static class ArcaneWandCollector
{
    public const string WandSymbolId = "WAND";
    public const string AetherSymbolId = "POWER";
    public const string FortuneSymbolId = "FORTUNE_RUNE";
    public const string LightningSymbolId = "BOLT";
    public const int PaylineId = 903;

    public static ArcaneWandCollection? Calculate(IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints, ArcanePotionBottle? potion = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wagerPoints);
        GridPosition? wand = null;
        var cells = new List<ArcaneWandConstellationAward>();
        var bonusPercent = ArcanePotions.ConstellationBonusPercent(potion);
        long qualifyingPoints = 0;
        for (var reel = 0; reel < reels.Count; reel++)
        for (var row = 0; row < reels[reel].Count; row++)
        {
            var symbol = reels[reel][row];
            if (symbol == WandSymbolId)
                wand ??= new(reel, row);
            var multiplier = symbol switch
            {
                AetherSymbolId => 2L,
                LightningSymbolId => 4L,
                FortuneSymbolId => 10L,
                _ => 0L
            };
            if (multiplier > 0)
            {
                var amount = checked(wagerPoints * multiplier);
                var bonus = ArcanePotions.NextPayoutBonus(amount, bonusPercent, ref qualifyingPoints);
                cells.Add(new(reel, row, symbol, multiplier, checked(amount + bonus)) { PotionBonusPoints = bonus });
            }
        }
        return wand is { } position && cells.Count > 0
            ? new(position, cells.ToArray(), cells.Sum(cell => cell.AmountPoints))
            : null;
    }

    public static PaylinePayout CreatePayout(ArcaneWandCollection collection)
    {
        GridPosition[] positions = [collection.Wand, .. collection.Cells.Select(cell => new GridPosition(cell.Reel, cell.Row))];
        var match = new SymbolMatch(PaylineId, WandSymbolId, positions.Length, positions, []);
        return new(PaylineId, collection.TotalPoints,
            [new PaidMatch(match, collection.Cells.Sum(cell => cell.Multiplier), collection.TotalPoints)]);
    }

    public static bool SameCollection(ArcaneWandCollection? expected, ArcaneWandCollection? actual) =>
        expected is null ? actual is null : actual is not null &&
            expected.Wand == actual.Wand && expected.TotalPoints == actual.TotalPoints &&
            expected.Cells is not null && actual.Cells is not null && expected.Cells.SequenceEqual(actual.Cells);
}
