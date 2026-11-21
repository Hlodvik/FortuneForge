using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.RoyalDraw.Server;

public static class RoyalDrawRoundStateCodec
{
    public const string OwnerField = "royalDrawRoundOwner";
    public const string Owner = RoyalDrawEngine.GameId;

    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "royalDrawRoundState", "Royal Draw", usesEnergy: true,
        pointValueInCents: RoyalDrawEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // Keep an unplayable historical one-point lock visible for reconciliation.
            if (wager != 1) RoyalDrawEngine.ValidateWager(wager, isFreeSpin: true);
        });
}
