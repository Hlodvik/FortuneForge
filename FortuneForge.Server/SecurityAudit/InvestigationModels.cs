using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Server.SecurityAudit;

internal static class InvestigationDocuments
{
    internal const string Cases = "securityAuditInvestigations";
    internal const string Instances = "securityAuditInvestigationInstances";
    internal const string Evidence = "securityAuditInvestigationEvidence";
    internal const string InstanceHolds = "securityAuditInstanceHolds";
    internal const string PairFeatures = "securityAuditPairFeatures";
    internal const string PairReceipts = "securityAuditPairReceipts";
    internal const string PairAnalysisQueue = "securityAuditPairAnalysisQueue";
    internal const string StrategyFeatures = "securityAuditStrategyFeatures";
    internal const string StrategyReceipts = "securityAuditStrategyReceipts";
    internal const string Coverage = "securityAuditInvestigationCoverage";
    internal const string Events = "gameSecurityEvents";
    internal const string RequestIntents = "gameSecurityRequestIntents";
    internal const string RequestResults = "gameSecurityRequestResults";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string InstanceKey(string gameId, string instanceId) => Hash($"{gameId}\n{instanceId}");
    internal static string CaseId(string sourceKind, string episodeId) => Hash($"{sourceKind}\n{episodeId}");
    internal static string PairId(string gameId, string first, string second) => Hash($"{gameId}\n{string.Join('\n', new[] { first, second }.Order(StringComparer.Ordinal))}");
}

public sealed class SecurityAuditInvestigationOptions
{
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 25;
    public int PollSeconds { get; set; } = 60;
    public int CapturePageSize { get; set; } = 25;
    public int PairReviewMinimumSharedRounds { get; set; } = 20;
    public int PairReviewMinimumOpposedOutcomes { get; set; } = 10;
    // Enable only after the production ingress peer CIDRs and forwarded chain have been verified.
    // Requests must also carry a trusted-proxy-chain source before their network context affects priority.
    public bool EnableVerifiedNetworkPairPriority { get; set; }
}

public sealed record InvestigationSignal(string Code, string Category, string Message, string Confidence);
public sealed record InvestigationCase(string Id, string Kind, string SourceEpisodeId, string Status,
    int ReviewPriority, IReadOnlyList<string> TargetUserIds, IReadOnlyList<InvestigationSignal> Signals,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, int InstanceCount,
    string AttributionStatus = "unattributed", string Policy = "observational-no-player-strikes-v1",
    DateTime? DeleteAfterUtc = null, string? ResolutionReason = null);
public sealed record InvestigationPage(IReadOnlyList<InvestigationCase> Items, string? NextCursor);
public sealed record InvestigationEvidencePage(IReadOnlyList<Dictionary<string, object>> Items, string? NextCursor);
public sealed record InvestigationPairAssessment(int SharedRounds, int OpposedOutcomeRounds, int ReviewPriority,
    bool ReviewCandidate, string Coverage, IReadOnlyList<InvestigationSignal> Signals, bool NeedsDeeperAnalysis = false);
public sealed record InvestigationNetworkObservation(string UserId, string? IpAddress, DateTime ReceivedAtUtc,
    string? City = null, string? Provider = null, DateTime? GeoObservedAtUtc = null,
    double? AccuracyKilometers = null, string? Uncertainty = null, string? Country = null, string? Region = null,
    string? IpSource = null);

internal static class InvestigationAssessment
{
    // These are review-queue heuristics, not calibrated probabilities or cheating classifications.
    internal static InvestigationPairAssessment Assess(int sharedRounds, int opposedOutcomeRounds,
        IReadOnlyList<InvestigationNetworkObservation> observations, string firstUser, string secondUser,
        SecurityAuditInvestigationOptions options)
    {
        var signals = new List<InvestigationSignal>
        {
            new("baseline-pair-review", "behavior", "Co-play and opposed net outcomes are screened for every human pair, including pairs without any network link. Opposed net is not a measured transfer or proof of soft play.", "uncalibrated")
        };
        var priority = 0;
        if (!options.EnableVerifiedNetworkPairPriority)
            signals.Add(new("network-priority-disabled", "coverage", "Network observations are retained for manual review but do not raise pair priority until trusted production ingress is verified and enabled.", "scope"));
        // A direct connection address can be an ingress proxy. Never promote it into a pair signal.
        var trusted = options.EnableVerifiedNetworkPairPriority
            ? observations.Where(value => value.IpSource == "trusted-proxy-chain").ToArray()
            : [];
        var first = trusted.Where(value => value.UserId == firstUser).ToArray();
        var second = trusted.Where(value => value.UserId == secondUser).ToArray();
        var sharedIpOverlap = first.Any(a => second.Any(b => !string.IsNullOrWhiteSpace(a.IpAddress) &&
            a.IpAddress == b.IpAddress && a.ReceivedAtUtc > DateTime.UnixEpoch && b.ReceivedAtUtc > DateTime.UnixEpoch &&
            Math.Abs((a.ReceivedAtUtc - b.ReceivedAtUtc).TotalMinutes) <= 5));
        if (sharedIpOverlap)
        {
            priority += 2;
            signals.Add(new("shared-ip-overlap", "network", "The accounts have requests from a shared address within five minutes in the preserved instance. This raises review priority; households, venues and carrier NAT are legitimate explanations.", "corroboration-only"));
        }
        var sameCityOverlap = first.Any(a => !string.IsNullOrWhiteSpace(a.City) && !string.IsNullOrWhiteSpace(a.Country) && !string.IsNullOrWhiteSpace(a.Region) &&
            second.Any(b => b.City == a.City && b.Region == a.Region && b.Country == a.Country));
        if (sameCityOverlap)
        {
            priority++;
            signals.Add(new("same-city", "geolocation", "A provider labels both observations with the same city. This is weak, approximate context; retain provider, lookup time and uncertainty in the private evidence.", "weak"));
        }
        signals.Add(new("network-not-exclusionary", "coverage", "Different addresses, VPNs, unknown geolocation or absent observations do not exclude collusion and do not disable baseline pair review.", "scope"));
        var sufficient = sharedRounds >= options.PairReviewMinimumSharedRounds;
        var eligible = sufficient || sharedIpOverlap || sameCityOverlap;
        // Ordinary opposing outcomes are expected in competitive games. They do not establish coordinated play or create a case.
        signals.Add(new("strategy-analysis-unavailable", "coverage", "Summary counts and network context select deeper analysis only. No calibrated strategy/hidden-information detector has run, and no behavioral case or player strike is created.", "limitation"));
        return new(sharedRounds, opposedOutcomeRounds, priority, false,
            eligible ? "needs-strategy-analysis" : "insufficient-shared-rounds", signals, eligible);
    }

    internal static bool OpposedNet(RoundAuditParticipant first, RoundAuditParticipant second) =>
        first.NetCents < 0 && second.NetCents > 0 || second.NetCents < 0 && first.NetCents > 0;

    internal static IReadOnlyDictionary<string, string> Versions(string payloadJson, string evidenceKind, string auditorVersion)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        { ["evidenceKind"] = evidenceKind, ["auditorVersion"] = auditorVersion };
        using var json = JsonDocument.Parse(payloadJson);
        Visit(json.RootElement, "", result, 0);
        return result;
    }
    private static void Visit(JsonElement element, string prefix, Dictionary<string, string> result, int depth)
    {
        if (depth > 3 || element.ValueKind != JsonValueKind.Object) return;
        foreach (var property in element.EnumerateObject())
        {
            var path = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
            if (property.Name.Contains("version", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("paytable", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("ruleId", StringComparison.OrdinalIgnoreCase))
                if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    result[path] = property.Value.ToString();
            if (property.Value.ValueKind == JsonValueKind.Object) Visit(property.Value, path, result, depth + 1);
        }
    }
}
