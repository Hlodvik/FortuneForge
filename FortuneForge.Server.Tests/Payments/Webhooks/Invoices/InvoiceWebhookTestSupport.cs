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

public sealed partial class PaymentWebhookServiceInvoiceTests
{
    private static PaymentWebhookService CreateService(
        InMemoryPaymentStore store,
        TestMerchantGatewayProvider reconciler) =>
        new(
            store,
            reconciler,
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
        Guid remoteInvoiceId,
        bool useValidSignature = true)
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
        var signature = useValidSignature
            ? CreateSignature(timestamp, eventId, body)
            : $"v1={new string('0', 64)}";

        return await service.HandleMerchantGatewayAsync(
            eventId.ToString("D"),
            eventType,
            timestamp.ToString(CultureInfo.InvariantCulture),
            signature,
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

    private static StoredPaymentCheckout CreateCheckout(
        string providerCheckoutId,
        string status)
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
            ProviderId,
            false,
            market,
            paymentMethod,
            10,
            1_000,
            100,
            status,
            createdAtUtc,
            createdAtUtc,
            createdAtUtc.AddMinutes(30),
            null,
            null,
            null,
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
}
