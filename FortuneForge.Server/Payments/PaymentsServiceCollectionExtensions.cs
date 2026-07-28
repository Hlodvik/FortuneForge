using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Providers;
using FortuneForge.Server.Payments.Storage;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Payments;

public static class PaymentsServiceCollectionExtensions
{
    public static IServiceCollection AddPayments(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<PaymentsOptions>()
            .Bind(configuration.GetSection(PaymentsOptions.SectionName))
            .Validate(
                options => options.Provider.Equals("mock", StringComparison.OrdinalIgnoreCase) ||
                           options.Provider.Equals("merchantgateway", StringComparison.OrdinalIgnoreCase),
                "Payments:Provider must be 'mock' or 'merchantgateway'.")
            .Validate(
                options => options.CheckoutLifetimeMinutes is >= 5 and <= 1_440,
                "Payments:CheckoutLifetimeMinutes must be between 5 and 1440.")
            .Validate(
                HasValidMerchantGatewayConfiguration,
                "MerchantGateway requires an HTTPS (or loopback HTTP) base URL, a server-side merchant API key, webhook signing secrets, and valid reconciliation limits.")
            .ValidateOnStart();

#pragma warning disable EXTEXP0001
        services.AddHttpClient(MerchantGatewayPaymentProvider.HttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<PaymentsOptions>>().Value;
            if (Uri.TryCreate(EnsureTrailingSlash(options.MerchantGateway.BaseUrl), UriKind.Absolute, out var baseUri))
            {
                client.BaseAddress = baseUri;
            }

            client.Timeout = TimeSpan.FromSeconds(15);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        .RemoveAllResilienceHandlers()
        .AddStandardResilienceHandler(static resilience =>
        {
            resilience.Retry.DisableForUnsafeHttpMethods();
        });
#pragma warning restore EXTEXP0001
        services.AddSingleton<IPaymentStore, FirestorePaymentStore>();
        services.AddSingleton<MockPaymentProvider>();
        services.AddSingleton<MerchantGatewayPaymentProvider>();
        services.AddHealthChecks()
            .AddCheck<MerchantGatewayHealthCheck>("merchantgateway-payment-provider");
        services.AddSingleton<IPaymentProvider>(serviceProvider =>
        {
            var paymentOptions = serviceProvider
                .GetRequiredService<IOptions<PaymentsOptions>>()
                .Value;
            return paymentOptions.Provider.ToLowerInvariant() switch
            {
                "mock" => serviceProvider.GetRequiredService<MockPaymentProvider>(),
                "merchantgateway" => serviceProvider.GetRequiredService<MerchantGatewayPaymentProvider>(),
                _ => throw new InvalidOperationException(
                    $"Payment provider '{paymentOptions.Provider}' is not registered.")
            };
        });
        services.AddSingleton(serviceProvider => new PaymentService(
            serviceProvider.GetRequiredService<IPaymentProvider>(),
            serviceProvider.GetRequiredService<IOptions<PaymentsOptions>>()));
        services.AddSingleton(serviceProvider => new PaymentWebhookService(
            serviceProvider.GetRequiredService<IPaymentStore>(),
            serviceProvider.GetRequiredService<IPaymentProvider>(),
            serviceProvider.GetRequiredService<IOptions<PaymentsOptions>>(),
            serviceProvider.GetRequiredService<ILogger<PaymentWebhookService>>()));
        services.AddHostedService<PaymentReconciliationWorker>();
        return services;
    }

    private static bool HasValidMerchantGatewayConfiguration(PaymentsOptions options)
    {
        if (!options.Provider.Equals("merchantgateway", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var gateway = options.MerchantGateway;
        if (!Uri.TryCreate(gateway.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps &&
            !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback)) ||
            string.IsNullOrWhiteSpace(gateway.ApiKey) ||
            gateway.ApiKey.Trim().Length < 16 ||
            gateway.WebhookSigningSecrets.Count == 0 ||
            gateway.WebhookSigningSecrets.Any(secret =>
                string.IsNullOrWhiteSpace(secret) || secret.Trim().Length < 32) ||
            gateway.WebhookToleranceSeconds is < 60 or > 900 ||
            gateway.ReconciliationIntervalSeconds is < 15 or > 3_600 ||
            gateway.ReconciliationBatchSize is < 1 or > 500)
        {
            return false;
        }

        return gateway.PathwayKeys.All(entry =>
            !string.IsNullOrWhiteSpace(entry.Key) &&
            !string.IsNullOrWhiteSpace(entry.Value) &&
            entry.Key.Length <= 20 &&
            entry.Value.Length <= 100);
    }

    private static string EnsureTrailingSlash(string value) =>
        string.IsNullOrWhiteSpace(value) ? value : $"{value.Trim().TrimEnd('/')}/";
}
