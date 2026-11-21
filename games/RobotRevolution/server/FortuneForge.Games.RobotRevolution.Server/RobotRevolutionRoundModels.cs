using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RobotRevolution.Server;

public sealed record RobotRevolutionRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record RobotRevolutionSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record RobotRevolutionRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<RobotRevolutionSealCollection> SealCollections,
    RobotRevolutionRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static RobotRevolutionRoundStateReply From(ClassicSlotRoundState state, RobotRevolutionRoundRequest? pending = null,
        long? balanceCents = null)
    {
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, [], pending, balanceCents / 100m);
    }
}

internal sealed record RobotRevolutionRoundProgress(ClassicSlotRoundState After);

internal sealed record RobotRevolutionPreparedRound(Guid AdmissionId, string AccountId, RobotRevolutionRoundRequest Request,
    ClassicSlotRoundState Before, RobotRevolutionRoundEngineResult Engine, RobotRevolutionRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<RobotRevolutionRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<RobotRevolutionRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<RobotRevolutionRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<RobotRevolutionRoundRequest>.StateBefore => Before;
    ClassicSlotRoundState IClassicSlotPreparedRound<RobotRevolutionRoundRequest>.StateAfter => Progress.After;
    public bool ImportedLegacyState { get; init; }
}

public sealed record RobotRevolutionRoundResult(RobotRevolutionRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal RobotRevolutionPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class RobotRevolutionRoundConflictException(string message, RobotRevolutionRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public RobotRevolutionRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class RobotRevolutionRoundPausedException() : InvalidOperationException("New Robot Revolution rounds are temporarily paused.");

public sealed class RobotRevolutionRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class RobotRevolutionFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");

public sealed class RobotRevolutionFreeSpinsUnavailableException() :
    InvalidOperationException("No Robot Revolution free games are available.");

public sealed class RobotRevolutionFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class RobotRevolutionLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class RobotRevolutionInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
