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

public sealed partial class PaymentWebhookServiceWithdrawalTests
{
    private sealed class TestMerchantGatewayProvider : IPaymentProvider, IPaymentReconciler
    {
        public string Id => ProviderId;

        public bool IsMock => false;

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

        public Task<PaymentReconciliationStatus> ReconcileInvoiceAsync(
            string checkoutId,
            string expectedStatus,
            CancellationToken cancellationToken) =>
            Task.FromResult(PaymentReconciliationStatus.Retryable);

        public Task<int> ReconcilePendingAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
