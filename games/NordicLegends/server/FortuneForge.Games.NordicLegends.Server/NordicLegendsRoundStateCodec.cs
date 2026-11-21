using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.NordicLegends.Server;

public static class NordicLegendsRoundStateCodec
{
    public const string OwnerField = "nordicLegendsRoundOwner";
    public const string Owner = NordicLegendsEngine.GameId;

    // Existing state and audit include the dormant meter until a successful spin clears it.
    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "nordicLegendsRoundState", "Nordic Legends", usesEnergy: true,
        pointValueInCents: NordicLegendsEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // Keep an unplayable historical one-point lock visible for reconciliation.
            if (wager != 1) NordicLegendsEngine.ValidateWager(wager, isFreeSpin: true);
        });
}
