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
}
