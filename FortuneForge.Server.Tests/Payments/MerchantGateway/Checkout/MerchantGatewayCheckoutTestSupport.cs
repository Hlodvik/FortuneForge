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
    private static MerchantGatewayPaymentProvider CreateProvider(
        IPaymentStore store,
        HttpMessageHandler handler) =>
        new(
            store,
            new TestHttpClientFactory(handler),
            Options.Create(new PaymentsOptions
            {
                Provider = "merchantgateway",
                MerchantGateway = new MerchantGatewayOptions
                {
                    BaseUrl = "https://gateway.test/",
                    ApiKey = "merchant-api-key-123456",
                    PathwayKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ZA"] = "active-pathway-key"
                    }
                }
            }),
            NullLogger<MerchantGatewayPaymentProvider>.Instance);

    private static PaymentWebhookService CreateWebhookService(
        IPaymentStore store,
        MerchantGatewayPaymentProvider provider) =>
        new(
            store,
            provider,
            Options.Create(new PaymentsOptions
            {
                Provider = "merchantgateway",
                MerchantGateway = new MerchantGatewayOptions
                {
                    WebhookSigningSecrets = [SigningSecret],
                    WebhookToleranceSeconds = 300
                }
            }),
            NullLogger<PaymentWebhookService>.Instance);

    private static async Task<PaymentWebhookStatus> SendInvoiceWebhookAsync(
        PaymentWebhookService service,
        Guid eventId,
        string eventType,
        Guid remoteInvoiceId)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            eventId,
            type = eventType,
            occurredAtUtc,
            data = new
            {
                publicId = remoteInvoiceId
            }
        });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return await service.HandleMerchantGatewayAsync(
            eventId.ToString("D"),
            eventType,
            timestamp.ToString(CultureInfo.InvariantCulture),
            CreateSignature(timestamp, eventId, body),
            body,
            CancellationToken.None);
    }

    private static string CreateSignature(long timestamp, Guid eventId, byte[] body)
    {
        var prefix = Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{eventId:D}."));
        var input = new byte[prefix.Length + body.Length];
        prefix.CopyTo(input, 0);
        body.CopyTo(input.AsSpan(prefix.Length));
        return $"v1={Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(SigningSecret),
            input)).ToLowerInvariant()}";
    }

    private static PaymentCheckoutDraft CreateDraft(
        string invoiceId,
        string customerReference)
    {
        var market = PaymentCatalog.Markets.First(candidate => candidate.Code == "ZA");
        var paymentMethod = market.PaymentMethods.First(candidate => candidate.Id == "regional-bank-transfer");
        var createdAtUtc = DateTime.UtcNow;
        return new PaymentCheckoutDraft(
            "fortune-forge-user-123",
            invoiceId,
            "checkout-idempotency-key-123",
            market,
            paymentMethod,
            10,
            1_000,
            100,
            new PaymentCustomerDetails(
                "Test",
                "Customer",
                "test@example.com",
                customerReference,
                customerReference),
            new PaymentBankDetails(
                "Test Customer",
                "Test Bank",
                "1234567890",
                "250655",
                "Cheque"),
            createdAtUtc,
            createdAtUtc.AddMinutes(30));
    }

    private static StoredPaymentCheckout CreateStoredCheckout(
        string providerCheckoutId,
        string status,
        long? creditedBalance = null,
        DateTime? completedAtUtc = null)
    {
        var market = PaymentCatalog.Markets.First(candidate => candidate.Code == "ZA");
        var paymentMethod = market.PaymentMethods.First(candidate => candidate.Id == "regional-bank-transfer");
        var createdAtUtc = DateTime.UtcNow.AddMinutes(-10);
        return new StoredPaymentCheckout(
            Guid.NewGuid().ToString("N"),
            providerCheckoutId,
            "active-pathway-key",
            $"FFDEP{RandomNumberGenerator.GetInt32(10000, 99999)}",
            "fortune-forge-user-123",
            Guid.NewGuid().ToString("N"),
            "merchantgateway-api",
            false,
            market,
            paymentMethod,
            10,
            1_000,
            100,
            status,
            completedAtUtc ?? createdAtUtc,
            createdAtUtc,
            createdAtUtc.AddMinutes(30),
            status == "processing" ? createdAtUtc.AddMinutes(1) : null,
            completedAtUtc,
            creditedBalance,
            new PaymentCustomerDetails(
                "Test",
                "Customer",
                "test@example.com",
                "ABCD2345",
                "ABCD2345"),
            new PaymentBankDetails(
                "Test Customer",
                "Test Bank",
                "1234567890",
                "250655",
                "Cheque"),
            new BankTransferInstructions(
                "Test Bank",
                "Test Account",
                "1234567890",
                "250655",
                "ABCD2345",
                "Transfer exactly 10 ZAR and use ABCD2345 as the payment reference."),
            "Payment confirmation is pending.");
    }

    private static object[] Pathways(string key) =>
    [
        new
        {
            key,
            name = "Active ZA pathway",
            bank = "Test Bank",
            accountHolder = "Test Account",
            accountNumber = "1234567890",
            branchCode = "250655",
            accountType = "Cheque",
            invoiceRate = 0,
            withdrawalRate = 0
        }
    ];

    private static object CreatedInvoice(Guid id, string status) => new
    {
        id,
        ourNumber = 1000001,
        status,
        amount = 10,
        currency = "ZAR",
        feeAmount = 0,
        netAmount = 10,
        rowVersion = "row-version",
        idempotentReplay = true
    };

    private static object RemoteInvoice(
        Guid id,
        string theirNumber,
        string customerReference,
        string status) => new
    {
        id,
        ourNumber = 1000001,
        theirNumber,
        customerReference,
        beneficiaryReference = customerReference,
        amount = 10,
        currency = "ZAR",
        feeRate = 0,
        feeAmount = 0,
        netAmount = 10,
        status,
        createdAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        completedAtUtc = status.Equals("Completed", StringComparison.OrdinalIgnoreCase)
            ? DateTimeOffset.UtcNow
            : (DateTimeOffset?)null,
        rowVersion = "row-version"
    };

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> JsonResponse(
        HttpStatusCode statusCode,
        object body) =>
        (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = JsonContent.Create(body)
        });

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> RawResponse(
        HttpStatusCode statusCode,
        string body) =>
        (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body)
        });

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Throw(
        Exception exception) =>
        (_, _) => Task.FromException<HttpResponseMessage>(exception);
}
