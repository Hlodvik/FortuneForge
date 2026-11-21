using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using LegacyAudit = FortuneForge.Games.JadeDragon.Models.SlotSpinEngineAudit;
using LegacySpin = FortuneForge.Games.JadeDragon.Models.SpinResult;

namespace FortuneForge.Games.JadeDragon.Server;

public sealed record JadeRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
{
    public static bool IsValidOperationId(string? operationId) => operationId is { Length: >= 16 and <= 128 } &&
        operationId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record JadeSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record JadeRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int SpecialPointsBalance, long EnergyBalance, IReadOnlyList<JadeSealCollection> SealCollections,
    JadeRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    internal static JadeRoundStateReply From(JadeRoundBefore state, JadeRoundRequest? pending = null,
        long? balanceCents = null) => new(state.Carry?.Remaining ?? 0, state.Carry?.WagerPoints, state.Carry?.Mode,
        state.SpecialPoints, state.Energy,
        JadeDragonCollections.TrackIds.Select(track =>
        {
            var count = Math.Min(state.SealCounts.GetValueOrDefault(track), JadeDragonCollections.CollectionTarget);
            var total = state.SealWagerPoints.GetValueOrDefault(track);
            var average = count == 0 ? 0 : checked((long)Math.Round(total / (decimal)count, MidpointRounding.AwayFromZero));
            return new JadeSealCollection(track, checked((int)count),
                average, JadeDragonCollections.CollectionTarget);
        }).ToArray(), pending, balanceCents / 100m);
}

internal sealed record JadeRoundEngineResult(JadeDragonCascadeSpin? Cascade, LegacySpin? Legacy, LegacyAudit? LegacyAudit)
{
    public byte[]? LegacyDrawIndexes { get; init; }
}

internal sealed record JadeRoundProgress(JadeRoundBefore After, int CollectionFreeSpinsAwarded, int TotalFreeSpinsAwarded);

internal sealed record JadePreparedRound(Guid AdmissionId, string AccountId, JadeRoundRequest Request,
    JadeRoundBefore Before, JadeRoundEngineResult Outcome, string OutcomeSha256, long WagerCents, long PayoutCents,
    bool SpendsLegacy, DateTime StartedAtUtc, GameRequestObservation? Observation, Guid SpinId, string EngineVersion,
    long EffectiveWagerPoints, SpinPayout SettledPayout, JadeRoundProgress Progress)
{
    public bool ImportedLegacyState { get; init; }
}

public sealed record JadeRoundResult(JadeRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
{
    internal JadePreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

internal sealed record JadeStoredRound(JadePreparedRound Round, JadeRoundResult? Final);

public sealed class JadeRoundConflictException(string message, JadeRoundRequest? pendingRequest = null) : InvalidOperationException(message)
{
    public JadeRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class JadeRoundPausedException() : InvalidOperationException("New Jade Dragon rounds are temporarily paused.");
public sealed class JadeRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class JadeFreeSpinsRequiredException() : InvalidOperationException("Finish the current Jade Dragon free games first.");
public sealed class JadeFreeSpinsUnavailableException() : InvalidOperationException("No Jade Dragon free games are available.");
public sealed class JadeFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Jade Dragon free games use their saved wager.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class JadeInsufficientSpecialPointsException(int available, int required)
    : InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
