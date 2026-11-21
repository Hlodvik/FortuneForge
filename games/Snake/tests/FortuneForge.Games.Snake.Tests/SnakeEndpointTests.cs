using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FortuneForge.Games.Snake.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Games.Snake.Tests;

public sealed class SnakeEndpointTests
{
    [FirestoreEmulatorFact]
    public async Task ExistingUrlsReturnOnlyPublicStateAndPreserveValidationAndAccountIsolation()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new FirestoreDbBuilder { ProjectId = $"demo-snake-http-{Guid.NewGuid():N}"[..29],
            EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<FirestoreSnakeGameService>();
        await using var app = builder.Build();
        app.MapSnake(context => context.Request.Headers["Test-Account"].FirstOrDefault(), _ => null);
        await app.StartAsync();
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/games/snake/status")).StatusCode);
        client.DefaultRequestHeaders.Add("Test-Account", "alice");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/games/snake/games", new { seed = 7 })).StatusCode);
        var started = await client.PostAsJsonAsync("/api/games/snake/games", new { width = 16, height = 16 });
        started.EnsureSuccessStatusCode();
        var json = await started.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.TryGetProperty("seed", out _));
        Assert.False(json.TryGetProperty("actions", out _));
        var id = json.GetProperty("gameId").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/games/snake/games/{id:D}/turn", new { direction = "diagonal" })).StatusCode);
        var tick = await client.PostAsync($"/api/games/snake/games/{id:D}/tick", null);
        Assert.Equal(1, (await tick.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("moves").GetInt32());
        client.DefaultRequestHeaders.Remove("Test-Account");
        client.DefaultRequestHeaders.Add("Test-Account", "bob");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/games/snake/games/{id:D}")).StatusCode);
    }
}
