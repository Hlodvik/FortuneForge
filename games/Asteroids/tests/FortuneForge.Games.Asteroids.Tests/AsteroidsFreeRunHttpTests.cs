using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FortuneForge.Games.Asteroids.Server;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FortuneForge.Games.Asteroids.Tests;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class AsteroidsFreeRunHttpTests(SolitaireFirestoreEmulatorFixture fixture)
{
    [Fact]
    public async Task ExistingWireRoutesRetainAuthenticationSeedAndWasReplayContracts()
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-asteroids-http-{Guid.NewGuid():N}", EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(new AsteroidsFreeRunStore(database, TimeProvider.System, () => 42));
        await using var app = builder.Build();
        app.MapAsteroidsFreeRuns(context => context.Request.Headers["Test-User"].FirstOrDefault());
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        const string path = "/api/arcade-competitions/asteroids/free/runs";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(path, null)).StatusCode);
        client.DefaultRequestHeaders.Add("Test-User", "alice");
        var missingKey = await client.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        Assert.Equal("arcade-asteroids-free-idempotency-key-required",
            (await missingKey.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "asteroids-http-start-0001");
        var firstResponse = await client.PostAsync(path, null);
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(first.GetProperty("wasReplay").GetBoolean());
        Assert.Equal("000000000000002a", first.GetProperty("seedHex").GetString());
        var runId = first.GetProperty("runId").GetString();
        var retry = await (await client.PostAsync(path, null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(retry.GetProperty("wasReplay").GetBoolean());
        Assert.Equal(runId, retry.GetProperty("runId").GetString());
        var payload = new { rulesVersion = 2, totalSteps = 3600, commands = Array.Empty<object>() };
        var completedResponse = await client.PostAsJsonAsync($"{path}/{runId}/replay", payload);
        completedResponse.EnsureSuccessStatusCode();
        var completed = await completedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(completed.GetProperty("wasReplay").GetBoolean());
        Assert.Equal("gameover", completed.GetProperty("terminal").GetString());
        var repeated = await (await client.PostAsJsonAsync($"{path}/{runId}/replay", payload)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(repeated.GetProperty("wasReplay").GetBoolean());
        Assert.Equal(completed.GetProperty("score").GetInt64(), repeated.GetProperty("score").GetInt64());
        var recovered = await client.GetFromJsonAsync<JsonElement>($"{path}/{runId}");
        Assert.Equal("completed", recovered.GetProperty("status").GetString());
        Assert.Equal(2, recovered.GetProperty("rulesVersion").GetInt32());
        client.DefaultRequestHeaders.Remove("Test-User");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{path}/{runId}")).StatusCode);
    }
}
