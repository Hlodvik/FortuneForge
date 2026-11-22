using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.RobotRevolution.Server;

public static class RobotRevolutionRoundStateCodec
{
    public const string OwnerField = "robotRevolutionRoundOwner";
    public const string Owner = RobotRevolutionEngine.GameId;

    // Existing state and audit include the dormant meter until a successful spin clears it.
    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "robotRevolutionRoundState", "Fifth Sun", usesEnergy: true,
        pointValueInCents: RobotRevolutionEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // Keep an unplayable historical one-point lock visible for reconciliation.
            if (wager != 1) RobotRevolutionEngine.ValidateWager(wager, isFreeSpin: true);
        }, validateNativeState: FifthSunPaidModeState.Validate);
}
