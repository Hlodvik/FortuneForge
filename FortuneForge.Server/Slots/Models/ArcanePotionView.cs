// Edited: 2026-11-18T18:01:37-06:00 | Computer: Vinnabok
using FortuneForge.Games.ArcaneArchives;

namespace FortuneForge.Server.Slots.Models;

public sealed record ArcanePotionBottleView(long Id, string IngredientId, string Name, string Effect, int Potency)
{
    internal static ArcanePotionBottleView FromBottle(ArcanePotionBottle bottle)
    {
        var description = ArcanePotions.Describe(bottle);
        return new(bottle.Id, description.IngredientId, description.Name, description.Effect, description.Potency);
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

    internal static ArcanePotionView FromState(ArcanePotionState state) => new(
        state.Pot, state.Bottles.Select(ArcanePotionBottleView.FromBottle).ToArray(),
        state.Active is { } active
            ? new(ArcanePotionBottleView.FromBottle(active.Bottle), active.SpinsRemaining)
            : null,
        ArcanePotions.Preview(state));
}
