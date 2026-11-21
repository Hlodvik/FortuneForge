namespace FortuneForge.Server.Accounts.Storage;

internal sealed record WalletCredit(
    string TransactionId,
    string UserId,
    long AmountCents,
    string LedgerType,
    string IdempotencyKey,
    IReadOnlyDictionary<string, object?> LedgerMetadata);

internal sealed record WalletCreditReceipt(
    string TransactionId,
    long BalanceAfterCents,
    DateTimeOffset CreditedAtUtc,
    bool WasAlreadyCredited);

internal interface IWalletCreditStore
{
    Task<WalletCreditReceipt> CreditAsync(
        WalletCredit credit, DateTimeOffset creditedAtUtc, CancellationToken cancellationToken);
}
