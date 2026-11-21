using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.HighNoonFortune.Server;

public sealed record HighNoonRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record HighNoonSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record HighNoonRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<HighNoonSealCollection> SealCollections,
    HighNoonRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static HighNoonRoundStateReply From(ClassicSlotRoundState state, HighNoonRoundRequest? pending = null,
        long? balanceCents = null)
    {
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, [], pending, balanceCents / 100m);
    }
}

internal sealed record HighNoonRoundProgress(ClassicSlotRoundState After);

internal sealed record HighNoonPreparedRound(Guid AdmissionId, string AccountId, HighNoonRoundRequest Request,
    ClassicSlotRoundState Before, HighNoonRoundEngineResult Engine, HighNoonRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<HighNoonRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<HighNoonRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<HighNoonRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<HighNoonRoundRequest>.StateBefore => Before;
    ClassicSlotRoundState IClassicSlotPreparedRound<HighNoonRoundRequest>.StateAfter => Progress.After;
    public bool ImportedLegacyState { get; init; }
}

public sealed record HighNoonRoundResult(HighNoonRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal HighNoonPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class HighNoonRoundConflictException(string message, HighNoonRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public HighNoonRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class HighNoonRoundPausedException() : InvalidOperationException("New High Noon Fortune rounds are temporarily paused.");

public sealed class HighNoonRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class HighNoonFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");

public sealed class HighNoonFreeSpinsUnavailableException() :
    InvalidOperationException("No High Noon Fortune free games are available.");

public sealed class HighNoonFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class HighNoonLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class HighNoonInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
