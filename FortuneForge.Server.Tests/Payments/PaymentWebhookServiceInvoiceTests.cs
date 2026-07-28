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

public sealed class PaymentWebhookServiceInvoiceTests
{
    private const string ProviderId = "merchantgateway-api";
    private const string EventProviderId = "merchantgateway";
    private const string SigningSecret = "fortune-forge-webhook-signing-secret-12345";

    [Fact]
    public async Task UnknownInvoiceWebhookIsRetryableWithoutApplyingEvent()
    {
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 500 };
        var reconciler = new TestMerchantGatewayProvider(store);
        var service = CreateService(store, reconciler);

        var result = await SendInvoiceWebhookAsync(
            service,
            Guid.NewGuid(),
            "invoice.completed",
            Guid.NewGuid());

        Assert.Equal(PaymentWebhookStatus.Retryable, result);
        Assert.Equal(1, reconciler.ReconcileAttempts);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(0, store.AppliedEventCount);
        Assert.Equal(500, store.SlotsCreditBalance);
        Assert.Equal(0, store.CreditLedgerCount);
    }

    [Fact]
    public async Task CallbackBeforeInvoiceProviderBindingIsRetryableUntilBindingExists()
    {
        var remoteInvoiceId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 500 };
        var checkout = CreateCheckout(providerCheckoutId: string.Empty, status: "received");
        store.AddCheckout(checkout);
        var reconciler = new TestMerchantGatewayProvider(store)
        {
            RemoteStatus = "completed"
        };
        var service = CreateService(store, reconciler);

        var firstResult = await SendInvoiceWebhookAsync(
            service,
            eventId,
            "invoice.completed",
            remoteInvoiceId);

        await store.UpdateCheckoutProviderAsync(
            checkout.CheckoutId,
            checkout.UserId,
            remoteInvoiceId.ToString("N"),
            "received",
            checkout.BankTransfer,
            DateTime.UtcNow,
            CancellationToken.None);
        var secondResult = await SendInvoiceWebhookAsync(
            service,
            eventId,
            "invoice.completed",
            remoteInvoiceId);

        Assert.Equal(PaymentWebhookStatus.Retryable, firstResult);
        Assert.Equal(PaymentWebhookStatus.Accepted, secondResult);
        Assert.Equal(2, reconciler.ReconcileAttempts);
        Assert.Equal("completed", store.GetCheckout(checkout.CheckoutId).Status);
        Assert.Equal(600, store.SlotsCreditBalance);
        Assert.Equal(1, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(1, store.AppliedEventCount);
    }

    [Fact]
    public async Task ProjectionExceptionLeavesInvoiceEventRetryableThenRetryApplies()
    {
        var remoteInvoiceId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 500 };
        store.AddCheckout(CreateCheckout(remoteInvoiceId.ToString("N"), "received"));
        var reconciler = new TestMerchantGatewayProvider(store)
        {
            RemoteStatus = "completed",
            ThrowOnNextReconcile = true
        };
        var service = CreateService(store, reconciler);

        var firstResult = await SendInvoiceWebhookAsync(
            service,
            eventId,
            "invoice.completed",
            remoteInvoiceId);
        var secondResult = await SendInvoiceWebhookAsync(
            service,
            eventId,
            "invoice.completed",
            remoteInvoiceId);

        Assert.Equal(PaymentWebhookStatus.Retryable, firstResult);
        Assert.Equal(PaymentWebhookStatus.Accepted, secondResult);
        Assert.Equal(2, reconciler.ReconcileAttempts);
        Assert.Equal(600, store.SlotsCreditBalance);
        Assert.Equal(1, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(1, store.AppliedEventCount);
    }

    [Fact]
    public async Task DuplicateInvoiceCompletionDoesNotCreditTwice()
    {
        var remoteInvoiceId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 500 };
        var checkout = CreateCheckout(remoteInvoiceId.ToString("N"), "received");
        store.AddCheckout(checkout);
        var reconciler = new TestMerchantGatewayProvider(store)
        {
            RemoteStatus = "completed"
        };
        var service = CreateService(store, reconciler);

        var firstResult = await SendInvoiceWebhookAsync(
            service,
            eventId,
            "invoice.completed",
            remoteInvoiceId);
        var duplicateResult = await SendInvoiceWebhookAsync(
            service,
            eventId,
            "invoice.completed",
            remoteInvoiceId);
        var secondEventResult = await SendInvoiceWebhookAsync(
            service,
            Guid.NewGuid(),
            "invoice.completed",
            remoteInvoiceId);

        Assert.Equal(PaymentWebhookStatus.Accepted, firstResult);
        Assert.Equal(PaymentWebhookStatus.Duplicate, duplicateResult);
        Assert.Equal(PaymentWebhookStatus.Accepted, secondEventResult);
        Assert.Equal("completed", store.GetCheckout(checkout.CheckoutId).Status);
        Assert.Equal(600, store.SlotsCreditBalance);
        Assert.Equal(1, store.CreditLedgerCount);
        Assert.Equal(2, reconciler.ReconcileAttempts);
        Assert.Equal(2, store.RecordedEventCount);
        Assert.Equal(2, store.AppliedEventCount);
    }

    [Fact]
    public async Task InvalidSignatureDoesNotRecordOrReconcileInvoiceEvent()
    {
        var remoteInvoiceId = Guid.NewGuid();
        var store = new InMemoryPaymentStore { SlotsCreditBalance = 500 };
        store.AddCheckout(CreateCheckout(remoteInvoiceId.ToString("N"), "received"));
        var reconciler = new TestMerchantGatewayProvider(store)
        {
            RemoteStatus = "completed"
        };
        var service = CreateService(store, reconciler);

        var result = await SendInvoiceWebhookAsync(
            service,
            Guid.NewGuid(),
            "invoice.completed",
            remoteInvoiceId,
            useValidSignature: false);

        Assert.Equal(PaymentWebhookStatus.Unauthorized, result);
        Assert.Equal(0, reconciler.ReconcileAttempts);
        Assert.Equal(0, store.RecordedEventCount);
        Assert.Equal(0, store.AppliedEventCount);
        Assert.Equal(500, store.SlotsCreditBalance);
        Assert.Equal(0, store.CreditLedgerCount);
    }

    private static PaymentWebhookService CreateService(
        InMemoryPaymentStore store,
        TestMerchantGatewayProvider reconciler) =>
        new(
            store,
            reconciler,
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

    private static async Task<PaymentWebhookStatus> SendInvoiceWebhookAsync(
        PaymentWebhookService service,
        Guid eventId,
        string eventType,
        Guid remoteInvoiceId,
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
                publicId = remoteInvoiceId
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

    private static StoredPaymentCheckout CreateCheckout(
        string providerCheckoutId,
        string status)
    {
        var market = PaymentCatalog.Markets.First(candidate => candidate.Code == "ZA");
        var paymentMethod = market.PaymentMethods.First(candidate => candidate.Id == "regional-bank-transfer");
        var createdAtUtc = DateTime.UtcNow.AddMinutes(-10);
        return new StoredPaymentCheckout(
            Guid.NewGuid().ToString("N"),
            providerCheckoutId,
            "active-pathway-key",
            $"FFDEP{RandomNumberGenerator.GetInt32(10000, 99999)}",
            "fortune-forge-user-123",
            Guid.NewGuid().ToString("N"),
            ProviderId,
            false,
            market,
            paymentMethod,
            10,
            1_000,
            100,
            status,
            createdAtUtc,
            createdAtUtc,
            createdAtUtc.AddMinutes(30),
            null,
            null,
            null,
            new PaymentCustomerDetails(
                "Test",
                "Customer",
                "test@example.com",
                "ABCD2345",
                "ABCD2345"),
            new PaymentBankDetails(
                "Test Customer",
                "Test Bank",
                "1234567890",
                "250655",
                "Cheque"),
            new BankTransferInstructions(
                "Test Bank",
                "Test Account",
                "1234567890",
                "250655",
                "ABCD2345",
                "Transfer exactly 10 ZAR and use ABCD2345 as the payment reference."),
            "Payment confirmation is pending.");
    }

    private sealed class InMemoryPaymentStore : IPaymentStore
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

        public Task<PaymentResult<StoredPaymentCheckout>> CreateAsync(
            StoredPaymentCheckout checkout,
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

        public Task<PaymentResult<StoredPaymentWithdrawal>> ProjectWithdrawalProviderStatusAsync(
            string providerId,
            string providerWithdrawalId,
            string status,
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

        private static bool CanTransition(string current, string next) => current switch
        {
            "received" => next is "processing" or "completed" or "failed" or "expired",
            "processing" => next is "completed" or "failed" or "expired",
            _ => string.Equals(current, next, StringComparison.Ordinal)
        };

        private static string ProviderKey(string providerId, string providerCheckoutId) =>
            $"{providerId}:{providerCheckoutId}";

        private sealed record ProviderEventRecord(
            string EventType,
            PaymentProviderEventProcessingState State,
            int Attempts);
    }

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
