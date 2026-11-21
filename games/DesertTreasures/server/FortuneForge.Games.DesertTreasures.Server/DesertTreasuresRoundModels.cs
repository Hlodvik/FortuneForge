using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.DesertTreasures.Server;

public sealed record DesertTreasuresRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record DesertTreasuresSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record DesertTreasuresRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<DesertTreasuresSealCollection> SealCollections,
    DesertTreasuresRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static DesertTreasuresRoundStateReply From(ClassicSlotRoundState state, DesertTreasuresRoundRequest? pending = null,
        long? balanceCents = null)
    {
        var required = DesertTreasuresEngine.Profile.CollectionTarget;
        var collections = ClassicSlotCollections.TrackIds.Select(track =>
        {
            var count = Math.Min(state.SealCounts.GetValueOrDefault(track), required);
            var wager = state.SealWagerPoints.GetValueOrDefault(track);
            var average = count == 0 ? 0 : checked((long)Math.Round(wager / (decimal)count, MidpointRounding.AwayFromZero));
            return new DesertTreasuresSealCollection(track, (int)count, average, required);
        }).ToArray();
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, collections, pending, balanceCents / 100m);
    }
}

internal sealed record DesertTreasuresRoundProgress(ClassicSlotRoundState After, ClassicSlotEnergySettlement EnergySettlement,
    int CollectionFreeSpinsAwarded,
    string? AwardedFeatureMode, long CollectionFreeSpinWagerPoints)
{
    internal SpinPayout SettledPayout => EnergySettlement.Payout;
}

internal sealed record DesertTreasuresPreparedRound(Guid AdmissionId, string AccountId, DesertTreasuresRoundRequest Request,
    ClassicSlotRoundState Before, DesertTreasuresRoundEngineResult Engine, DesertTreasuresRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<DesertTreasuresRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<DesertTreasuresRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<DesertTreasuresRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<DesertTreasuresRoundRequest>.StateBefore => Before;
    ClassicSlotRoundState IClassicSlotPreparedRound<DesertTreasuresRoundRequest>.StateAfter => Progress.After;
    public bool ImportedLegacyState { get; init; }
}

public sealed record DesertTreasuresRoundResult(DesertTreasuresRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal DesertTreasuresPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class DesertTreasuresRoundConflictException(string message, DesertTreasuresRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public DesertTreasuresRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class DesertTreasuresRoundPausedException() : InvalidOperationException("New Desert Treasures rounds are temporarily paused.");

public sealed class DesertTreasuresRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class DesertTreasuresFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");

public sealed class DesertTreasuresFreeSpinsUnavailableException() :
    InvalidOperationException("No Desert Treasures free games are available.");

public sealed class DesertTreasuresFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class DesertTreasuresLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class DesertTreasuresInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
