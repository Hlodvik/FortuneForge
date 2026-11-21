using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.SamuraiFortune.Server;

public sealed record SamuraiFortuneRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record SamuraiFortuneSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record SamuraiFortuneRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<SamuraiFortuneSealCollection> SealCollections,
    SamuraiFortuneRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static SamuraiFortuneRoundStateReply From(ClassicSlotRoundState state, SamuraiFortuneRoundRequest? pending = null,
        long? balanceCents = null)
    {
        var required = SamuraiFortuneEngine.Profile.CollectionTarget;
        var collections = ClassicSlotCollections.TrackIds.Select(track =>
        {
            var count = Math.Min(state.SealCounts.GetValueOrDefault(track), required);
            var wager = state.SealWagerPoints.GetValueOrDefault(track);
            var average = count == 0 ? 0 : checked((long)Math.Round(wager / (decimal)count, MidpointRounding.AwayFromZero));
            return new SamuraiFortuneSealCollection(track, (int)count, average, required);
        }).ToArray();
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, collections, pending, balanceCents / 100m);
    }
}

internal sealed record SamuraiFortuneRoundProgress(ClassicSlotRoundState After, ClassicSlotEnergySettlement EnergySettlement,
    int CollectionFreeSpinsAwarded,
    string? AwardedFeatureMode, long CollectionFreeSpinWagerPoints)
{
    internal SpinPayout SettledPayout => EnergySettlement.Payout;
}

internal sealed record SamuraiFortunePreparedRound(Guid AdmissionId, string AccountId, SamuraiFortuneRoundRequest Request,
    ClassicSlotRoundState Before, SamuraiFortuneRoundEngineResult Engine, SamuraiFortuneRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<SamuraiFortuneRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<SamuraiFortuneRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<SamuraiFortuneRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<SamuraiFortuneRoundRequest>.StateBefore => Before;
    ClassicSlotRoundState IClassicSlotPreparedRound<SamuraiFortuneRoundRequest>.StateAfter => Progress.After;
    public bool ImportedLegacyState { get; init; }
}

public sealed record SamuraiFortuneRoundResult(SamuraiFortuneRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal SamuraiFortunePreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class SamuraiFortuneRoundConflictException(string message, SamuraiFortuneRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public SamuraiFortuneRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class SamuraiFortuneRoundPausedException() : InvalidOperationException("New Samurai Fortune rounds are temporarily paused.");

public sealed class SamuraiFortuneRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class SamuraiFortuneFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");

public sealed class SamuraiFortuneFreeSpinsUnavailableException() :
    InvalidOperationException("No Samurai Fortune free games are available.");

public sealed class SamuraiFortuneFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class SamuraiFortuneLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class SamuraiFortuneInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
