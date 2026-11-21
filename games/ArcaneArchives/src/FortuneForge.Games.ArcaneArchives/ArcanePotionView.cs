using System.Text.Json.Serialization;

namespace FortuneForge.Games.ArcaneArchives;

public sealed record ArcanePotionBottleView(long Id, string IngredientId, string Name, string Effect, int Potency,
    long LockedWagerPoints)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VariantId { get; init; }

    internal static ArcanePotionBottleView FromBottle(ArcanePotionBottle bottle)
    {
        var description = ArcanePotions.Describe(bottle);
        return new(bottle.Id, description.IngredientId, description.Name, description.Effect, description.Potency,
            ArcanePotions.LockedWagerPoints(bottle)) { VariantId = bottle.VariantId };
    }
}

public sealed record ArcaneActivePotionView(ArcanePotionBottleView Bottle, int SpinsRemaining);

public sealed record ArcanePotionView(
    IReadOnlyList<string> Pot,
    IReadOnlyList<ArcanePotionBottleView> Bottles,
    ArcaneActivePotionView? Active,
    ArcanePotionDescription? Preview)
{
    public int Capacity => ArcanePotions.Capacity;
    public int Majority => ArcanePotions.Majority;

    public static ArcanePotionView FromState(ArcanePotionState state) => new(
        state.Pot, state.Bottles.Select(ArcanePotionBottleView.FromBottle).ToArray(),
        state.Active is { } active
            ? new(ArcanePotionBottleView.FromBottle(active.Bottle), active.SpinsRemaining)
            : null,
        ArcanePotions.Preview(state));
}
