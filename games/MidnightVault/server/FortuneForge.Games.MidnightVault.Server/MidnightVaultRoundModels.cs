using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.MidnightVault.Server;

public sealed record MidnightVaultRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
{
    public static bool IsValidOperationId(string? operationId) => operationId is { Length: >= 16 and <= 128 } &&
        operationId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record MidnightVaultSealCollection(string SealId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record MidnightVaultRoundStateReply(MidnightVaultBonusRound? BonusRound,
    IReadOnlyList<MidnightVaultBonusRound> QueuedBonusRounds, int SpecialPointsBalance, long EnergyBalance,
    IReadOnlyList<MidnightVaultSealCollection> SealCollections, MidnightVaultRoundRequest? PendingOperation = null,
    decimal? SlotsCreditsBalance = null)
{
    public int FreeSpinsRemaining => BonusRound?.RemainingSpins ?? 0;
    public long? FreeSpinWagerPoints => BonusRound?.WagerPoints;
    public string? FreeSpinFeatureMode => BonusRound?.FeatureMode;

    internal static MidnightVaultRoundStateReply From(MidnightVaultRoundBefore before,
        MidnightVaultRoundRequest? pending = null, long? balanceCents = null)
    {
        var required = MidnightVaultEngine.Profile.CollectionTarget;
        var collections = MidnightVaultBonusRounds.CollectionTrackIds.Select(track =>
        {
            var count = Math.Min(before.SealCounts.GetValueOrDefault(track), required);
            var wager = before.SealWagerPoints.GetValueOrDefault(track);
            var average = count == 0 ? 0 : checked((long)Math.Round(wager / (decimal)count, MidpointRounding.AwayFromZero));
            return new MidnightVaultSealCollection(track, (int)count, average, required);
        }).ToArray();
        return new(before.Rounds.FirstOrDefault(), before.Rounds.Skip(1).ToArray(), before.SpecialPoints,
            before.Energy, collections, pending, balanceCents / 100m);
    }
}

internal sealed record MidnightVaultRoundProgress(MidnightVaultRoundBefore After, MidnightVaultBonusEvidence BonusEvidence,
    int CollectionFreeSpinsAwarded);

internal sealed record MidnightVaultPreparedRound(Guid AdmissionId, string AccountId, MidnightVaultRoundRequest Request,
    MidnightVaultRoundBefore Before, MidnightVaultSpinOutcome Outcome, MidnightVaultRoundProgress Progress,
    string OutcomeSha256, long WagerCents, long PayoutCents, bool SpendsLegacy, DateTime StartedAtUtc,
    GameRequestObservation? Observation)
{
    public bool ImportedLegacyState { get; init; }
}

public sealed record MidnightVaultRoundResult(MidnightVaultRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
{
    internal MidnightVaultPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

internal sealed record MidnightVaultStoredRound(MidnightVaultPreparedRound Round, MidnightVaultRoundResult? Final);

public sealed class MidnightVaultRoundConflictException(string message, MidnightVaultRoundRequest? pendingRequest = null)
    : InvalidOperationException(message)
{
    public MidnightVaultRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class MidnightVaultRoundPausedException() : InvalidOperationException("New Midnight Vault rounds are temporarily paused.");
public sealed class MidnightVaultRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class MidnightVaultFreeSpinsRequiredException() :
    InvalidOperationException("Finish the awarded Vault Breach spins before placing another cash bet.");
public sealed class MidnightVaultFreeSpinsUnavailableException() :
    InvalidOperationException("No Midnight Vault free games are available.");
public sealed class MidnightVaultFreeWagerLockedException(long wagerPoints) :
    InvalidOperationException("Vault Breach spins use the wager saved on the active round.")
{
    public long WagerPoints { get; } = wagerPoints;
}

public sealed class MidnightVaultInsufficientSpecialPointsException(int available, int required) :
    InvalidOperationException("There are insufficient special points for that boost.")
{
    public int Available { get; } = available;
    public int Required { get; } = required;
}
