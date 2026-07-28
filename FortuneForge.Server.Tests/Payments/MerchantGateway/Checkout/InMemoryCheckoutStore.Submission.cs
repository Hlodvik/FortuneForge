using System.Net;
using System.Net.Http.Json;
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

public sealed partial class MerchantGatewayPaymentProviderCheckoutTests
{
    private sealed partial class InMemoryPaymentStore
    {
        public Task<PaymentCheckoutProviderSubmissionLease> TryBeginCheckoutProviderSubmissionAsync(
            string checkoutId,
            string userId,
            DateTime nowUtc,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_checkouts.TryGetValue(checkoutId, out var checkout) ||
                    checkout.UserId != userId)
                {
                    return Task.FromResult(new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.NotFound,
                        null,
                        null));
                }

                if (!string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId))
                {
                    return Task.FromResult(new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.AlreadyBound,
                        checkout,
                        null));
                }

                if (checkout.Status is "completed" or "failed" or "expired")
                {
                    return Task.FromResult(new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.Terminal,
                        checkout,
                        null));
                }

                if (checkout.NextProviderSubmissionAtUtc is { } nextRetryAtUtc &&
                    nextRetryAtUtc > nowUtc)
                {
                    return Task.FromResult(new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.NotDue,
                        checkout,
                        null));
                }

                if (checkout.ProviderSubmissionLeaseUntilUtc is { } leaseUntilUtc &&
                    leaseUntilUtc > nowUtc &&
                    !string.IsNullOrWhiteSpace(checkout.ProviderSubmissionLeaseId))
                {
                    return Task.FromResult(new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.NotDue,
                        checkout,
                        null));
                }

                var leaseId = Guid.NewGuid().ToString("N");
                var updated = checkout with
                {
                    ProviderSubmissionStatus = "submitting",
                    ProviderSubmissionLeaseId = leaseId,
                    ProviderSubmissionLeaseUntilUtc = nowUtc.Add(leaseDuration),
                    LastProviderSubmissionAtUtc = nowUtc,
                    ProviderSubmissionAttempt = Math.Max(0, checkout.ProviderSubmissionAttempt) + 1
                };
                _checkouts[checkoutId] = updated;
                return Task.FromResult(new PaymentCheckoutProviderSubmissionLease(
                    PaymentCheckoutProviderSubmissionLeaseState.Acquired,
                    updated,
                    leaseId));
            }
        }

        public Task<PaymentResult<StoredPaymentCheckout>> MarkCheckoutProviderSubmissionUncertainAsync(
            string checkoutId,
            string userId,
            string leaseId,
            DateTime updatedAtUtc,
            DateTime nextRetryAtUtc,
            int? providerStatusCode,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_checkouts.TryGetValue(checkoutId, out var checkout) ||
                    checkout.UserId != userId)
                {
                    return Task.FromResult(
                        PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.CheckoutNotFound));
                }

                if (!string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId) ||
                    checkout.Status is "completed" or "failed" or "expired")
                {
                    return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(checkout));
                }

                if (!string.Equals(checkout.ProviderSubmissionLeaseId, leaseId, StringComparison.Ordinal))
                {
                    return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(checkout));
                }

                UncertainMarkCount++;
                var updated = checkout with
                {
                    Status = "received",
                    StatusUpdatedAtUtc = updatedAtUtc,
                    ProviderSubmissionStatus = "uncertain",
                    ProviderSubmissionLeaseId = null,
                    ProviderSubmissionLeaseUntilUtc = null,
                    NextProviderSubmissionAtUtc = nextRetryAtUtc,
                    LastProviderSubmissionStatusCode = providerStatusCode,
                    Notice = "Payment invoice was submitted to the payment provider, but confirmation is pending. The same invoice will be retried automatically."
                };
                _checkouts[checkoutId] = updated;
                return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(updated));
            }
        }

        public Task<StoredPaymentCheckout?> FindByCheckoutIdAsync(
            string checkoutId,
            string userId,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var checkout = _checkouts.GetValueOrDefault(checkoutId);
                if (checkout is null &&
                    _checkoutIdByProviderId.TryGetValue(ProviderKey("merchantgateway-api", checkoutId), out var localId))
                {
                    checkout = _checkouts[localId];
                }

                return Task.FromResult(
                    checkout is not null && checkout.UserId == userId ? checkout : null);
            }
        }

        public Task<StoredPaymentCheckout?> FindByCheckoutIdForAdminAsync(
            string checkoutId,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var checkout = _checkouts.GetValueOrDefault(checkoutId);
                if (checkout is null &&
                    _checkoutIdByProviderId.TryGetValue(ProviderKey("merchantgateway-api", checkoutId), out var localId))
                {
                    checkout = _checkouts[localId];
                }

                return Task.FromResult(checkout);
            }
        }

        public Task<StoredPaymentCheckout?> FindByProviderCheckoutIdForAdminAsync(
            string providerId,
            string providerCheckoutId,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var checkout = _checkoutIdByProviderId.TryGetValue(
                    ProviderKey(providerId, providerCheckoutId),
                    out var checkoutId)
                    ? _checkouts[checkoutId]
                    : null;
                return Task.FromResult(checkout);
            }
        }

        public Task<StoredPaymentCheckout?> FindByInvoiceIdAsync(
            string invoiceId,
            string userId,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var checkout = _checkoutIdByInvoiceId.TryGetValue(invoiceId, out var checkoutId)
                    ? _checkouts[checkoutId]
                    : null;
                return Task.FromResult(
                    checkout is not null && checkout.UserId == userId ? checkout : null);
            }
        }

        public Task<StoredPaymentCheckout?> FindByInvoiceIdForAdminAsync(
            string invoiceId,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var checkout = _checkoutIdByInvoiceId.TryGetValue(invoiceId, out var checkoutId)
                    ? _checkouts[checkoutId]
                    : null;
                return Task.FromResult(checkout);
            }
        }

        public Task<IReadOnlyList<StoredPaymentCheckout>> ListAsync(
            string userId,
            int limit,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                return Task.FromResult<IReadOnlyList<StoredPaymentCheckout>>(
                    _checkouts.Values
                        .Where(checkout => checkout.UserId == userId)
                        .Take(limit)
                        .ToArray());
            }
        }

        public Task<IReadOnlyList<StoredPaymentCheckout>> ListPendingAsync(
            string providerId,
            int limit,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                return Task.FromResult<IReadOnlyList<StoredPaymentCheckout>>(
                    _checkouts.Values
                        .Where(checkout =>
                            checkout.ProviderId == providerId &&
                            checkout.Status is "received" or "processing")
                        .Take(limit)
                        .ToArray());
            }
        }
    }
}
