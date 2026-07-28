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

}
