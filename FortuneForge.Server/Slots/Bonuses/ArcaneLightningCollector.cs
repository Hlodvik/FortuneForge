using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Bonuses;

public sealed record ArcaneConstellationValue(int Reel, int Row, long AmountPoints);

public sealed record ArcaneLightningCollection(
    GridPosition Collector, IReadOnlyList<ArcaneConstellationValue> Cells, long TotalPoints);

internal static class ArcaneLightningCollector
{
    internal const string EngineVersion = "arcane-lightning-collector-v1";
    internal const string ReelSetId = "arcane-archives-lightning-reels-v1";
    internal const string CollectorSymbolId = "BOLT";
    internal const int PaylineId = 902;

    internal static bool IsModel(GameDefinition game) =>
        game.Id == SlotSpecialRoundProfiles.ArcaneArchivesGameId && game.Math.ReelSetId == ReelSetId;

    internal static bool IsValidCarryWager(GameDefinition game, long wagerPoints) =>
        wagerPoints >= game.Wagering.MinimumWagerPoints &&
        (game.Wagering.MaximumWagerPoints is not { } maximum || wagerPoints <= maximum);

    internal static void ValidateCarryWager(GameDefinition game, long wagerPoints)
    {
        if (!IsValidCarryWager(game, wagerPoints))
            throw new ArgumentOutOfRangeException(nameof(wagerPoints), "The bonus wager is outside Arcane Archives' stake bounds.");
    }

    internal static ArcaneLightningCollection? Calculate(
        IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints)
    {
        GridPosition? collector = null;
        var values = new List<ArcaneConstellationValue>();
        for (var reel = 0; reel < reels.Count; reel++)
        {
            for (var row = 0; row < reels[reel].Count; row++)
            {
                var symbol = reels[reel][row];
                if (symbol == CollectorSymbolId)
                    collector ??= new GridPosition(reel, row);

                var multiplier = symbol switch
                {
                    "7" => 0.5m,
                    "POWER" => 1m,
                    "SEAL_RAND" => 2m,
                    _ => 0m
                };
                if (multiplier > 0)
                    values.Add(new ArcaneConstellationValue(reel, row,
                        checked((long)Math.Round(wagerPoints * multiplier, MidpointRounding.AwayFromZero))));
            }
        }

        return collector is { } position && values.Count > 0
            ? new ArcaneLightningCollection(position, values.ToArray(), values.Sum(value => value.AmountPoints))
            : null;
    }

    internal static PaylinePayout CreatePayout(ArcaneLightningCollection collection, long wagerPoints)
    {
        var positions = new[] { collection.Collector }
            .Concat(collection.Cells.Select(cell => new GridPosition(cell.Reel, cell.Row)))
            .ToArray();
        var multiplier = checked((long)Math.Ceiling(collection.TotalPoints / (decimal)wagerPoints));
        return new PaylinePayout(PaylineId, collection.TotalPoints,
            [new PaidMatch(new SymbolMatch(PaylineId, CollectorSymbolId, positions.Length, positions, []),
                multiplier, collection.TotalPoints)]);
    }
}
