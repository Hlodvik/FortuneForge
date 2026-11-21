using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PhantomManor.Server;

public sealed record PhantomManorRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record PhantomManorSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record PhantomManorRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<PhantomManorSealCollection> SealCollections,
    PhantomManorRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static PhantomManorRoundStateReply From(ClassicSlotRoundState state, PhantomManorRoundRequest? pending = null,
        long? balanceCents = null)
    {
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, [], pending, balanceCents / 100m);
    }
}

internal sealed record PhantomManorRoundProgress(ClassicSlotRoundState After);

internal sealed record PhantomManorPreparedRound(Guid AdmissionId, string AccountId, PhantomManorRoundRequest Request,
    ClassicSlotRoundState Before, PhantomManorRoundEngineResult Engine, PhantomManorRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<PhantomManorRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<PhantomManorRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<PhantomManorRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<PhantomManorRoundRequest>.StateBefore => Before;
    ClassicSlotRoundState IClassicSlotPreparedRound<PhantomManorRoundRequest>.StateAfter => Progress.After;
    public bool ImportedLegacyState { get; init; }
}

public sealed record PhantomManorRoundResult(PhantomManorRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal PhantomManorPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class PhantomManorRoundConflictException(string message, PhantomManorRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public PhantomManorRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class PhantomManorRoundPausedException() : InvalidOperationException("New Phantom Manor rounds are temporarily paused.");

public sealed class PhantomManorRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class PhantomManorFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");

public sealed class PhantomManorFreeSpinsUnavailableException() :
    InvalidOperationException("No Phantom Manor free games are available.");

public sealed class PhantomManorFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class PhantomManorLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class PhantomManorInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
