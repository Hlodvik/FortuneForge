using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PiratesFortune.Server;

public sealed record PiratesRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record PiratesSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record PiratesRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<PiratesSealCollection> SealCollections,
    PiratesRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static PiratesRoundStateReply From(PiratesRoundBefore state, PiratesRoundRequest? pending = null,
        long? balanceCents = null)
    {
        var required = PiratesFortuneEngine.Profile.CollectionTarget;
        var collections = PiratesGemBonus.Modes.Select(track =>
        {
            var count = Math.Min(state.SealCounts.GetValueOrDefault(track), required);
            var wager = state.SealWagerPoints.GetValueOrDefault(track);
            var average = count == 0 ? 0 : checked((long)Math.Round(wager / (decimal)count, MidpointRounding.AwayFromZero));
            return new PiratesSealCollection(track, (int)count, average, required);
        }).ToArray();
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, collections, pending, balanceCents / 100m);
    }
}

internal sealed record PiratesRoundProgress(PiratesRoundBefore After, int CollectionFreeSpinsAwarded,
    string? AwardedFeatureMode, long CollectionFreeSpinWagerPoints, IReadOnlyList<PiratesGemGrant> GemAwards);

internal sealed record PiratesPreparedRound(Guid AdmissionId, string AccountId, PiratesRoundRequest Request,
    PiratesRoundBefore Before, PiratesRoundEngineResult Engine, PiratesRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<PiratesRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<PiratesRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<PiratesRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<PiratesRoundRequest>.StateBefore => Before.ToShared();
    ClassicSlotRoundState IClassicSlotPreparedRound<PiratesRoundRequest>.StateAfter => Progress.After.ToShared();
    public bool ImportedLegacyState { get; init; }
}

public sealed record PiratesRoundResult(PiratesRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal PiratesPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class PiratesRoundConflictException(string message, PiratesRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public PiratesRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class PiratesRoundPausedException() : InvalidOperationException("New Pirates' Fortune rounds are temporarily paused.");
public sealed class PiratesRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class PiratesFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");
public sealed class PiratesFreeSpinsUnavailableException() :
    InvalidOperationException("No Pirates' Fortune free games are available.");
public sealed class PiratesFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class PiratesLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class PiratesInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
