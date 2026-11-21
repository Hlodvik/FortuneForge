using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.DropMerge.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.TwentyFortyEight.Server;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Activity;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class SignedPuzzleWorkerIntegrationTests(SolitaireFirestoreEmulatorFixture fixture)
{
    private const string Player = "puzzle-gateway-player";
    private const string Other = "puzzle-gateway-other";
    private const string StartKey = "signed-puzzle-start-0001";
    private const string ActionKey = "signed-puzzle-action-0001";
    private const string UndoKey = "signed-puzzle-undo-0001";

    [Theory]
    [InlineData("2048", "twentyFortyEightSessions", "{ \"seed\": 32 }\n", "move", "{ \"direction\": \"up\" }\n")]
    [InlineData("drop-merge", "dropMergeSessions", "{ \"seed\": 41 }\n", "drop", "{ \"column\": 0 }\n")]
    public async Task Signed_public_gateway_recovers_exact_commands_and_keeps_durable_sessions_on_their_worker(
        string game, string collection, string startBody, string action, string actionBody)
    {
        var database = Database();
        var store = new FirestoreAccountStore(database);
        var playerToken = await CreateAccountAsync(store, Player, "Player");
        var otherToken = await CreateAccountAsync(store, Other, "Other Player");
        var signingKey = RandomNumberGenerator.GetBytes(32);
        var observed = new ConcurrentQueue<VerifiedCommand>();
        var fault = new LoseCommittedResponse();
        fault.Lose(StartKey);
        await using var worker = await StartWorkerAsync(database, game, signingKey, observed);
        var gatewayBuilder = GatewayBuilder(database, store, game, new Uri(worker.Urls.Single()), signingKey, fault);
        await using var gateway = gatewayBuilder.Build();
        gateway.UseRouting();
        gateway.UseGameSecurityRequests();
        gateway.UsePackagedGameWorkerForwarding();
        MapGame(gateway, game, worker: false);
        await gateway.StartAsync();
        using var browser = Client(gateway);
        using var direct = Client(worker);
        var gamesPath = $"/api/games/{game}/games";

        using var unsigned = await SendAsync(direct, HttpMethod.Post, gamesPath, playerToken, startBody, StartKey);
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        using var wrongAccount = await SendAsync(browser, HttpMethod.Post, gamesPath, playerToken, startBody, StartKey, Other);
        Assert.Equal(HttpStatusCode.Forbidden, wrongAccount.StatusCode);
        Assert.Empty((await database.Collection(collection).GetSnapshotAsync()).Documents);

        var workerUrlSetting = $"GameWorkers:{game}:Url";
        gateway.Configuration[workerUrlSetting] = null;
        using var unboundStart = await SendAsync(browser, HttpMethod.Post, gamesPath, playerToken, startBody, StartKey, Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unboundStart.StatusCode);
        using var unboundError = JsonDocument.Parse(await unboundStart.Content.ReadAsStringAsync());
        Assert.Equal($"{game}-worker-unavailable", unboundError.RootElement.GetProperty("code").GetString());
        Assert.Empty(observed);
        Assert.Empty((await database.Collection(collection).GetSnapshotAsync()).Documents);
        gateway.Configuration[workerUrlSetting] = worker.Urls.Single();

        using var lostStart = await SendAsync(browser, HttpMethod.Post, gamesPath, playerToken, startBody, StartKey, Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lostStart.StatusCode);
        var startedDocument = Assert.Single((await database.Collection(collection).GetSnapshotAsync()).Documents);
        var startCommand = Assert.Single(observed);
        Assert.Equal(Player, startCommand.Actor);
        Assert.Equal(StartKey, startCommand.Key);
        Assert.Equal(Digest(startBody), startCommand.Digest);
        Assert.Equal(Player, startCommand.Observation?.UserId);
        Assert.Equal(game, startCommand.Observation?.GameId);

        // A cookie switch must not consume the first account's still-unknown start.
        using var switched = await SendAsync(browser, HttpMethod.Post, gamesPath, otherToken, startBody, StartKey, Player);
        Assert.Equal(HttpStatusCode.Forbidden, switched.StatusCode);
        Assert.Single((await database.Collection(collection).GetSnapshotAsync()).Documents);
        using var startRetry = await SendAsync(browser, HttpMethod.Post, gamesPath, playerToken, startBody, StartKey, Player);
        Assert.Equal(HttpStatusCode.OK, startRetry.StatusCode);
        var startedJson = await startRetry.Content.ReadAsStringAsync();
        Assert.Equal(fault.LostResponses[StartKey], startedJson);
        using var started = JsonDocument.Parse(startedJson);
        var id = started.RootElement.GetProperty("gameId").GetString()!;
        Assert.Equal(7, Guid.Parse(id).Version);
        Assert.Equal(Guid.Parse(id).ToString("N"), startedDocument.Id);

        using var changedStart = await SendAsync(browser, HttpMethod.Post, gamesPath, playerToken, startBody + " ", StartKey, Player);
        Assert.Equal(HttpStatusCode.BadRequest, changedStart.StatusCode);
        var actionPath = $"{gamesPath}/{id}/{action}";
        fault.Lose(ActionKey);
        using var lostAction = await SendAsync(browser, HttpMethod.Post, actionPath, playerToken, actionBody, ActionKey, Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lostAction.StatusCode);
        using var retriedAction = await SendAsync(browser, HttpMethod.Post, actionPath, playerToken, actionBody, ActionKey, Player);
        Assert.Equal(HttpStatusCode.OK, retriedAction.StatusCode);
        var movedJson = await retriedAction.Content.ReadAsStringAsync();
        Assert.Equal(fault.LostResponses[ActionKey], movedJson);
        using (var moved = JsonDocument.Parse(movedJson)) Assert.Equal(1, moved.RootElement.GetProperty("moves").GetInt32());

        // Pausing new starts and a worker outage cannot move an existing durable board into host memory.
        gatewayBuilder.Configuration[$"GameWorkers:{game}:ForwardPublicRoutes"] = "false";
        await worker.StopAsync();
        using var unavailable = await SendAsync(browser, HttpMethod.Get, $"{gamesPath}/{id}", playerToken, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        await using var replica = await StartWorkerAsync(database, game, signingKey, observed);
        gatewayBuilder.Configuration[$"GameWorkers:{game}:Url"] = replica.Urls.Single();
        using var resumed = await SendAsync(browser, HttpMethod.Get, $"{gamesPath}/{id}", playerToken, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        using (var state = JsonDocument.Parse(await resumed.Content.ReadAsStringAsync()))
            Assert.Equal(1, state.RootElement.GetProperty("moves").GetInt32());
        using var originalRetry = await SendAsync(browser, HttpMethod.Post, actionPath, playerToken, actionBody, ActionKey, Player);
        Assert.Equal(movedJson, await originalRetry.Content.ReadAsStringAsync());
        using var changedAction = await SendAsync(browser, HttpMethod.Post, actionPath, playerToken, actionBody + " ", ActionKey, Player);
        Assert.Equal(HttpStatusCode.BadRequest, changedAction.StatusCode);
        using var foreign = await SendAsync(browser, HttpMethod.Get, $"{gamesPath}/{id}", otherToken, expectedAccount: Other);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        fault.Lose(UndoKey);
        var undoPath = $"{gamesPath}/{id}/undo";
        using var lostUndo = await SendAsync(browser, HttpMethod.Post, undoPath, playerToken, operation: UndoKey, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lostUndo.StatusCode);
        using var undoRetry = await SendAsync(browser, HttpMethod.Post, undoPath, playerToken, operation: UndoKey, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, undoRetry.StatusCode);
        var undoJson = await undoRetry.Content.ReadAsStringAsync();
        Assert.Equal(fault.LostResponses[UndoKey], undoJson);
        using (var undone = JsonDocument.Parse(undoJson))
        {
            Assert.Equal(0, undone.RootElement.GetProperty("moves").GetInt32());
            Assert.False(undone.RootElement.GetProperty("canUndo").GetBoolean());
        }
        Assert.Single((await database.Collection(collection).GetSnapshotAsync()).Documents);
        Assert.Equal(3, (await database.Collection(GameSecurityEventDocuments.CollectionName).GetSnapshotAsync()).Count);
        Assert.All(observed.Where(item => item.Key == UndoKey), item => Assert.Equal(Digest(""), item.Digest));
    }

    [Fact]
    public async Task Legacy_Snake_controller_uses_exact_body_receipts_for_every_mutation()
    {
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(accounts, Player, "Player");
        var otherToken = await CreateAccountAsync(accounts, Other, "Other Player");
        var builder = Loopback();
        RegisterAccountBoundary(builder, database, accounts);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<FortuneForge.Server.Games.Snake.FirestoreSnakeGameService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(FortuneForge.Server.Games.Snake.SnakeController).Assembly);
        builder.Services.AddRateLimiter(options =>
        {
            options.AddConcurrencyLimiter(RateLimitPolicies.SlotReads, limiter => limiter.PermitLimit = 100);
            options.AddConcurrencyLimiter(RateLimitPolicies.SlotSpins, limiter => limiter.PermitLimit = 100);
        });
        await using var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseGameSecurityRequests();
        app.MapControllers();
        await app.StartAsync();
        using var browser = Client(app);
        const string startPath = "/api/games/snake/games";
        const string startBody = "{ \"width\": 16, \"height\": 16 }\r\n";
        using var staleAccount = await SendAsync(browser, HttpMethod.Post, startPath, otherToken, startBody,
            "legacy-snake-account-switch-0001", Player);
        Assert.Equal(HttpStatusCode.Forbidden, staleAccount.StatusCode);
        Assert.Empty((await database.Collection("snakeGames").GetSnapshotAsync()).Documents);
        using var start = await SendAsync(browser, HttpMethod.Post, startPath, token, startBody, StartKey, Player);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var originalStart = await start.Content.ReadAsStringAsync();
        using var parsedStart = JsonDocument.Parse(originalStart);
        var id = parsedStart.RootElement.GetProperty("gameId").GetString()!;
        using var startRetry = await SendAsync(browser, HttpMethod.Post, startPath, token, startBody, StartKey, Player);
        Assert.Equal(originalStart, await startRetry.Content.ReadAsStringAsync());
        using var changedStart = await SendAsync(browser, HttpMethod.Post, startPath, token, startBody + " ", StartKey, Player);
        Assert.Equal(HttpStatusCode.BadRequest, changedStart.StatusCode);

        foreach (var (action, body) in new (string, string?)[]
        {
            ("turn", "{ \"direction\": \"down\" }"), ("tick", null), ("reset", "{ \"width\": 20, \"height\": 20 }"),
        })
        {
            var path = $"{startPath}/{id}/{action}";
            var key = "legacy-snake-" + action + "-0001";
            using var first = await SendAsync(browser, HttpMethod.Post, path, token, body, key, Player);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var firstJson = await first.Content.ReadAsStringAsync();
            using var retry = await SendAsync(browser, HttpMethod.Post, path, token, body, key, Player);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            Assert.Equal(firstJson, await retry.Content.ReadAsStringAsync());
            using var changed = await SendAsync(browser, HttpMethod.Post, path, token, (body ?? "") + " ", key, Player);
            Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        }
    }

    private static WebApplicationBuilder GatewayBuilder(FirestoreDb database, FirestoreAccountStore accounts,
        string game, Uri worker, byte[] key, LoseCommittedResponse fault)
    {
        var builder = Loopback();
        builder.Configuration[$"GameWorkers:{game}:Url"] = worker.AbsoluteUri;
        builder.Configuration[$"GameWorkers:{game}:SigningKey"] = Convert.ToBase64String(key);
        RegisterAccountBoundary(builder, database, accounts);
        builder.Services.AddHttpClient<GameWorkerClient>();
        builder.Services.AddHttpClient(GameWorkerClient.ExactResponseClient)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false })
            .AddHttpMessageHandler(() => fault).SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        AddGame(builder.Services, game);
        return builder;
    }

    private static void RegisterAccountBoundary(WebApplicationBuilder builder, FirestoreDb database, FirestoreAccountStore accounts)
    {
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(accounts, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
    }

    private static async Task<WebApplication> StartWorkerAsync(FirestoreDb database, string game, byte[] key,
        ConcurrentQueue<VerifiedCommand> observed)
    {
        var builder = Loopback();
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(key);
        builder.Services.AddSingleton(database);
        AddGame(builder.Services, game);
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(game);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method))
                observed.Enqueue(new(GameWorkerAuthentication.GetAccountId(context), context.Request.Headers["Idempotency-Key"].ToString(),
                    GameWorkerAuthentication.GetCommandBodySha256(context), GameSecurityContext.Current));
            await next(context);
        });
        MapGame(app, game, worker: true);
        await app.StartAsync();
        return app;
    }

    private static void AddGame(IServiceCollection services, string game)
    {
        if (game == "2048") services.AddSingleton<TwentyFortyEightFirestoreSessions>();
        else services.AddSingleton<DropMergeFirestoreSessions>();
    }

    private static void MapGame(WebApplication app, string game, bool worker)
    {
        string? Actor(HttpContext context) => worker ? GameWorkerAuthentication.GetAccountId(context)
            : throw new InvalidOperationException("A durable puzzle request bypassed its authoritative worker.");
        if (game == "2048") app.MapTwentyFortyEight(Actor, GameWorkerAuthentication.GetCommandBodySha256);
        else app.MapDropMerge(Actor, GameWorkerAuthentication.GetCommandBodySha256);
    }

    private FirestoreDb Database()
    {
        _ = fixture;
        return new FirestoreDbBuilder
        {
            ProjectId = $"demo-puzzle-gateway-{Guid.NewGuid():N}", EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
    }

    private static WebApplicationBuilder Loopback()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }

    private static async Task<string> CreateAccountAsync(FirestoreAccountStore accounts, string id, string name)
    {
        Assert.NotNull((await accounts.CreateAsync(id, name, name.ToUpperInvariant(), id + "@example.test",
            "emulator-test", "active", default)).Value);
        var token = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        await accounts.CreateSessionAsync(GameSecurityEventDocuments.Hash(token), id, now, now.AddHours(1), null, default);
        return token;
    }

    private static HttpClient Client(WebApplication app) => new(new SocketsHttpHandler { UseProxy = false })
    {
        BaseAddress = new Uri(app.Urls.Single()),
    };

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string token,
        string? body = null, string? operation = null, string? expectedAccount = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{AccountSessionCookie.Name}={token}");
        if (operation is not null) request.Headers.Add("Idempotency-Key", operation);
        if (expectedAccount is not null) request.Headers.Add("X-FortuneForge-Expected-Account", expectedAccount);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static string Digest(string body) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    private sealed record VerifiedCommand(string? Actor, string Key, string? Digest, GameRequestObservation? Observation);

    private sealed class LoseCommittedResponse : DelegatingHandler
    {
        private readonly ConcurrentDictionary<string, byte> pending = new();
        public ConcurrentDictionary<string, string> LostResponses { get; } = new();
        public void Lose(string operation) => pending[operation] = 0;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (request.Method != HttpMethod.Post || !response.IsSuccessStatusCode ||
                !request.Headers.TryGetValues("Idempotency-Key", out var values) ||
                !pending.TryRemove(values.Single(), out _)) return response;
            var operation = values.Single();
            LostResponses[operation] = await response.Content.ReadAsStringAsync(token);
            response.Dispose();
            throw new HttpRequestException("Lost the worker response after its committed command.");
        }
    }
}
