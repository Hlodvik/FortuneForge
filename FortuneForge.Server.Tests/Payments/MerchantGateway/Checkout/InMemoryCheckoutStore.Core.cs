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
    private sealed partial class InMemoryPaymentStore(long availableCredits) : IPaymentStore
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, StoredPaymentCheckout> _checkouts =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _checkoutIdByIdempotency =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _checkoutIdByInvoiceId =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _checkoutIdByProviderId =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ProviderEventRecord> _providerEvents =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _creditLedger =
            new(StringComparer.OrdinalIgnoreCase);

        public long AvailableCredits { get; private set; } = availableCredits;

        public PaymentError? NextStatusUpdateFailure { get; set; }

        public int CreditLedgerCount => _creditLedger.Count;

        public int RecordedEventCount => _providerEvents.Count;

        public int AppliedEventCount => _providerEvents.Values.Count(providerEvent =>
            providerEvent.State == PaymentProviderEventProcessingState.Applied);

        public int UncertainMarkCount { get; private set; }

        public int ProviderUpdateCount { get; private set; }

        public void AddCheckout(StoredPaymentCheckout checkout)
        {
            lock (_sync)
            {
                _checkouts[checkout.CheckoutId] = checkout;
                _checkoutIdByInvoiceId[checkout.InvoiceId] = checkout.CheckoutId;
                if (!string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId))
                {
                    _checkoutIdByProviderId[
                        ProviderKey(checkout.ProviderId, checkout.ProviderCheckoutId)] = checkout.CheckoutId;
                }
            }
        }

        public void AddCompletedCheckoutWithLedger(StoredPaymentCheckout checkout)
        {
            AddCheckout(checkout);
            _creditLedger.Add(checkout.CheckoutId);
        }

        public StoredPaymentCheckout GetCheckout(string checkoutId)
        {
            lock (_sync)
            {
                return _checkouts[checkoutId];
            }
        }

        public void MakeCheckoutProviderSubmissionDue(string checkoutId)
        {
            lock (_sync)
            {
                var checkout = _checkouts[checkoutId];
                _checkouts[checkoutId] = checkout with
                {
                    ProviderSubmissionLeaseId = null,
                    ProviderSubmissionLeaseUntilUtc = null,
                    NextProviderSubmissionAtUtc = DateTime.UtcNow.AddSeconds(-1)
                };
            }
        }

        public Task<PaymentResult<StoredPaymentCheckout>> CreateAsync(
            StoredPaymentCheckout checkout,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var idempotencyKey = IdempotencyKey(checkout.UserId, checkout.IdempotencyKey);
                if (_checkoutIdByIdempotency.TryGetValue(idempotencyKey, out var existingCheckoutId))
                {
                    var existing = _checkouts[existingCheckoutId];
                    return Task.FromResult(Matches(existing, checkout)
                        ? PaymentResult<StoredPaymentCheckout>.Success(existing)
                        : PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.IdempotencyConflict));
                }

                if (_checkoutIdByInvoiceId.ContainsKey(checkout.InvoiceId) ||
                    _checkouts.ContainsKey(checkout.CheckoutId))
                {
                    return Task.FromResult(
                        PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.InvoiceConflict));
                }

                _checkouts[checkout.CheckoutId] = checkout;
                _checkoutIdByIdempotency[idempotencyKey] = checkout.CheckoutId;
                _checkoutIdByInvoiceId[checkout.InvoiceId] = checkout.CheckoutId;
                return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(checkout));
            }
        }

        public Task<PaymentResult<StoredPaymentCheckout>> UpdateCheckoutProviderAsync(
            string checkoutId,
            string userId,
            string providerCheckoutId,
            string status,
            BankTransferInstructions? bankTransfer,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_checkouts.TryGetValue(checkoutId, out var checkout) ||
                    checkout.UserId != userId ||
                    string.IsNullOrWhiteSpace(providerCheckoutId))
                {
                    return Task.FromResult(
                        PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.CheckoutNotFound));
                }

                if (!string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId) &&
                    !checkout.ProviderCheckoutId.Equals(providerCheckoutId, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(
                        PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.InvalidStatusTransition));
                }

                if (!checkout.Status.Equals(status, StringComparison.Ordinal) &&
                    !CanTransition(checkout.Status, status))
                {
                    return Task.FromResult(
                        PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.InvalidStatusTransition));
                }

                if (string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId))
                {
                    ProviderUpdateCount++;
                }

                var updated = checkout with
                {
                    ProviderCheckoutId = providerCheckoutId,
                    Status = status,
                    StatusUpdatedAtUtc = updatedAtUtc,
                    BankTransfer = bankTransfer ?? checkout.BankTransfer,
                    ProviderSubmissionStatus = "bound",
                    ProviderSubmissionLeaseId = null,
                    ProviderSubmissionLeaseUntilUtc = null,
                    NextProviderSubmissionAtUtc = null
                };

                if (status == "processing")
                {
                    updated = updated with { ProcessingAtUtc = updatedAtUtc };
                }
                else if (status == "completed")
                {
                    if (_creditLedger.Add(checkout.CheckoutId))
                    {
                        AvailableCredits += checkout.Credits;
                    }

                    updated = updated with
                    {
                        CompletedAtUtc = updatedAtUtc,
                        CreditedBalance = AvailableCredits
                    };
                }

                _checkouts[checkoutId] = updated;
                _checkoutIdByProviderId[ProviderKey(updated.ProviderId, providerCheckoutId)] = checkoutId;
                return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(updated));
            }
        }

    }
}
