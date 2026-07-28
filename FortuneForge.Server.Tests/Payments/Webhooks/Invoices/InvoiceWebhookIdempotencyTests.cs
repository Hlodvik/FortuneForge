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
}
