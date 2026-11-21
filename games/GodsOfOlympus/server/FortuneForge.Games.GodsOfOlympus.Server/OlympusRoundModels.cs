using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.GodsOfOlympus.Server;

public sealed record OlympusRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record OlympusSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record OlympusRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<OlympusSealCollection> SealCollections,
    OlympusRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static OlympusRoundStateReply From(ClassicSlotRoundState state, OlympusRoundRequest? pending = null,
        long? balanceCents = null)
    {
        var required = GodsOfOlympusEngine.Profile.CollectionTarget;
        var collections = ClassicSlotCollections.TrackIds.Select(track =>
        {
            var count = Math.Min(state.SealCounts.GetValueOrDefault(track), required);
            var wager = state.SealWagerPoints.GetValueOrDefault(track);
            var average = count == 0 ? 0 : checked((long)Math.Round(wager / (decimal)count, MidpointRounding.AwayFromZero));
            return new OlympusSealCollection(track, (int)count, average, required);
        }).ToArray();
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, collections, pending, balanceCents / 100m);
    }
}

internal sealed record OlympusRoundProgress(ClassicSlotRoundState After, int CollectionFreeSpinsAwarded,
    string? AwardedFeatureMode, long CollectionFreeSpinWagerPoints);

internal sealed record OlympusPreparedRound(Guid AdmissionId, string AccountId, OlympusRoundRequest Request,
    ClassicSlotRoundState Before, OlympusRoundEngineResult Engine, OlympusRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<OlympusRoundRequest>
{
    internal Guid SpinId => Engine.SpinId;
    Guid IClassicSlotPreparedRound<OlympusRoundRequest>.SpinId => SpinId;
    DateTime IClassicSlotPreparedRound<OlympusRoundRequest>.GeneratedAtUtc => Engine.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<OlympusRoundRequest>.StateBefore => Before;
    ClassicSlotRoundState IClassicSlotPreparedRound<OlympusRoundRequest>.StateAfter => Progress.After;
    public bool ImportedLegacyState { get; init; }
}

public sealed record OlympusRoundResult(OlympusRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal OlympusPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class OlympusRoundConflictException(string message, OlympusRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public OlympusRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class OlympusRoundPausedException() : InvalidOperationException("New Gods of Olympus rounds are temporarily paused.");

public sealed class OlympusRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class OlympusFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");

public sealed class OlympusFreeSpinsUnavailableException() :
    InvalidOperationException("No Gods of Olympus free games are available.");

public sealed class OlympusFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class OlympusLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class OlympusInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
