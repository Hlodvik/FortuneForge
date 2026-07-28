using FortuneForge.Server.Payments.Models;

namespace FortuneForge.Server.Payments.Providers;

internal interface IPaymentProvider
{
    string Id { get; }

    bool IsMock { get; }

    Task<PaymentResult<StoredPaymentCheckout>> CreateCheckoutAsync(
        PaymentCheckoutDraft draft,
        CancellationToken cancellationToken);

    Task<PaymentResult<StoredPaymentWithdrawal>> CreateWithdrawalAsync(
        PaymentWithdrawalDraft draft,
        CancellationToken cancellationToken);

    Task<StoredPaymentCheckout?> GetCheckoutAsync(
        string checkoutId,
        string userId,
        CancellationToken cancellationToken);

    Task<StoredPaymentCheckout?> GetInvoiceAsync(
        string invoiceId,
        string userId,
        CancellationToken cancellationToken);

    Task<StoredPaymentCheckout?> GetInvoiceForAdminAsync(
        string invoiceId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredPaymentCheckout>> ListInvoicesAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken);
}

internal interface IMockPaymentSimulator
{
    Task<PaymentResult<StoredPaymentCheckout>> SimulateAsync(
        string checkoutId,
        string userId,
        string status,
        CancellationToken cancellationToken);
}

internal interface IPaymentReconciler
{
    Task<PaymentReconciliationStatus> ReconcileInvoiceAsync(
        string checkoutId,
        string expectedStatus,
        CancellationToken cancellationToken);

    Task<int> ReconcilePendingAsync(CancellationToken cancellationToken);
}

internal enum PaymentReconciliationStatus
{
    Applied,
    Retryable,
    TerminalNoOp
}
