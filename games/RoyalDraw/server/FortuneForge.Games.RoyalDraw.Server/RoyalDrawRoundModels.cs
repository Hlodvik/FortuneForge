using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RoyalDraw.Server;

public sealed record RoyalDrawRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record RoyalDrawSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record RoyalDrawRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<RoyalDrawSealCollection> SealCollections,
    RoyalDrawRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static RoyalDrawRoundStateReply From(ClassicSlotRoundState state, RoyalDrawRoundRequest? pending = null,
        long? balanceCents = null)
    {
        var required = RoyalDrawEngine.Profile.CollectionTarget;
        var collections = ClassicSlotCollections.TrackIds.Select(track =>
        {
            var count = Math.Min(state.SealCounts.GetValueOrDefault(track), required);
            var wager = state.SealWagerPoints.GetValueOrDefault(track);
            var average = count == 0 ? 0 : checked((long)Math.Round(wager / (decimal)count, MidpointRounding.AwayFromZero));
            return new RoyalDrawSealCollection(track, (int)count, average, required);
        }).ToArray();
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, collections, pending, balanceCents / 100m);
    }
}

internal sealed record RoyalDrawPreparedRound(Guid AdmissionId, string AccountId, RoyalDrawRoundRequest Request,
    ClassicSlotRoundState Before, RoyalDrawRoundEngineResult Engine, ClassicSlotActiveRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<RoyalDrawRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<RoyalDrawRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<RoyalDrawRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<RoyalDrawRoundRequest>.StateBefore => Before;
    ClassicSlotRoundState IClassicSlotPreparedRound<RoyalDrawRoundRequest>.StateAfter => Progress.After;
    public bool ImportedLegacyState { get; init; }
}

public sealed record RoyalDrawRoundResult(RoyalDrawRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal RoyalDrawPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class RoyalDrawRoundConflictException(string message, RoyalDrawRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public RoyalDrawRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class RoyalDrawRoundPausedException() : InvalidOperationException("New Royal Draw rounds are temporarily paused.");

public sealed class RoyalDrawRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class RoyalDrawFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");

public sealed class RoyalDrawFreeSpinsUnavailableException() :
    InvalidOperationException("No Royal Draw free games are available.");

public sealed class RoyalDrawFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class RoyalDrawLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class RoyalDrawInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
