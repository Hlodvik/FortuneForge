namespace FortuneForge.Server.Payments.Configuration;

public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    public string Provider { get; set; } = "mock";

    public bool MockSimulationEnabled { get; set; }

    public int CheckoutLifetimeMinutes { get; set; } = 30;

    public MerchantGatewayOptions MerchantGateway { get; set; } = new();
}

public sealed class MerchantGatewayOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public Dictionary<string, string> PathwayKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> WebhookSigningSecrets { get; set; } = [];

    public int WebhookToleranceSeconds { get; set; } = 300;

    public int ReconciliationIntervalSeconds { get; set; } = 30;

    public int ReconciliationBatchSize { get; set; } = 100;
}
