using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RainbowRealm.Server;

public sealed record RainbowRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record RainbowSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record RainbowRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<RainbowSealCollection> SealCollections,
    RainbowRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static RainbowRoundStateReply From(RainbowRoundBefore state, RainbowRoundRequest? pending = null,
        long? balanceCents = null)
    {
        var required = RainbowRealmEngine.Profile.CollectionTarget;
        var collections = ClassicSlotCollections.TrackIds.Select(track =>
        {
            var count = Math.Min(state.SealCounts.GetValueOrDefault(track), required);
            var wager = state.SealWagerPoints.GetValueOrDefault(track);
            var average = count == 0 ? 0 : checked((long)Math.Round(wager / (decimal)count, MidpointRounding.AwayFromZero));
            return new RainbowSealCollection(track, (int)count, average, required);
        }).ToArray();
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            0, collections, pending, balanceCents / 100m);
    }
}

internal sealed record RainbowRoundProgress(RainbowRoundBefore After, int CollectionFreeSpinsAwarded);

internal sealed record RainbowPreparedRound(Guid AdmissionId, string AccountId, RainbowRoundRequest Request,
    RainbowRoundBefore Before, RainbowRoundEngineResult Engine, RainbowRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<RainbowRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<RainbowRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<RainbowRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<RainbowRoundRequest>.StateBefore => Before.ToShared();
    ClassicSlotRoundState IClassicSlotPreparedRound<RainbowRoundRequest>.StateAfter => Progress.After.ToShared();
    public bool ImportedLegacyState { get; init; }
}

public sealed record RainbowRoundResult(RainbowRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal RainbowPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class RainbowRoundConflictException(string message, RainbowRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public RainbowRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class RainbowRoundPausedException() : InvalidOperationException("New Rainbow Realm rounds are temporarily paused.");
public sealed class RainbowRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class RainbowFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active Orchard Rush bonus spins before placing another cash bet.");
public sealed class RainbowFreeSpinsUnavailableException() :
    InvalidOperationException("No Rainbow Realm free games are available.");
public sealed class RainbowFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Orchard Rush bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class RainbowInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
