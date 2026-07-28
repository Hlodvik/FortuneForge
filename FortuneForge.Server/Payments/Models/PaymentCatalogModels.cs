namespace FortuneForge.Server.Payments.Models;

public sealed record PaymentCatalogResponse(
    string ProviderId,
    bool IsMock,
    bool MockSimulationEnabled,
    IReadOnlyList<PaymentMarketOption> Markets);

public sealed record PaymentMarketOption(
    string Code,
    string DisplayName,
    string Currency,
    string Locale,
    string AudienceLabel,
    string PaymentNotice,
    long MinimumAmount,
    long MaximumAmount,
    long CreditsPerCurrencyUnit,
    IReadOnlyList<long> SuggestedAmounts,
    IReadOnlyList<PaymentMethodOption> PaymentMethods);

public sealed record PaymentMethodOption(
    string Id,
    string Type,
    string DisplayName,
    string Description,
    string SettlementLabel);
