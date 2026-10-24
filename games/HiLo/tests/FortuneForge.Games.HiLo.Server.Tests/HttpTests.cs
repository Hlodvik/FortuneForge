using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FortuneForge.Games.HiLo.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Games.HiLo.Server.Tests;

public sealed class HttpTests
{
    [Fact]
    public async Task NormalFlow_IdempotencyResumeRefundAndNoPrivateFields()
    {
        await using var app = await App(); var client = Client(app);
        var initial = await client.GetFromJsonAsync<HiLoSessionResponse>("/api/games/hi-lo/session");
        Assert.True(initial!.Practice); Assert.Null(initial.Round);
        var command = new { operationId = Guid.NewGuid().ToString("N"), wagerCents = 100, userId = "victim", nextCard = "king", outcome = "win" };
        var response = await client.PostAsJsonAsync("/api/games/hi-lo/rounds", command);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var body = await response.Content.ReadAsStringAsync();
        var state = JsonSerializer.Deserialize<HiLoSessionResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(999900, state.WalletBalanceCents); Assert.Single(state.Round!.History);
        Assert.DoesNotContain("victim", body);
        foreach (var secret in new[] { "\"seed\"", "\"userId\"", "\"futureCards\"", "\"deck\"", "\"operations\"" }) Assert.DoesNotContain(secret, body);
        Assert.Equal(body, await (await client.PostAsJsonAsync("/api/games/hi-lo/rounds", command)).Content.ReadAsStringAsync());
        Assert.Equal(body, await client.GetStringAsync("/api/games/hi-lo/session"));
        var cashout = new HiLoActionRequest(Guid.NewGuid().ToString("N"), 0, "cashout");
        var paid = await (await client.PostAsJsonAsync($"/api/games/hi-lo/rounds/{state.Round.RoundId}/actions", cashout)).Content.ReadFromJsonAsync<HiLoSessionResponse>();
        Assert.Equal(1000000, paid!.WalletBalanceCents); Assert.Equal("cashedOut", paid.Round!.Status);
        Assert.False(paid.Round.Higher.Available); Assert.False(paid.Round.CanCashOut); Assert.False(paid.Round.CanSkip);
    }

    [Theory]
    [InlineData(false, true, HttpStatusCode.ServiceUnavailable)]
    [InlineData(true, false, HttpStatusCode.Unauthorized)]
    public async Task DisabledAndUnauthenticatedRequestsCannotAccessStore(bool enabled, bool authenticated, HttpStatusCode expected)
    {
        await using var app = await App(enabled, authenticated); var client = Client(app);
        Assert.Equal(expected, (await client.GetAsync("/api/games/hi-lo/session")).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/games/hi-lo/rounds", new HiLoStartRequest(Guid.NewGuid().ToString("N"), 100))).StatusCode);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "https://hostile.example")]
    [InlineData(true, "null")]
    public async Task CsrfDefense_RequiresCustomHeaderAndApprovedOrigin(bool customHeader, string? origin)
    {
        await using var app = await App(); var client = app.GetTestClient();
        if (customHeader) client.DefaultRequestHeaders.Add("X-FortuneForge-Game", "hi-lo");
        if (origin is not null) client.DefaultRequestHeaders.Add("Origin", origin);
        var response = await client.PostAsJsonAsync("/api/games/hi-lo/rounds", new HiLoStartRequest(Guid.NewGuid().ToString("N"), 100));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("invalid_origin", (await response.Content.ReadFromJsonAsync<HiLoErrorResponse>())!.Code);
        Assert.Null((await client.GetFromJsonAsync<HiLoSessionResponse>("/api/games/hi-lo/session"))!.Round);
    }

    [Fact]
    public async Task AuthenticatedIdentityIsExclusive_InvalidRequestsAndOperationConflictsAreExplicit()
    {
        await using var app = await App(); var alice = Client(app); var bob = Client(app);
        bob.DefaultRequestHeaders.Add("Test-Account", "bob");
        var start = new HiLoStartRequest(Guid.NewGuid().ToString("N"), 100);
        var state = await (await alice.PostAsJsonAsync("/api/games/hi-lo/rounds", start)).Content.ReadFromJsonAsync<HiLoSessionResponse>();
        var stolen = await bob.PostAsJsonAsync($"/api/games/hi-lo/rounds/{state!.Round!.RoundId}/actions", new HiLoActionRequest(Guid.NewGuid().ToString("N"), 0, "cashout"));
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
        Assert.Null((await bob.GetFromJsonAsync<HiLoSessionResponse>("/api/games/hi-lo/session"))!.Round);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsJsonAsync("/api/games/hi-lo/rounds", start with { WagerCents = 200 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/games/hi-lo/rounds", start with { OperationId = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/games/hi-lo/rounds", start with { WagerCents = 100001 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsync("/api/games/hi-lo/rounds", new StringContent("{", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsJsonAsync($"/api/games/hi-lo/rounds/{state.Round.RoundId}/actions", new HiLoActionRequest(Guid.NewGuid().ToString("N"), 9, "cashout"))).StatusCode);
    }

    [Theory]
    [InlineData("https://fortuneforgegame.web.app", HttpStatusCode.OK)]
    [InlineData("https://fortuneforgegame.firebaseapp.com", HttpStatusCode.OK)]
    [InlineData("https://fortuneforgegame.web.app.hostile.example", HttpStatusCode.Forbidden)]
    public async Task Hosting_proxy_trusts_only_exact_allowlisted_origins(string origin, HttpStatusCode expected)
    {
        await using var app = await App(allowedOrigins: ["https://fortuneforgegame.web.app", "https://fortuneforgegame.firebaseapp.com"]);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-FortuneForge-Game", "hi-lo");
        client.DefaultRequestHeaders.Add("Origin", origin);
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/games/hi-lo/rounds", new HiLoStartRequest(Guid.NewGuid().ToString("N"), 100))).StatusCode);
    }

    [Fact]
    public async Task MaintenanceMode_AllowsReadCashoutAndExactAcceptedReplay_BlocksNewWagersGuessesAndSkips()
    {
        await using var app = await App(); var client = Client(app);
        var start = new HiLoStartRequest(Guid.NewGuid().ToString("N"), 100);
        var initial = await client.PostAsJsonAsync("/api/games/hi-lo/rounds", start);
        var original = await initial.Content.ReadAsStringAsync();
        var state = await initial.Content.ReadFromJsonAsync<HiLoSessionResponse>();
        app.Services.GetRequiredService<IConfiguration>()["Features:HiLoEnabled"] = "false";
        var restored = await client.GetFromJsonAsync<HiLoSessionResponse>("/api/games/hi-lo/session");
        Assert.False(restored!.Enabled); Assert.Equal(state!.Round!.RoundId, restored.Round!.RoundId);
        Assert.Equal(original, await (await client.PostAsJsonAsync("/api/games/hi-lo/rounds", start)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/games/hi-lo/rounds", start with { OperationId = Guid.NewGuid().ToString("N") })).StatusCode);
        foreach (var choice in new[] { "higher", "lower", "skip" })
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync($"/api/games/hi-lo/rounds/{state!.Round!.RoundId}/actions", new HiLoActionRequest(Guid.NewGuid().ToString("N"), 0, choice))).StatusCode);
        var cashout = new HiLoActionRequest(Guid.NewGuid().ToString("N"), 0, "cashout");
        var paid = await client.PostAsJsonAsync($"/api/games/hi-lo/rounds/{state!.Round!.RoundId}/actions", cashout);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var settlement = await paid.Content.ReadFromJsonAsync<HiLoSessionResponse>();
        Assert.Equal(1000000, settlement!.WalletBalanceCents);
        Assert.False(settlement.Enabled);
        Assert.Equal(await paid.Content.ReadAsStringAsync(), await (await client.PostAsJsonAsync($"/api/games/hi-lo/rounds/{state.Round.RoundId}/actions", cashout)).Content.ReadAsStringAsync());
        app.Services.GetRequiredService<IConfiguration>()["Features:HiLoEnabled"] = "true";
        Assert.Equal(await paid.Content.ReadAsStringAsync(), await (await client.PostAsJsonAsync($"/api/games/hi-lo/rounds/{state.Round.RoundId}/actions", cashout)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownServiceFailure_IsRetryableAndDoesNotExposeServerDetails()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Configuration["Features:HiLoEnabled"] = "true"; builder.Services.AddHiLoServer(options => options.ForcePractice = true);
        await using var app = builder.Build();
        app.MapHiLo((context, ct) => throw new IOException("private-database-path-and-secret-details"));
        await app.StartAsync(); var client = Client(app);
        var response = await client.PostAsJsonAsync("/api/games/hi-lo/rounds", new HiLoStartRequest(Guid.NewGuid().ToString("N"), 100));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("retry_required", (await response.Content.ReadFromJsonAsync<HiLoErrorResponse>())!.Code);
        Assert.DoesNotContain("private-database", await response.Content.ReadAsStringAsync());
    }

    private static HttpClient Client(WebApplication app)
    { var client = app.GetTestClient(); client.DefaultRequestHeaders.Add("X-FortuneForge-Game", "hi-lo"); client.DefaultRequestHeaders.Add("Origin", "http://localhost"); return client; }
    private static async Task<WebApplication> App(bool enabled = true, bool authenticated = true, string[]? allowedOrigins = null)
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Configuration["Features:HiLoEnabled"] = enabled.ToString();
        builder.Services.AddHiLoServer(options => { options.ForcePractice = true; options.PracticeStorePath = Path.Combine(Path.GetTempPath(), "hilo-http-" + Guid.NewGuid().ToString("N") + ".json"); options.AllowedOrigins = allowedOrigins ?? []; });
        var app = builder.Build();
        app.MapHiLo((context, ct) => Task.FromResult<HiLoAccount?>(authenticated ? new(context.Request.Headers["Test-Account"].FirstOrDefault() ?? "alice") : null));
        await app.StartAsync(); return app;
    }
}
