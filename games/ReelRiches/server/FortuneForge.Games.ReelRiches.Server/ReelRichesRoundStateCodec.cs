using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.ReelRiches.Server;

public static class ReelRichesRoundStateCodec
{
    public const string OwnerField = "reelRichesRoundOwner";
    public const string Owner = ReelRichesEngine.GameId;

    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "reelRichesRoundState", "Reel Riches", usesEnergy: true,
        pointValueInCents: ReelRichesEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // Weighted legacy collections could save a one-point lock. Preserve it for reconciliation.
            if (wager != 1) ReelRichesEngine.ValidateWager(wager, isFreeSpin: true);
        });
}
