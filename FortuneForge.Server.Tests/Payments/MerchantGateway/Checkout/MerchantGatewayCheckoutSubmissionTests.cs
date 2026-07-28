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
    public async Task ConcurrentUnboundInvoicePathsShareOneProviderSubmissionLease()
    {
        var store = new InMemoryPaymentStore(availableCredits: 500);
        var handler = new BlockingInvoiceCreateHttpMessageHandler();
        var provider = CreateProvider(store, handler);
        var draft = CreateDraft("FFDEP26001", "QRS789TU");

        var createTask = provider.CreateCheckoutAsync(draft, CancellationToken.None);
        var started = await Task.WhenAny(
            handler.PostStarted,
            Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(handler.PostStarted, started);

        var checkout = await store.FindByInvoiceIdForAdminAsync(
            draft.InvoiceId,
            CancellationToken.None);
        Assert.NotNull(checkout);
        Assert.Equal("submitting", checkout.ProviderSubmissionStatus);
        Assert.NotNull(checkout.ProviderSubmissionLeaseId);

        var retryTasks = new Task[]
        {
            provider.CreateCheckoutAsync(draft, CancellationToken.None),
            provider.GetInvoiceAsync(draft.InvoiceId, draft.UserId, CancellationToken.None),
            provider.ListInvoicesAsync(draft.UserId, 10, CancellationToken.None),
            provider.ReconcilePendingAsync(CancellationToken.None)
        };

        await Task.WhenAll(retryTasks);
        Assert.Single(handler.CapturedRequests, request => request.Method == HttpMethod.Post);
        Assert.Equal(500, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
        Assert.Equal(0, store.UncertainMarkCount);

        handler.ReleasePost(RemoteInvoiceId);
        var created = await createTask;

        Assert.NotNull(created.Value);
        Assert.Equal(RemoteInvoiceId.ToString("N"), created.Value.ProviderCheckoutId);
        Assert.Equal("bound", created.Value.ProviderSubmissionStatus);
        Assert.Null(created.Value.ProviderSubmissionLeaseId);
        Assert.Null(created.Value.NextProviderSubmissionAtUtc);
        Assert.Single(handler.CapturedRequests, request => request.Method == HttpMethod.Post);
        Assert.Equal(1, store.ProviderUpdateCount);
        Assert.Equal(500, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
    }

    [Fact]
    public async Task SubmissionPendingConflictSchedulesBackoffAndReplaysExactRequestOnlyWhenDue()
    {
        var store = new InMemoryPaymentStore(availableCredits: 500);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            RawResponse(
                HttpStatusCode.Conflict,
                "Idempotency-Key is already associated with a SubmissionPending invoice."),
            JsonResponse(HttpStatusCode.OK, Array.Empty<object>()),
            JsonResponse(HttpStatusCode.OK, CreatedInvoice(RemoteInvoiceId, "Pending")));
        var provider = CreateProvider(store, handler);
        var draft = CreateDraft("FFDEP27001", "UVWX3456");

        var first = await provider.CreateCheckoutAsync(draft, CancellationToken.None);
        Assert.NotNull(first.Value);
        var uncertain = store.GetCheckout(first.Value.CheckoutId);
        Assert.Equal("received", uncertain.Status);
        Assert.Equal("uncertain", uncertain.ProviderSubmissionStatus);
        Assert.Equal(409, uncertain.LastProviderSubmissionStatusCode);
        Assert.NotNull(uncertain.NextProviderSubmissionAtUtc);
        Assert.True(uncertain.NextProviderSubmissionAtUtc > uncertain.LastProviderSubmissionAtUtc);
        Assert.Equal(string.Empty, uncertain.ProviderCheckoutId);
        Assert.Equal(500, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);

        await provider.GetInvoiceAsync(draft.InvoiceId, draft.UserId, CancellationToken.None);
        await provider.ReconcilePendingAsync(CancellationToken.None);
        await provider.ListInvoicesAsync(draft.UserId, 10, CancellationToken.None);
        Assert.Single(handler.CapturedRequests, request => request.Method == HttpMethod.Post);

        store.MakeCheckoutProviderSubmissionDue(first.Value.CheckoutId);
        var reconciled = await provider.ReconcilePendingAsync(CancellationToken.None);
        var recovered = store.GetCheckout(first.Value.CheckoutId);

        Assert.Equal(1, reconciled);
        Assert.Equal(RemoteInvoiceId.ToString("N"), recovered.ProviderCheckoutId);
        Assert.Equal("bound", recovered.ProviderSubmissionStatus);
        Assert.Null(recovered.ProviderSubmissionLeaseId);
        Assert.Null(recovered.NextProviderSubmissionAtUtc);
        Assert.Equal(500, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
        Assert.Equal(1, store.UncertainMarkCount);
        Assert.Equal(1, store.ProviderUpdateCount);

        var posts = handler.CapturedRequests
            .Where(request => request.Method == HttpMethod.Post)
            .ToArray();
        Assert.Equal(2, posts.Length);
        Assert.Equal(posts[0].Body, posts[1].Body);
        Assert.All(posts, request =>
        {
            Assert.Equal(draft.IdempotencyKey, request.IdempotencyKey);
            Assert.Contains(draft.InvoiceId, request.Body, StringComparison.Ordinal);
            Assert.Contains(draft.Customer.CustomerReference, request.Body, StringComparison.Ordinal);
            Assert.Contains("\"pathwayKey\":\"active-pathway-key\"", request.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task InvalidSuccessResponseLeavesLocalInvoiceRetryableWithoutCrediting()
    {
        var store = new InMemoryPaymentStore(availableCredits: 500);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            RawResponse(HttpStatusCode.OK, "{not valid json"));
        var provider = CreateProvider(store, handler);

        var result = await provider.CreateCheckoutAsync(
            CreateDraft("FFDEP30001", "WXYZ6789"),
            CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.Equal("received", result.Value.Status);
        Assert.Equal(string.Empty, result.Value.ProviderCheckoutId);
        Assert.Equal(500, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
        Assert.Equal(1, store.UncertainMarkCount);
        Assert.Equal(0, store.ProviderUpdateCount);
    }
}
