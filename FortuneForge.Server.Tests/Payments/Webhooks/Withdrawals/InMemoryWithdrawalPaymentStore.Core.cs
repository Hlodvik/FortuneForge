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
    private sealed partial class InMemoryPaymentStore : IPaymentStore
    {
        private readonly Dictionary<string, StoredPaymentWithdrawal> _withdrawals =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ProviderEventRecord> _providerEvents =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _refundLedger = new(StringComparer.OrdinalIgnoreCase);

        public long SlotsCreditBalance { get; set; }

        public bool ThrowOnNextProjection { get; set; }

        public int ProjectionAttempts { get; private set; }

        public int InvalidTransitionCount { get; private set; }

        public int RefundCount { get; private set; }

        public int RecordedEventCount => _providerEvents.Count;

        public int AppliedEventCount => _providerEvents.Values.Count(providerEvent =>
            providerEvent.State == PaymentProviderEventProcessingState.Applied);

        public void AddWithdrawal(StoredPaymentWithdrawal withdrawal) =>
            _withdrawals[WithdrawalKey(withdrawal.ProviderId, withdrawal.ProviderWithdrawalId)] = withdrawal;

        public StoredPaymentWithdrawal GetWithdrawal(Guid providerWithdrawalId) =>
            _withdrawals[WithdrawalKey(ProviderId, providerWithdrawalId.ToString("N"))];

        public Task<PaymentResult<StoredPaymentWithdrawal>> ProjectWithdrawalProviderStatusAsync(
            string providerId,
            string providerWithdrawalId,
            string status,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            ProjectionAttempts++;
            if (ThrowOnNextProjection)
            {
                ThrowOnNextProjection = false;
                throw new InvalidOperationException("Synthetic projection failure.");
            }

            var key = WithdrawalKey(providerId, providerWithdrawalId);
            if (!_withdrawals.TryGetValue(key, out var withdrawal))
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.CheckoutNotFound));
            }

            var normalizedStatus = WithdrawalStatusProjection.NormalizeProviderStatus(status);
            if (normalizedStatus is null ||
                !string.Equals(withdrawal.ProviderId, providerId, StringComparison.Ordinal) ||
                !string.Equals(
                    withdrawal.ProviderWithdrawalId,
                    providerWithdrawalId,
                    StringComparison.OrdinalIgnoreCase))
            {
                InvalidTransitionCount++;
                return Task.FromResult(
                    PaymentResult<StoredPaymentWithdrawal>.Failure(
                        PaymentError.InvalidStatusTransition));
            }

            var isSameStatus = string.Equals(
                withdrawal.Status,
                normalizedStatus,
                StringComparison.Ordinal);
            if (isSameStatus &&
                !WithdrawalStatusProjection.IsNegativeTerminal(normalizedStatus))
            {
                return Task.FromResult(PaymentResult<StoredPaymentWithdrawal>.Success(withdrawal));
            }

            if (!isSameStatus &&
                !WithdrawalStatusProjection.CanApply(withdrawal.Status, normalizedStatus))
            {
                InvalidTransitionCount++;
                return Task.FromResult(
                    PaymentResult<StoredPaymentWithdrawal>.Failure(
                        PaymentError.InvalidStatusTransition));
            }

            var updated = withdrawal with
            {
                Status = normalizedStatus,
                StatusUpdatedAtUtc = updatedAtUtc,
                Notice = WithdrawalStatusProjection.NoticeFor(normalizedStatus)
            };
            if (normalizedStatus == "completed")
            {
                updated = updated with { CompletedAtUtc = updatedAtUtc };
            }
            else if (WithdrawalStatusProjection.IsNegativeTerminal(normalizedStatus) &&
                _refundLedger.Add(withdrawal.WithdrawalId))
            {
                SlotsCreditBalance = checked(SlotsCreditBalance + withdrawal.CreditsDebited);
                RefundCount++;
            }

            _withdrawals[key] = updated;
            return Task.FromResult(PaymentResult<StoredPaymentWithdrawal>.Success(updated));
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
