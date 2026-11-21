using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.DesertTreasures.Server;

public static class DesertTreasuresRoundStateCodec
{
    public const string OwnerField = "desertTreasuresRoundOwner";
    public const string Owner = DesertTreasuresEngine.GameId;

    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "desertTreasuresRoundState", "Desert Treasures", usesEnergy: true,
        pointValueInCents: DesertTreasuresEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // Weighted legacy collections could save a one-point lock. Preserve it for reconciliation.
            if (wager != 1) DesertTreasuresEngine.ValidateWager(wager, isFreeSpin: true);
        });
}
