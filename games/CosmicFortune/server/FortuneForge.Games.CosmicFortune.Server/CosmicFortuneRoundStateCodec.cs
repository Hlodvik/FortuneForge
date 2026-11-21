using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.CosmicFortune.Server;

public static class CosmicFortuneRoundStateCodec
{
    public const string OwnerField = "cosmicFortuneRoundOwner";
    public const string Owner = CosmicFortuneEngine.GameId;

    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "cosmicFortuneRoundState", "Cosmic Fortune", usesEnergy: true,
        pointValueInCents: CosmicFortuneEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // Keep an unplayable historical one-point lock visible for reconciliation.
            if (wager != 1) CosmicFortuneEngine.ValidateWager(wager, isFreeSpin: true);
        });
}
