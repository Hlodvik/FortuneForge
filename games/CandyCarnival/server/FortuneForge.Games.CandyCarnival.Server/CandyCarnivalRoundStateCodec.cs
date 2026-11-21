using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.CandyCarnival.Server;

public static class CandyCarnivalRoundStateCodec
{
    public const string OwnerField = "candyCarnivalRoundOwner";
    public const string Owner = CandyCarnivalEngine.GameId;

    // Existing state and audit include the dormant meter until a successful spin clears it.
    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "candyCarnivalRoundState", "Candy Carnival", usesEnergy: true,
        pointValueInCents: CandyCarnivalEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // Keep an unplayable historical one-point lock visible for reconciliation.
            if (wager != 1) CandyCarnivalEngine.ValidateWager(wager, isFreeSpin: true);
        });
}
