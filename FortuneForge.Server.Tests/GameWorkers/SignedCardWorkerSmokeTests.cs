using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Cards.Baccarat;
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
public sealed partial class SignedCardWorkerSmokeTests(SolitaireFirestoreEmulatorFixture fixture)
{
    private const string Player = "card-smoke-player";
    private const string OtherPlayer = "card-smoke-other";
    private const string OperationKey = "signed-card-smoke-operation-001";
    private const long InitialCents = 10_000;
    private const long WagerCents = 50;

    [Theory]
    [InlineData("andar-bahar", "AndarBahar", "{ \"side\": \"andar\", \"stake\": 0.50 }", "{\"side\":\"bahar\",\"stake\":0.50}")]
    [InlineData("dragon-tiger", "DragonTiger", "{ \"dragonStake\": 0.50 }", "{\"dragonStake\":1.00}")]
    public async Task Real_signed_gateway_retries_a_lost_wallet_reply_once_and_keeps_the_original_account_and_digest(
        string game, string feature, string body, string changedBody)
    {
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(database, accounts, Player, "Player");
        var otherToken = await CreateAccountAsync(database, accounts, OtherPlayer, "Other Player");
        var walletKey = RandomNumberGenerator.GetBytes(32);
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var outbound = new DropFirstCommittedWalletReply(game);
        var observed = new ConcurrentQueue<VerifiedWorkerRequest>();
        await using var wallet = await StartWalletAsync(database, game, walletKey);
        await using var worker = await StartWorkerAsync(database, game, feature, workerKey, new Uri(wallet.Urls.Single()),
            walletKey, outbound, observed);
        await using var gateway = await StartGatewayAsync(database, accounts, game, feature, new Uri(worker.Urls.Single()), workerKey);
        using var browser = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
        using var direct = new HttpClient { BaseAddress = new Uri(worker.Urls.Single()) };
        var publicPath = $"/api/games/{game}/rounds";
        var walletReceipts = database.Collection("walletRoundSettlements");
        var ledgers = database.Collection("balanceTransactions");
        Assert.Empty((await walletReceipts.GetSnapshotAsync()).Documents);
        Assert.Empty((await ledgers.GetSnapshotAsync()).Documents);

        using var unsigned = await SendAsync(direct, HttpMethod.Post, publicPath, token, body, OperationKey);
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        using var malformedPractice = await SendAsync(browser, HttpMethod.Post, publicPath, token, body, OperationKey,
            Player, practiceValues: ["true", "false"]);
        Assert.Equal(HttpStatusCode.BadRequest, malformedPractice.StatusCode);
        using var practiceError = JsonDocument.Parse(await malformedPractice.Content.ReadAsStringAsync());
        Assert.Equal("invalid-practice-header", practiceError.RootElement.GetProperty("code").GetString());
        using var wrongSession = await SendAsync(browser, HttpMethod.Post, publicPath, token, body, OperationKey, OtherPlayer);
        Assert.Equal(HttpStatusCode.Forbidden, wrongSession.StatusCode);
        Assert.Empty((await walletReceipts.GetSnapshotAsync()).Documents);
        Assert.Empty((await ledgers.GetSnapshotAsync()).Documents);
        Assert.Empty(observed);

        using var ambiguous = await SendAsync(browser, HttpMethod.Post, publicPath, token, body, OperationKey, Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ambiguous.StatusCode);
        var receipt = Assert.Single((await walletReceipts.GetSnapshotAsync()).Documents);
        var original = Assert.Single(observed);
        Assert.Equal(Player, original.Actor);
        Assert.Equal(OperationKey, original.OperationKey);
        Assert.Equal(publicPath, original.Path);
        Assert.Equal(BodySha(body), original.BodySha256);
        var observation = Assert.IsType<GameRequestObservation>(original.Observation);
        Assert.Equal(game, observation.GameId);
        Assert.Equal(Player, observation.UserId);
        Assert.Equal(publicPath, observation.Path);
        Assert.Equal(OperationKey, observation.CommandId);

        var roundId = receipt.GetValue<string>("operationId");
        var settlementId = GameWalletSettlementIdentity.CreateId(game, Player, roundId);
        Assert.Equal(settlementId, receipt.Id);
        Assert.Equal(Player, receipt.GetValue<string>("userId"));
        Assert.Equal(game, receipt.GetValue<string>("gameId"));
        Assert.Equal(WagerCents, receipt.GetValue<long>("wagerCents"));
        Assert.Equal(InitialCents, receipt.GetValue<long>("balanceBeforeCents"));
        var payout = receipt.GetValue<long>("payoutCents");
        var finalBalance = InitialCents - WagerCents + payout;
        Assert.Equal(finalBalance, receipt.GetValue<long>("balanceAfterCents"));
        Assert.Equal(finalBalance, await BalanceAsync(database, Player));
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));
        var firstLegs = (await ledgers.GetSnapshotAsync()).Documents;
        Assert.Equal(payout > 0 ? 2 : 1, firstLegs.Count);
        Assert.Empty((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);

        worker.Configuration[$"Features:{feature}Enabled"] = "false";
        using var retried = await SendAsync(browser, HttpMethod.Post, publicPath, token, body, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.Equal($"{publicPath}/{roundId}", retried.Headers.Location?.OriginalString);
        Assert.Equal(roundId, await ReadRoundIdAsync(retried));
        var attempts = observed.ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.Equal(original.Actor, attempts[1].Actor);
        Assert.Equal(original.Path, attempts[1].Path);
        Assert.Equal(original.OperationKey, attempts[1].OperationKey);
        Assert.Equal(original.BodySha256, attempts[1].BodySha256);
        Assert.NotEqual(observation.RequestId, attempts[1].Observation?.RequestId);
        var walletBodies = outbound.SettlementBodies.ToArray();
        Assert.Equal(2, walletBodies.Length);
        Assert.Equal(walletBodies[0], walletBodies[1]);
        Assert.True(string.Equals(BodySha(walletBodies[0]), receipt.GetValue<string>("requestBodySha256"), StringComparison.OrdinalIgnoreCase));

        using var changed = await SendAsync(browser, HttpMethod.Post, publicPath, token, changedBody, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var pausedWager = await SendAsync(browser, HttpMethod.Post, publicPath, token, body, "paused-new-wager-0001", Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, pausedWager.StatusCode);
        using var pausedStatus = await SendAsync(browser, HttpMethod.Get, $"/api/games/{game}/status", token, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, pausedStatus.StatusCode);
        using var statusBody = JsonDocument.Parse(await pausedStatus.Content.ReadAsStringAsync());
        Assert.False(statusBody.RootElement.GetProperty("available").GetBoolean());
        using var latest = await SendAsync(browser, HttpMethod.Get, publicPath + "/latest", token, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        Assert.Equal(roundId, await ReadRoundIdAsync(latest));
        using var read = await SendAsync(browser, HttpMethod.Get, publicPath + "/" + roundId, token, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(roundId, await ReadRoundIdAsync(read));
        using var foreign = await SendAsync(browser, HttpMethod.Get, publicPath + "/" + roundId, otherToken, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(2, outbound.SettlementBodies.Count);

        gateway.Configuration[$"GameWorkers:{game}:Url"] = null;
        using var unbound = await SendAsync(browser, HttpMethod.Get, publicPath + "/latest", token, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unbound.StatusCode);
        using var unavailableBody = JsonDocument.Parse(await unbound.Content.ReadAsStringAsync());
        Assert.Equal("game-worker-unavailable", unavailableBody.RootElement.GetProperty("code").GetString());

        var savedReceipt = Assert.Single((await walletReceipts.GetSnapshotAsync()).Documents);
        Assert.Equal(receipt.Id, savedReceipt.Id);
        Assert.Equal(receipt.UpdateTime, savedReceipt.UpdateTime);
        var finalLegs = (await ledgers.GetSnapshotAsync()).Documents;
        Assert.Equal(firstLegs.Select(leg => (leg.Id, leg.UpdateTime)).OrderBy(leg => leg.Id).ToArray(),
            finalLegs.Select(leg => (leg.Id, leg.UpdateTime)).OrderBy(leg => leg.Id).ToArray());
        var wagerLeg = Assert.Single(finalLegs, leg => leg.Id == settlementId + "-wager");
        Assert.Equal(-WagerCents, wagerLeg.GetValue<long>("amountCents"));
        Assert.All(finalLegs, leg =>
        {
            Assert.Equal(settlementId, leg.GetValue<string>("walletSettlementId"));
            Assert.Equal(Player, leg.GetValue<string>("userId"));
            Assert.Equal(game, leg.GetValue<string>("gameId"));
        });
        if (payout > 0)
            Assert.Equal(payout, Assert.Single(finalLegs, leg => leg.Id == settlementId + "-payout").GetValue<long>("amountCents"));
        Assert.Equal(finalBalance, await BalanceAsync(database, Player));
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));

        var accepted = Assert.Single((await database.Collection(GameSecurityEventDocuments.CollectionName)
            .WhereEqualTo("instanceId", roundId).GetSnapshotAsync()).Documents);
        Assert.Equal(observation.RequestId, accepted.GetValue<string>("requestId"));
        Assert.Equal(game, accepted.GetValue<string>("gameId"));
        Assert.Equal(Player, accepted.GetValue<string>("userId"));
        var intent = await database.Collection(FirestoreGameSecurityRequestStore.Intents)
            .Document(observation.RequestId).GetSnapshotAsync();
        Assert.Equal(observation, JsonSerializer.Deserialize<GameRequestObservation>(intent.GetValue<string>("observationJson"), RoundAuditCapture.Json));
        Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
    }

    private static async Task<WebApplication> StartWalletAsync(FirestoreDb database, string game, byte[] key)
    {
        var builder = LoopbackBuilder();
        builder.Configuration[$"GameWallet:Games:{game}:SigningKey"] = Convert.ToBase64String(key);
        builder.Configuration[$"GameWallet:Games:{game}:AllowAccountBalanceRead"] = "true";
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<IWalletRoundSettlementStore>(new FirestoreWalletRoundSettlementStore(database));
        var app = builder.Build();
        app.MapGameWalletSettlement();
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartWorkerAsync(FirestoreDb database, string game, string feature, byte[] workerKey,
        Uri wallet, byte[] walletKey, DropFirstCommittedWalletReply fault, ConcurrentQueue<VerifiedWorkerRequest> observed)
    {
        var builder = LoopbackBuilder();
        builder.Configuration[$"Features:{feature}Enabled"] = "true";
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Configuration["GameWallet:Url"] = wallet.AbsoluteUri;
        builder.Configuration["GameWallet:SigningKey"] = Convert.ToBase64String(walletKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddGameWalletClient(game).AddHttpMessageHandler(() => fault);
        AddGame(builder.Services, game);
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(game);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method))
                observed.Enqueue(new(GameWorkerAuthentication.GetAccountId(context), context.Request.Path.Value,
                    context.Request.Headers["Idempotency-Key"].ToString(), GameWorkerAuthentication.GetVerifiedBodySha256(context),
                    GameSecurityContext.Current));
            await next(context);
        });
        MapGame(app, game, worker: true);
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartGatewayAsync(FirestoreDb database, FirestoreAccountStore store,
        string game, string feature, Uri worker, byte[] workerKey)
    {
        var builder = LoopbackBuilder();
        builder.Configuration[$"Features:{feature}Enabled"] = "false";
        builder.Configuration[$"GameWorkers:{game}:Url"] = worker.AbsoluteUri;
        builder.Configuration[$"GameWorkers:{game}:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(store, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        AddGame(builder.Services, game);
        var app = builder.Build();
        app.UseRouting();
        app.UseGameSecurityRequests();
        app.UsePackagedGameWorkerForwarding();
        MapGame(app, game, worker: false);
        await app.StartAsync();
        return app;
    }

    private static WebApplicationBuilder LoopbackBuilder()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }

    private static void AddGame(IServiceCollection services, string game)
    {
        if (game == "baccarat") services.AddBaccaratServer();
        else if (game == "andar-bahar") services.AddAndarBaharServer();
        else services.AddDragonTigerServer();
    }

    private static void MapGame(WebApplication app, string game, bool worker)
    {
        string? Actor(HttpContext context) => worker ? GameWorkerAuthentication.GetAccountId(context)
            : throw new InvalidOperationException("The public request bypassed the signed worker.");
        if (game == "baccarat") app.MapBaccarat(Actor);
        else if (game == "andar-bahar")
            app.MapAndarBahar((context, _) => Task.FromResult<AndarBaharAccount?>(Actor(context) is { } actor ? new(actor, 0m) : null));
        else
            app.MapDragonTiger(async (context, ct) =>
            {
                if (Actor(context) is not { } actor) return null;
                var balance = await context.RequestServices.GetRequiredService<IGameWalletAccountReader>().GetStateAsync(actor, ct);
                return new DragonTigerAccount(actor, (balance.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException()) / 100m);
            });
    }

    private FirestoreDb Database()
    {
        _ = fixture;
        return new FirestoreDbBuilder
        {
            ProjectId = $"demo-signed-card-smoke-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
    }

    private static async Task<string> CreateAccountAsync(FirestoreDb database, FirestoreAccountStore store, string user, string name)
    {
        Assert.NotNull((await store.CreateAsync(user, name, name.ToUpperInvariant(), $"{user}@example.test",
            "emulator-test", "active", default)).Value);
        await database.Collection("userBalances").Document(user + "_slotsCredits").UpdateAsync(new Dictionary<string, object>
            { ["available"] = InitialCents / 100, ["availableFractionalCents"] = 0L });
        var token = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        await store.CreateSessionAsync(GameSecurityEventDocuments.Hash(token), user, now, now.AddHours(1), null, default);
        return token;
    }

    private static Task<long> BalanceAsync(FirestoreDb database, string actor) =>
        FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, actor, useLegacyLedger: false, default);

    private static string BodySha(string body) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    private static async Task<string?> ReadRoundIdAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("roundId").GetString();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string token,
        string? body = null, string? operation = null, string? expectedAccount = null, string[]? practiceValues = null)
    {
        using var message = new HttpRequestMessage(method, path);
        message.Headers.Add("Cookie", $"{AccountSessionCookie.Name}={token}");
        if (operation is not null) message.Headers.Add("Idempotency-Key", operation);
        if (expectedAccount is not null) message.Headers.Add("X-FortuneForge-Expected-Account", expectedAccount);
        if (practiceValues is not null) message.Headers.Add("X-FortuneForge-Practice", practiceValues);
        if (body is not null) message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(message);
    }

    private sealed record VerifiedWorkerRequest(string? Actor, string? Path, string OperationKey, string? BodySha256,
        GameRequestObservation? Observation);

    private sealed class DropFirstCommittedWalletReply(string game, bool loseFirst = true) : DelegatingHandler
    {
        private int loseReply = loseFirst ? 1 : 0;
        public ConcurrentQueue<string> SettlementBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var settlement = request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == GameWalletAuthentication.SettlementRoute(game);
            if (settlement) SettlementBodies.Enqueue(await request.Content!.ReadAsStringAsync(ct));
            var response = await base.SendAsync(request, ct);
            if (!settlement || !response.IsSuccessStatusCode || Interlocked.Exchange(ref loseReply, 0) == 0) return response;
            response.Dispose();
            throw new HttpRequestException("Lost the wallet reply after its successful commit.");
        }
    }
}
