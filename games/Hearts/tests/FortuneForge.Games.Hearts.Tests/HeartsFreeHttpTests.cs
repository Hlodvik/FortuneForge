using System.Net;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Games.Hearts.Tests;

[Collection("Blackjack table Firestore emulator")]
public sealed class HeartsFreeHttpTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task Mutations_require_account_and_key_and_private_matches_require_the_same_owner()
    {
        await using var host = await Open();
        using var unauthorized = await Send(host, "/matches", """{"seed":1931}""", "open", null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var missingKey = await Send(host, "/matches", """{"seed":1931}""", null);
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        using var opened = await Send(host, "/matches", """{"seed":1931}""", "open");
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var game = JsonSerializer.Deserialize<HeartsMatchResponse>(await opened.Content.ReadAsStringAsync(), Json)!;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/games/hearts/matches/" + game.MatchId);
        request.Headers.Add("X-Test-Account", "bruno");
        using var stolen = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
    }

    [Fact]
    public async Task Paused_admission_replays_original_body_and_rejects_changed_terms_without_a_new_board()
    {
        await using var host = await Open();
        const string body = """{"seed":1931,"targetScore":100}""";
        using var opened = await Send(host, "/matches", body, "open");
        var original = await opened.Content.ReadAsStringAsync();
        host.Configuration[HeartsBackend.FreeAdmissionSetting] = "false";
        using var replay = await Send(host, "/matches", body, "open");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(original, await replay.Content.ReadAsStringAsync());
        using var changed = await Send(host, "/matches", """{"seed":1942,"targetScore":100}""", "open");
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var paused = await Send(host, "/matches", body, "new-open");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, paused.StatusCode);
        Assert.Single((await host.Database.Collection(FirestoreHeartsFreeStore.Sessions).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Exact_pass_and_trial_cancel_replies_replay_without_repeating_the_mutation()
    {
        await using var host = await Open();
        using var opened = await Send(host, "/trial", "", "trial");
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var game = JsonSerializer.Deserialize<HeartsMatchResponse>(await opened.Content.ReadAsStringAsync(), Json)!;
        var path = "/matches/" + game.MatchId + "/pass";
        var body = JsonSerializer.Serialize(new PassHeartsRequest(game.Hand.Take(3).Select(card => card.Code).ToArray()), Json);
        using var passed = await Send(host, path, body, "pass");
        var original = await passed.Content.ReadAsStringAsync();
        using var replay = await Send(host, path, body, "pass");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(original, await replay.Content.ReadAsStringAsync());
        Assert.Single(JsonSerializer.Deserialize<HeartsMatchResponse>(original, Json)!.SubmittedPasses);
        using var cancelled = await Send(host, "/trial/" + game.MatchId + "/cancel", "", "cancel");
        var cancelledBody = await cancelled.Content.ReadAsStringAsync();
        using var cancelRetry = await Send(host, "/trial/" + game.MatchId + "/cancel", "", "cancel");
        Assert.Equal(cancelledBody, await cancelRetry.Content.ReadAsStringAsync());
        Assert.True(JsonSerializer.Deserialize<HeartsOnboardingResponse>(cancelledBody, Json)!.ShouldAskPlayedBefore);
        Assert.Empty((await host.Database.Collection("heartsAccountOnboarding").GetSnapshotAsync()).Documents);
    }

    private async Task<Harness> Open()
    {
        var database = new FirestoreDbBuilder
        { ProjectId = "demo-hearts-http-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
            ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None }.Build();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [HeartsBackend.FreeAdmissionSetting] = "true" });
        builder.Services.AddSingleton(new FirestoreHeartsFreeStore(database, TimeProvider.System,
            () => builder.Configuration.GetValue<bool>(HeartsBackend.FreeAdmissionSetting)));
        var app = builder.Build();
        app.UseLocalGameWorkerCommandDigests(_ => true);
        app.MapHeartsFree(context => context.Request.Headers["X-Test-Account"].FirstOrDefault());
        await app.StartAsync();
        return new(app, app.GetTestClient(), database, builder.Configuration);
    }
    private static Task<HttpResponseMessage> Send(Harness host, string path, string body, string? key, string? user = "alice")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/games/hearts" + path);
        if (body.Length > 0) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (user is not null) request.Headers.Add("X-Test-Account", user);
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return host.Client.SendAsync(request);
    }
    private sealed record Harness(WebApplication App, HttpClient Client, FirestoreDb Database, IConfiguration Configuration) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() { Client.Dispose(); await App.DisposeAsync(); }
    }
}
