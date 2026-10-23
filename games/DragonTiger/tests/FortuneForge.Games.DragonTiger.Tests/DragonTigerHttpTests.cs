using System.Net;
using System.Net.Http.Json;
using FortuneForge.Games.DragonTiger.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace FortuneForge.Games.DragonTiger.Tests;

public sealed class DragonTigerHttpTests
{
    [Fact]
    public async Task Api_NormalPlayReplayConflictAndRecovery()
    {
        await using var app = await CreateApp(true, true);
        var client = app.GetTestClient();
        var status = await client.GetFromJsonAsync<DragonTigerStatusResponse>("/api/games/dragon-tiger/status");
        Assert.Equal(0.50m, status!.MinimumStake);
        Assert.Equal(100m, status.MaximumTotalStake);
        Assert.Equal("practice-eight-deck-dragon-tiger", status.Mode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/games/dragon-tiger/rounds/latest")).StatusCode);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "dragon_tiger_http_0001");
        var first = await client.PostAsJsonAsync("/api/games/dragon-tiger/rounds", new CreateDragonTigerRoundRequest(0.50m, 0, 0.50m));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Contains("no-store", first.Headers.CacheControl!.ToString());
        var round = await first.Content.ReadFromJsonAsync<DragonTigerRoundResponse>();
        Assert.Equal(1m, round!.TotalStake);
        Assert.Equal(3, round.Bets.Count);
        Assert.Equal("settled", round.Phase);
        var replay = await client.PostAsJsonAsync("/api/games/dragon-tiger/rounds", new CreateDragonTigerRoundRequest(0.50m, 0, 0.50m));
        Assert.Equal(round.RoundId, (await replay.Content.ReadFromJsonAsync<DragonTigerRoundResponse>())!.RoundId);
        var restored = await client.GetFromJsonAsync<DragonTigerRoundResponse>(first.Headers.Location);
        var latest = await client.GetFromJsonAsync<DragonTigerRoundResponse>("/api/games/dragon-tiger/rounds/latest");
        Assert.Equal(round.RoundId, restored!.RoundId);
        Assert.Equal(round.RoundId, latest!.RoundId);
        var conflict = await client.PostAsJsonAsync("/api/games/dragon-tiger/rounds", new CreateDragonTigerRoundRequest(1));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var after = await client.GetFromJsonAsync<DragonTigerStatusResponse>("/api/games/dragon-tiger/status");
        Assert.Equal(round.Balance, after!.Balance);
    }

    [Theory]
    [InlineData(false, true, HttpStatusCode.ServiceUnavailable)]
    [InlineData(true, false, HttpStatusCode.Unauthorized)]
    public async Task Api_GatesBeforeStoreAccess(bool enabled, bool authenticated, HttpStatusCode expected)
    {
        await using var app = await CreateApp(enabled, authenticated);
        var client = app.GetTestClient();
        Assert.Equal(expected, (await client.GetAsync("/api/games/dragon-tiger/status")).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/games/dragon-tiger/rounds", new CreateDragonTigerRoundRequest(1))).StatusCode);
        Assert.Equal(expected, (await client.GetAsync("/api/games/dragon-tiger/rounds/latest")).StatusCode);
    }

    [Fact]
    public async Task Api_RejectsMalformedAndOverLimitWagers()
    {
        await using var app = await CreateApp(true, true);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "dragon_tiger_http_0002");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/games/dragon-tiger/rounds", new CreateDragonTigerRoundRequest(100, 0.50m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/games/dragon-tiger/rounds", new StringContent("{", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/games/dragon-tiger/rounds/bad")).StatusCode);
    }

    private static async Task<WebApplication> CreateApp(bool enabled, bool authenticated)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Features:DragonTigerEnabled"] = enabled.ToString();
        builder.Services.AddDragonTigerServer(options => options.ForcePractice = true);
        var app = builder.Build();
        app.MapDragonTiger((context, cancellationToken) => Task.FromResult(authenticated ? new DragonTigerAccount("http-player", 10_000m) : null));
        await app.StartAsync();
        return app;
    }
}
