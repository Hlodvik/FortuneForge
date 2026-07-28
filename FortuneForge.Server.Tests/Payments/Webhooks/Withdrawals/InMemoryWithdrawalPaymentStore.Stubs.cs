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
    private sealed partial class InMemoryPaymentStore
    {
        public Task<PaymentResult<StoredPaymentCheckout>> CreateAsync(
            StoredPaymentCheckout checkout,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentCheckout>> UpdateCheckoutProviderAsync(
            string checkoutId,
            string userId,
            string providerCheckoutId,
            string status,
            BankTransferInstructions? bankTransfer,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentCheckoutProviderSubmissionLease> TryBeginCheckoutProviderSubmissionAsync(
            string checkoutId,
            string userId,
            DateTime nowUtc,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentCheckout>> MarkCheckoutProviderSubmissionUncertainAsync(
            string checkoutId,
            string userId,
            string leaseId,
            DateTime updatedAtUtc,
            DateTime nextRetryAtUtc,
            int? providerStatusCode,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentWithdrawal>> CreateWithdrawalReservationAsync(
            StoredPaymentWithdrawal withdrawal,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentWithdrawal>> UpdateWithdrawalProviderAsync(
            string withdrawalId,
            string userId,
            string providerWithdrawalId,
            string status,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentWithdrawal>> FailWithdrawalReservationAsync(
            string withdrawalId,
            string userId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentWithdrawal>> MarkWithdrawalProviderSubmissionUncertainAsync(
            string withdrawalId,
            string userId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StoredPaymentCheckout?> FindByCheckoutIdAsync(
            string checkoutId,
            string userId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StoredPaymentCheckout?> FindByCheckoutIdForAdminAsync(
            string checkoutId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StoredPaymentCheckout?> FindByProviderCheckoutIdForAdminAsync(
            string providerId,
            string providerCheckoutId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StoredPaymentCheckout?> FindByInvoiceIdAsync(
            string invoiceId,
            string userId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StoredPaymentCheckout?> FindByInvoiceIdForAdminAsync(
            string invoiceId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<StoredPaymentCheckout>> ListAsync(
            string userId,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<StoredPaymentCheckout>> ListPendingAsync(
            string providerId,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentCheckout>> UpdateStatusAsync(
            string checkoutId,
            string userId,
            string status,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        private static string WithdrawalKey(string providerId, string providerWithdrawalId) =>
            $"{providerId}:{providerWithdrawalId}";

        private sealed record ProviderEventRecord(
            string EventType,
            PaymentProviderEventProcessingState State,
            int Attempts);
    }
}
