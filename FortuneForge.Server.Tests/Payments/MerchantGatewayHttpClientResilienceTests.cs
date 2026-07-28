using System.Net;
using System.Net.Http.Json;
using FortuneForge.Server.Payments;
using FortuneForge.Server.Payments.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace FortuneForge.Server.Tests.Payments;

public sealed class MerchantGatewayHttpClientResilienceTests
{
    [Fact]
    public async Task MerchantGatewayClientDoesNotRetryUnsafePostOnTransientServerError()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var serviceProvider = CreateServiceProvider(handler);
        var client = serviceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(MerchantGatewayPaymentProvider.HttpClientName);

        using var response = await client.PostAsJsonAsync(
            "api/v1/invoices",
            new { theirNumber = "FF-DEP-PIPELINE-1" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, handler.PostSendCount);
        Assert.Equal(1, handler.TotalSendCount);
    }

    [Fact]
    public async Task MerchantGatewayClientDoesNotRetryUnsafePostOnHandlerTimeout()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => Task.FromException<HttpResponseMessage>(
                new TaskCanceledException("Synthetic handler timeout.")),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var serviceProvider = CreateServiceProvider(handler);
        var client = serviceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(MerchantGatewayPaymentProvider.HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/withdrawals")
        {
            Content = JsonContent.Create(new { theirNumber = "FF-WD-PIPELINE-1" })
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendAsync(request));

        Assert.Equal(1, handler.PostSendCount);
        Assert.Equal(1, handler.TotalSendCount);
    }

    private static ServiceProvider CreateServiceProvider(HttpMessageHandler primaryHandler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
        });

        services.AddPayments(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:Provider"] = "merchantgateway",
                ["Payments:CheckoutLifetimeMinutes"] = "30",
                ["Payments:MerchantGateway:BaseUrl"] = "https://gateway.test/",
                ["Payments:MerchantGateway:ApiKey"] = "merchant-api-key-123456",
                ["Payments:MerchantGateway:WebhookSigningSecrets:0"] =
                    "fortune-forge-webhook-signing-secret-12345",
                ["Payments:MerchantGateway:WebhookToleranceSeconds"] = "300",
                ["Payments:MerchantGateway:ReconciliationIntervalSeconds"] = "30",
                ["Payments:MerchantGateway:ReconciliationBatchSize"] = "100"
            })
            .Build());
        services.AddHttpClient(MerchantGatewayPaymentProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => primaryHandler);

        return services.BuildServiceProvider();
    }

    private sealed class SequenceHttpMessageHandler(
        params Func<HttpRequestMessage, Task<HttpResponseMessage>>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _responses =
            new(responses);

        public int PostSendCount { get; private set; }

        public int TotalSendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            TotalSendCount++;
            if (request.Method == HttpMethod.Post)
            {
                PostSendCount++;
            }

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued HTTP response was available.");
            }

            return _responses.Dequeue()(request);
        }
    }
}
