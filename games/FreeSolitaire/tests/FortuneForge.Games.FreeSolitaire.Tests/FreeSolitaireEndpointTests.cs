using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.FreeSolitaire.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Games.FreeSolitaire.Tests;

public sealed class FreeSolitaireEndpointTests
{
    [FirestoreEmulatorFact]
    public async Task SignedLegacyRoutesRecoverLostStartAndCompletionResponsesWithoutReplayingOutcomes()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-free-solitaire-http-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(key);
        builder.Services.AddSingleton(new FirestoreSolitaireFreeRunService(database, TimeProvider.System, () => 42));
        await using var app = builder.Build();
        app.UseGameWorkerAuthentication(SolitaireFreeRunIdentity.StandaloneGameId);
        app.MapFreeSolitaireRuns(GameWorkerAuthentication.GetAccountId);
        await app.StartAsync();
        using var http = app.GetTestClient();
        var startPath = FreeSolitaireRouteContract.PathPrefix + "/runs";
        const string operation = "free-solitaire-http-lost-0001";
        var terms = new SolitaireFreeRunStartRequest(3, GameId: SolitaireFreeRunIdentity.StandaloneGameId);

        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync(startPath, terms)).StatusCode);
        using var firstStart = await SendAsync(HttpMethod.Post, startPath, terms, operation);
        Assert.Equal(HttpStatusCode.OK, firstStart.StatusCode);
        // Simulate a response lost after commit: the next call uses the original key and body.
        using var retriedStart = await SendAsync(HttpMethod.Post, startPath, terms, operation);
        var started = (await retriedStart.Content.ReadFromJsonAsync<SolitaireFreeRunStartResult>())!;
        Assert.True(started.WasAlreadyStarted);
        Assert.Equal(42u, started.Seed);
        Assert.Equal("no-store, max-age=0", retriedStart.Headers.CacheControl!.ToString());
        using var changedStart = await SendAsync(HttpMethod.Post, startPath, terms with { DrawCount = 1 }, operation);
        Assert.Equal(HttpStatusCode.Conflict, changedStart.StatusCode);
        using var recoveredStart = await SendAsync(HttpMethod.Get, $"{startPath}/{started.RunId}");
        var active = (await recoveredStart.Content.ReadFromJsonAsync<SolitaireFreeRunStateResult>())!;
        Assert.Equal("started", active.Status);
        Assert.Equal(started.Seed, active.Seed);

        var replayPath = $"{startPath}/{started.RunId}/replay";
        var replay = new SolitaireFreeRunReplayRequest([new("draw", null, null, null, null)]);
        using var firstCompletion = await SendAsync(HttpMethod.Post, replayPath, replay);
        Assert.Equal(HttpStatusCode.OK, firstCompletion.StatusCode);
        using var retriedCompletion = await SendAsync(HttpMethod.Post, replayPath, replay);
        var completion = (await retriedCompletion.Content.ReadFromJsonAsync<SolitaireFreeRunCompletionResult>())!;
        Assert.True(completion.WasAlreadyCompleted);
        Assert.Equal(1, completion.Moves);
        using var recoveredCompletion = await SendAsync(HttpMethod.Get, $"{startPath}/{started.RunId}");
        var completed = (await recoveredCompletion.Content.ReadFromJsonAsync<SolitaireFreeRunStateResult>())!;
        Assert.Equal(completion, completed.Completion);
        Assert.Equal(replay.Commands, completed.Commands);
        using var changedCompletion = await SendAsync(HttpMethod.Post, replayPath, new SolitaireFreeRunReplayRequest([]));
        Assert.Equal(HttpStatusCode.Conflict, changedCompletion.StatusCode);
        using var foreign = await SendAsync(HttpMethod.Get, $"{startPath}/{started.RunId}", account: "someone-else");
        Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);
        Assert.Single((await database.Collection(SolitaireFreeRunIdentity.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(FortuneForge.Games.Abstractions.Analytics.GameAnalyticsOutbox.CollectionName)
            .GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);

        async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? payload = null,
            string? idempotencyKey = null, string account = "alice")
        {
            var body = payload is null ? [] : JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var headers = idempotencyKey is null ? null : new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey };
            var observation = new GameRequestObservation(Guid.NewGuid().ToString("N"), account, null, null,
                DateTime.UtcNow, method.Method, path, FreeSolitaireRouteContract.AuditGameId, null,
                idempotencyKey, null, null, "sanitized", "direct");
            using var request = new HttpRequestMessage(method, path);
            if (body.Length > 0)
            {
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new("application/json");
            }
            if (headers is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
            request.Headers.Add(GameWorkerAuthentication.AssertionHeader, GameWorkerAuthentication.CreateAssertion(
                SolitaireFreeRunIdentity.StandaloneGameId, account, method.Method, path, body, key,
                DateTimeOffset.UtcNow, observation, headers));
            return await http.SendAsync(request);
        }
    }
}
