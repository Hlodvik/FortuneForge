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

public sealed class PaymentWebhookServiceWithdrawalTests
{
    private const string ProviderId = "merchantgateway-api";
    private const string EventProviderId = "merchantgateway";
    private const string SigningSecret = "fortune-forge-webhook-signing-secret-12345";

    [Fact]
    public async Task CompletedWithdrawalWebhookProjectsCompletedWithoutRefund()
    {
        var remoteWithdrawalId = Guid.NewGuid();
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 900 };
        store.AddWithdrawal(CreateWithdrawal(remoteWithdrawalId, "processing"));
        var service = CreateService(store);

        var result = await SendWithdrawalWebhookAsync(
            service,
            Guid.NewGuid(),
            "withdrawal.completed",
            remoteWithdrawalId);

        Assert.Equal(PaymentWebhookStatus.Accepted, result);
        var withdrawal = store.GetWithdrawal(remoteWithdrawalId);
        Assert.Equal("completed", withdrawal.Status);
        Assert.NotNull(withdrawal.CompletedAtUtc);
        Assert.Equal(900, store.SlotsCreditBalance);
        Assert.Equal(0, store.RefundCount);
    }

    [Fact]
    public async Task RejectedWithdrawalWebhookRefundsReservedCreditsOnce()
    {
        var remoteWithdrawalId = Guid.NewGuid();
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 900 };
        store.AddWithdrawal(CreateWithdrawal(remoteWithdrawalId, "processing", creditsDebited: 100));
        var service = CreateService(store);

        var result = await SendWithdrawalWebhookAsync(
            service,
            Guid.NewGuid(),
            "withdrawal.rejected",
            remoteWithdrawalId);

        Assert.Equal(PaymentWebhookStatus.Accepted, result);
        var withdrawal = store.GetWithdrawal(remoteWithdrawalId);
        Assert.Equal("rejected", withdrawal.Status);
        Assert.Null(withdrawal.CompletedAtUtc);
        Assert.Equal(1_000, store.SlotsCreditBalance);
        Assert.Equal(1, store.RefundCount);
    }

    [Fact]
    public async Task DuplicateWithdrawalWebhookDoesNotProjectOrRefundTwice()
    {
        var eventId = Guid.NewGuid();
        var remoteWithdrawalId = Guid.NewGuid();
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 900 };
        store.AddWithdrawal(CreateWithdrawal(remoteWithdrawalId, "processing", creditsDebited: 100));
        var service = CreateService(store);

        var firstResult = await SendWithdrawalWebhookAsync(
            service,
            eventId,
            "withdrawal.rejected",
            remoteWithdrawalId);
        var secondResult = await SendWithdrawalWebhookAsync(
            service,
            eventId,
            "withdrawal.rejected",
            remoteWithdrawalId);

        Assert.Equal(PaymentWebhookStatus.Accepted, firstResult);
        Assert.Equal(PaymentWebhookStatus.Duplicate, secondResult);
        Assert.Equal("rejected", store.GetWithdrawal(remoteWithdrawalId).Status);
        Assert.Equal(1_000, store.SlotsCreditBalance);
        Assert.Equal(1, store.ProjectionAttempts);
        Assert.Equal(1, store.RefundCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(1, store.AppliedEventCount);
    }

    [Fact]
    public async Task ProjectionExceptionLeavesEventRetryableThenRetryApplies()
    {
        var eventId = Guid.NewGuid();
        var remoteWithdrawalId = Guid.NewGuid();
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 900 };
        store.AddWithdrawal(CreateWithdrawal(remoteWithdrawalId, "processing"));
        store.ThrowOnNextProjection = true;
        var service = CreateService(store);

        var firstResult = await SendWithdrawalWebhookAsync(
            service,
            eventId,
            "withdrawal.completed",
            remoteWithdrawalId);
        var secondResult = await SendWithdrawalWebhookAsync(
            service,
            eventId,
            "withdrawal.completed",
            remoteWithdrawalId);

        Assert.Equal(PaymentWebhookStatus.Retryable, firstResult);
        Assert.Equal(PaymentWebhookStatus.Accepted, secondResult);
        Assert.Equal("completed", store.GetWithdrawal(remoteWithdrawalId).Status);
        Assert.Equal(2, store.ProjectionAttempts);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(1, store.AppliedEventCount);
    }

    [Fact]
    public async Task CallbackBeforeProviderKeyIsRetryableUntilBindingExists()
    {
        var eventId = Guid.NewGuid();
        var remoteWithdrawalId = Guid.NewGuid();
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 900 };
        var service = CreateService(store);

        var firstResult = await SendWithdrawalWebhookAsync(
            service,
            eventId,
            "withdrawal.completed",
            remoteWithdrawalId);

        store.AddWithdrawal(CreateWithdrawal(remoteWithdrawalId, "processing"));
        var secondResult = await SendWithdrawalWebhookAsync(
            service,
            eventId,
            "withdrawal.completed",
            remoteWithdrawalId);

        Assert.Equal(PaymentWebhookStatus.Retryable, firstResult);
        Assert.Equal(PaymentWebhookStatus.Accepted, secondResult);
        Assert.Equal("completed", store.GetWithdrawal(remoteWithdrawalId).Status);
        Assert.Equal(2, store.ProjectionAttempts);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(1, store.AppliedEventCount);
    }

    [Fact]
    public async Task RepeatedTerminalRejectionWithNewEventDoesNotRefundTwice()
    {
        var remoteWithdrawalId = Guid.NewGuid();
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 900 };
        store.AddWithdrawal(CreateWithdrawal(remoteWithdrawalId, "processing", creditsDebited: 100));
        var service = CreateService(store);

        var firstResult = await SendWithdrawalWebhookAsync(
            service,
            Guid.NewGuid(),
            "withdrawal.rejected",
            remoteWithdrawalId);
        var secondResult = await SendWithdrawalWebhookAsync(
            service,
            Guid.NewGuid(),
            "withdrawal.rejected",
            remoteWithdrawalId);

        Assert.Equal(PaymentWebhookStatus.Accepted, firstResult);
        Assert.Equal(PaymentWebhookStatus.Accepted, secondResult);
        Assert.Equal("rejected", store.GetWithdrawal(remoteWithdrawalId).Status);
        Assert.Equal(1_000, store.SlotsCreditBalance);
        Assert.Equal(2, store.ProjectionAttempts);
        Assert.Equal(1, store.RefundCount);
        Assert.Equal(2, store.RecordedEventCount);
        Assert.Equal(2, store.AppliedEventCount);
    }

    [Fact]
    public async Task UnknownWithdrawalWebhookIsRetryableWithoutApplyingEvent()
    {
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 900 };
        var service = CreateService(store);

        var result = await SendWithdrawalWebhookAsync(
            service,
            Guid.NewGuid(),
            "withdrawal.completed",
            Guid.NewGuid());

        Assert.Equal(PaymentWebhookStatus.Retryable, result);
        Assert.Equal(1, store.ProjectionAttempts);
        Assert.Equal(0, store.RefundCount);
        Assert.Equal(900, store.SlotsCreditBalance);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(0, store.AppliedEventCount);
    }

    [Fact]
    public async Task UnsupportedEventTypeDoesNotRecordProviderEvent()
    {
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 900 };
        var service = CreateService(store);

        var result = await SendWithdrawalWebhookAsync(
            service,
            Guid.NewGuid(),
            "withdrawal.settled",
            Guid.NewGuid());

        Assert.Equal(PaymentWebhookStatus.Invalid, result);
        Assert.Equal(0, store.ProjectionAttempts);
        Assert.Equal(0, store.RecordedEventCount);
        Assert.Equal(0, store.AppliedEventCount);
    }

    [Fact]
    public async Task InvalidSignatureDoesNotRecordOrProjectEvent()
    {
        var remoteWithdrawalId = Guid.NewGuid();
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 900 };
        store.AddWithdrawal(CreateWithdrawal(remoteWithdrawalId, "processing"));
        var service = CreateService(store);

        var result = await SendWithdrawalWebhookAsync(
            service,
            Guid.NewGuid(),
            "withdrawal.completed",
            remoteWithdrawalId,
            useValidSignature: false);

        Assert.Equal(PaymentWebhookStatus.Unauthorized, result);
        Assert.Equal("processing", store.GetWithdrawal(remoteWithdrawalId).Status);
        Assert.Equal(0, store.ProjectionAttempts);
        Assert.Equal(0, store.RecordedEventCount);
        Assert.Equal(0, store.AppliedEventCount);
    }

    [Fact]
    public async Task OutOfOrderWithdrawalWebhookDoesNotRegressTerminalStatus()
    {
        var remoteWithdrawalId = Guid.NewGuid();
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 900 };
        store.AddWithdrawal(CreateWithdrawal(
            remoteWithdrawalId,
            "completed",
            completedAtUtc: DateTime.UtcNow.AddMinutes(-5)));
        var service = CreateService(store);

        var result = await SendWithdrawalWebhookAsync(
            service,
            Guid.NewGuid(),
            "withdrawal.processing",
            remoteWithdrawalId);

        Assert.Equal(PaymentWebhookStatus.Accepted, result);
        var withdrawal = store.GetWithdrawal(remoteWithdrawalId);
        Assert.Equal("completed", withdrawal.Status);
        Assert.NotNull(withdrawal.CompletedAtUtc);
        Assert.Equal(1, store.InvalidTransitionCount);
        Assert.Equal(0, store.RefundCount);
        Assert.Equal(900, store.SlotsCreditBalance);
    }

    private static PaymentWebhookService CreateService(InMemoryPaymentStore store) =>
        new(
            store,
            new TestMerchantGatewayProvider(),
            Options.Create(new PaymentsOptions
            {
                Provider = "merchantgateway",
                MerchantGateway = new MerchantGatewayOptions
                {
                    WebhookSigningSecrets = [SigningSecret],
                    WebhookToleranceSeconds = 300
                }
            }),
            NullLogger<PaymentWebhookService>.Instance);

    private static async Task<PaymentWebhookStatus> SendWithdrawalWebhookAsync(
        PaymentWebhookService service,
        Guid eventId,
        string eventType,
        Guid remoteWithdrawalId,
        bool useValidSignature = true)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            eventId,
            type = eventType,
            occurredAtUtc,
            data = new
            {
                publicId = remoteWithdrawalId
            }
        });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = useValidSignature
            ? CreateSignature(timestamp, eventId, body)
            : $"v1={new string('0', 64)}";

        return await service.HandleMerchantGatewayAsync(
            eventId.ToString("D"),
            eventType,
            timestamp.ToString(CultureInfo.InvariantCulture),
            signature,
            body,
            CancellationToken.None);
    }

    private static string CreateSignature(long timestamp, Guid eventId, byte[] body)
    {
        var prefix = Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{eventId:D}."));
        var input = new byte[prefix.Length + body.Length];
        prefix.CopyTo(input, 0);
        body.CopyTo(input.AsSpan(prefix.Length));
        return $"v1={Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(SigningSecret),
            input)).ToLowerInvariant()}";
    }

    private static StoredPaymentWithdrawal CreateWithdrawal(
        Guid remoteWithdrawalId,
        string status,
        long creditsDebited = 100,
        DateTime? completedAtUtc = null)
    {
        var market = PaymentCatalog.Markets.First(candidate => candidate.Code == "ZA");
        var createdAtUtc = DateTime.UtcNow.AddMinutes(-10);
        return new StoredPaymentWithdrawal(
            $"FF-WD-{createdAtUtc:yyyyMMddHHmmssfff}-TEST",
            remoteWithdrawalId.ToString("N"),
            "active-pathway-key",
            "fortune-forge-user-123",
            Guid.NewGuid().ToString("N"),
            ProviderId,
            false,
            market,
            10,
            1_000,
            creditsDebited,
            status,
            createdAtUtc,
            createdAtUtc,
            completedAtUtc,
            new PaymentCustomerDetails(
                "Test",
                "Customer",
                "test@example.com",
                "ABCD2345",
                "ABCD2345"),
            new WithdrawalBankDetails(
                "Test Customer",
                "Test Bank",
                "1234567890",
                "250655",
                "Cheque"),
            "Withdrawal request reserved locally.");
    }

    private sealed class InMemoryPaymentStore : IPaymentStore
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
