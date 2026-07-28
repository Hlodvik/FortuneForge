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

}
