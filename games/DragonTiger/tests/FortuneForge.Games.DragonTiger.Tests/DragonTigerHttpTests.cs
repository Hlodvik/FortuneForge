using System.Net;
using System.Net.Http.Json;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.Cards;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Games.DragonTiger.Tests;

public sealed class DragonTigerHttpTests
{
    [Fact]
    public async Task Api_AbortedResponseAdvertisesOriginalKeyRecoveryWithoutAnotherDebit()
    {
        var attempts = 0;
        await using var app = await CreateApp(true, true, (context, cancellationToken) =>
        {
            // Inject a transient RPC failure into the endpoint's existing error
            // boundary. No production-only store seam is needed for this contract.
            if (Interlocked.Increment(ref attempts) == 1)
                throw new RpcException(new Status(StatusCode.Aborted, "Transaction lock timeout."));
            return Task.FromResult<DragonTigerAccount?>(new DragonTigerAccount("http-player", 10_000m));
        });
        var practice = app.Services.GetRequiredService<PracticeDragonTigerStore>();
        var service = new DragonTigerService(practice, TimeProvider.System,
            () => DragonTigerTests.Shoe(CardRank.King, CardRank.Ace));
        var request = new CreateDragonTigerRoundRequest(10, 0, 0.50m);
        const string key = "dragon_tiger_http_recovery_0001";
        // The original operation committed, but its response was lost.
        var original = await service.StartAsync("http-player", request, key);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);

        var busy = await client.PostAsJsonAsync("/api/games/dragon-tiger/rounds", request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), busy.Headers.RetryAfter!.Delta);
        var error = await busy.Content.ReadFromJsonAsync<DragonTigerErrorResponse>();
        Assert.Equal("dragon-tiger-retry-required", error!.Code);
        Assert.Contains("original Idempotency-Key", error.Message);
        Assert.Equal(1, practice.RoundCount);
        var recovered = await client.PostAsJsonAsync("/api/games/dragon-tiger/rounds", request);
        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        var replay = await recovered.Content.ReadFromJsonAsync<DragonTigerRoundResponse>();
        Assert.Equal(original.RoundId, replay!.RoundId);
        Assert.Equal(original.Balance, replay.Balance);
        Assert.Equal(original.DragonCard, replay.DragonCard);
        Assert.Equal(original.TigerCard, replay.TigerCard);
        Assert.Equal(2, replay.ShoeCardsUsed);
        Assert.Equal(1, practice.RoundCount);
        Assert.Equal(original.Balance, DragonTigerMoney.ToRand(practice.GetBalanceCents("http-player")));
    }

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

    private static async Task<WebApplication> CreateApp(bool enabled, bool authenticated, DragonTigerAccountResolver? resolveAccount = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Features:DragonTigerEnabled"] = enabled.ToString();
        builder.Services.AddDragonTigerServer(options => options.ForcePractice = true);
        var app = builder.Build();
        app.MapDragonTiger(resolveAccount ?? ((context, cancellationToken) => Task.FromResult(authenticated ? new DragonTigerAccount("http-player", 10_000m) : null)));
        await app.StartAsync();
        return app;
    }
}
