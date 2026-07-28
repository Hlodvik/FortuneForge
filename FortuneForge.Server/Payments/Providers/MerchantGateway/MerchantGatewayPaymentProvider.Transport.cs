using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Storage;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Payments.Providers;

internal sealed partial class MerchantGatewayPaymentProvider
{
    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("x-merchant-api-key", _options.ApiKey.Trim());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }

    private async Task<string> LogProviderRejectionAsync(
        HttpResponseMessage response,
        string transactionType,
        string localNumber,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning(
            "MerchantGateway rejected {TransactionType} {LocalNumber} with HTTP {StatusCode}: {ResponseBody}",
            transactionType,
            localNumber,
            (int)response.StatusCode,
            Truncate(body, 512));
        return body;
    }

    private void LogRefreshFailure(HttpStatusCode statusCode)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogError("MerchantGateway rejected the configured API credential while refreshing invoices.");
            return;
        }

        logger.LogWarning("MerchantGateway returned HTTP {StatusCode} while refreshing invoices.", (int)statusCode);
    }

    private async Task<PaymentResult<StoredPaymentWithdrawal>> MarkWithdrawalSubmissionUncertainAsync(
        StoredPaymentWithdrawal? withdrawal,
        CancellationToken cancellationToken)
    {
        if (withdrawal is null)
        {
            return PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.ProviderUnavailable);
        }

        var result = await paymentStore.MarkWithdrawalProviderSubmissionUncertainAsync(
            withdrawal.WithdrawalId,
            withdrawal.UserId,
            DateTime.UtcNow,
            cancellationToken);
        return result.Value is null
            ? result
            : PaymentResult<StoredPaymentWithdrawal>.Success(result.Value);
    }

    private async Task<PaymentResult<StoredPaymentCheckout>> MarkCheckoutSubmissionUncertainAsync(
        StoredPaymentCheckout checkout,
        string leaseId,
        HttpStatusCode? providerStatusCode,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var nextRetryAtUtc = nowUtc.Add(ComputeCheckoutSubmissionBackoff(checkout));
        var result = await paymentStore.MarkCheckoutProviderSubmissionUncertainAsync(
            checkout.CheckoutId,
            checkout.UserId,
            leaseId,
            nowUtc,
            nextRetryAtUtc,
            providerStatusCode is null ? null : (int)providerStatusCode.Value,
            cancellationToken);
        return result.Value is null
            ? result
            : PaymentResult<StoredPaymentCheckout>.Success(result.Value);
    }

    private static TimeSpan ComputeCheckoutSubmissionBackoff(StoredPaymentCheckout checkout)
    {
        var attempt = Math.Max(1, checkout.ProviderSubmissionAttempt);
        var exponent = Math.Min(attempt - 1, 8);
        var baseSeconds = Math.Min(
            CheckoutSubmissionMaximumBackoffSeconds,
            CheckoutSubmissionInitialBackoffSeconds * (1 << exponent));
        var jitterMilliseconds = RandomNumberGenerator.GetInt32(
            0,
            CheckoutSubmissionJitterMaximumMilliseconds + 1);
        return TimeSpan.FromSeconds(baseSeconds) + TimeSpan.FromMilliseconds(jitterMilliseconds);
    }
}
