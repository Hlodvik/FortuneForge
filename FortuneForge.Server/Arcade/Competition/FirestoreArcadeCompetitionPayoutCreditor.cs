using FortuneForge.Server.Accounts.Storage;

namespace FortuneForge.Server.Arcade.Competition;

internal sealed class FirestoreArcadeCompetitionPayoutCreditor(IWalletCreditStore wallet) : IArcadeCompetitionPayoutCreditor
{
    public async Task<ArcadeCompetitionPayoutCreditResult> CreditAsync(
        ArcadeCompetitionIdentity competition,
        ArcadeCompetitionCreditInstruction instruction,
        DateTimeOffset creditedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(competition);
        ArgumentNullException.ThrowIfNull(instruction);
        if (!ArcadeCompetitionPayoutPlan.HasValidIdempotencyKey(competition, instruction))
            throw new ArgumentException("The payout instruction idempotency key does not match its competition and credit data.", nameof(instruction));

        var credit = new WalletCredit(
            TransactionIdFor(instruction.IdempotencyKey),
            instruction.PlayerId,
            instruction.AmountCents,
            "arcade-competition-payout",
            instruction.IdempotencyKey,
            new Dictionary<string, object?>
            {
                ["competitionId"] = competition.DocumentId,
                ["gameId"] = competition.GameId,
                ["windowKind"] = competition.WindowKind.ToString().ToLowerInvariant(),
                ["startsAt"] = competition.StartsAtUtc,
                ["endsAt"] = competition.EndsAtUtc,
                ["payoutKind"] = instruction.Kind.ToString().ToLowerInvariant(),
                ["placement"] = (long?)instruction.Placement,
            });
        var receipt = await wallet.CreditAsync(credit, creditedAtUtc, cancellationToken);
        return new(receipt.TransactionId, receipt.BalanceAfterCents, receipt.CreditedAtUtc, receipt.WasAlreadyCredited);
    }

    internal static string TransactionIdFor(string idempotencyKey) => ArcadeCompetitionPayoutPlan.WalletTransactionId(idempotencyKey);
}
