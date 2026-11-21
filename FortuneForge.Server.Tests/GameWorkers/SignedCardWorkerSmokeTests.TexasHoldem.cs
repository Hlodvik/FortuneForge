using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.TexasHoldem;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.SecurityAudit;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Managed = FortuneForge.Games.Abstractions.ManagedPlayers;

namespace FortuneForge.Server.Tests.GameWorkers;

public sealed partial class SignedCardWorkerSmokeTests
{
    private const string HoldemGame = CreditHoldemBackend.GameId;

    [Fact]
    public async Task Texas_Holdem_mvc_gateway_recovers_scoped_escrow_debit_and_refund_while_paused()
    {
        const string queuePath = CreditHoldemBackend.ApiPath + "/queue";
        const string sessionPath = CreditHoldemBackend.ApiPath + "/session";
        const string joinKey = "texas-holdem-gateway-join-0001";
        const string cancelKey = "texas-holdem-gateway-cancel-0001";
        const string joinBody = "{ \"expectedVersion\": 0, \"buyInCredits\": 25, \"tableRuleId\": \"standard\" }";
        const long buyIn = 2500;
        const string playerName = "Trusted Holdem Player";
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(database, accounts, Player, playerName);
        var otherToken = await CreateAccountAsync(database, accounts, OtherPlayer, "Other Player");
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var accountKey = RandomNumberGenerator.GetBytes(32);
        var batchKey = RandomNumberGenerator.GetBytes(32);
        var rosterKey = RandomNumberGenerator.GetBytes(32);
        var fault = new DropHoldemBatchReplies();
        var observed = new ConcurrentQueue<VerifiedWorkerRequest>();
        await using var platform = await StartHoldemWalletAsync(database, accountKey, batchKey, rosterKey);
        await using var worker = await StartHoldemWorkerAsync(database, platform.Urls.Single(), workerKey, accountKey, batchKey, rosterKey, fault, observed);
        await using var gateway = await StartHoldemGatewayAsync(database, accounts, worker.Urls.Single(), workerKey);
        using var browser = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
        Task<HttpResponseMessage> Call(HttpMethod method, string path, string? body = null, string? key = null) =>
            SendAsync(browser, method, path, token, body, key, Player);

        using var stale = await SendAsync(browser, HttpMethod.Post, queuePath, token, joinBody, joinKey, OtherPlayer);
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Empty(observed);

        using var lostJoin = await Call(HttpMethod.Post, queuePath, joinBody, joinKey);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lostJoin.StatusCode);
        AssertHoldemCommand(Assert.Single(observed), queuePath, joinKey, joinBody, "POST");
        var receipts = database.Collection(GameWalletBatchFirestoreStore.ReceiptCollection);
        var held = Assert.Single((await receipts.GetSnapshotAsync()).Documents);
        Assert.Equal(InitialCents - buyIn, await BalanceAsync(database, Player));
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));
        Assert.Empty((await database.Collection("creditHoldemSessions").GetSnapshotAsync()).Documents);

        platform.Configuration[$"GameWallet:Games:{HoldemGame}:AllowPlayerNameRead"] = "false";
        var accountReader = worker.Services.GetRequiredService<IGameWalletAccountReader>();
        Assert.Equal(new GameWalletAccountState(0, InitialCents - buyIn), await accountReader.GetStateAsync(Player, default));
        worker.Configuration[CreditHoldemBackend.EnabledSetting] = "false";
        gateway.Configuration[CreditHoldemBackend.EnabledSetting] = "false";
        using var joinReplay = await Call(HttpMethod.Post, queuePath, joinBody, joinKey);
        Assert.Equal(HttpStatusCode.OK, joinReplay.StatusCode);
        var frozenJoin = await joinReplay.Content.ReadAsStringAsync();
        using var joinReceipt = JsonDocument.Parse(frozenJoin);
        Assert.Equal(75m, joinReceipt.RootElement.GetProperty("balanceCredits").GetDecimal());

        using var recovered = await Call(HttpMethod.Get, sessionPath);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        using var queue = JsonDocument.Parse(await recovered.Content.ReadAsStringAsync());
        Assert.Equal(CreditHoldemSessionKinds.Queue, queue.RootElement.GetProperty("kind").GetString());
        Assert.Equal(playerName, queue.RootElement.GetProperty("players")[0].GetProperty("displayName").GetString());
        var ticketId = queue.RootElement.GetProperty("ticketId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(ticketId));
        var cancelPath = $"{queuePath}/{ticketId}";
        var cancelBody = $"{{ \"expectedVersion\": {queue.RootElement.GetProperty("version").GetInt32()} }}\r\n";
        Assert.Equal(ticketId, joinReceipt.RootElement.GetProperty("session").GetProperty("ticketId").GetString());
        Assert.Equal(playerName, joinReceipt.RootElement.GetProperty("session").GetProperty("players")[0].GetProperty("displayName").GetString());

        using var foreign = await SendAsync(browser, HttpMethod.Delete, cancelPath, otherToken, cancelBody, cancelKey, OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Single((await receipts.GetSnapshotAsync()).Documents);
        using var lostCancel = await Call(HttpMethod.Delete, cancelPath, cancelBody, cancelKey);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lostCancel.StatusCode);
        var deletion = Assert.Single(observed, request => request.Actor == Player && request.OperationKey == cancelKey);
        AssertHoldemCommand(deletion, cancelPath, cancelKey, cancelBody, "DELETE");
        var refunded = Assert.Single((await receipts.GetSnapshotAsync()).Documents, document => document.Id != held.Id);
        Assert.Equal(InitialCents, await BalanceAsync(database, Player));

        using var retryCancel = await Call(HttpMethod.Delete, cancelPath, cancelBody, cancelKey);
        Assert.Equal(HttpStatusCode.OK, retryCancel.StatusCode);
        var frozenCancel = await retryCancel.Content.ReadAsStringAsync();
        using var cancelReceipt = JsonDocument.Parse(frozenCancel);
        Assert.Equal(CreditHoldemSessionKinds.Idle, cancelReceipt.RootElement.GetProperty("session").GetProperty("kind").GetString());
        Assert.Equal(100m, cancelReceipt.RootElement.GetProperty("balanceCredits").GetDecimal());
        using var repeatCancel = await Call(HttpMethod.Delete, cancelPath, cancelBody, cancelKey);
        Assert.Equal(HttpStatusCode.OK, repeatCancel.StatusCode);
        Assert.Equal(frozenCancel, await repeatCancel.Content.ReadAsStringAsync());
        using var repeatJoin = await Call(HttpMethod.Post, queuePath, joinBody, joinKey);
        Assert.Equal(HttpStatusCode.OK, repeatJoin.StatusCode);
        Assert.Equal(frozenJoin, await repeatJoin.Content.ReadAsStringAsync());
        using var resumed = await Call(HttpMethod.Get, sessionPath);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        using var idle = JsonDocument.Parse(await resumed.Content.ReadAsStringAsync());
        Assert.Equal(CreditHoldemSessionKinds.Idle, idle.RootElement.GetProperty("kind").GetString());
        using var paused = await Call(HttpMethod.Post, queuePath, joinBody, "texas-holdem-gateway-paused-0001");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, paused.StatusCode);
        using var unavailable = JsonDocument.Parse(await paused.Content.ReadAsStringAsync());
        Assert.Equal("credit-texas-holdem-disabled", unavailable.RootElement.GetProperty("code").GetString());

        using var history = await Call(HttpMethod.Get, CreditHoldemBackend.ApiPath + "/history?limit=1");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using var hands = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        Assert.Equal(0, hands.RootElement.GetProperty("items").GetArrayLength());
        Assert.Empty((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        Assert.Equal(InitialCents, await BalanceAsync(database, Player));
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));
        Assert.Empty((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);

        var batches = fault.Bodies.ToArray();
        Assert.Equal(4, batches.Length);
        Assert.Equal(batches[0], batches[1]);
        Assert.Equal(batches[2], batches[3]);
        var archived = (await receipts.GetSnapshotAsync()).Documents;
        Assert.Equal(2, archived.Count);
        Assert.Equal(held.UpdateTime, Assert.Single(archived, item => item.Id == held.Id).UpdateTime);
        Assert.Equal(refunded.UpdateTime, Assert.Single(archived, item => item.Id == refunded.Id).UpdateTime);
        var ledger = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(2, ledger.Count);
        AssertHoldemBatch(batches[0], held, ledger, buyIn, 0, InitialCents, InitialCents - buyIn);
        AssertHoldemBatch(batches[2], refunded, ledger, 0, buyIn, InitialCents - buyIn, InitialCents);
        var journal = (await database.Collection("creditHoldemWalletJournal").GetSnapshotAsync()).Documents;
        Assert.Equal(2, journal.Count);
        foreach (var (batch, reason, cents) in new[] { (held, "texas-holdem-buy-in", -buyIn), (refunded, "texas-holdem-buy-in-refund", buyIn) })
        {
            var entry = Assert.Single(journal, item => item.GetValue<string>("walletBatchId") == batch.Id);
            Assert.Equal((Player, ticketId, reason, cents), (entry.GetValue<string>("accountId"), entry.GetValue<string>("sourceKey"),
                entry.GetValue<string>("reason"), entry.GetValue<long>("deltaCents")));
        }
    }

    private static void AssertHoldemBatch(string body, DocumentSnapshot document, IReadOnlyList<DocumentSnapshot> ledger,
        long wager, long payout, long before, long after)
    {
        var request = Assert.IsType<GameWalletBatchRequest>(JsonSerializer.Deserialize<GameWalletBatchRequest>(body, GameWalletAuthentication.WireJson));
        var effect = Assert.Single(request.Effects);
        Assert.Equal((Player, wager, payout), (effect.AccountId, effect.WagerCents, effect.PayoutCents));
        Assert.Equal(GameWalletBatchIdentity.CreateId(HoldemGame, request.OperationId), document.Id);
        Assert.Equal(BodySha(body).ToLowerInvariant(), document.GetValue<string>("requestBodySha256"));
        var receipt = Assert.IsType<GameWalletBatchReceipt>(JsonSerializer.Deserialize<GameWalletBatchReceipt>(
            document.GetValue<string>("receiptJson"), GameWalletAuthentication.WireJson));
        Assert.True(GameWalletBatchVerifier.MatchesSettlement(receipt, HoldemGame, Player, request));
        var account = Assert.Single(receipt.Accounts);
        Assert.Equal((before, after), (account.BalanceBeforeCents, account.BalanceAfterCents));
        var leg = Assert.Single(ledger, item => item.GetValue<string>("walletBatchId") == receipt.BatchId);
        Assert.Equal((Player, HoldemGame, payout - wager), (leg.GetValue<string>("userId"), leg.GetValue<string>("gameId"), leg.GetValue<long>("amountCents")));
    }

    private static void AssertHoldemCommand(VerifiedWorkerRequest command, string path, string key, string body, string method)
    {
        Assert.Equal((Player, path, key, BodySha(body)), (command.Actor, command.Path, command.OperationKey, command.BodySha256));
        var observation = Assert.IsType<GameRequestObservation>(command.Observation);
        Assert.Equal((HoldemGame, Player, method, key), (observation.GameId, observation.UserId, observation.Method, observation.CommandId));
    }

    private static async Task<WebApplication> StartHoldemWalletAsync(FirestoreDb database, byte[] accountKey, byte[] batchKey, byte[] rosterKey)
    {
        var builder = LoopbackBuilder();
        var wallet = $"GameWallet:Games:{HoldemGame}:";
        builder.Configuration[wallet + "SigningKey"] = Convert.ToBase64String(accountKey);
        builder.Configuration[wallet + "AllowAccountBalanceRead"] = "true";
        builder.Configuration[wallet + "AllowPlayerNameRead"] = "true";
        builder.Configuration[wallet + "BatchSigningKey"] = Convert.ToBase64String(batchKey);
        builder.Configuration[wallet + "AllowMultiAccountBatch"] = "true";
        builder.Configuration[$"ManagedPlayerRoster:Games:{HoldemGame}:Enabled"] = "true";
        builder.Configuration[$"ManagedPlayerRoster:Games:{HoldemGame}:SigningKey"] = Convert.ToBase64String(rosterKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<IWalletRoundSettlementStore>(new FirestoreWalletRoundSettlementStore(database));
        var profiles = new FirestoreManagedPlayerProfileRepository(database);
        builder.Services.AddSingleton<IManagedPlayerQueuer>(new ManagedPlayerQueuer(new ManagedPlayerProfileGenerator(profiles),
            profiles, new FirestoreManagedPlayerAssignmentStore(database)));
        var app = builder.Build();
        app.MapGameWalletSettlement();
        app.MapGameWalletBatchSettlement();
        app.MapGameManagedPlayerRoster();
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartHoldemWorkerAsync(FirestoreDb database, string platformUrl, byte[] workerKey,
        byte[] accountKey, byte[] batchKey, byte[] rosterKey, DropHoldemBatchReplies fault, ConcurrentQueue<VerifiedWorkerRequest> observed)
    {
        var builder = LoopbackBuilder();
        builder.Configuration[CreditHoldemBackend.EnabledSetting] = "true";
        builder.Configuration[$"{CreditHoldemOptions.SectionName}:AllowSingleHumanBotFill"] = "false";
        builder.Configuration["GameWorker:Mode"] = "hosted";
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Configuration["GameWallet:Url"] = platformUrl;
        builder.Configuration["GameWallet:SigningKey"] = Convert.ToBase64String(accountKey);
        builder.Configuration["GameWallet:BatchSigningKey"] = Convert.ToBase64String(batchKey);
        builder.Configuration["ManagedPlayerRoster:Url"] = platformUrl;
        builder.Configuration["ManagedPlayerRoster:SigningKey"] = Convert.ToBase64String(rosterKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddGameWalletClient(HoldemGame);
        builder.Services.AddGameWalletBatchClient(HoldemGame).AddHttpMessageHandler(() => fault);
        builder.Services.AddManagedPlayerRosterClient(HoldemGame);
        builder.Services.AddSingleton<Managed.IManagedTablePopulationDirector, Managed.ManagedTablePopulationDirector>();
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(HoldemGame);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
                observed.Enqueue(new(GameWorkerAuthentication.GetAccountId(context), context.Request.Path.Value,
                    context.Request.Headers["Idempotency-Key"].ToString(), GameWorkerAuthentication.GetVerifiedBodySha256(context),
                    GameSecurityContext.Current));
            await next(context);
        });
        app.MapCreditHoldem(GameWorkerAuthentication.GetAccountId);
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartHoldemGatewayAsync(FirestoreDb database, FirestoreAccountStore store, string workerUrl, byte[] workerKey)
    {
        var builder = LoopbackBuilder();
        builder.Configuration[CreditHoldemBackend.EnabledSetting] = "true";
        builder.Configuration[$"GameWorkers:{HoldemGame}:ForwardPublicRoutes"] = "true";
        builder.Configuration[$"GameWorkers:{HoldemGame}:Url"] = workerUrl;
        builder.Configuration[$"GameWorkers:{HoldemGame}:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(store, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        builder.Services.AddControllers().AddApplicationPart(typeof(CreditHoldemController).Assembly);
        builder.Services.AddRateLimiter(options =>
        {
            options.AddConcurrencyLimiter(RateLimitPolicies.CreditHoldemReads, limiter => limiter.PermitLimit = 100);
            options.AddConcurrencyLimiter(RateLimitPolicies.CreditHoldemWrites, limiter => limiter.PermitLimit = 100);
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

    private sealed class DropHoldemBatchReplies : DelegatingHandler
    {
        private int debitPending = 1;
        private int refundPending = 1;
        internal ConcurrentQueue<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method != HttpMethod.Post || request.RequestUri?.AbsolutePath != GameWalletBatchAuthentication.Route(HoldemGame))
                return await base.SendAsync(request, token);
            var body = await request.Content!.ReadAsStringAsync(token);
            Bodies.Enqueue(body);
            var batch = JsonSerializer.Deserialize<GameWalletBatchRequest>(body, GameWalletAuthentication.WireJson)!;
            var response = await base.SendAsync(request, token);
            if (!response.IsSuccessStatusCode) return response;
            var drop = batch.Effects.Any(effect => effect.WagerCents > 0)
                ? Interlocked.Exchange(ref debitPending, 0) == 1
                : Interlocked.Exchange(ref refundPending, 0) == 1;
            if (!drop) return response;
            response.Dispose();
            throw new HttpRequestException("Lost the Hold'em batch reply after its successful commit.");
        }
    }
}
