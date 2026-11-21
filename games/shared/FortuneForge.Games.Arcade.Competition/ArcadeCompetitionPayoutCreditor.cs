namespace FortuneForge.Server.Arcade.Competition;

public sealed record ArcadeCompetitionPayoutCreditResult(
    string TransactionId,
    long BalanceAfterCents,
    DateTimeOffset CreditedAtUtc,
    bool WasAlreadyCredited);

public interface IArcadeCompetitionPayoutCreditor
{
    Task<ArcadeCompetitionPayoutCreditResult> CreditAsync(
        ArcadeCompetitionIdentity competition,
        ArcadeCompetitionCreditInstruction instruction,
        DateTimeOffset creditedAtUtc,
        CancellationToken cancellationToken);
}
