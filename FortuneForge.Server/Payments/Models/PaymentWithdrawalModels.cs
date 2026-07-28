namespace FortuneForge.Server.Payments.Models;

public sealed record CreatePaymentWithdrawalRequest(
    string Market,
    string Currency,
    long Amount,
    string CustomerFirstName,
    string CustomerLastName,
    string CustomerEmail,
    string AccountHolder,
    string BankName,
    string AccountNumber,
    string BranchCode,
    string AccountType);

public sealed record PaymentWithdrawalResponse(
    string WithdrawalId,
    string ProviderWithdrawalId,
    string UserId,
    string ProviderId,
    bool IsMock,
    string Market,
    string MarketName,
    string Currency,
    string Locale,
    long Amount,
    long AmountMinor,
    long CreditsDebited,
    string Status,
    DateTime StatusUpdatedAtUtc,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    PaymentCustomerDetails Customer,
    WithdrawalBankDetails Bank,
    string Notice);

public sealed record WithdrawalBankDetails(
    string AccountHolder,
    string BankName,
    string AccountNumber,
    string BranchCode,
    string AccountType);
