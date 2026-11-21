using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Craps.Server;
using FortuneForge.Games.Dice;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Activity;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.Games.Craps;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

public sealed partial class SignedCardWorkerSmokeTests
{
    private const string CrapsGame = "craps";

    [Fact]
    public async Task Craps_mvc_gateway_recovers_opening_and_settles_saved_dice_and_odds_refund_while_paused()
    {
        const string roundsPath = "/api/games/craps/rounds";
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(database, accounts, Player, "Player");
        var otherToken = await CreateAccountAsync(database, accounts, OtherPlayer, "Other Player");
        var walletKey = RandomNumberGenerator.GetBytes(32);
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var fault = new DropFirstCommittedWalletReply(CrapsGame);
        var observed = new ConcurrentQueue<VerifiedWorkerRequest>();
        // Two legal rolls establish and then resolve a point of four, independently of RNG.
        var dice = new Queue<DicePair>([new(new DieValue(1), new DieValue(3)), new(new DieValue(2), new DieValue(2))]);
        await using var wallet = await StartWalletAsync(database, CrapsGame, walletKey);
        await using var worker = await StartCrapsWorkerAsync(database, wallet.Urls.Single(), walletKey, workerKey, fault, dice, observed);
        await using var gateway = await StartCrapsGatewayAsync(database, accounts, worker.Urls.Single(), workerKey);
        using var browser = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
        Task<HttpResponseMessage> Post(string path, string json) => SendAsync(browser, HttpMethod.Post, path, token, json, expectedAccount: Player);
        Task<HttpResponseMessage> Get(string path) => SendAsync(browser, HttpMethod.Get, path, token, expectedAccount: Player);

        using var empty = await Get(roundsPath + "/active");
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
        var opening = new StartCrapsRoundRequest(1m, [new("field", 1m)], CrapsKey());
        var openingJson = CrapsJson(opening);
        using var stale = await SendAsync(browser, HttpMethod.Post, roundsPath, token, openingJson, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Empty(observed);
        using var lostReply = await Post(roundsPath, openingJson);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lostReply.StatusCode);
        var first = Assert.Single(observed);
        Assert.Equal((Player, roundsPath, BodySha(openingJson)), (first.Actor, first.Path, first.BodySha256));
        var original = Assert.IsType<GameRequestObservation>(first.Observation);
        Assert.Equal(opening.OperationId, original.CommandId);
        var savedWager = Assert.Single((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);
        Assert.Equal(GameWalletSettlementIdentity.CreateId(CrapsGame, Player, opening.OperationId!), savedWager.Id);
        Assert.Equal(InitialCents - 200, await BalanceAsync(database, Player));
        Assert.Empty((await database.Collection("crapsPaidRounds").GetSnapshotAsync()).Documents);

        worker.Configuration["Features:CrapsEnabled"] = "false";
        gateway.Configuration["Features:CrapsEnabled"] = "false";
        using var restored = await Get(roundsPath + "/active");
        var opened = await ReadCrapsRoundAsync(restored);
        Assert.Equal("come-out", opened.Phase);
        Assert.Equal(200, opened.TotalWagerCents);
        Assert.Equal(InitialCents - 200, opened.WalletBalanceCents);
        var originalOpen = await restored.Content.ReadAsStringAsync();
        using var sameOpening = await Post(roundsPath, openingJson);
        Assert.Equal(HttpStatusCode.OK, sameOpening.StatusCode);
        Assert.Equal(originalOpen, await sameOpening.Content.ReadAsStringAsync());
        using var changedOpening = await Post(roundsPath, CrapsJson(opening with { Stake = 2m }));
        Assert.Equal(HttpStatusCode.Conflict, changedOpening.StatusCode);

        var roundPath = $"{roundsPath}/{opened.RoundId:D}";
        var firstRollKey = CrapsKey();
        var firstRollJson = CrapsJson(new CrapsOperationRequest(firstRollKey, opened.Revision, 1));
        using var firstRoll = await Post(roundPath + "/roll", firstRollJson);
        var point = await ReadCrapsRoundAsync(firstRoll);
        var frozenRoll = await firstRoll.Content.ReadAsStringAsync();
        Assert.Equal("point", point.Phase);
        Assert.Equal(4, point.Point);
        Assert.Equal(InitialCents, point.WalletBalanceCents);

        var oddsKey = CrapsKey();
        using var addOdds = await Post(roundPath + "/odds", CrapsJson(new PlaceCrapsOddsRequest(2m, oddsKey, point.Revision, 2)));
        var odds = await ReadCrapsRoundAsync(addOdds);
        Assert.Equal(InitialCents - 200, odds.WalletBalanceCents);
        var refundKey = CrapsKey();
        using var refund = await Post(roundPath + "/wagers", CrapsJson(new SetCrapsWagerRequest("odds", 0m, refundKey, odds.Revision, 2)));
        var refunded = await ReadCrapsRoundAsync(refund);
        Assert.True(Assert.Single(refunded.ExtraBets, bet => bet.Kind == "odds").Cancelled);
        Assert.Equal(InitialCents, refunded.WalletBalanceCents);
        var finishKey = CrapsKey();
        using var finish = await Post(roundPath + "/roll", CrapsJson(new CrapsOperationRequest(finishKey, refunded.Revision, 2)));
        var terminal = await ReadCrapsRoundAsync(finish);
        Assert.Equal("resolved", terminal.Phase);
        Assert.Equal("point-hit", terminal.LastOutcome?.Result);
        Assert.Equal(InitialCents + 200, terminal.WalletBalanceCents);
        Assert.Equal(new[] { (1, 3), (2, 2) }, terminal.Rolls.Select(roll => (roll.First, roll.Second)));
        Assert.Empty(dice);

        using var replay = await Post(roundPath + "/roll", firstRollJson);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(frozenRoll, await replay.Content.ReadAsStringAsync());
        using var history = await Get(roundPath);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Equal(await finish.Content.ReadAsStringAsync(), await history.Content.ReadAsStringAsync());
        using var foreign = await SendAsync(browser, HttpMethod.Get, roundPath, otherToken, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var active = await Get(roundsPath + "/active");
        Assert.Equal(HttpStatusCode.NoContent, active.StatusCode);
        using var paused = await Post(roundsPath, CrapsJson(opening with { OperationId = CrapsKey() }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, paused.StatusCode);
        using var pausedBody = JsonDocument.Parse(await paused.Content.ReadAsStringAsync());
        Assert.Equal("craps-disabled", pausedBody.RootElement.GetProperty("code").GetString());
        Assert.Equal(InitialCents + 200, await BalanceAsync(database, Player));
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));
        var walletBodies = fault.SettlementBodies.ToArray();
        Assert.Equal(6, walletBodies.Length);
        Assert.Equal(walletBodies[0], walletBodies[1]);

        var receipts = (await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents;
        var ledger = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(5, receipts.Count);
        Assert.Equal(5, ledger.Count);
        Assert.Equal(savedWager.UpdateTime, Assert.Single(receipts, doc => doc.Id == savedWager.Id).UpdateTime);
        foreach (var (key, wager, payout) in new (string, long, long)[]
        {
            (opening.OperationId!, 200, 0), (firstRollKey, 0, 200), (oddsKey, 200, 0), (refundKey, 0, 200), (finishKey, 0, 200),
        })
        {
            var id = GameWalletSettlementIdentity.CreateId(CrapsGame, Player, key);
            var receipt = Assert.Single(receipts, doc => doc.Id == id);
            Assert.Equal((wager, payout), (receipt.GetValue<long>("wagerCents"), receipt.GetValue<long>("payoutCents")));
            var leg = Assert.Single(ledger, doc => doc.GetValue<string>("walletSettlementId") == id);
            Assert.Equal((Player, CrapsGame, payout - wager), (leg.GetValue<string>("userId"), leg.GetValue<string>("gameId"),
                leg.GetValue<long>("amountCents")));
        }
        var roundId = terminal.RoundId.ToString("N");
        var journal = (await database.Collection(GameSecurityEventDocuments.CollectionName)
            .WhereEqualTo("instanceId", roundId).GetSnapshotAsync()).Documents;
        Assert.Contains(journal, entry => entry.GetValue<string>("requestId") == original.RequestId);
        var audit = Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        Assert.Equal(roundId, audit.GetValue<string>("roundId"));
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(audit.Id).GetSnapshotAsync();
        var payload = evidence.GetValue<string>("payloadJson");
        Assert.Equal(audit.GetValue<string>("evidenceSha256"), GameSecurityEventDocuments.Hash(payload));
        using var details = JsonDocument.Parse(payload);
        Assert.Equal(200, details.RootElement.GetProperty("wagerCents").GetInt64());
        Assert.Equal(400, details.RootElement.GetProperty("payoutCents").GetInt64());
        var summary = FirestoreGameRoundAuditStore.Summary(audit);
        Assert.Empty(PaidCrapsReplayAudit.Inspect(summary, payload,
            journal.Select(entry => entry.GetValue<string>("payloadJson")).ToArray()));
        Assert.Empty(await PaidCrapsReplayAudit.InspectFinancialAsync(database, summary, payload, default));
    }

    private static async Task<WebApplication> StartCrapsWorkerAsync(FirestoreDb database, string walletUrl, byte[] walletKey,
        byte[] workerKey, DropFirstCommittedWalletReply fault, Queue<DicePair> dice, ConcurrentQueue<VerifiedWorkerRequest> observed)
    {
        var builder = LoopbackBuilder();
        builder.Configuration["Features:CrapsEnabled"] = "true";
        builder.Configuration["GameWorker:Mode"] = "hosted";
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Configuration["GameWallet:Url"] = walletUrl;
        builder.Configuration["GameWallet:SigningKey"] = Convert.ToBase64String(walletKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddGameWalletClient(CrapsGame).AddHttpMessageHandler(() => fault);
        builder.Services.AddCrapsWorker(builder.Configuration);
        builder.Services.AddSingleton(provider => new FirestorePaidCrapsStore(database,
            provider.GetRequiredService<IGameWalletClient>(), provider.GetRequiredService<IGameWalletAccountReader>(),
            dice.Dequeue, () => builder.Configuration.GetValue("Features:CrapsEnabled", false)));
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(CrapsGame);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                var observation = GameSecurityContext.Current;
                observed.Enqueue(new(GameWorkerAuthentication.GetAccountId(context), context.Request.Path.Value,
                    observation?.CommandId ?? "", GameWorkerAuthentication.GetVerifiedBodySha256(context), observation));
            }
            await next(context);
        });
        app.MapCrapsWorker(GameWorkerAuthentication.GetAccountId);
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartCrapsGatewayAsync(FirestoreDb database, FirestoreAccountStore store, string workerUrl, byte[] workerKey)
    {
        var builder = LoopbackBuilder();
        builder.Configuration["Features:CrapsEnabled"] = "true";
        builder.Configuration[$"GameWorkers:{CrapsGame}:ForwardPublicRoutes"] = "true";
        builder.Configuration[$"GameWorkers:{CrapsGame}:Url"] = workerUrl;
        builder.Configuration[$"GameWorkers:{CrapsGame}:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(store, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        builder.Services.AddControllers().AddApplicationPart(typeof(CrapsController).Assembly);
        builder.Services.AddRateLimiter(options =>
        {
            options.AddConcurrencyLimiter(RateLimitPolicies.SlotReads, limiter => limiter.PermitLimit = 100);
            options.AddConcurrencyLimiter(RateLimitPolicies.SlotSpins, limiter => limiter.PermitLimit = 100);
        });
        var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseGameSecurityRequests();
        app.UsePackagedGameWorkerForwarding();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static async Task<CrapsRoundResponse> ReadCrapsRoundAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<CrapsRoundResponse>(JsonSerializer.Deserialize<CrapsRoundResponse>(
            await response.Content.ReadAsStringAsync(), JsonSerializerOptions.Web));
    }

    private static string CrapsKey() => Guid.NewGuid().ToString("N");

    private static string CrapsJson<T>(T action) => JsonSerializer.Serialize(action, JsonSerializerOptions.Web);
}
