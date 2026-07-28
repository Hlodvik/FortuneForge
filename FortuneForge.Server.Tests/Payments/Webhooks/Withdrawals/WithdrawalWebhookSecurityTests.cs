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
}
