using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.GoldFoundry.Server;

public sealed record GoldFoundryRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
{
    public static bool IsValidOperationId(string? operationId) => operationId is { Length: >= 16 and <= 128 } &&
        operationId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record GoldFoundryRoundStateReply(long EnergyBalance, long CrucibleWageredCents, long CruciblePourValueCents,
    GoldFoundryRoundRequest? PendingOperation = null, decimal? SlotsCreditsBalance = null)
{
    public int FreeSpinsRemaining => 0;
    public long? FreeSpinWagerPoints => null;
    public int SpecialPointsBalance => 0;
    public IReadOnlyList<object> SealCollections => [];
    public string? FreeSpinFeatureMode => null;

    internal static GoldFoundryRoundStateReply From(GoldFoundryRoundBefore state, GoldFoundryRoundRequest? pending = null,
        long? balanceCents = null) => new(state.Energy, state.CrucibleWageredCents,
        GoldFoundryCrucibleRules.PourValueCents(state.CrucibleWageredCents), pending, balanceCents / 100m);
}

internal sealed record GoldFoundryPreparedRound(Guid AdmissionId, string AccountId, GoldFoundryRoundRequest Request,
    GoldFoundryRoundBefore Before, GoldFoundrySpinOutcome Outcome, GoldFoundryCrucibleSettlement Crucible, string OutcomeSha256,
    long WagerCents, long PayoutCents, DateTime StartedAtUtc, GameRequestObservation? Observation)
{
    internal GoldFoundryRoundBefore After => new(Crucible.EnergyBalance, Crucible.WageredCents, Outcome.ConsecutiveFiveMisses);
}

public sealed record GoldFoundryRoundResult(GoldFoundryRoundStateReply? Features, GameWalletSettlementReceipt Wallet)
{
    internal GoldFoundryPreparedRound? Round { get; init; }
    public long ChargedWagerCents { get; init; }
}

internal sealed record GoldFoundryStoredRound(GoldFoundryPreparedRound Round, GoldFoundryRoundResult? Final);

public sealed class GoldFoundryRoundConflictException(string message, GoldFoundryRoundRequest? pendingRequest = null)
    : InvalidOperationException(message)
{
    public GoldFoundryRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class GoldFoundryRoundPausedException() : InvalidOperationException("New Gold Foundry rounds are temporarily paused.");
public sealed class GoldFoundryRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}
