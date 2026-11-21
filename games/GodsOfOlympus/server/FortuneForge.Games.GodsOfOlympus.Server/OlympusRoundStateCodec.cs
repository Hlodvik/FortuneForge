using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.GodsOfOlympus.Server;

public static class OlympusRoundStateCodec
{
    public const string OwnerField = "godsOfOlympusRoundOwner";
    public const string Owner = GodsOfOlympusEngine.GameId;

    // Cash tumbles carry the raw historical energy forward. A legacy classic spin clears it on settlement.
    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "godsOfOlympusRoundState", "Gods of Olympus", usesEnergy: true,
        pointValueInCents: GodsOfOlympusEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // Weighted legacy collections could save a one-point lock. Preserve it for reconciliation.
            if (wager != 1) GodsOfOlympusEngine.ValidateWager(wager, isFreeSpin: true);
        });
}
