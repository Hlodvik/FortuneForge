using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FortuneForge.Games.Mines.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace FortuneForge.Games.Mines.Server.Tests;

public sealed class MinesHttpTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ff-mines-http-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Api_PracticeFlowIsPrivateAndRecoversWithPersistentIdentity()
    {
        await using var app = await CreateApp();
        var client = app.GetTestClient();
        var session = await client.GetFromJsonAsync<MinesSessionResponse>("/api/games/mines/session");
        Assert.True(session!.Practice);
        Assert.Equal(100_000, session.BalanceCents);
        Assert.Null(session.Round);
        var start = await client.PostAsJsonAsync("/api/games/mines/rounds", new { requestId = "mines-http-request-0001", wagerCents = 100, mineCount = 3, outcome = "win", payoutCents = 1_000_000, minePositions = new[] { 24 } });
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        Assert.Contains("no-store", start.Headers.CacheControl!.ToString());
        var first = (await start.Content.ReadFromJsonAsync<MinesSessionResponse>())!;
        Assert.Equal(99_900, first.BalanceCents);
        Assert.Equal("active", first.Round!.Status);
        Assert.All(first.Round.Tiles, tile => Assert.Equal("hidden", tile.Kind));
        var body = await start.Content.ReadAsStringAsync();
        Assert.DoesNotContain("minePositions", body);
        Assert.DoesNotContain("userId", body);
        Assert.DoesNotContain("state", body);
        var replay = await client.PostAsJsonAsync("/api/games/mines/rounds", new StartMinesRequest("mines-http-request-0001", 100, 3));
        Assert.Equal(body, await replay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/games/mines/rounds", new StartMinesRequest("mines-http-request-0001", 101, 3))).StatusCode);
        var recovered = (await client.GetFromJsonAsync<MinesSessionResponse>("/api/games/mines/session"))!;
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(recovered));
        client.DefaultRequestHeaders.Add("X-Test-Player", "attacker");
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/games/mines/rounds/{first.Round.RoundId}/reveal", new RevealMinesRequest("mines-http-request-0002", 24))).StatusCode);
        Assert.Null((await client.GetFromJsonAsync<MinesSessionResponse>("/api/games/mines/session"))!.Round);
    }

    [Theory]
    [InlineData(false, true, true, false, HttpStatusCode.ServiceUnavailable)]
    [InlineData(true, false, true, false, HttpStatusCode.Unauthorized)]
    [InlineData(true, true, false, false, HttpStatusCode.ServiceUnavailable)]
    public async Task Api_RequiresFeatureAuthenticationAndExplicitPaidGate(bool enabled, bool authenticated, bool practice, bool paidEnabled, HttpStatusCode expected)
    {
        await using var app = await CreateApp(enabled, authenticated, practice, paidEnabled);
        var client = app.GetTestClient();
        Assert.Equal(expected, (await client.GetAsync("/api/games/mines/session")).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/games/mines/rounds", new StartMinesRequest("mines-http-request-0001", 100, 3))).StatusCode);
        Assert.False(File.Exists(Path.Combine(directory, "practice.json")));
    }

    [Fact]
    public async Task Api_PaidWriteRejectsForeignOriginEvenWhenHostCorsIsPermissive()
    {
        await using var app = await CreateApp(practice: false, paidEnabled: true);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/games/mines/rounds", new StartMinesRequest("mines-http-request-0001", 100, 3))).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/games/mines/rounds", new StartMinesRequest("mines-http-request-0001", 100, 3))).StatusCode);
    }

    [Fact]
    public async Task Api_RejectsFormCsrfMalformedBodiesWagerLimitsAndBadIdentifiers()
    {
        await using var app = await CreateApp();
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/games/mines/rounds", new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("wagerCents", "100") }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/games/mines/rounds", new StringContent("{", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/games/mines/rounds", new StartMinesRequest("bad", 100, 3))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/games/mines/rounds", new StartMinesRequest("mines-http-request-0001", 10_001, 3))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/games/mines/rounds/bad/reveal", new RevealMinesRequest("mines-http-request-0001", 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/games/mines/rounds", new StringContent("{\"requestId\":\"mines-http-request-0001\",\"wagerCents\":100.5,\"mineCount\":3}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.False(File.Exists(Path.Combine(directory, "practice.json")));
    }

    private async Task<WebApplication> CreateApp(bool enabled = true, bool authenticated = true, bool practice = true, bool paidEnabled = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Features:MinesEnabled"] = enabled.ToString();
        builder.Configuration["Features:MinesPaidEnabled"] = paidEnabled.ToString();
        builder.Services.AddMinesServer(options => { options.ForcePractice = practice; options.PracticeLedgerPath = Path.Combine(directory, "practice.json"); });
        var app = builder.Build();
        app.MapMines((context, _) => Task.FromResult(authenticated ? new MinesAccount(context.Request.Headers["X-Test-Player"].FirstOrDefault() ?? "http-player") : null));
        await app.StartAsync();
        return app;
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
