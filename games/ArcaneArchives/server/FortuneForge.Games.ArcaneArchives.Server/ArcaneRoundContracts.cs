using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.ArcaneArchives.Server;

public sealed record ArcaneRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
{
    public static bool IsValidOperationId(string? id) => id is { Length: >= 16 and <= 128 } &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record ArcanePreparedRound(Guid AdmissionId, string AccountId, ArcaneRoundRequest Request,
    ArcaneFeatureAdmission Features, ClassicSlotSpinOutcome Outcome, string OutcomeSha256,
    long WagerCents, long PayoutCents, DateTime StartedAtUtc)
{
    public GameRequestObservation? Observation { get; init; }
    public int PityMissesBefore { get; init; }
    public long? FreeSpinWagerPoints { get; init; }
    public string? FreeSpinFeatureMode { get; init; }
    public string? AdmittedFreeSpinFeatureMode { get; init; }
    public int FreeSpinsRemaining { get; init; }
}

public sealed record ArcaneRoundResult(ArcanePreparedRound? Round, ArcaneFeatureSettlement? Features, GameWalletSettlementReceipt Wallet)
{
    public decimal SlotsCreditsBalance => Wallet.BalanceAfterCents / 100m;
}

public sealed record ArcaneRoundState(ArcaneTomeState Tome, ArcanePotionState Potions, ArcaneRoundRequest? PendingRequest,
    bool LegacyPending, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode);

public sealed class ArcaneRoundCooldownException(TimeSpan remaining)
    : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class ArcaneRoundConflictException(string message, ArcaneRoundRequest? pendingRequest = null)
    : InvalidOperationException(message)
{
    public ArcaneRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class ArcaneRoundStartPausedException()
    : InvalidOperationException("Starting new prepared Arcane Archives spins is paused.");

public sealed class ArcaneObsoleteRoundException()
    : InvalidOperationException("This saved Arcane Archives round uses an obsolete local format. Use a new isolated local account.");
