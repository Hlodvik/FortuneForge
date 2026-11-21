using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.NordicLegends.Server;

public sealed record NordicLegendsRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record NordicLegendsSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record NordicLegendsRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<NordicLegendsSealCollection> SealCollections,
    NordicLegendsRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static NordicLegendsRoundStateReply From(ClassicSlotRoundState state, NordicLegendsRoundRequest? pending = null,
        long? balanceCents = null)
    {
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, [], pending, balanceCents / 100m);
    }
}

internal sealed record NordicLegendsRoundProgress(ClassicSlotRoundState After);

internal sealed record NordicLegendsPreparedRound(Guid AdmissionId, string AccountId, NordicLegendsRoundRequest Request,
    ClassicSlotRoundState Before, NordicLegendsRoundEngineResult Engine, NordicLegendsRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<NordicLegendsRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<NordicLegendsRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<NordicLegendsRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<NordicLegendsRoundRequest>.StateBefore => Before;
    ClassicSlotRoundState IClassicSlotPreparedRound<NordicLegendsRoundRequest>.StateAfter => Progress.After;
    public bool ImportedLegacyState { get; init; }
}

public sealed record NordicLegendsRoundResult(NordicLegendsRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal NordicLegendsPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class NordicLegendsRoundConflictException(string message, NordicLegendsRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public NordicLegendsRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class NordicLegendsRoundPausedException() : InvalidOperationException("New Nordic Legends rounds are temporarily paused.");

public sealed class NordicLegendsRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class NordicLegendsFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");

public sealed class NordicLegendsFreeSpinsUnavailableException() :
    InvalidOperationException("No Nordic Legends free games are available.");

public sealed class NordicLegendsFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class NordicLegendsLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class NordicLegendsInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
