using FortuneForge.Games.Craps;
namespace FortuneForge.Server.Games.Craps;

internal sealed record PaidCrapsRound(Guid Id, string UserId, CrapsPassLineState State, PaidCrapsExtra[] Extras,
    long TotalWagerCents, long TotalReturnCents, DateTime StartedAtUtc, DateTime? CompletedAtUtc, long Revision,
    PaidCrapsAction[]? Actions = null, int SchemaVersion = 1);
internal sealed record PaidCrapsExtra(string Kind, decimal Stake, bool Resolved, bool Won, decimal? TotalReturn,
    string? WagerId = null, int? PlacedForRoll = null, int? ResolvedOnRoll = null, bool Cancelled = false);
internal sealed record PaidCrapsAction(string OperationId, string Kind, string? WagerKind, decimal? Stake, long Revision,
    long? ExpectedRevision, int? ExpectedRollNumber, long WalletBeforeCents, long WalletAfterCents);
public sealed class CrapsPaidException(string code, string message, int statusCode) : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
