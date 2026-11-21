using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.Wukong.Server;

public sealed record WukongRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record WukongSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record WukongRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<WukongSealCollection> SealCollections,
    WukongRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static WukongRoundStateReply From(WukongRoundBefore state, WukongRoundRequest? pending = null,
        long? balanceCents = null)
    {
        var required = WukongEngine.Profile.CollectionTarget;
        var collections = ClassicSlotCollections.TrackIds.Select(track =>
        {
            var count = Math.Min(state.SealCounts.GetValueOrDefault(track), required);
            var wager = state.SealWagerPoints.GetValueOrDefault(track);
            var average = count == 0 ? 0 : checked((long)Math.Round(wager / (decimal)count, MidpointRounding.AwayFromZero));
            return new WukongSealCollection(track, (int)count, average, required);
        }).ToArray();
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, collections, pending, balanceCents / 100m);
    }
}

internal sealed record WukongRoundProgress(WukongRoundBefore After, ClassicSlotEnergySettlement EnergySettlement,
    int CollectionFreeSpinsAwarded)
{
    internal SpinPayout SettledPayout => EnergySettlement.Payout;
}

internal sealed record WukongPreparedRound(Guid AdmissionId, string AccountId, WukongRoundRequest Request,
    WukongRoundBefore Before, WukongRoundEngineResult Engine, WukongRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<WukongRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<WukongRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<WukongRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<WukongRoundRequest>.StateBefore => Before.ToShared();
    ClassicSlotRoundState IClassicSlotPreparedRound<WukongRoundRequest>.StateAfter => Progress.After.ToShared();
    public bool ImportedLegacyState { get; init; }
}

public sealed record WukongRoundResult(WukongRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal WukongPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class WukongRoundConflictException(string message, WukongRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public WukongRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class WukongRoundPausedException() : InvalidOperationException("New Wukong rounds are temporarily paused.");
public sealed class WukongRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class WukongFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active Celestial Quest spins before placing another cash bet.");
public sealed class WukongFreeSpinsUnavailableException() :
    InvalidOperationException("No Wukong free games are available.");
public sealed class WukongFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Celestial Quest spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class WukongInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
