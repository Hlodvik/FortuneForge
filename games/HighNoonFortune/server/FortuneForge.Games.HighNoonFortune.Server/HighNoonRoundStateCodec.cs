using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.HighNoonFortune.Server;

public static class HighNoonRoundStateCodec
{
    public const string OwnerField = "highNoonFortuneRoundOwner";
    public const string Owner = HighNoonFortuneEngine.GameId;

    // Existing state and audit include the dormant meter until a successful spin clears it.
    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "highNoonFortuneRoundState", "High Noon Fortune", usesEnergy: true,
        pointValueInCents: HighNoonFortuneEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // Keep an unplayable historical one-point lock visible for reconciliation.
            if (wager != 1) HighNoonFortuneEngine.ValidateWager(wager, isFreeSpin: true);
        });
}
