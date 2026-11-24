using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.PharaohsAscent.Server;

public static class PharaohsAscentRoundStateCodec
{
    public const string OwnerField = "pharaohsAscentRoundOwner";
    public const string Owner = PharaohsAscentEngine.GameId;

    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "pharaohsAscentRoundState", "Pharaoh's Ascent", usesEnergy: true,
        pointValueInCents: PharaohsAscentEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // Keep an unplayable historical one-point lock visible for reconciliation.
            if (wager != 1) PharaohsAscentEngine.ValidateWager(wager, isFreeSpin: true);
        }, validateNativeState: static native => _ = SealedTombState.Parse(native));
}
