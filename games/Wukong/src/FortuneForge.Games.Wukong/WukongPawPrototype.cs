namespace FortuneForge.Games.Wukong;

/// <summary>Nonfinancial fixture evidence; it is not a paid entitlement or earning rule.</summary>
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

public static class WukongPawPrototype
{
    public static IReadOnlyList<IReadOnlyList<string>> Apply(
        IReadOnlyList<IReadOnlyList<string>> source, WukongPawPrototypeEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(evidence);
        if (!evidence.SyntheticOnly || evidence.CollectionRule != WukongPawPrototypeEvidence.RuleVersion ||
            string.IsNullOrWhiteSpace(evidence.SessionId) || string.IsNullOrWhiteSpace(evidence.AdmissionId) ||
            evidence.ForcedPawColumn is { } column &&
                (column < 0 || column >= source.Count || string.IsNullOrWhiteSpace(evidence.GrantId)))
            throw new ArgumentException("Invalid synthetic Wukong paw evidence.", nameof(evidence));
        var reels = source.Select(reel => reel.ToArray()).ToArray();
        if (reels.Length == 0 || reels.Any(reel => reel.Length == 0))
            throw new ArgumentException("A visible reel window is required.", nameof(source));
        if (evidence.InjectEarnSymbol)
        {
            var earnColumn = ((evidence.ForcedPawColumn ?? 0) + 1) % reels.Length;
            reels[earnColumn][^1] = WukongPawPrototypeEvidence.EarnSymbolId;
        }
        if (evidence.ForcedPawColumn is { } pawColumn) reels[pawColumn][0] = "PAW";
        return reels.Select(reel => (IReadOnlyList<string>)reel).ToArray();
    }
}
