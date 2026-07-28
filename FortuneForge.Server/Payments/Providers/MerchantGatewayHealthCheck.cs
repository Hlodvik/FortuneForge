using System.Net;
using FortuneForge.Server.Payments.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Payments.Providers;

internal sealed class MerchantGatewayHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<PaymentsOptions> options) : IHealthCheck
{
    private readonly PaymentsOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Provider.Equals("merchantgateway", StringComparison.OrdinalIgnoreCase))
        {
            return HealthCheckResult.Healthy("The mock payment provider is active.");
        }

        try
        {
            var gateway = _options.MerchantGateway;
            var client = httpClientFactory.CreateClient(MerchantGatewayPaymentProvider.HttpClientName);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "api/v1/auth-check");
            request.Headers.TryAddWithoutValidation(
                "x-merchant-api-key",
                gateway.ApiKey.Trim());
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("MerchantGateway accepted the configured merchant credential.");
            }

            return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? HealthCheckResult.Unhealthy("MerchantGateway rejected the configured merchant credential.")
                : HealthCheckResult.Degraded(
                    $"MerchantGateway returned HTTP {(int)response.StatusCode} during authentication check.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded("MerchantGateway authentication check timed out.");
        }
        catch (HttpRequestException exception)
        {
            return HealthCheckResult.Degraded(
                "MerchantGateway could not be reached.",
                exception);
        }
    }
}
