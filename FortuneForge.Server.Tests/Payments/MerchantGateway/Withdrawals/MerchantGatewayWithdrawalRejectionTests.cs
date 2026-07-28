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
    [Fact]
    public async Task FirstAttemptConflictIsUncertainAndDoesNotRefundWithoutNoCreateClassifier()
    {
        var store = new InMemoryPaymentStore(availableCredits: 1_000);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            RawResponse(
                HttpStatusCode.Conflict,
                "Idempotency-Key was already used with a different request"));
        var provider = CreateProvider(store, handler);

        var result = await provider.CreateWithdrawalAsync(
            CreateDraft("FF-WD-20260727013131000-EEEE"),
            CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.Equal("pending", result.Value.Status);
        Assert.Equal(900, store.AvailableCredits);
        Assert.Equal(1, store.ReservationDebitCount);
        Assert.Equal(0, store.RefundCount);
        Assert.Equal(1, store.UncertainMarkCount);
    }

    [Fact]
    public async Task InvalidSuccessResponsePreservesPendingReservationWithoutRefund()
    {
        var store = new InMemoryPaymentStore(availableCredits: 1_000);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            RawResponse(HttpStatusCode.OK, "{not valid json"));
        var provider = CreateProvider(store, handler);

        var result = await provider.CreateWithdrawalAsync(
            CreateDraft("FF-WD-20260727020202000-BBBB"),
            CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.Equal("pending", result.Value.Status);
        Assert.Equal(string.Empty, result.Value.ProviderWithdrawalId);
        Assert.Equal(900, store.AvailableCredits);
        Assert.Equal(1, store.ReservationDebitCount);
        Assert.Equal(0, store.RefundCount);
        Assert.Equal(1, store.UncertainMarkCount);
        Assert.Equal(0, store.ProviderUpdateCount);
    }

    [Fact]
    public async Task TransientHttpResponsePreservesPendingReservationWithoutRefund()
    {
        var store = new InMemoryPaymentStore(availableCredits: 1_000);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            RawResponse(HttpStatusCode.ServiceUnavailable, "gateway unavailable"));
        var provider = CreateProvider(store, handler);

        var result = await provider.CreateWithdrawalAsync(
            CreateDraft("FF-WD-20260727030303000-CCCC"),
            CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.Equal("pending", result.Value.Status);
        Assert.Equal(900, store.AvailableCredits);
        Assert.Equal(1, store.ReservationDebitCount);
        Assert.Equal(0, store.RefundCount);
        Assert.Equal(1, store.UncertainMarkCount);
    }

    [Fact]
    public async Task AuthoritativeRejectionRefundsOnceAndDoesNotReplayTerminalWithdrawal()
    {
        var store = new InMemoryPaymentStore(availableCredits: 1_000);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            RawResponse(HttpStatusCode.UnprocessableEntity, "withdrawal rejected"),
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")));
        var provider = CreateProvider(store, handler);
        var firstDraft = CreateDraft("FF-WD-20260727040404000-DDDD");
        var replayDraft = CreateDraft("FF-WD-20260727040404999-EEEE");

        var first = await provider.CreateWithdrawalAsync(firstDraft, CancellationToken.None);
        var replay = await provider.CreateWithdrawalAsync(replayDraft, CancellationToken.None);

        Assert.Null(first.Value);
        Assert.Equal(PaymentError.ProviderRejected, first.Error);
        Assert.Null(replay.Value);
        Assert.Equal(PaymentError.ProviderRejected, replay.Error);
        Assert.Equal("failed", store.Withdrawal!.Status);
        Assert.Equal(1_000, store.AvailableCredits);
        Assert.Equal(1, store.ReservationDebitCount);
        Assert.Equal(1, store.RefundCount);
        Assert.Equal(0, store.UncertainMarkCount);

        Assert.Single(handler.CapturedRequests, request => request.Method == HttpMethod.Post);
    }
}
