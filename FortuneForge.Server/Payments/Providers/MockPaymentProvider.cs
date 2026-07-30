using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Storage;

namespace FortuneForge.Server.Payments.Providers;

internal sealed class MockPaymentProvider(IPaymentStore paymentStore)
    : IPaymentProvider, IMockPaymentSimulator
{
    public string Id => "mock-regional-bank-transfer";

    public bool IsMock => true;

    public Task<PaymentResult<StoredPaymentCheckout>> CreateCheckoutAsync(
        PaymentCheckoutDraft draft,
        CancellationToken cancellationToken)
    {
        var checkoutId = Guid.NewGuid().ToString("N");
        var reference = draft.Customer.CustomerReference;
        var checkout = new StoredPaymentCheckout(
            checkoutId,
            checkoutId,
            null,
            draft.InvoiceId,
            draft.UserId,
            draft.IdempotencyKey,
            Id,
            true,
            draft.Market,
            draft.PaymentMethod,
            draft.Amount,
            draft.AmountMinor,
            draft.Credits,
            "received",
            draft.CreatedAtUtc,
            draft.CreatedAtUtc,
            draft.ExpiresAtUtc,
            null,
            null,
            null,
            draft.Customer,
            draft.PayerBank,
            FakeInstructions(draft.Market.Code, reference),
            "Mock checkout only. Do not transfer real money. Development Rand is added only after this invoice reaches completed status.");
        return paymentStore.CreateAsync(checkout, cancellationToken);
    }

    public Task<PaymentResult<StoredPaymentWithdrawal>> CreateWithdrawalAsync(
        PaymentWithdrawalDraft draft,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var withdrawal = new StoredPaymentWithdrawal(
            draft.WithdrawalId,
            Guid.NewGuid().ToString("N"),
            null,
            draft.UserId,
            draft.IdempotencyKey,
            Id,
            true,
            draft.Market,
            draft.Amount,
            draft.AmountMinor,
            draft.CreditsDebited,
            "pending",
            now,
            draft.CreatedAtUtc,
            null,
            draft.Customer,
            draft.Bank,
            "Mock withdrawal only. Development Rand is reserved locally; no bank payout is sent.");
        return paymentStore.CreateWithdrawalReservationAsync(withdrawal, cancellationToken);
    }

    public Task<StoredPaymentCheckout?> GetCheckoutAsync(
        string checkoutId,
        string userId,
        CancellationToken cancellationToken) =>
        paymentStore.FindByCheckoutIdAsync(checkoutId, userId, cancellationToken);

    public Task<StoredPaymentCheckout?> GetInvoiceAsync(
        string invoiceId,
        string userId,
        CancellationToken cancellationToken) =>
        paymentStore.FindByInvoiceIdAsync(invoiceId, userId, cancellationToken);

    public Task<StoredPaymentCheckout?> GetInvoiceForAdminAsync(
        string invoiceId,
        CancellationToken cancellationToken) =>
        paymentStore.FindByInvoiceIdForAdminAsync(invoiceId, cancellationToken);

    public Task<IReadOnlyList<StoredPaymentCheckout>> ListInvoicesAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken) =>
        paymentStore.ListAsync(userId, limit, cancellationToken);

    public Task<PaymentResult<StoredPaymentCheckout>> SimulateAsync(
        string checkoutId,
        string userId,
        string status,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = status.Trim().ToLowerInvariant();
        if (normalizedStatus is not ("processing" or "completed" or "failed"))
        {
            return Task.FromResult(
                PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.InvalidMockStatus));
        }

        return paymentStore.UpdateStatusAsync(
            checkoutId,
            userId,
            normalizedStatus,
            DateTime.UtcNow,
            cancellationToken);
    }

    private static BankTransferInstructions FakeInstructions(string market, string reference) =>
        market switch
        {
            "LS" => new(
                "Fortune Forge Mock Lesotho Bank",
                "FORTUNE FORGE MOCK — DO NOT PAY",
                "0000000001",
                "000001",
                reference,
                "This is a fake Lesotho bank account for interface testing. Do not send money."),
            _ => new(
                "Fortune Forge Mock South Africa Bank",
                "FORTUNE FORGE MOCK — DO NOT PAY",
                "0000000000",
                "000000",
                reference,
                "This is a fake South African bank account for interface testing. Do not send money.")
        };

}
