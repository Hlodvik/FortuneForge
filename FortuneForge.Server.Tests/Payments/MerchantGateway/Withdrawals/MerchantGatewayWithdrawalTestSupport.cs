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
    private static MerchantGatewayPaymentProvider CreateProvider(
        IPaymentStore store,
        QueueHttpMessageHandler handler) =>
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

    private static PaymentWithdrawalDraft CreateDraft(string withdrawalId)
    {
        var market = PaymentCatalog.Markets.First(candidate => candidate.Code == "ZA");
        var createdAtUtc = DateTime.UtcNow;
        return new PaymentWithdrawalDraft(
            "fortune-forge-user-123",
            withdrawalId,
            "withdrawal-idempotency-key-123",
            market,
            10,
            1_000,
            100,
            new PaymentCustomerDetails(
                "Test",
                "Customer",
                "test@example.com",
                "ABCDEFGH",
                "ABCDEFGH"),
            new WithdrawalBankDetails(
                "Test Customer",
                "Test Bank",
                "1234567890",
                "250655",
                "Cheque"),
            createdAtUtc);
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

    private static object CreatedWithdrawal(Guid id, string status) => new
    {
        id,
        ourNumber = 1000002,
        status,
        amount = 10,
        currency = "ZAR",
        feeAmount = 0,
        netAmount = 10,
        rowVersion = "row-version",
        idempotentReplay = true
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
