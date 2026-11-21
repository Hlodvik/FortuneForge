using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.SamuraiFortune.Server;

public static class SamuraiFortuneRoundStateCodec
{
    public const string OwnerField = "samuraiFortuneRoundOwner";
    public const string Owner = SamuraiFortuneEngine.GameId;

    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "samuraiFortuneRoundState", "Samurai Fortune", usesEnergy: true,
        pointValueInCents: SamuraiFortuneEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // Weighted legacy collections could save a one-point lock. Preserve it for reconciliation.
            if (wager != 1) SamuraiFortuneEngine.ValidateWager(wager, isFreeSpin: true);
        });
}
