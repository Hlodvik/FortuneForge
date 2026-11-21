using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.OceanOdyssey.Server;

public sealed record OceanOdysseyRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record OceanOdysseySealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record OceanOdysseyRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<OceanOdysseySealCollection> SealCollections,
    OceanOdysseyRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static OceanOdysseyRoundStateReply From(ClassicSlotRoundState state, OceanOdysseyRoundRequest? pending = null,
        long? balanceCents = null)
    {
        var required = OceanOdysseyEngine.Profile.CollectionTarget;
        var collections = ClassicSlotCollections.TrackIds.Select(track =>
        {
            var count = Math.Min(state.SealCounts.GetValueOrDefault(track), required);
            var wager = state.SealWagerPoints.GetValueOrDefault(track);
            var average = count == 0 ? 0 : checked((long)Math.Round(wager / (decimal)count, MidpointRounding.AwayFromZero));
            return new OceanOdysseySealCollection(track, (int)count, average, required);
        }).ToArray();
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, collections, pending, balanceCents / 100m);
    }
}

internal sealed record OceanOdysseyPreparedRound(Guid AdmissionId, string AccountId, OceanOdysseyRoundRequest Request,
    ClassicSlotRoundState Before, OceanOdysseyRoundEngineResult Engine, ClassicSlotActiveRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<OceanOdysseyRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<OceanOdysseyRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<OceanOdysseyRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<OceanOdysseyRoundRequest>.StateBefore => Before;
    ClassicSlotRoundState IClassicSlotPreparedRound<OceanOdysseyRoundRequest>.StateAfter => Progress.After;
    public bool ImportedLegacyState { get; init; }
}

public sealed record OceanOdysseyRoundResult(OceanOdysseyRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal OceanOdysseyPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class OceanOdysseyRoundConflictException(string message, OceanOdysseyRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public OceanOdysseyRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class OceanOdysseyRoundPausedException() : InvalidOperationException("New Ocean Odyssey rounds are temporarily paused.");

public sealed class OceanOdysseyRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class OceanOdysseyFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");

public sealed class OceanOdysseyFreeSpinsUnavailableException() :
    InvalidOperationException("No Ocean Odyssey free games are available.");

public sealed class OceanOdysseyFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class OceanOdysseyLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class OceanOdysseyInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
