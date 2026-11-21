using System.Net;
using System.Net.Http.Json;
using FortuneForge.Games.Flappy.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.Flappy.Tests;

public sealed class FlappyFreeRunEndpointTests
{
    [Fact]
    public async Task ExistingFreeUrlRejectsAnonymousCallerBeforeOpeningStorage()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapFlappyFreeRuns(_ => null);
        await app.StartAsync();
        var response = await app.GetTestClient().PostAsJsonAsync(FlappyFreeRunEndpoints.PathPrefix, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("arcade-competition-authentication-required", await response.Content.ReadAsStringAsync());
    }

    [FirestoreEmulatorFact]
    public async Task OriginalFreeUrlsKeepWireShapeAndRecoverLostResponses()
    {
        var database = new FirestoreDbBuilder { ProjectId = $"demo-flap-http-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new FlappyFreeRunStore(database, TimeProvider.System, () => 17));
        await using var app = builder.Build();
        app.MapFlappyFreeRuns(_ => "alice");
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "flappy-http-start-0001");
        var started = await client.PostAsJsonAsync(FlappyFreeRunEndpoints.PathPrefix, new { });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var run = await started.Content.ReadFromJsonAsync<FlappyRecordedRunStart>();
        Assert.NotNull(run);
        Assert.Equal(17U, run.Seed);
        var recovered = await client.PostAsJsonAsync(FlappyFreeRunEndpoints.PathPrefix, new { });
        Assert.True((await recovered.Content.ReadFromJsonAsync<FlappyRecordedRunStart>())!.WasReplay);
        var path = FlappyFreeRunEndpoints.PathPrefix + "/" + run.RunId + "/replay";
        var complete = await client.PostAsJsonAsync(path, new { totalTicks = 36, flapTicks = Array.Empty<int>() });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var result = await complete.Content.ReadFromJsonAsync<FlappyRecordedRunCompletion>();
        Assert.Equal("ground-collision", result!.Terminal);
        var retry = await client.PostAsJsonAsync(path, new { totalTicks = 36, flapTicks = Array.Empty<int>() });
        Assert.True((await retry.Content.ReadFromJsonAsync<FlappyRecordedRunCompletion>())!.WasReplay);
        Assert.Single((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
    }
}
