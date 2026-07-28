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
    private sealed partial class InMemoryPaymentStore : IPaymentStore
    {
        private readonly Dictionary<string, StoredPaymentCheckout> _checkouts =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _checkoutIdByProviderId =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ProviderEventRecord> _providerEvents =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _creditLedger =
            new(StringComparer.OrdinalIgnoreCase);

        public long SlotsCreditBalance { get; set; }

        public int CreditLedgerCount { get; private set; }

        public int RecordedEventCount => _providerEvents.Count;

        public int AppliedEventCount => _providerEvents.Values.Count(providerEvent =>
            providerEvent.State == PaymentProviderEventProcessingState.Applied);

        public void AddCheckout(StoredPaymentCheckout checkout)
        {
            _checkouts[checkout.CheckoutId] = checkout;
            if (!string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId))
            {
                _checkoutIdByProviderId[
                    ProviderKey(checkout.ProviderId, checkout.ProviderCheckoutId)] = checkout.CheckoutId;
            }
        }

        public StoredPaymentCheckout GetCheckout(string checkoutId) => _checkouts[checkoutId];

        public Task<PaymentResult<StoredPaymentCheckout>> UpdateCheckoutProviderAsync(
            string checkoutId,
            string userId,
            string providerCheckoutId,
            string status,
            BankTransferInstructions? bankTransfer,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            if (!_checkouts.TryGetValue(checkoutId, out var checkout) ||
                checkout.UserId != userId)
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.CheckoutNotFound));
            }

            var updated = checkout with
            {
                ProviderCheckoutId = providerCheckoutId,
                Status = status,
                StatusUpdatedAtUtc = updatedAtUtc,
                BankTransfer = bankTransfer ?? checkout.BankTransfer
            };
            _checkouts[checkoutId] = updated;
            _checkoutIdByProviderId[ProviderKey(updated.ProviderId, providerCheckoutId)] = checkoutId;
            return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(updated));
        }

        public Task<PaymentResult<StoredPaymentCheckout>> UpdateStatusAsync(
            string checkoutId,
            string userId,
            string status,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            if (!_checkouts.TryGetValue(checkoutId, out var checkout) ||
                checkout.UserId != userId)
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.CheckoutNotFound));
            }

            if (!checkout.Status.Equals(status, StringComparison.Ordinal) &&
                !CanTransition(checkout.Status, status))
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.InvalidStatusTransition));
            }

            var updated = checkout with
            {
                Status = status,
                StatusUpdatedAtUtc = updatedAtUtc
            };
            if (status == "processing")
            {
                updated = updated with { ProcessingAtUtc = updatedAtUtc };
            }
            else if (status == "completed")
            {
                if (_creditLedger.Add(checkout.CheckoutId))
                {
                    SlotsCreditBalance = checked(SlotsCreditBalance + checkout.Credits);
                    CreditLedgerCount++;
                }

                updated = updated with
                {
                    CompletedAtUtc = updatedAtUtc,
                    CreditedBalance = SlotsCreditBalance
                };
            }

            _checkouts[checkoutId] = updated;
            return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(updated));
        }

        public Task<StoredPaymentCheckout?> FindByProviderCheckoutIdForAdminAsync(
            string providerId,
            string providerCheckoutId,
            CancellationToken cancellationToken)
        {
            var checkout = _checkoutIdByProviderId.TryGetValue(
                ProviderKey(providerId, providerCheckoutId),
                out var checkoutId)
                ? _checkouts[checkoutId]
                : null;
            return Task.FromResult(checkout);
        }

        public Task<PaymentProviderEventProcessingLease> BeginProviderEventProcessingAsync(
            string providerId,
            string eventId,
            string eventType,
            DateTime occurredAtUtc,
            DateTime receivedAtUtc,
            CancellationToken cancellationToken)
        {
            var key = $"{providerId}:{eventId}";
            if (_providerEvents.TryGetValue(key, out var providerEvent))
            {
                if (!string.Equals(providerEvent.EventType, eventType, StringComparison.Ordinal))
                {
                    return Task.FromResult(new PaymentProviderEventProcessingLease(
                        PaymentProviderEventProcessingState.Conflict,
                        IsRetry: true));
                }

                if (providerEvent.State == PaymentProviderEventProcessingState.Applied)
                {
                    return Task.FromResult(new PaymentProviderEventProcessingLease(
                        PaymentProviderEventProcessingState.Applied,
                        IsRetry: true));
                }

                _providerEvents[key] = providerEvent with
                {
                    State = PaymentProviderEventProcessingState.Processing,
                    Attempts = providerEvent.Attempts + 1
                };
                return Task.FromResult(new PaymentProviderEventProcessingLease(
                    PaymentProviderEventProcessingState.Processing,
                    IsRetry: true));
            }

            _providerEvents[key] = new ProviderEventRecord(
                eventType,
                PaymentProviderEventProcessingState.Processing,
                Attempts: 1);
            return Task.FromResult(new PaymentProviderEventProcessingLease(
                PaymentProviderEventProcessingState.Processing,
                IsRetry: false));
        }

        public Task MarkProviderEventAppliedAsync(
            string providerId,
            string eventId,
            DateTime appliedAtUtc,
            CancellationToken cancellationToken)
        {
            var key = $"{providerId}:{eventId}";
            if (_providerEvents.TryGetValue(key, out var providerEvent))
            {
                _providerEvents[key] = providerEvent with
                {
                    State = PaymentProviderEventProcessingState.Applied
                };
            }

            return Task.CompletedTask;
        }

    }
}
