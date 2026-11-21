using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.CandyCarnival.Server;

public sealed record CandyCarnivalRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record CandyCarnivalSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record CandyCarnivalRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<CandyCarnivalSealCollection> SealCollections,
    CandyCarnivalRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static CandyCarnivalRoundStateReply From(ClassicSlotRoundState state, CandyCarnivalRoundRequest? pending = null,
        long? balanceCents = null)
    {
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, [], pending, balanceCents / 100m);
    }
}

internal sealed record CandyCarnivalRoundProgress(ClassicSlotRoundState After);

internal sealed record CandyCarnivalPreparedRound(Guid AdmissionId, string AccountId, CandyCarnivalRoundRequest Request,
    ClassicSlotRoundState Before, CandyCarnivalRoundEngineResult Engine, CandyCarnivalRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<CandyCarnivalRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<CandyCarnivalRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<CandyCarnivalRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<CandyCarnivalRoundRequest>.StateBefore => Before;
    ClassicSlotRoundState IClassicSlotPreparedRound<CandyCarnivalRoundRequest>.StateAfter => Progress.After;
    public bool ImportedLegacyState { get; init; }
}

public sealed record CandyCarnivalRoundResult(CandyCarnivalRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal CandyCarnivalPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class CandyCarnivalRoundConflictException(string message, CandyCarnivalRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public CandyCarnivalRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class CandyCarnivalRoundPausedException() : InvalidOperationException("New Candy Carnival rounds are temporarily paused.");

public sealed class CandyCarnivalRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class CandyCarnivalFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");

public sealed class CandyCarnivalFreeSpinsUnavailableException() :
    InvalidOperationException("No Candy Carnival free games are available.");

public sealed class CandyCarnivalFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class CandyCarnivalLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class CandyCarnivalInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
