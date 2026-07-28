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
    private static PaymentWebhookService CreateService(InMemoryPaymentStore store) =>
        new(
            store,
            new TestMerchantGatewayProvider(),
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

    private static async Task<PaymentWebhookStatus> SendWithdrawalWebhookAsync(
        PaymentWebhookService service,
        Guid eventId,
        string eventType,
        Guid remoteWithdrawalId,
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
                publicId = remoteWithdrawalId
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

    private static StoredPaymentWithdrawal CreateWithdrawal(
        Guid remoteWithdrawalId,
        string status,
        long creditsDebited = 100,
        DateTime? completedAtUtc = null)
    {
        var market = PaymentCatalog.Markets.First(candidate => candidate.Code == "ZA");
        var createdAtUtc = DateTime.UtcNow.AddMinutes(-10);
        return new StoredPaymentWithdrawal(
            $"FF-WD-{createdAtUtc:yyyyMMddHHmmssfff}-TEST",
            remoteWithdrawalId.ToString("N"),
            "active-pathway-key",
            "fortune-forge-user-123",
            Guid.NewGuid().ToString("N"),
            ProviderId,
            false,
            market,
            10,
            1_000,
            creditsDebited,
            status,
            createdAtUtc,
            createdAtUtc,
            completedAtUtc,
            new PaymentCustomerDetails(
                "Test",
                "Customer",
                "test@example.com",
                "ABCD2345",
                "ABCD2345"),
            new WithdrawalBankDetails(
                "Test Customer",
                "Test Bank",
                "1234567890",
                "250655",
                "Cheque"),
            "Withdrawal request reserved locally.");
    }
}
