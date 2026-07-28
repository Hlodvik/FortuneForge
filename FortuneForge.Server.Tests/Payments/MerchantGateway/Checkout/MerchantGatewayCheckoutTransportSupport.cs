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
    private sealed class QueueHttpMessageHandler(
        params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses =
            new(responses);

        public List<CapturedRequest> CapturedRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var idempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.Single()
                : string.Empty;
            CapturedRequests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                idempotencyKey,
                body));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued HTTP response was available.");
            }

            return await _responses.Dequeue()(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Uri,
        string IdempotencyKey,
        string Body);

    private sealed class BlockingInvoiceCreateHttpMessageHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _postStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<Guid> _releasePost =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PostStarted => _postStarted.Task;

        public List<CapturedRequest> CapturedRequests { get; } = [];

        public void ReleasePost(Guid remoteInvoiceId) =>
            _releasePost.TrySetResult(remoteInvoiceId);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var idempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.Single()
                : string.Empty;
            lock (CapturedRequests)
            {
                CapturedRequests.Add(new CapturedRequest(
                    request.Method,
                    request.RequestUri?.ToString() ?? string.Empty,
                    idempotencyKey,
                    body));
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.ToString().Contains("api/v1/pathway-configs", StringComparison.OrdinalIgnoreCase) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(Pathways("active-pathway-key"))
                };
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.ToString().Contains("api/v1/invoices?limit=", StringComparison.OrdinalIgnoreCase) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(Array.Empty<object>())
                };
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.ToString().Contains("api/v1/invoices", StringComparison.OrdinalIgnoreCase) == true)
            {
                _postStarted.TrySetResult();
                var remoteInvoiceId = await _releasePost.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(CreatedInvoice(remoteInvoiceId, "Pending"))
                };
            }

            throw new InvalidOperationException(
                $"Unexpected request in blocking handler: {request.Method} {request.RequestUri}");
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://gateway.test/")
        };
    }
}
