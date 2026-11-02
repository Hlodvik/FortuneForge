using System.Text.Json;
using FortuneForge.Server.Risk;
using Xunit;

namespace FortuneForge.Server.Tests.Risk;

public sealed class AccountReviewStatusTests
{
    [Fact]
    public void Account_review_shows_only_own_contributing_reasons_without_private_evidence_or_balances()
    {
        var critical = PlayerRiskPolicyTests.Incident("one", "critical");
        var incidental = PlayerRiskPolicyTests.Incident("two", "high", "mines");
        var foreign = PlayerRiskPolicyTests.Incident("three", "critical") with { UserId = "bob" };
        var all = new[] { critical, incidental, foreign };
        var state = PlayerRiskPolicy.Evaluate("alice", all, 1, DateTime.UtcNow);
        var result = AccountReviewController.SafeStatus(state, all);
        Assert.Equal("pending", result.ReviewStatus);
        Assert.Equal(critical.AttributionBasis, Assert.Single(result.Incidents).Reason);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("evidence-one", json); Assert.DoesNotContain("round-one", json);
        Assert.DoesNotContain("source-one", json); Assert.DoesNotContain("balances", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Upheld_is_confirmed_cleared_is_clear_and_high_only_restrictions_are_not_announced()
    {
        var upheld = PlayerRiskPolicyTests.Incident("one", "critical") with
        { Status = "resolved", ResolutionOutcome = "upheld", ResolutionReason = "The review confirmed the reported action.", ResolvedAtUtc = DateTime.UtcNow };
        var state = PlayerRiskPolicy.Evaluate("alice", [upheld], 1, DateTime.UtcNow);
        var result = AccountReviewController.SafeStatus(state, [upheld]);
        Assert.Equal("confirmed", result.ReviewStatus); Assert.Equal(upheld.ResolutionReason, Assert.Single(result.Incidents).ResolutionReason);
        var cleared = upheld with { ResolutionOutcome = "cleared" };
        state = PlayerRiskPolicy.Evaluate("alice", [cleared], 2, DateTime.UtcNow);
        result = AccountReviewController.SafeStatus(state, [cleared]);
        Assert.Equal("clear", result.ReviewStatus); Assert.Empty(result.Incidents); Assert.False(result.AccountRestricted);
        var high = PlayerRiskPolicyTests.Incident("two", "high");
        state = PlayerRiskPolicy.Evaluate("alice", [high], 3, DateTime.UtcNow);
        result = AccountReviewController.SafeStatus(state, [high]);
        Assert.Equal("clear", result.ReviewStatus); Assert.Empty(result.Incidents);
    }

    [Fact]
    public void Cross_game_explanation_lists_qualifying_high_cases_but_never_fabricates_confirmed_for_truncated_results()
    {
        var all = new[] { PlayerRiskPolicyTests.Incident("one", "high"), PlayerRiskPolicyTests.Incident("two", "high"),
            PlayerRiskPolicyTests.Incident("three", "high", "mines"), PlayerRiskPolicyTests.Incident("four", "high", "mines") };
        var state = PlayerRiskPolicy.Evaluate("alice", all, 1, DateTime.UtcNow);
        Assert.Equal(4, AccountReviewController.SafeStatus(state, all).Incidents.Count);
        var oneUpheld = all[0] with { Status = "resolved", ResolutionOutcome = "upheld" };
        Assert.Equal("restricted", AccountReviewController.SafeStatus(state, [oneUpheld]).ReviewStatus);
    }
}
