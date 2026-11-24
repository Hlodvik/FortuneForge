using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Bonuses;

public sealed record ArcaneConstellationValue(int Reel, int Row, long AmountPoints);

public sealed record ArcaneLightningCollection(
    GridPosition Collector, IReadOnlyList<ArcaneConstellationValue> Cells, long TotalPoints);
