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
        Assert.Equal(MinesRules.HouseEdgeBasisPoints, session.HouseEdgeBasisPoints);
        Assert.Equal(MinesRules.PayoutPolicyVersion, session.PayoutPolicyVersion);
        Assert.Equal(100, session.MinimumWagerCents);
        Assert.Equal(500, session.MaximumWagerCents);
        var start = await client.PostAsJsonAsync("/api/games/mines/rounds", new
        {
            requestId = "mines-http-request-0001", wagerCents = 100, mineCount = 3,
            outcome = "win", payoutCents = 1_000_000, minePositions = new[] { 24 },
            houseEdgeBasisPoints = MinesRules.LegacyHouseEdgeBasisPoints, rtpBasisPoints = 9_900,
            payoutPolicyVersion = MinesRules.LegacyPayoutPolicyVersion, nextMinePayoutCents = 1_000_000,
        });
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        Assert.Contains("no-store", start.Headers.CacheControl!.ToString());
        var first = (await start.Content.ReadFromJsonAsync<MinesSessionResponse>())!;
        Assert.Equal(99_900, first.BalanceCents);
        Assert.Equal("active", first.Round!.Status);
        Assert.Equal(MinesRules.HouseEdgeBasisPoints, first.HouseEdgeBasisPoints);
        Assert.Equal(MinesRules.HouseEdgeBasisPoints, first.Round.HouseEdgeBasisPoints);
        Assert.Equal(MinesRules.PayoutPolicyVersion, first.PayoutPolicyVersion);
        Assert.Equal(MinesRules.PayoutPolicyVersion, first.Round.PayoutPolicyVersion);
        Assert.Equal(0, first.Round.NextMinePayoutCents);
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Api_LateMineQuoteAndConsolationExposeNoHiddenBoardOrPlayerIdentity(bool sendInjectedPayout)
    {
        var now = DateTimeOffset.UtcNow;
        var id = MinesService.Hash("http-player\naccepted-late-round");
        var state = new MinesRoundState(500, 12, [.. Enumerable.Range(0, 12)], [12, 13, 14, 15, 16],
            MinesRoundStatus.Active, 0, MinesRules.HouseEdgeBasisPoints, MinesRules.PayoutPolicyVersion);
        var ledger = new FileMinesPracticeStore.PracticeLedger
        {
            Balances = { ["http-player"] = 99_500 }, Latest = { ["http-player"] = id },
            Rounds = { [id] = new(id, "http-player", state, 6, now, now) },
        };
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "practice.json"), JsonSerializer.Serialize(ledger));
        await using var app = await CreateApp();
        var client = app.GetTestClient();
        var before = (await client.GetFromJsonAsync<MinesSessionResponse>("/api/games/mines/session"))!;
        Assert.Equal("active", before.Round!.Status);
        Assert.Equal(18_041, before.Round.NextMinePayoutCents);
        Assert.All(before.Round.Tiles.Where(tile => !tile.IsRevealed), tile => Assert.Equal("hidden", tile.Kind));
        var request = new { requestId = "mines-consolation-request-0001", tile = 0,
            payoutCents = sendInjectedPayout ? 999_999 : 0, payoutPolicyVersion = 1, playerId = "attacker", minePositions = new[] { 24 } };
        var response = await client.PostAsJsonAsync($"/api/games/mines/rounds/{id}/reveal", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var secret in new[] { "minePositions", "privateRoundJson", "userId", "http-player", "attacker" })
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
        var result = (await response.Content.ReadFromJsonAsync<MinesSessionResponse>())!;
        Assert.Equal("consolation", result.Round!.Status);
        Assert.Equal(18_041, result.Round.PayoutCents);
        Assert.Equal(2, result.Round.PayoutPolicyVersion);
        Assert.Equal(117_541, result.BalanceCents);
        var replay = await client.PostAsJsonAsync($"/api/games/mines/rounds/{id}/reveal", request);
        Assert.Equal(body, await replay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/games/mines/rounds/{id}/cashout",
            new CashOutMinesRequest("mines-consolation-request-0002"))).StatusCode);
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
    public async Task Api_RejectsPracticeHeaderWithoutSilentPaidWalletFallback_EvenWhenPaidPlayIsEnabled()
    {
        foreach (var paid in new[] { false, true })
        {
            await using var app = await CreateApp(practice: false, paidEnabled: paid);
            var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add("X-FortuneForge-Practice", "true");
            foreach (var response in new[]
            {
                await client.GetAsync("/api/games/mines/session"),
                await client.PostAsJsonAsync("/api/games/mines/rounds", new StartMinesRequest("mines-http-request-0001", 100, 3)),
            })
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal("mines-practice-disabled", (await response.Content.ReadFromJsonAsync<MinesErrorResponse>())!.Code);
            }
            Assert.False(File.Exists(Path.Combine(directory, "practice.json")));
        }
    }

    [Fact]
    public async Task Api_TrustsOnlyExactConfiguredFirebaseOrigin_NotForwardedHeadersOrLookalikes()
    {
        await using var app = await CreateApp(allowedOrigins: ["https://fortuneforgegame.web.app"]);
        var client = app.GetTestClient();
        foreach (var origin in new[] { "https://fortuneforgegame.web.app.attacker.example", "https://fortuneforgegame.web.app/path", "null", "https://attacker.example" })
        {
            client.DefaultRequestHeaders.Remove("Origin");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", origin);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-Host", "fortuneforgegame.web.app");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/games/mines/rounds", new StartMinesRequest("mines-http-request-0001", 100, 3))).StatusCode);
        }
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://fortuneforgegame.web.app");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/games/mines/rounds", new StartMinesRequest("mines-http-request-0001", 100, 3))).StatusCode);
    }

    [Fact]
    public async Task Api_PaidConfiguredOriginPassesOriginGuard_ButNeverUsesPracticeFunds()
    {
        // No paid Firestore registration: reaching storage yields unavailable, not an origin rejection or practice wager.
        await using var app = await CreateApp(practice: false, paidEnabled: true, allowedOrigins: ["https://fortuneforgegame.web.app"]);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Origin", "https://fortuneforgegame.web.app");
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync("/api/games/mines/rounds", new StartMinesRequest("mines-http-request-0001", 100, 3))).StatusCode);
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
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/games/mines/rounds", new StartMinesRequest("mines-http-request-0001", 99, 3))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/games/mines/rounds", new StartMinesRequest("mines-http-request-0001", 501, 3))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/games/mines/rounds/bad/reveal", new RevealMinesRequest("mines-http-request-0001", 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/games/mines/rounds", new StringContent("{\"requestId\":\"mines-http-request-0001\",\"wagerCents\":100.5,\"mineCount\":3}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.False(File.Exists(Path.Combine(directory, "practice.json")));
    }

    private async Task<WebApplication> CreateApp(bool enabled = true, bool authenticated = true, bool practice = true, bool paidEnabled = false, string[]? allowedOrigins = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Features:MinesEnabled"] = enabled.ToString();
        builder.Configuration["Features:MinesPaidEnabled"] = paidEnabled.ToString();
        builder.Services.AddMinesServer(options => { options.ForcePractice = practice; options.PracticeLedgerPath = Path.Combine(directory, "practice.json"); options.AllowedOrigins = allowedOrigins ?? []; });
        var app = builder.Build();
        app.MapMines((context, _) => Task.FromResult(authenticated ? new MinesAccount(context.Request.Headers["X-Test-Player"].FirstOrDefault() ?? "http-player") : null));
        await app.StartAsync();
        return app;
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
