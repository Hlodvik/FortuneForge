using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.CosmicFortune.Server;

public sealed record CosmicFortuneRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest
{
    public static bool IsValidOperationId(string? operationId) => IClassicSlotRoundRequest.IsValidOperationId(operationId);
}

public sealed record CosmicFortuneSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record CosmicFortuneRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<CosmicFortuneSealCollection> SealCollections,
    CosmicFortuneRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static CosmicFortuneRoundStateReply From(ClassicSlotRoundState state, CosmicFortuneRoundRequest? pending = null,
        long? balanceCents = null)
    {
        var required = CosmicFortuneEngine.Profile.CollectionTarget;
        var collections = ClassicSlotCollections.TrackIds.Select(track =>
        {
            var count = Math.Min(state.SealCounts.GetValueOrDefault(track), required);
            var wager = state.SealWagerPoints.GetValueOrDefault(track);
            var average = count == 0 ? 0 : checked((long)Math.Round(wager / (decimal)count, MidpointRounding.AwayFromZero));
            return new CosmicFortuneSealCollection(track, (int)count, average, required);
        }).ToArray();
        return new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode, state.SpecialPoints,
            state.Energy, collections, pending, balanceCents / 100m);
    }
}

internal sealed record CosmicFortunePreparedRound(Guid AdmissionId, string AccountId, CosmicFortuneRoundRequest Request,
    ClassicSlotRoundState Before, CosmicFortuneRoundEngineResult Engine, ClassicSlotActiveRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation) : IClassicSlotPreparedRound<CosmicFortuneRoundRequest>
{
    internal ClassicSlotSpinOutcome Outcome => Engine.Outcome;
    Guid IClassicSlotPreparedRound<CosmicFortuneRoundRequest>.SpinId => Outcome.SpinId;
    DateTime IClassicSlotPreparedRound<CosmicFortuneRoundRequest>.GeneratedAtUtc => Outcome.Audit.GeneratedAtUtc;
    ClassicSlotRoundState IClassicSlotPreparedRound<CosmicFortuneRoundRequest>.StateBefore => Before;
    ClassicSlotRoundState IClassicSlotPreparedRound<CosmicFortuneRoundRequest>.StateAfter => Progress.After;
    public bool ImportedLegacyState { get; init; }
}

public sealed record CosmicFortuneRoundResult(CosmicFortuneRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
    : IClassicSlotRoundResult
{
    bool IClassicSlotRoundResult.Settled => Features is not null;
    internal CosmicFortunePreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

public sealed class CosmicFortuneRoundConflictException(string message, CosmicFortuneRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public CosmicFortuneRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class CosmicFortuneRoundPausedException() : InvalidOperationException("New Cosmic Fortune rounds are temporarily paused.");

public sealed class CosmicFortuneRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class CosmicFortuneFreeSpinsRequiredException() :
    InvalidOperationException("Finish the active bonus spins before placing another cash bet.");

public sealed class CosmicFortuneFreeSpinsUnavailableException() :
    InvalidOperationException("No Cosmic Fortune free games are available.");

public sealed class CosmicFortuneFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Bonus spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class CosmicFortuneLegacyFreeWagerUnplayableException(long wagerPoints) :
    InvalidOperationException("This saved bonus wager is outside the playable range and needs reconciliation.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class CosmicFortuneInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
