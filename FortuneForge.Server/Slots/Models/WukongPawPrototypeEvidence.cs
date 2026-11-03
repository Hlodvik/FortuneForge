namespace FortuneForge.Server.Slots.Models;

/// <summary>Nonfinancial fixture evidence, never a paid entitlement or an earning rule.</summary>
public sealed record WukongPawPrototypeEvidence(
    string SessionId,
    string AdmissionId,
    string? GrantId,
    long WagerPoints,
    int? ForcedPawColumn,
    bool InjectEarnSymbol)
{
    public const string RuleVersion = "wukong-every-paw-preview-v1";
    public const string EngineVersion = "shared-slots-engine-v1/wukong-every-paw-preview-v1";
    public const string EarnSymbolId = "PAW_EARN_SCROLL";
    public string CollectionRule { get; init; } = RuleVersion;
    public bool SyntheticOnly { get; init; } = true;
}
