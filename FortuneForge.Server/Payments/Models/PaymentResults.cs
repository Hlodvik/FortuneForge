namespace FortuneForge.Server.Payments.Models;

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
