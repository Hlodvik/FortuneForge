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
    public const string RuleVersion = FortuneForge.Games.Wukong.WukongPawPrototypeEvidence.RuleVersion;
    public const string EngineVersion = FortuneForge.Games.Wukong.WukongPawPrototypeEvidence.EngineVersion;
    public const string EarnSymbolId = FortuneForge.Games.Wukong.WukongPawPrototypeEvidence.EarnSymbolId;
    public string CollectionRule { get; init; } = RuleVersion;
    public bool SyntheticOnly { get; init; } = true;
}
