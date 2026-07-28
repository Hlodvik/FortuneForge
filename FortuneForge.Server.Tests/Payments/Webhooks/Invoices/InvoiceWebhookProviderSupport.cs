using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Payments;
using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Providers;
using FortuneForge.Server.Payments.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Payments;

public sealed partial class PaymentWebhookServiceInvoiceTests
{
    private sealed class TestMerchantGatewayProvider(InMemoryPaymentStore store)
        : IPaymentProvider, IPaymentReconciler
    {
        public string Id => ProviderId;

        public bool IsMock => false;

        public string RemoteStatus { get; init; } = "received";

        public bool ThrowOnNextReconcile { get; set; }

        public int ReconcileAttempts { get; private set; }

        public async Task<PaymentReconciliationStatus> ReconcileInvoiceAsync(
            string checkoutId,
            string expectedStatus,
            CancellationToken cancellationToken)
        {
            ReconcileAttempts++;
            if (ThrowOnNextReconcile)
            {
                ThrowOnNextReconcile = false;
                throw new InvalidOperationException("Synthetic invoice projection failure.");
            }

            var checkout = await store.FindByProviderCheckoutIdForAdminAsync(
                Id,
                checkoutId,
                cancellationToken);
            if (checkout is null)
            {
                return PaymentReconciliationStatus.Retryable;
            }

            var result = await store.UpdateStatusAsync(
                checkout.CheckoutId,
                checkout.UserId,
                RemoteStatus,
                DateTime.UtcNow,
                cancellationToken);
            return result.Value is not null
                ? PaymentReconciliationStatus.Applied
                : PaymentReconciliationStatus.Retryable;
        }

        public Task<int> ReconcilePendingAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<PaymentResult<StoredPaymentCheckout>> CreateCheckoutAsync(
            PaymentCheckoutDraft draft,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentWithdrawal>> CreateWithdrawalAsync(
            PaymentWithdrawalDraft draft,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StoredPaymentCheckout?> GetCheckoutAsync(
            string checkoutId,
            string userId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StoredPaymentCheckout?> GetInvoiceAsync(
            string invoiceId,
            string userId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StoredPaymentCheckout?> GetInvoiceForAdminAsync(
            string invoiceId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<StoredPaymentCheckout>> ListInvoicesAsync(
            string userId,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
