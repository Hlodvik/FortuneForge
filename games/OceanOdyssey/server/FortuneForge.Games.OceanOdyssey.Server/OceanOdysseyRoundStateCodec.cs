using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.OceanOdyssey.Server;

public static class OceanOdysseyRoundStateCodec
{
    public const string OwnerField = "oceanOdysseyRoundOwner";
    public const string Owner = OceanOdysseyEngine.GameId;

    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "oceanOdysseyRoundState", "Ocean Odyssey", usesEnergy: true,
        pointValueInCents: OceanOdysseyEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // Keep an unplayable historical one-point lock visible for reconciliation.
            if (wager != 1) OceanOdysseyEngine.ValidateWager(wager, isFreeSpin: true);
        });
}
