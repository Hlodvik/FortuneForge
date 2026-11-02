using FortuneForge.Server.Risk;
using Xunit;

namespace FortuneForge.Server.Tests.Risk;

public sealed class PlayerRiskPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("possible", 1, false, false, false)]
    [InlineData("possible", 2, false, false, false)]
    [InlineData("possible", 3, false, false, true)]
    [InlineData("high", 1, false, true, false)]
    [InlineData("high", 2, false, true, true)]
    [InlineData("critical", 1, true, true, false)]
    public void Thresholds_apply_only_the_requested_restrictions(string severity, int count,
        bool account, bool withdrawals, bool game)
    {
        var state = PlayerRiskPolicy.Evaluate("alice", Enumerable.Range(0, count)
            .Select(index => Incident(index.ToString(), severity)), 4, Now);
        Assert.Equal(account, state.AccountRestricted);
        Assert.Equal(withdrawals, state.WithdrawalsBlocked);
        Assert.Equal(game, state.BlockedGameIds.Contains("blackjack"));
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(2, 1, false)]
    [InlineData(1, 3, false)]
    [InlineData(2, 2, true)]
    public void Cross_game_restriction_requires_two_high_in_each_of_two_games(int first, int second, bool restricted)
    {
        var incidents = Enumerable.Range(0, first).Select(index => Incident("a" + index, "high"))
            .Concat(Enumerable.Range(0, second).Select(index => Incident("b" + index, "high", "mines")));
        var state = PlayerRiskPolicy.Evaluate("alice", incidents, 1, Now);
        Assert.Equal(restricted, state.AccountRestricted);
        Assert.Equal("two-high-in-each-of-two-games", state.CrossGameRule);
    }

    [Fact]
    public void Duplicate_ids_other_players_and_cleared_decisions_do_not_add_strikes_but_upheld_does()
    {
        var first = Incident("one", "high");
        var cleared = Incident("two", "critical") with { Status = "resolved", ResolutionOutcome = "cleared" };
        var other = Incident("three", "critical") with { UserId = "bob" };
        var upheld = Incident("four", "high") with { Status = "resolved", ResolutionOutcome = "upheld" };
        var state = PlayerRiskPolicy.Evaluate("alice", [first, first, cleared, other, upheld], 1, Now);
        Assert.False(state.AccountRestricted);
        Assert.True(state.WithdrawalsBlocked);
        Assert.Equal(new RiskLevelCounts(0, 2, 0), state.CountsByGame["blackjack"]);
        Assert.Equal(["blackjack"], state.BlockedGameIds);
    }

    internal static RiskIncident Incident(string id, string severity, string game = "blackjack") => new(
        id, "alice", game, "round-" + id, severity, "admin-review", "source-" + id,
        "explicit-attribution", "1", "Specific reviewed evidence ties this action to the account.",
        ["evidence-" + id], "admin", Now, "open", null, null, null, null);
}
