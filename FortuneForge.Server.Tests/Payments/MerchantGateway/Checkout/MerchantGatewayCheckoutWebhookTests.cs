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
    [Fact]
    public async Task CompletedInvoiceWebhookWithBalanceFailureStaysRetryableThenRetryCreditsOnce()
    {
        const long startingBalance = 500;
        var remoteInvoiceId = Guid.NewGuid();
        var store = new InMemoryPaymentStore(availableCredits: startingBalance)
        {
            NextStatusUpdateFailure = PaymentError.AccountBalanceNotFound
        };
        var checkout = CreateStoredCheckout(remoteInvoiceId.ToString("N"), "received");
        store.AddCheckout(checkout);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                checkout.InvoiceId,
                checkout.Customer.CustomerReference,
                "Completed")),
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                checkout.InvoiceId,
                checkout.Customer.CustomerReference,
                "Completed")));
        var provider = CreateProvider(store, handler);
        var service = CreateWebhookService(store, provider);
        var eventId = Guid.NewGuid();

        var first = await SendInvoiceWebhookAsync(
            service,
            eventId,
            "invoice.completed",
            remoteInvoiceId);
        var afterFirst = store.GetCheckout(checkout.CheckoutId);
        Assert.Equal(PaymentWebhookStatus.Retryable, first);
        Assert.Equal("received", afterFirst.Status);
        Assert.Equal(startingBalance, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(0, store.AppliedEventCount);

        var second = await SendInvoiceWebhookAsync(
            service,
            eventId,
            "invoice.completed",
            remoteInvoiceId);
        var afterSecond = store.GetCheckout(checkout.CheckoutId);

        Assert.Equal(PaymentWebhookStatus.Accepted, second);
        Assert.Equal("completed", afterSecond.Status);
        Assert.Equal(startingBalance + checkout.Credits, store.AvailableCredits);
        Assert.Equal(1, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(1, store.AppliedEventCount);
    }

    [Fact]
    public async Task InvoiceCompletedIdentityMismatchIsRetryableAndDoesNotCredit()
    {
        const long startingBalance = 500;
        var remoteInvoiceId = Guid.NewGuid();
        var store = new InMemoryPaymentStore(availableCredits: startingBalance);
        var checkout = CreateStoredCheckout(remoteInvoiceId.ToString("N"), "received");
        store.AddCheckout(checkout);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                "DIFFERENT-INVOICE",
                checkout.Customer.CustomerReference,
                "Completed")));
        var provider = CreateProvider(store, handler);
        var service = CreateWebhookService(store, provider);

        var result = await SendInvoiceWebhookAsync(
            service,
            Guid.NewGuid(),
            "invoice.completed",
            remoteInvoiceId);

        Assert.Equal(PaymentWebhookStatus.Retryable, result);
        Assert.Equal("received", store.GetCheckout(checkout.CheckoutId).Status);
        Assert.Equal(startingBalance, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(0, store.AppliedEventCount);
    }

    [Fact]
    public async Task InvoiceProcessingInvalidTransitionIsRetryableAndDoesNotApplyEvent()
    {
        const long startingBalance = 600;
        var remoteInvoiceId = Guid.NewGuid();
        var store = new InMemoryPaymentStore(availableCredits: startingBalance);
        var checkout = CreateStoredCheckout(
            remoteInvoiceId.ToString("N"),
            "completed",
            creditedBalance: startingBalance,
            completedAtUtc: DateTime.UtcNow.AddMinutes(-5));
        store.AddCompletedCheckoutWithLedger(checkout);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                checkout.InvoiceId,
                checkout.Customer.CustomerReference,
                "Processing")));
        var provider = CreateProvider(store, handler);
        var service = CreateWebhookService(store, provider);

        var result = await SendInvoiceWebhookAsync(
            service,
            Guid.NewGuid(),
            "invoice.processing",
            remoteInvoiceId);

        Assert.Equal(PaymentWebhookStatus.Retryable, result);
        Assert.Equal("completed", store.GetCheckout(checkout.CheckoutId).Status);
        Assert.Equal(startingBalance, store.AvailableCredits);
        Assert.Equal(1, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(0, store.AppliedEventCount);
    }

    [Fact]
    public async Task AlreadyCompletedInvoiceWebhookIsSafeNoOpAndDoesNotCreditTwice()
    {
        const long startingBalance = 600;
        var remoteInvoiceId = Guid.NewGuid();
        var store = new InMemoryPaymentStore(availableCredits: startingBalance);
        var checkout = CreateStoredCheckout(
            remoteInvoiceId.ToString("N"),
            "completed",
            creditedBalance: startingBalance,
            completedAtUtc: DateTime.UtcNow.AddMinutes(-5));
        store.AddCompletedCheckoutWithLedger(checkout);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                checkout.InvoiceId,
                checkout.Customer.CustomerReference,
                "Completed")));
        var provider = CreateProvider(store, handler);
        var service = CreateWebhookService(store, provider);

        var result = await SendInvoiceWebhookAsync(
            service,
            Guid.NewGuid(),
            "invoice.completed",
            remoteInvoiceId);

        Assert.Equal(PaymentWebhookStatus.Accepted, result);
        Assert.Equal("completed", store.GetCheckout(checkout.CheckoutId).Status);
        Assert.Equal(startingBalance, store.AvailableCredits);
        Assert.Equal(1, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(1, store.AppliedEventCount);
    }

    [Fact]
    public async Task CompletedInvoiceWithoutCreditedBalanceIsRetryableNotSafeNoOp()
    {
        const long startingBalance = 600;
        var remoteInvoiceId = Guid.NewGuid();
        var store = new InMemoryPaymentStore(availableCredits: startingBalance);
        var checkout = CreateStoredCheckout(
            remoteInvoiceId.ToString("N"),
            "completed",
            creditedBalance: null,
            completedAtUtc: DateTime.UtcNow.AddMinutes(-5));
        store.AddCheckout(checkout);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                checkout.InvoiceId,
                checkout.Customer.CustomerReference,
                "Completed")));
        var provider = CreateProvider(store, handler);
        var service = CreateWebhookService(store, provider);

        var result = await SendInvoiceWebhookAsync(
            service,
            Guid.NewGuid(),
            "invoice.completed",
            remoteInvoiceId);

        Assert.Equal(PaymentWebhookStatus.Retryable, result);
        Assert.Equal("completed", store.GetCheckout(checkout.CheckoutId).Status);
        Assert.Equal(startingBalance, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(0, store.AppliedEventCount);
    }

    [Fact]
    public async Task CompletedInvoiceWebhookAddsCalculatedCreditsAndOneLedger()
    {
        const long startingBalance = 500;
        var remoteInvoiceId = Guid.NewGuid();
        var store = new InMemoryPaymentStore(availableCredits: startingBalance);
        var checkout = CreateStoredCheckout(remoteInvoiceId.ToString("N"), "received");
        store.AddCheckout(checkout);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                checkout.InvoiceId,
                checkout.Customer.CustomerReference,
                "Completed")));
        var provider = CreateProvider(store, handler);
        var service = CreateWebhookService(store, provider);

        var result = await SendInvoiceWebhookAsync(
            service,
            Guid.NewGuid(),
            "invoice.completed",
            remoteInvoiceId);

        Assert.Equal(PaymentWebhookStatus.Accepted, result);
        var updated = store.GetCheckout(checkout.CheckoutId);
        Assert.Equal("completed", updated.Status);
        Assert.Equal(startingBalance + checkout.Credits, store.AvailableCredits);
        Assert.Equal(startingBalance + checkout.Credits, updated.CreditedBalance);
        Assert.Equal(1, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(1, store.AppliedEventCount);
    }
}
