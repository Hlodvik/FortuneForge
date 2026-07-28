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

            if (NextStatusUpdateFailure is { } failure)
            {
                NextStatusUpdateFailure = null;
                return Task.FromResult(
                    PaymentResult<StoredPaymentCheckout>.Failure(failure));
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
            if (status == "completed")
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
            return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(updated));
        }

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

        public Task<PaymentResult<StoredPaymentWithdrawal>> ProjectWithdrawalProviderStatusAsync(
            string providerId,
            string providerWithdrawalId,
            string status,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

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

        private static bool Matches(
            StoredPaymentCheckout existing,
            StoredPaymentCheckout proposed) =>
            string.Equals(existing.UserId, proposed.UserId, StringComparison.Ordinal) &&
            string.Equals(existing.IdempotencyKey, proposed.IdempotencyKey, StringComparison.Ordinal) &&
            string.Equals(existing.Market.Code, proposed.Market.Code, StringComparison.Ordinal) &&
            string.Equals(existing.Market.Currency, proposed.Market.Currency, StringComparison.Ordinal) &&
            string.Equals(existing.PaymentMethod.Id, proposed.PaymentMethod.Id, StringComparison.Ordinal) &&
            existing.Amount == proposed.Amount &&
            existing.Credits == proposed.Credits &&
            string.Equals(existing.Customer.Email, proposed.Customer.Email, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Customer.FirstName, proposed.Customer.FirstName, StringComparison.Ordinal) &&
            string.Equals(existing.Customer.LastName, proposed.Customer.LastName, StringComparison.Ordinal) &&
            string.Equals(existing.PayerBank.AccountHolder, proposed.PayerBank.AccountHolder, StringComparison.Ordinal) &&
            string.Equals(existing.PayerBank.BankName, proposed.PayerBank.BankName, StringComparison.Ordinal) &&
            string.Equals(existing.PayerBank.AccountNumber, proposed.PayerBank.AccountNumber, StringComparison.Ordinal) &&
            string.Equals(existing.PayerBank.BranchCode, proposed.PayerBank.BranchCode, StringComparison.Ordinal) &&
            string.Equals(existing.PayerBank.AccountType, proposed.PayerBank.AccountType, StringComparison.Ordinal);

        private static bool CanTransition(string current, string next) => current switch
        {
            "received" => next is "processing" or "completed" or "failed" or "expired",
            "processing" => next is "completed" or "failed" or "expired",
            _ => string.Equals(current, next, StringComparison.Ordinal)
        };

        private static string IdempotencyKey(string userId, string idempotencyKey) =>
            $"{userId}:{idempotencyKey}";

        private static string ProviderKey(string providerId, string providerCheckoutId) =>
            $"{providerId}:{providerCheckoutId}";

        private sealed record ProviderEventRecord(
            string EventType,
            PaymentProviderEventProcessingState State,
            int Attempts);
    }
}
