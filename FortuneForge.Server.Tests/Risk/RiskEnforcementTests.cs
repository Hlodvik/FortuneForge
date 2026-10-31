using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.Risk;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Risk;

public sealed class RiskEnforcementTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    private sealed class Accounts(string? id = "alice") : IGameActivityAccountResolver
    { public Task<string?> UserIdAsync(HttpContext context, CancellationToken ct) => Task.FromResult(id); }
    private sealed class Risks(PlayerRiskState state) : IRiskStateReader
    {
        internal bool Fail;
        public Task<PlayerRiskState> GetStateAsync(string userId, CancellationToken cancellationToken) => Fail
            ? Task.FromException<PlayerRiskState>(new InvalidOperationException("storage failed")) : Task.FromResult(state);
    }
    private static PlayerRiskState State(bool account = false, bool withdrawals = false, params string[] games) =>
        new("alice", account, withdrawals, games, new Dictionary<string, RiskLevelCounts>(), 1, Now, "test", "test");
    private static DefaultHttpContext Context(string path, string method = "GET", string? json = null)
    {
        var context = new DefaultHttpContext(); context.Request.Path = path; context.Request.Method = method;
        context.Request.Headers.Authorization = "Bearer " + new string('a', 40);
        context.Response.Body = new MemoryStream();
        if (json is not null) { context.Request.ContentType = "application/json"; context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json)); }
        return context;
    }
    private static string Body(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
    private static async Task<bool> Invoke(HttpContext context, PlayerRiskState state, bool fail = false, string? user = "alice")
    {
        var reached = false;
        var middleware = new RiskEnforcementMiddleware(_ => { reached = true; return Task.CompletedTask; }, NullLogger<RiskEnforcementMiddleware>.Instance);
        await middleware.InvokeAsync(context, new Accounts(user), new Risks(state) { Fail = fail });
        return reached;
    }

    [Theory]
    [InlineData("/api/accounts/me", "GET")]
    [InlineData("/api/accounts/me/history", "GET")]
    [InlineData("/api/cards/texas-holdem/credit/matches/table", "GET")]
    [InlineData("/api/games/mines/rounds", "POST")]
    [InlineData("/api/payments/checkouts", "POST")]
    [InlineData("/api/accounts/me", "PATCH")]
    public async Task Account_restriction_blocks_balance_reads_history_and_actions_before_the_endpoint(string path, string method)
    {
        var context = Context(path, method);
        Assert.False(await Invoke(context, State(account: true)));
        Assert.Equal(403, context.Response.StatusCode); Assert.Contains("account_restricted", Body(context));
        Assert.Contains("/account/review", Body(context)); Assert.DoesNotContain("balance", Body(context));
    }

    [Theory]
    [InlineData("/api/accounts/login", "POST")]
    [InlineData("/api/accounts/logout", "POST")]
    [InlineData("/api/account/review", "GET")]
    [InlineData("/api/payments/webhooks/merchantgateway", "POST")]
    [InlineData("/assets/table.webp", "GET")]
    public async Task Login_review_logout_and_provider_settlement_remain_available(string path, string method)
    {
        Assert.True(await Invoke(Context(path, method), State(account: true), fail: true));
    }

    [Fact]
    public async Task High_risk_is_silent_until_withdrawal_attempt_and_does_not_disable_other_games()
    {
        Assert.True(await Invoke(Context("/api/accounts/me"), State(withdrawals: true)));
        Assert.True(await Invoke(Context("/api/games/mines/rounds", "POST"), State(withdrawals: true)));
        var withdrawal = Context("/api/payments/withdrawals", "POST");
        Assert.False(await Invoke(withdrawal, State(withdrawals: true)));
        Assert.Contains("withdrawals_restricted", Body(withdrawal));
    }

    [Fact]
    public async Task Restricted_games_include_every_game_endpoint_without_blocking_history_or_other_games()
    {
        foreach (var game in GameAnalyticsOutbox.GameIds.Where(game => !AcceptedGameActivityReader.SlotIds.Values.Contains(game)))
        {
            var path = game == "solitaire" ? "/api/solitaire/matches" : game is "blackjack" or "three-card-poker" or "texas-holdem" ? $"/api/cards/{game}/table" : $"/api/games/{game}/rounds";
            var context = Context(path, "POST");
            Assert.False(await Invoke(context, State(games: [game])));
            Assert.Contains("game_restricted", Body(context));
        }
        Assert.True(await Invoke(Context("/api/cards/history"), State(games: ["blackjack"])));
        Assert.True(await Invoke(Context("/api/games/mines/rounds", "POST"), State(games: ["blackjack"])));
    }

    [Fact]
    public async Task Slot_aliases_query_spoofing_and_duplicate_json_keys_cannot_bypass_game_restriction()
    {
        foreach (var (alias, game) in AcceptedGameActivityReader.SlotIds)
        {
            var context = Context("/api/slots/spins", "POST", JsonSerializer.Serialize(new { GameId = alias.ToUpperInvariant() }));
            Assert.False(await Invoke(context, State(games: [game])));
            Assert.Equal(0, context.Request.Body.Position);
        }
        var spoof = Context("/api/slots/spins", "POST", "{\"gameId\":\"rainbow-realm-fruits-v1\",\"GameId\":\"wukong-journey-v1\"}");
        spoof.Request.QueryString = new QueryString("?gameId=rainbow-realm-fruits-v1");
        Assert.False(await Invoke(spoof, State(games: ["wukong-journey-to-the-west"])));
        var read = Context("/api/slots/state", "GET", "{\"gameId\":\"rainbow-realm-fruits-v1\"}"); read.Request.QueryString = new QueryString("?gameId=wukong-journey-v1");
        Assert.False(await Invoke(read, State(games: ["wukong-journey-to-the-west"])));
    }

    [Fact]
    public async Task Unknown_or_failed_account_check_never_grants_authenticated_access()
    {
        var context = Context("/api/accounts/me");
        Assert.False(await Invoke(context, State(), fail: true)); Assert.Equal(503, context.Response.StatusCode);
        Assert.Contains("restriction_check_unavailable", Body(context));
        Assert.True(await Invoke(Context("/api/accounts/me"), State(), user: null)); // endpoint owns ordinary authentication
        var anonymous = Context("/api/catalog/visibility"); anonymous.Request.Headers.Remove("Authorization");
        Assert.True(await Invoke(anonymous, State(), fail: true)); // public data is independent of risk storage
    }

    [Fact]
    public void Restricted_login_response_has_authentication_but_no_account_balance_or_statistics()
    {
        var account = new AccountSummary("alice", "Alice", "alice@example.test", Now, new(9876.54m, 2), new(7, 4, 3, 100, 150, 50), "player");
        var authentication = new AuthenticationResponse(account, "token", Now.AddDays(1));
        var json = JsonSerializer.Serialize(AccountsController.LoginBody(authentication, State(account: true)), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"account\":null", json); Assert.Contains("account_restricted", json); Assert.Contains("\"token\":\"token\"", json);
        Assert.DoesNotContain("9876", json); Assert.DoesNotContain("balances", json); Assert.DoesNotContain("alice@example", json);
        Assert.Same(authentication, AccountsController.LoginBody(authentication, State(withdrawals: true)));
    }
}
