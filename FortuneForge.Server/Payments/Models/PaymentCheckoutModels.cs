namespace FortuneForge.Server.Payments.Models;

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
    decimal? CreditedBalance,
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
