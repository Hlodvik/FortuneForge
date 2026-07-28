namespace FortuneForge.Server.Payments.Models;

public sealed record PaymentCatalogResponse(
    string ProviderId,
    bool IsMock,
    bool MockSimulationEnabled,
    IReadOnlyList<PaymentMarketOption> Markets);

public sealed record PaymentMarketOption(
    string Code,
    string DisplayName,
    string Currency,
    string Locale,
    string AudienceLabel,
    string PaymentNotice,
    long MinimumAmount,
    long MaximumAmount,
    long CreditsPerCurrencyUnit,
    IReadOnlyList<long> SuggestedAmounts,
    IReadOnlyList<PaymentMethodOption> PaymentMethods);

public sealed record PaymentMethodOption(
    string Id,
    string Type,
    string DisplayName,
    string Description,
    string SettlementLabel);

public sealed record CreatePaymentCheckoutRequest(
    string Market,
    string Currency,
    string PaymentMethodId,
    long Amount,
    string CustomerFirstName,
    string CustomerLastName,
    string CustomerEmail,
    string AccountHolder,
    string BankName,
    string AccountNumber,
    string BranchCode,
    string AccountType);

public sealed record MockPaymentStatusRequest(string Status);

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

public sealed record PaymentInvoiceListResponse(
    IReadOnlyList<PaymentCheckoutResponse> Invoices);

public sealed record PaymentCheckoutResponse(
    string CheckoutId,
    string ProviderCheckoutId,
    string InvoiceId,
    string UserId,
    string ProviderId,
    bool IsMock,
    string Market,
    string MarketName,
    string Currency,
    string Locale,
    string PaymentMethodId,
    string PaymentMethodName,
    string PaymentMethodType,
    long Amount,
    long AmountMinor,
    long Credits,
    string Status,
    DateTime StatusUpdatedAtUtc,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? ProcessingAtUtc,
    DateTime? CompletedAtUtc,
    long? CreditedBalance,
    PaymentCustomerDetails Customer,
    PaymentBankDetails PayerBank,
    BankTransferInstructions? BankTransfer,
    string Notice);

public sealed record PaymentCustomerDetails(
    string FirstName,
    string LastName,
    string Email,
    string CustomerReference,
    string BeneficiaryReference);

public sealed record BankTransferInstructions(
    string BankName,
    string AccountName,
    string AccountNumber,
    string BranchCode,
    string Reference,
    string Instructions);

public sealed record PaymentBankDetails(
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

internal sealed record PaymentCheckoutDraft(
    string UserId,
    string InvoiceId,
    string IdempotencyKey,
    PaymentMarketOption Market,
    PaymentMethodOption PaymentMethod,
    long Amount,
    long AmountMinor,
    long Credits,
    PaymentCustomerDetails Customer,
    PaymentBankDetails PayerBank,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc);

internal sealed record PaymentWithdrawalDraft(
    string UserId,
    string WithdrawalId,
    string IdempotencyKey,
    PaymentMarketOption Market,
    long Amount,
    long AmountMinor,
    long CreditsDebited,
    PaymentCustomerDetails Customer,
    WithdrawalBankDetails Bank,
    DateTime CreatedAtUtc);

internal sealed record StoredPaymentCheckout(
    string CheckoutId,
    string ProviderCheckoutId,
    string? ProviderPathwayKey,
    string InvoiceId,
    string UserId,
    string IdempotencyKey,
    string ProviderId,
    bool IsMock,
    PaymentMarketOption Market,
    PaymentMethodOption PaymentMethod,
    long Amount,
    long AmountMinor,
    long Credits,
    string Status,
    DateTime StatusUpdatedAtUtc,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? ProcessingAtUtc,
    DateTime? CompletedAtUtc,
    long? CreditedBalance,
    PaymentCustomerDetails Customer,
    PaymentBankDetails PayerBank,
    BankTransferInstructions? BankTransfer,
    string Notice,
    string ProviderSubmissionStatus = "idle",
    string? ProviderSubmissionLeaseId = null,
    DateTime? ProviderSubmissionLeaseUntilUtc = null,
    DateTime? NextProviderSubmissionAtUtc = null,
    DateTime? LastProviderSubmissionAtUtc = null,
    int ProviderSubmissionAttempt = 0,
    int? LastProviderSubmissionStatusCode = null)
{
    public PaymentCheckoutResponse ToResponse() => new(
        CheckoutId,
        ProviderCheckoutId,
        InvoiceId,
        UserId,
        ProviderId,
        IsMock,
        Market.Code,
        Market.DisplayName,
        Market.Currency,
        Market.Locale,
        PaymentMethod.Id,
        PaymentMethod.DisplayName,
        PaymentMethod.Type,
        Amount,
        AmountMinor,
        Credits,
        Status,
        StatusUpdatedAtUtc,
        CreatedAtUtc,
        ExpiresAtUtc,
        ProcessingAtUtc,
        CompletedAtUtc,
        CreditedBalance,
        Customer,
        PayerBank,
        BankTransfer,
        Notice);
}

internal sealed record StoredPaymentWithdrawal(
    string WithdrawalId,
    string ProviderWithdrawalId,
    string? ProviderPathwayKey,
    string UserId,
    string IdempotencyKey,
    string ProviderId,
    bool IsMock,
    PaymentMarketOption Market,
    long Amount,
    long AmountMinor,
    long CreditsDebited,
    string Status,
    DateTime StatusUpdatedAtUtc,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    PaymentCustomerDetails Customer,
    WithdrawalBankDetails Bank,
    string Notice)
{
    public PaymentWithdrawalResponse ToResponse() => new(
        WithdrawalId,
        ProviderWithdrawalId,
        UserId,
        ProviderId,
        IsMock,
        Market.Code,
        Market.DisplayName,
        Market.Currency,
        Market.Locale,
        Amount,
        AmountMinor,
        CreditsDebited,
        Status,
        StatusUpdatedAtUtc,
        CreatedAtUtc,
        CompletedAtUtc,
        Customer,
        Bank,
        Notice);
}

public enum PaymentError
{
    None,
    UnsupportedMarket,
    UnsupportedCurrency,
    UnsupportedPaymentMethod,
    InvalidAmount,
    InvalidIdempotencyKey,
    IdempotencyConflict,
    InvoiceConflict,
    CheckoutNotFound,
    InvalidMockStatus,
    InvalidStatusTransition,
    MockSimulationUnavailable,
    InsufficientCredits,
    ProviderAuthenticationFailed,
    ProviderRejected,
    PaymentPathwayUnavailable,
    ProviderUnavailable,
    InvalidCustomerDetails,
    InvalidBankDetails,
    InvalidWithdrawalDetails,
    AccountNotFound,
    AccountBalanceNotFound
}

public sealed record PaymentResult<T>(T? Value, PaymentError Error) where T : class
{
    public static PaymentResult<T> Success(T value) => new(value, PaymentError.None);

    public static PaymentResult<T> Failure(PaymentError error) => new(null, error);
}
