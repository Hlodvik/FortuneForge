using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FortuneForge.Games.AndarBahar.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace FortuneForge.Games.AndarBahar.Tests;

public sealed class AndarBaharHttpTests
{
    private const string Api = "/api/games/andar-bahar";
    [Fact]
    public async Task FullApiFlow_ReplayConflictGetLatestAndRestartRecovery()
    {
        var path = AndarBaharServerTests.NewPath();
        AndarBaharRoundResponse round;
        await using (var app = await App(path))
        {
            var client = app.GetTestClient();
            var status = await client.GetFromJsonAsync<AndarBaharStatusResponse>($"{Api}/status");
            Assert.Equal(.50m, status!.MinimumStake);
            Assert.Equal(100m, status.MaximumStake);
            Assert.Equal("development-practice-andar-first", status.Mode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"{Api}/rounds/latest")).StatusCode);
            client.DefaultRequestHeaders.Add("Idempotency-Key", "andar_http_fullflow_01");
            var response = await client.PostAsJsonAsync($"{Api}/rounds", new CreateAndarBaharRoundRequest("andar", .50m));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
            round = (await response.Content.ReadFromJsonAsync<AndarBaharRoundResponse>())!;
            Assert.Equal("settled", round.Phase);
            Assert.InRange(round.Deals.Count, 1, 49);
            Assert.Equal(round.Joker.Rank, round.Deals[^1].Card.Rank);
            Assert.All(round.Deals.SkipLast(1), deal => Assert.NotEqual(round.Joker.Rank, deal.Card.Rank));
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            Assert.Equal(11, json.AsObject().Count);
            Assert.False(json.AsObject().ContainsKey("deck"));
            Assert.False(json.AsObject().ContainsKey("userId"));
            var replay = await client.PostAsJsonAsync($"{Api}/rounds", new CreateAndarBaharRoundRequest("andar", .50m));
            Assert.Equal(round.RoundId, (await replay.Content.ReadFromJsonAsync<AndarBaharRoundResponse>())!.RoundId);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{Api}/rounds", new CreateAndarBaharRoundRequest("bahar", .50m))).StatusCode);
            Assert.Equal(round.RoundId, (await client.GetFromJsonAsync<AndarBaharRoundResponse>(response.Headers.Location))!.RoundId);
        }
        await using var restarted = await App(path);
        var recovered = await restarted.GetTestClient().GetFromJsonAsync<AndarBaharRoundResponse>($"{Api}/rounds/latest");
        Assert.Equal(round.RoundId, recovered!.RoundId);
        Assert.Equal(round.Balance, recovered.Balance);
        await using var other = await App(path, user: "other");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetTestClient().GetAsync($"{Api}/rounds/{round.RoundId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.GetTestClient().GetAsync($"{Api}/rounds/latest")).StatusCode);
    }
    [Theory]
    [InlineData(false, true, 503)] [InlineData(true, false, 401)]
    public async Task FeatureAndAuthenticationGatesRunBeforePersistence(bool enabled, bool authenticated, int code)
    {
        var path = AndarBaharServerTests.NewPath();
        await using var app = await App(path, enabled, authenticated);
        var client = app.GetTestClient();
        Assert.Equal(code, (int)(await client.GetAsync($"{Api}/status")).StatusCode);
        Assert.Equal(code, (int)(await client.PostAsJsonAsync($"{Api}/rounds", new CreateAndarBaharRoundRequest("andar", 1))).StatusCode);
        Assert.False(File.Exists(path));
    }
    [Theory]
    [InlineData("{\"side\":\"andar\",\"stake\":0.25}")]
    [InlineData("{\"side\":\"andar\",\"stake\":1,\"winner\":\"andar\"}")]
    [InlineData("{\"side\":\"andar\",\"stake\":1,\"cards\":[]}")]
    [InlineData("{")]
    [InlineData("null")]
    public async Task MalformedOrClientSuppliedOutcomesCannotSettle(string body)
    {
        var path = AndarBaharServerTests.NewPath();
        await using var app = await App(path);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "andar_http_invalid_01");
        var response = await client.PostAsync($"{Api}/rounds", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("andar-bahar-invalid-request", (await response.Content.ReadFromJsonAsync<AndarBaharErrorResponse>())!.Code);
        Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task InsufficientFundsAndMissingKeyDoNotChangeBalance()
    {
        var path = AndarBaharServerTests.NewPath();
        await using var app = await App(path, startingCents: 50);
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Api}/rounds", new CreateAndarBaharRoundRequest("andar", .50m))).StatusCode);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "andar_http_insuff_0001");
        var response = await client.PostAsJsonAsync($"{Api}/rounds", new CreateAndarBaharRoundRequest("andar", 1m));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("insufficient-slot-credits", (await response.Content.ReadFromJsonAsync<AndarBaharErrorResponse>())!.Code);
        Assert.Equal(.50m, (await client.GetFromJsonAsync<AndarBaharStatusResponse>($"{Api}/status"))!.Balance);
        Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task PracticeHeaderCannotSilentlySelectPaidFundsWhenPracticeDisabled()
    {
        await using var app = await App(AndarBaharServerTests.NewPath(), forcePractice: false);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-FortuneForge-Practice", "true");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Api}/status")).StatusCode);
    }
    private static async Task<WebApplication> App(string path, bool enabled = true, bool authenticated = true, string user = "http-player", long startingCents = 1_000_000, bool forcePractice = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Features:AndarBaharEnabled"] = enabled.ToString();
        builder.Services.AddAndarBaharServer(options => { options.ForcePractice = forcePractice; options.PracticeStatePath = path; options.PracticeStartingBalanceCents = startingCents; });
        var app = builder.Build();
        app.MapAndarBahar((context, token) => Task.FromResult(authenticated ? new AndarBaharAccount(user, 12345m) : null));
        await app.StartAsync();
        return app;
    }
}
