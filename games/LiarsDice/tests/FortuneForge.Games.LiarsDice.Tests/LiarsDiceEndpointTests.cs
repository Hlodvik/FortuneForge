using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FortuneForge.Games.LiarsDice.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Games.LiarsDice.Tests;

public sealed class LiarsDiceEndpointTests
{
    [FirestoreEmulatorFact]
    public async Task ExistingMatchUrlsDoNotExposeOpponentHandsAndRetainAccountIsolation()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new FirestoreDbBuilder { ProjectId = $"demo-liars-http-{Guid.NewGuid():N}"[..29],
            EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ILiarsDiceManagedPlayerRoster, Roster>();
        builder.Services.AddSingleton<LiarsDiceGameService>();
        await using var app = builder.Build();
        app.MapLiarsDice(context => context.Request.Headers["Test-Account"].FirstOrDefault(), _ => null);
        await app.StartAsync();
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/games/liars-dice/status")).StatusCode);
        client.DefaultRequestHeaders.Add("Test-Account", "alice");
        var started = await client.PostAsJsonAsync("/api/games/liars-dice/matches", new { dicePerPlayer = 3, seed = 77 });
        started.EnsureSuccessStatusCode();
        var json = await started.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, json.GetProperty("hand").GetArrayLength());
        Assert.False(json.TryGetProperty("state", out _));
        Assert.False(json.TryGetProperty("managedActors", out _));
        Assert.All(json.GetProperty("players").EnumerateArray(), player => Assert.False(player.TryGetProperty("hand", out _)));
        var id = json.GetProperty("matchId").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/games/liars-dice/matches/{id:D}/bid", new { quantity = 1, face = 7 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/games/liars-dice/matches/{id:D}/bid", new { quantity = 1, face = 1 })).StatusCode);
        client.DefaultRequestHeaders.Remove("Test-Account"); client.DefaultRequestHeaders.Add("Test-Account", "bob");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/games/liars-dice/matches/{id:D}")).StatusCode);
    }

    private sealed class Roster : ILiarsDiceManagedPlayerRoster
    {
        public Task<IReadOnlyList<LiarsDiceManagedPlayer>> ReserveAsync(string assignmentId, int count,
            IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<LiarsDiceManagedPlayer>>([new("a", "MossRiver", 2), new("b", "CopperFinch", 3), new("c", "NightWindow", 4)]);
        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) => Task.CompletedTask;
        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) => Task.CompletedTask;
    }
}
