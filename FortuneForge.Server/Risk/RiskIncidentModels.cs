namespace FortuneForge.Server.Risk;

public static class RiskLevels
{
    public const string Possible = "possible";
    public const string High = "high";
    public const string Critical = "critical";
}

public sealed record RiskIncidentDraft(
    string UserId, string GameId, string RoundId, string Severity,
    string SourceKind, string SourceIncidentId, string RuleId, string RuleVersion,
    string AttributionBasis, IReadOnlyList<string> EvidenceIds, string CreatedBy);

public sealed record RiskIncident(
    string Id, string UserId, string GameId, string RoundId, string Severity,
    string SourceKind, string SourceIncidentId, string RuleId, string RuleVersion,
    string AttributionBasis, IReadOnlyList<string> EvidenceIds, string CreatedBy,
    DateTime CreatedAtUtc, string Status, DateTime? ResolvedAtUtc,
    string? ResolvedBy, string? ResolutionOutcome, string? ResolutionReason,
    IReadOnlyList<RiskIncidentReview>? Reviews = null);

public sealed record RiskIncidentReview(
    DateTime ReviewedAtUtc, string ReviewedBy, string? PreviousOutcome, string Outcome, string Reason,
    string? PreviousSeverity = null, string? Severity = null);

public sealed record RiskLevelCounts(int Possible, int High, int Critical);

public sealed record PlayerRiskState(
    string UserId, bool AccountRestricted, bool WithdrawalsBlocked,
    IReadOnlyList<string> BlockedGameIds, IReadOnlyDictionary<string, RiskLevelCounts> CountsByGame,
    long Version, DateTime UpdatedAtUtc, string PolicyVersion, string CrossGameRule);

public sealed record RiskIncidentMutation(RiskIncident Incident, PlayerRiskState State, bool Changed);

public interface IRiskStateReader
{
    Task<PlayerRiskState> GetStateAsync(string userId, CancellationToken cancellationToken);
}

public sealed class RiskIncidentConflictException(string message) : Exception(message);

public static class PlayerRiskPolicy
{
    public const string Version = "attributed-incidents-v2";
    public const string CrossGameEscalation = "two-high-in-each-of-two-games";

    // Only explicitly attributed incidents enter this policy. A generic game
    // integrity finding can be a server bug and is never itself a player strike.
    public static PlayerRiskState Evaluate(string userId, IEnumerable<RiskIncident> incidents,
        long version, DateTime nowUtc)
    {
        var counts = incidents.Where(incident => incident.UserId == userId && CountsForRestrictions(incident))
            .DistinctBy(incident => incident.Id).GroupBy(incident => incident.GameId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new RiskLevelCounts(
                group.Count(incident => incident.Severity == RiskLevels.Possible),
                group.Count(incident => incident.Severity == RiskLevels.High),
                group.Count(incident => incident.Severity == RiskLevels.Critical)), StringComparer.Ordinal);
        return EvaluateCounts(userId, counts, version, nowUtc);
    }

    internal static PlayerRiskState EvaluateCounts(string userId, IReadOnlyDictionary<string, RiskLevelCounts> counts,
        long version, DateTime nowUtc) => new(userId,
        counts.Values.Any(value => value.Critical > 0) || counts.Values.Count(value => value.High >= 2) >= 2,
        counts.Values.Any(value => value.High > 0 || value.Critical > 0),
        counts.Where(pair => pair.Value.High >= 2 || pair.Value.Possible >= 3).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray(),
        new Dictionary<string, RiskLevelCounts>(counts, StringComparer.Ordinal), version, nowUtc, Version, CrossGameEscalation);

    public static bool CountsForRestrictions(RiskIncident incident) =>
        incident.Status == "open" || incident.Status == "resolved" && incident.ResolutionOutcome == "upheld";
}
