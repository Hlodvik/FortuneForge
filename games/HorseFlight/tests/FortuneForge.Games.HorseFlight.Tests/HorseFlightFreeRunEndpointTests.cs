using System.Net;
using System.Net.Http.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.HorseFlight.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.HorseFlight.Tests;

public sealed class HorseFlightFreeRunEndpointTests
{
    [Fact]
    public async Task ExistingUrlFamiliesKeepTheirFeatureGateAndAuthenticationErrors()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        var enabled = false;
        app.MapHorseFlightFreeRuns(_ => null, () => enabled);
        await app.StartAsync();
        var client = app.GetTestClient();
        foreach (var path in new[] { HorseFlightFreeRunEndpoints.GamePath + "/runs", HorseFlightFreeRunEndpoints.CompetitionPath })
        {
            var disabled = await client.PostAsJsonAsync(path, new { });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
            Assert.Contains("horse-flight-disabled", await disabled.Content.ReadAsStringAsync());
        }
        enabled = true;
        foreach (var path in new[] { HorseFlightFreeRunEndpoints.GamePath + "/runs", HorseFlightFreeRunEndpoints.CompetitionPath })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, new { })).StatusCode);
    }

    [FirestoreEmulatorFact]
    public async Task BothLegacyUrlFamiliesUseTheSameDurableRunAndReplay()
    {
        var database = new FirestoreDbBuilder { ProjectId = $"demo-horse-http-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var account = new AccountReader();
        builder.Services.AddSingleton<IGameWalletAccountReader>(account);
        builder.Services.AddSingleton(new HorseFlightFreeRunStore(database, account, createSeed: () => 17));
        await using var app = builder.Build();
        app.MapHorseFlightFreeRuns(_ => "alice", () => true);
        await app.StartAsync();
        var client = app.GetTestClient();
        var status = await client.GetFromJsonAsync<HorseFlightStatusResponse>(HorseFlightFreeRunEndpoints.GamePath + "/status");
        Assert.Equal(123.45m, status!.Balance);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "horse-http-start-0001");
        var start = await client.PostAsJsonAsync(HorseFlightFreeRunEndpoints.GamePath + "/runs", new { });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var run = await start.Content.ReadFromJsonAsync<HorseFlightStartResponse>();
        var recovered = await client.PostAsJsonAsync(HorseFlightFreeRunEndpoints.CompetitionPath, new { });
        var retry = await recovered.Content.ReadFromJsonAsync<HorseFlightRecordedRunStart>();
        Assert.Equal(run!.RunId, retry!.RunId);
        Assert.Equal(run.Seed, retry.Seed);
        Assert.True(retry.WasReplay);
        var state = HorseFlightEngine.Start(run.Seed);
        var ticks = 0;
        while (state.Phase == HorseFlightPhase.Running)
        {
            state = HorseFlightEngine.Step(state, HorseFlightInput.None).State;
            ticks++;
        }
        var request = new { totalTicks = ticks, jumpTicks = Array.Empty<int>(), rightClickTicks = Array.Empty<int>() };
        var completed = await client.PostAsJsonAsync(HorseFlightFreeRunEndpoints.CompetitionPath + "/" + run.RunId + "/replay", request);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(state.Score, (await completed.Content.ReadFromJsonAsync<HorseFlightRecordedRunCompletion>())!.Score);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "horse-http-finish-0001");
        var final = await client.PostAsJsonAsync(HorseFlightFreeRunEndpoints.GamePath + "/runs/" + run.RunId + "/complete", request);
        Assert.Equal(HttpStatusCode.OK, final.StatusCode);
        Assert.True((await final.Content.ReadFromJsonAsync<HorseFlightRunResponse>())!.WasReplay);
        Assert.Single((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
    }

    private sealed class AccountReader : IGameWalletAccountReader
    {
        public Task<GameWalletAccountState> GetStateAsync(string user, CancellationToken token) =>
            Task.FromResult(new GameWalletAccountState(0, 12345));
    }
}
