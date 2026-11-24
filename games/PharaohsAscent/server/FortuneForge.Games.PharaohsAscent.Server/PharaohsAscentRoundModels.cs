using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;
using System.Text.Json.Serialization;

namespace FortuneForge.Games.PharaohsAscent.Server;

public sealed record PharaohsAscentRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SealedTombAction? TombAction { get; init; }
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record PharaohsAscentSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record PharaohsAscentRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<PharaohsAscentSealCollection> SealCollections,
    PharaohsAscentRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    public PharaohsAscentState GameFeatureState { get; init; } =
        new(SealCollections, FreeSpinsRemaining, FreeSpinFeatureMode, FreeSpinWagerPoints, EnergyBalance);
    public bool FeatureActionRequired => GameFeatureState.Tomb is { ActiveRun: not null } or
        { LastRun: not null } or { PendingAction: not null };

    internal static PharaohsAscentRoundStateReply From(ClassicSlotRoundState state, PharaohsAscentRoundRequest? pending = null,
        long? balanceCents = null, bool includeTomb = true, bool includeFundingRequirement = true)
    {
        var required = PharaohsAscentEngine.Profile.CollectionTarget;
        var collections = ClassicSlotCollections.TrackIds.Select(track =>
        {
            var count = Math.Min(state.SealCounts.GetValueOrDefault(track), required);
            var wager = state.SealWagerPoints.GetValueOrDefault(track);
            var average = count == 0 ? 0 : checked((long)Math.Round(wager / (decimal)count, MidpointRounding.AwayFromZero));
            return new PharaohsAscentSealCollection(track, (int)count, average, required);
        }).ToArray();
        var remaining = state.Carry?.Remaining ?? 0;
        return new(remaining, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, collections, pending?.TombAction is null ? pending : null, balanceCents / 100m)
        {
            GameFeatureState = new(collections, remaining, state.Carry?.Mode, state.Carry?.WagerPoints, state.Energy,
                includeTomb ? SealedTombState.Parse(state.NativeStateJson).View(pending?.TombAction, includeFundingRequirement) : null),
        };
    }
}

internal sealed record PharaohsAscentPreparedRound(Guid AdmissionId, string AccountId, PharaohsAscentRoundRequest Request,
    ClassicSlotRoundState Before, PharaohsAscentRoundEngineResult? Engine, ClassicSlotActiveRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<PharaohsAscentRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine?.Outcome ?? throw new InvalidOperationException("A tomb action has no reel outcome.");
    Guid IClassicSlotPreparedRound<PharaohsAscentRoundRequest>.SpinId => TombEvidence?.EventId ?? Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<PharaohsAscentRoundRequest>.GeneratedAtUtc => TombEvidence?.GeneratedAtUtc ?? Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<PharaohsAscentRoundRequest>.StateBefore => Before;
    ClassicSlotRoundState IClassicSlotPreparedRound<PharaohsAscentRoundRequest>.StateAfter => Progress.After;
    public bool ImportedLegacyState { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SealedTombActionEvidence? TombEvidence { get; init; }
}

public sealed record PharaohsAscentRoundResult(PharaohsAscentRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal PharaohsAscentPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class PharaohsAscentRoundConflictException(string message, PharaohsAscentRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public PharaohsAscentRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class PharaohsAscentRoundPausedException() : InvalidOperationException("New Pharaoh's Ascent rounds are temporarily paused.");

public sealed class PharaohsAscentRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class PharaohsAscentFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");

public sealed class PharaohsAscentFreeSpinsUnavailableException() :
    InvalidOperationException("No Pharaoh's Ascent free games are available.");

public sealed class PharaohsAscentFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class PharaohsAscentLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class PharaohsAscentInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
