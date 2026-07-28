using FortuneForge.Server.Payments.Models;

namespace FortuneForge.Server.Payments.Storage;

internal interface IPaymentStore
{
    Task<PaymentResult<StoredPaymentCheckout>> CreateAsync(
        StoredPaymentCheckout checkout,
        CancellationToken cancellationToken);

    Task<PaymentResult<StoredPaymentCheckout>> UpdateCheckoutProviderAsync(
        string checkoutId,
        string userId,
        string providerCheckoutId,
        string status,
        BankTransferInstructions? bankTransfer,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken);

    Task<PaymentCheckoutProviderSubmissionLease> TryBeginCheckoutProviderSubmissionAsync(
        string checkoutId,
        string userId,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<PaymentResult<StoredPaymentCheckout>> MarkCheckoutProviderSubmissionUncertainAsync(
        string checkoutId,
        string userId,
        string leaseId,
        DateTime updatedAtUtc,
        DateTime nextRetryAtUtc,
        int? providerStatusCode,
        CancellationToken cancellationToken);

    Task<PaymentResult<StoredPaymentWithdrawal>> CreateWithdrawalReservationAsync(
        StoredPaymentWithdrawal withdrawal,
        CancellationToken cancellationToken);

    Task<PaymentResult<StoredPaymentWithdrawal>> UpdateWithdrawalProviderAsync(
        string withdrawalId,
        string userId,
        string providerWithdrawalId,
        string status,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken);

    Task<PaymentResult<StoredPaymentWithdrawal>> FailWithdrawalReservationAsync(
        string withdrawalId,
        string userId,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken);

    Task<PaymentResult<StoredPaymentWithdrawal>> MarkWithdrawalProviderSubmissionUncertainAsync(
        string withdrawalId,
        string userId,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken);

    Task<PaymentResult<StoredPaymentWithdrawal>> ProjectWithdrawalProviderStatusAsync(
        string providerId,
        string providerWithdrawalId,
        string status,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken);

    Task<StoredPaymentCheckout?> FindByCheckoutIdAsync(
        string checkoutId,
        string userId,
        CancellationToken cancellationToken);

    Task<StoredPaymentCheckout?> FindByCheckoutIdForAdminAsync(
        string checkoutId,
        CancellationToken cancellationToken);

    Task<StoredPaymentCheckout?> FindByProviderCheckoutIdForAdminAsync(
        string providerId,
        string providerCheckoutId,
        CancellationToken cancellationToken);

    Task<StoredPaymentCheckout?> FindByInvoiceIdAsync(
        string invoiceId,
        string userId,
        CancellationToken cancellationToken);

    Task<StoredPaymentCheckout?> FindByInvoiceIdForAdminAsync(
        string invoiceId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredPaymentCheckout>> ListAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredPaymentCheckout>> ListPendingAsync(
        string providerId,
        int limit,
        CancellationToken cancellationToken);

    Task<PaymentResult<StoredPaymentCheckout>> UpdateStatusAsync(
        string checkoutId,
        string userId,
        string status,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken);

    Task<PaymentProviderEventProcessingLease> BeginProviderEventProcessingAsync(
        string providerId,
        string eventId,
        string eventType,
        DateTime occurredAtUtc,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken);

    Task MarkProviderEventAppliedAsync(
        string providerId,
        string eventId,
        DateTime appliedAtUtc,
        CancellationToken cancellationToken);
}

internal enum PaymentProviderEventProcessingState
{
    Processing,
    Applied,
    Conflict
}

internal sealed record PaymentProviderEventProcessingLease(
    PaymentProviderEventProcessingState State,
    bool IsRetry);

internal enum PaymentCheckoutProviderSubmissionLeaseState
{
    Acquired,
    NotDue,
    AlreadyBound,
    Terminal,
    NotFound
}

internal sealed record PaymentCheckoutProviderSubmissionLease(
    PaymentCheckoutProviderSubmissionLeaseState State,
    StoredPaymentCheckout? Checkout,
    string? LeaseId)
{
    public bool Acquired =>
        State == PaymentCheckoutProviderSubmissionLeaseState.Acquired &&
        Checkout is not null &&
        !string.IsNullOrWhiteSpace(LeaseId);
}
