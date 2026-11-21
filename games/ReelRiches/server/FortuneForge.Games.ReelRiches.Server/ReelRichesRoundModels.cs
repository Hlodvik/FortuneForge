using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.ReelRiches.Server;

public sealed record ReelRichesRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record ReelRichesSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record ReelRichesRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<ReelRichesSealCollection> SealCollections,
    ReelRichesRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static ReelRichesRoundStateReply From(ClassicSlotRoundState state, ReelRichesRoundRequest? pending = null,
        long? balanceCents = null)
    {
        var required = ReelRichesEngine.Profile.CollectionTarget;
        var collections = ClassicSlotCollections.TrackIds.Select(track =>
        {
            var count = Math.Min(state.SealCounts.GetValueOrDefault(track), required);
            var wager = state.SealWagerPoints.GetValueOrDefault(track);
            var average = count == 0 ? 0 : checked((long)Math.Round(wager / (decimal)count, MidpointRounding.AwayFromZero));
            return new ReelRichesSealCollection(track, (int)count, average, required);
        }).ToArray();
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, collections, pending, balanceCents / 100m);
    }
}

internal sealed record ReelRichesRoundProgress(ClassicSlotRoundState After, ClassicSlotEnergySettlement EnergySettlement,
    int CollectionFreeSpinsAwarded,
    string? AwardedFeatureMode, long CollectionFreeSpinWagerPoints)
{
    internal SpinPayout SettledPayout => EnergySettlement.Payout;
}

internal sealed record ReelRichesPreparedRound(Guid AdmissionId, string AccountId, ReelRichesRoundRequest Request,
    ClassicSlotRoundState Before, ReelRichesRoundEngineResult Engine, ReelRichesRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<ReelRichesRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<ReelRichesRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<ReelRichesRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<ReelRichesRoundRequest>.StateBefore => Before;
    ClassicSlotRoundState IClassicSlotPreparedRound<ReelRichesRoundRequest>.StateAfter => Progress.After;
    public bool ImportedLegacyState { get; init; }
}

public sealed record ReelRichesRoundResult(ReelRichesRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal ReelRichesPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class ReelRichesRoundConflictException(string message, ReelRichesRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public ReelRichesRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class ReelRichesRoundPausedException() : InvalidOperationException("New Reel Riches rounds are temporarily paused.");

public sealed class ReelRichesRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class ReelRichesFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");

public sealed class ReelRichesFreeSpinsUnavailableException() :
    InvalidOperationException("No Reel Riches free games are available.");

public sealed class ReelRichesFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class ReelRichesLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class ReelRichesInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
