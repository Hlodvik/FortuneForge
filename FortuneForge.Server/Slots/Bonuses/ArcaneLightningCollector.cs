using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Bonuses;

public sealed record ArcaneConstellationValue(int Reel, int Row, long AmountPoints);

public sealed record ArcaneLightningCollection(
    GridPosition Collector, IReadOnlyList<ArcaneConstellationValue> Cells, long TotalPoints);

internal static class ArcaneLightningCollector
{
    internal const string EngineVersion = FortuneForge.Games.ArcaneArchives.ArcaneArchivesEngine.EngineVersion;
    internal const string ReelSetId = FortuneForge.Games.ArcaneArchives.ArcaneArchivesEngine.ReelSetId;
    internal const string CollectorSymbolId = FortuneForge.Games.ArcaneArchives.ArcaneLightningCollector.CollectorSymbolId;
    internal const int PaylineId = FortuneForge.Games.ArcaneArchives.ArcaneLightningCollector.PaylineId;

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
        var package = FortuneForge.Games.ArcaneArchives.ArcaneLightningCollector.Calculate(
            reels, wagerPoints);
        return package is null ? null : new ArcaneLightningCollection(
            package.Collector,
            package.Cells.Select(cell => new ArcaneConstellationValue(
                cell.Reel, cell.Row, cell.AmountPoints)).ToArray(),
            package.TotalPoints);
    }

    internal static PaylinePayout CreatePayout(ArcaneLightningCollection collection, long wagerPoints)
    {
        var package = new FortuneForge.Games.ArcaneArchives.ArcaneLightningCollection(
            collection.Collector,
            collection.Cells.Select(cell =>
                new FortuneForge.Games.ArcaneArchives.ArcaneConstellationValue(
                    cell.Reel, cell.Row, cell.AmountPoints)).ToArray(),
            collection.TotalPoints);
        return FortuneForge.Games.ArcaneArchives.ArcaneLightningCollector.CreatePayout(
            package, wagerPoints);
    }
}
