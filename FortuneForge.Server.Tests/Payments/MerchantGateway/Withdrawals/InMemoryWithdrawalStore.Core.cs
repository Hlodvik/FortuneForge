using System.Net;
using System.Net.Http.Json;
using FortuneForge.Server.Payments;
using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Providers;
using FortuneForge.Server.Payments.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Payments;

public sealed partial class MerchantGatewayPaymentProviderWithdrawalTests
{
    private sealed partial class InMemoryPaymentStore(long availableCredits) : IPaymentStore
    {
        private bool _refundRecorded;

        public StoredPaymentWithdrawal? Withdrawal { get; private set; }

        public long AvailableCredits { get; private set; } = availableCredits;

        public int ReservationDebitCount { get; private set; }

        public int RefundCount { get; private set; }

        public int UncertainMarkCount { get; private set; }

        public int ProviderUpdateCount { get; private set; }

        public Task<PaymentResult<StoredPaymentWithdrawal>> CreateWithdrawalReservationAsync(
            StoredPaymentWithdrawal withdrawal,
            CancellationToken cancellationToken)
        {
            if (Withdrawal is not null)
            {
                return Task.FromResult(Matches(Withdrawal, withdrawal)
                    ? PaymentResult<StoredPaymentWithdrawal>.Success(Withdrawal)
                    : PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.IdempotencyConflict));
            }

            if (AvailableCredits < withdrawal.CreditsDebited)
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.InsufficientCredits));
            }

            AvailableCredits -= withdrawal.CreditsDebited;
            ReservationDebitCount++;
            Withdrawal = withdrawal;
            return Task.FromResult(PaymentResult<StoredPaymentWithdrawal>.Success(withdrawal));
        }

        public Task<PaymentResult<StoredPaymentWithdrawal>> UpdateWithdrawalProviderAsync(
            string withdrawalId,
            string userId,
            string providerWithdrawalId,
            string status,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            if (Withdrawal is null ||
                Withdrawal.WithdrawalId != withdrawalId ||
                Withdrawal.UserId != userId)
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.CheckoutNotFound));
            }

            var normalized = WithdrawalStatusProjection.NormalizeProviderStatus(status);
            if (string.IsNullOrWhiteSpace(providerWithdrawalId) ||
                normalized is null ||
                !WithdrawalStatusProjection.CanApply(Withdrawal.Status, normalized))
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentWithdrawal>.Failure(
                        PaymentError.InvalidStatusTransition));
            }

            ProviderUpdateCount++;
            Withdrawal = Withdrawal with
            {
                ProviderWithdrawalId = providerWithdrawalId,
                Status = normalized,
                StatusUpdatedAtUtc = updatedAtUtc,
                Notice = WithdrawalStatusProjection.NoticeFor(normalized)
            };
            return Task.FromResult(PaymentResult<StoredPaymentWithdrawal>.Success(Withdrawal));
        }

        public Task<PaymentResult<StoredPaymentWithdrawal>> FailWithdrawalReservationAsync(
            string withdrawalId,
            string userId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            if (Withdrawal is null ||
                Withdrawal.WithdrawalId != withdrawalId ||
                Withdrawal.UserId != userId)
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.CheckoutNotFound));
            }

            if (!_refundRecorded)
            {
                _refundRecorded = true;
                AvailableCredits += Withdrawal.CreditsDebited;
                RefundCount++;
            }

            Withdrawal = Withdrawal with
            {
                Status = "failed",
                StatusUpdatedAtUtc = updatedAtUtc,
                Notice = "Withdrawal request failed before the payment provider accepted it. Reserved credits were returned."
            };
            return Task.FromResult(PaymentResult<StoredPaymentWithdrawal>.Success(Withdrawal));
        }

        public Task<PaymentResult<StoredPaymentWithdrawal>> MarkWithdrawalProviderSubmissionUncertainAsync(
            string withdrawalId,
            string userId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            if (Withdrawal is null ||
                Withdrawal.WithdrawalId != withdrawalId ||
                Withdrawal.UserId != userId)
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.CheckoutNotFound));
            }

            UncertainMarkCount++;
            Withdrawal = Withdrawal with
            {
                Status = "pending",
                StatusUpdatedAtUtc = updatedAtUtc,
                Notice = "Withdrawal request was submitted to the payment provider, but confirmation is pending. Reserved credits remain held."
            };
            return Task.FromResult(PaymentResult<StoredPaymentWithdrawal>.Success(Withdrawal));
        }

    }
}
