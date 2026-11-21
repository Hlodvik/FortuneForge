using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.PhantomManor.Server;

public static class PhantomManorRoundStateCodec
{
    public const string OwnerField = "phantomManorRoundOwner";
    public const string Owner = PhantomManorEngine.GameId;

    // Existing state and audit include the dormant meter until a successful spin clears it.
    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "phantomManorRoundState", "Phantom Manor", usesEnergy: true,
        pointValueInCents: PhantomManorEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // Keep an unplayable historical one-point lock visible for reconciliation.
            if (wager != 1) PhantomManorEngine.ValidateWager(wager, isFreeSpin: true);
        });
}
