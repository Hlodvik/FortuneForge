using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.FreeSolitaire.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Solitaire;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Cards.Solitaire;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class SignedFreeSolitaireWorkerIntegrationTests(SolitaireFirestoreEmulatorFixture fixture)
{
    private const string Player = "free-solitaire-player";
    private const string Other = "free-solitaire-other";
    private const string Starts = "/api/solitaire/free/runs";
    private const string StartKey = "signed-free-solitaire-start-0001";
    private const string WorkerGame = SolitaireFreeRunIdentity.StandaloneGameId;

    [Fact]
    public async Task PublicRouteForwardsSignedActorAndBodyAndReopensOneArchivedRun()
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-free-gateway-{Guid.NewGuid():N}", EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateSessionAsync(accounts, Player);
        var otherToken = await CreateSessionAsync(accounts, Other);
        var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var observed = new ConcurrentQueue<SignedStart>();
        await using var worker = await StartWorkerAsync(database, signingKey, observed);
        var builder = Loopback();
        builder.Configuration[$"GameWorkers:{WorkerGame}:ForwardPublicRoutes"] = "true";
        builder.Configuration[$"GameWorkers:{WorkerGame}:Url"] = worker.Urls.Single();
        builder.Configuration[$"GameWorkers:{WorkerGame}:SigningKey"] = signingKey;
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(accounts, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        builder.Services.AddHttpClient(GameWorkerClient.ExactResponseClient).ConfigurePrimaryHttpMessageHandler(() =>
            new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false });
        builder.Services.AddControllers().AddApplicationPart(typeof(SolitaireController).Assembly);
        await using var gateway = builder.Build();
        gateway.UseRouting();
        gateway.UseGameSecurityRequests();
        gateway.UsePackagedGameWorkerForwarding();
        gateway.MapControllers().WithFreeSolitaireWorkerRoutes();
        await gateway.StartAsync();
        using var browser = Client(gateway);
        using var privateWorker = Client(worker);
        const string startBody = "{ \"drawCount\": 3, \"gameId\": \"free-solitaire\" }\n";

        using var unsigned = await SendAsync(privateWorker, HttpMethod.Post, Starts, token, body: startBody, operation: StartKey);
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        using var switched = await SendAsync(browser, HttpMethod.Post, Starts, otherToken, Player, startBody, StartKey);
        Assert.Equal(HttpStatusCode.Forbidden, switched.StatusCode);
        var runs = database.Collection(SolitaireFreeRunIdentity.CollectionName);
        Assert.Empty((await runs.GetSnapshotAsync()).Documents);

        using var firstStart = await SendAsync(browser, HttpMethod.Post, Starts, token, Player, startBody, StartKey);
        var started = await ReadAsync<SolitaireFreeRunStartResult>(firstStart);
        Assert.False(started.WasAlreadyStarted);
        Assert.Equal(42u, started.Seed);
        Assert.Equal(WorkerGame, started.GameId);
        Assert.Equal(new SignedStart(Player, StartKey, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(startBody))),
            SolitaireFreeRunIdentity.LegacyGameId), Assert.Single(observed));

        using var duplicateStart = await SendAsync(browser, HttpMethod.Post, Starts, token, Player, startBody, StartKey);
        var retried = await ReadAsync<SolitaireFreeRunStartResult>(duplicateStart);
        Assert.True(retried.WasAlreadyStarted);
        Assert.Equal(started.RunId, retried.RunId);
        Assert.Equal(started.Seed, retried.Seed);
        using var conflict = await SendAsync(browser, HttpMethod.Post, Starts, token, Player, startBody.Replace("3", "1"), StartKey);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var runPath = $"{Starts}/{started.RunId}";
        var replayPath = runPath + "/replay";
        const string replayBody = "{ \"commands\": [{ \"type\": \"draw\" }] }";
        using var firstReplay = await SendAsync(browser, HttpMethod.Post, replayPath, token, Player, replayBody);
        var completion = await ReadAsync<SolitaireFreeRunCompletionResult>(firstReplay);
        Assert.False(completion.WasAlreadyCompleted);
        Assert.Equal(1, completion.Moves);
        using var duplicateReplay = await SendAsync(browser, HttpMethod.Post, replayPath, token, Player, replayBody);
        var recoveredCompletion = await ReadAsync<SolitaireFreeRunCompletionResult>(duplicateReplay);
        Assert.True(recoveredCompletion.WasAlreadyCompleted);
        Assert.Equal(completion.Score, recoveredCompletion.Score);
        using var changedReplay = await SendAsync(browser, HttpMethod.Post, replayPath, token, Player, "{\"commands\":[]}");
        Assert.Equal(HttpStatusCode.Conflict, changedReplay.StatusCode);
        using var foreign = await SendAsync(browser, HttpMethod.Get, runPath, otherToken, Other);
        Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);

        await worker.StopAsync();
        await using var replica = await StartWorkerAsync(database, signingKey, observed);
        builder.Configuration[$"GameWorkers:{WorkerGame}:Url"] = replica.Urls.Single();
        using var recovered = await SendAsync(browser, HttpMethod.Get, runPath, token, Player);
        var archived = await ReadAsync<SolitaireFreeRunStateResult>(recovered);
        Assert.Equal("completed", archived.Status);
        Assert.Equal(recoveredCompletion, archived.Completion);
        Assert.Equal("draw", Assert.Single(archived.Commands!).Type);
        var savedRun = Assert.Single((await runs.GetSnapshotAsync()).Documents);
        Assert.Equal(Player, savedRun.GetValue<string>("userId"));
        Assert.Equal("completed", savedRun.GetValue<string>("status"));
        Assert.Single((await database.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    private static async Task<WebApplication> StartWorkerAsync(FirestoreDb database, string signingKey,
        ConcurrentQueue<SignedStart> observed)
    {
        var builder = Loopback();
        builder.Configuration["GameWorker:SigningKey"] = signingKey;
        builder.Services.AddSingleton(new FirestoreSolitaireFreeRunService(database, TimeProvider.System, () => 42));
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(WorkerGame);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path == Starts)
                observed.Enqueue(new(GameWorkerAuthentication.GetAccountId(context),
                    context.Request.Headers["Idempotency-Key"].ToString(), GameWorkerAuthentication.GetVerifiedBodySha256(context),
                    GameSecurityContext.Current?.GameId));
            await next(context);
        });
        app.MapFreeSolitaireRuns(GameWorkerAuthentication.GetAccountId);
        await app.StartAsync();
        return app;
    }

    private static WebApplicationBuilder Loopback()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }

    private static async Task<string> CreateSessionAsync(FirestoreAccountStore accounts, string accountId)
    {
        Assert.NotNull((await accounts.CreateAsync(accountId, accountId, accountId.ToUpperInvariant(),
            accountId + "@example.test", "emulator-test", "active", default)).Value);
        var token = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        await accounts.CreateSessionAsync(GameSecurityEventDocuments.Hash(token), accountId, now, now.AddHours(1), null, default);
        return token;
    }

    private static HttpClient Client(WebApplication app) => new(new SocketsHttpHandler { UseProxy = false, UseCookies = false })
    {
        BaseAddress = new Uri(app.Urls.Single()),
    };

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string token,
        string? expectedAccount = null, string? body = null, string? operation = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{AccountSessionCookie.Name}={token}");
        if (expectedAccount is not null) request.Headers.Add("X-FortuneForge-Expected-Account", expectedAccount);
        if (operation is not null) request.Headers.Add("Idempotency-Key", operation);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) where T : class
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>());
    }

    private sealed record SignedStart(string? Actor, string Operation, string? BodySha256, string? AuditGame);
}
