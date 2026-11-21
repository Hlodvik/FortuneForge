using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Blackjack;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Cards.Blackjack.Table;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

public sealed partial class SignedCardWorkerSmokeTests
{
    private const string BlackjackGameId = "blackjack";
    private const string BlackjackPath = "/api/cards/blackjack";
    private const string BlackjackActionKey = "signed-blackjack-action-0001";

    [Fact]
    public async Task Blackjack_mvc_gateway_recovers_one_paid_hand_while_new_wagers_are_paused()
    {
        const string starts = BlackjackPath + "/games";
        const string body = "{ \"wager\": 0.50 }\n";
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(database, accounts, Player, "Player");
        var otherToken = await CreateAccountAsync(database, accounts, OtherPlayer, "Other Player");
        var walletKey = RandomNumberGenerator.GetBytes(32);
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var fault = new DropFirstCommittedWalletReply(BlackjackGameId);
        var observed = new ConcurrentQueue<VerifiedWorkerRequest>();
        await using var wallet = await StartWalletAsync(database, BlackjackGameId, walletKey);
        await using var worker = await StartBlackjackWorkerAsync(database, new Uri(wallet.Urls.Single()), walletKey, workerKey, observed, fault);
        await using var gateway = await StartBlackjackGatewayAsync(database, accounts, new Uri(worker.Urls.Single()), workerKey);
        using var browser = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
        using var direct = new HttpClient { BaseAddress = new Uri(worker.Urls.Single()) };
        var receipts = database.Collection("walletRoundSettlements");
        var games = database.Collection(BlackjackBackend.SoloGames);

        using var unsigned = await SendAsync(direct, HttpMethod.Post, starts, token, body, OperationKey);
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        using var switched = await SendAsync(browser, HttpMethod.Post, starts, otherToken, body, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Forbidden, switched.StatusCode);
        Assert.Empty((await receipts.GetSnapshotAsync()).Documents);
        Assert.Empty(observed);

        using var lost = await SendAsync(browser, HttpMethod.Post, starts, token, body, OperationKey, Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        var openingReceipt = Assert.Single((await receipts.GetSnapshotAsync()).Documents);
        var pendingGame = Assert.Single((await games.GetSnapshotAsync()).Documents);
        var pending = pendingGame.GetValue<string>("pendingWalletCommand");
        Assert.Equal(pending, openingReceipt.GetValue<string>("operationId"));
        Assert.Equal(WagerCents, openingReceipt.GetValue<long>("wagerCents"));
        Assert.Equal(InitialCents - WagerCents, await BalanceAsync(database, Player));
        var openingObservation = AssertSignedBlackjack(Assert.Single(observed), starts, body, OperationKey);
        var handPath = $"{starts}/{pendingGame.Id}";
        using var foreignPending = await SendAsync(browser, HttpMethod.Get, handPath, otherToken, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreignPending.StatusCode);
        Assert.Equal(pending, (await pendingGame.Reference.GetSnapshotAsync()).GetValue<string>("pendingWalletCommand"));
        Assert.Single(fault.SettlementBodies);

        worker.Configuration["Features:BlackjackEnabled"] = "false";
        using var recovered = await SendAsync(browser, HttpMethod.Post, starts, token, body, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        Assert.Equal(handPath, recovered.Headers.Location?.OriginalString);
        var active = Assert.IsType<BlackjackGameResponse>(await recovered.Content.ReadFromJsonAsync<BlackjackGameResponse>());
        Assert.Equal(pendingGame.Id, active.GameId);
        Assert.Equal(BlackjackStatuses.Active, active.Status);
        Assert.True(active.Dealer.Cards[1].Hidden);
        Assert.False((await pendingGame.Reference.GetSnapshotAsync()).ContainsField("pendingWalletCommand"));
        var retryObservation = AssertSignedBlackjack(observed.ToArray()[1], starts, body, OperationKey);
        Assert.NotEqual(openingObservation.RequestId, retryObservation.RequestId);
        using var conflictingOpen = await SendAsync(browser, HttpMethod.Post, starts, token, "{\"wager\":1.00}", OperationKey, Player);
        Assert.Equal(HttpStatusCode.Conflict, conflictingOpen.StatusCode);
        using var newWager = await SendAsync(browser, HttpMethod.Post, starts, token, body, "blackjack-paused-opening-0001", Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, newWager.StatusCode);
        Assert.Single((await games.GetSnapshotAsync()).Documents);

        var actionPath = handPath + "/actions";
        var actionBody = $"{{ \"action\": \"stand\", \"expectedVersion\": {active.Version} }}\n";
        using var finished = await SendAsync(browser, HttpMethod.Post, actionPath, token, actionBody, BlackjackActionKey, Player);
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
        var result = Assert.IsType<BlackjackGameResponse>(await finished.Content.ReadFromJsonAsync<BlackjackGameResponse>());
        Assert.Equal(BlackjackStatuses.Completed, result.Status);
        Assert.Equal(1m, result.Payout);
        Assert.Equal(active.Version + 1, result.Version);
        var actionObservation = AssertSignedBlackjack(observed.Last(), actionPath, actionBody, BlackjackActionKey);
        using var replay = await SendAsync(browser, HttpMethod.Post, actionPath, token, actionBody, BlackjackActionKey, Player);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await finished.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var conflict = await SendAsync(browser, HttpMethod.Post, actionPath, token,
            actionBody.Replace("stand", "hit", StringComparison.Ordinal), BlackjackActionKey, Player);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var owned = await SendAsync(browser, HttpMethod.Get, handPath, token, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, owned.StatusCode);
        var archived = Assert.IsType<BlackjackGameResponse>(await owned.Content.ReadFromJsonAsync<BlackjackGameResponse>());
        Assert.Equal((result.GameId, result.Status, result.Version, result.Payout),
            (archived.GameId, archived.Status, archived.Version, archived.Payout));
        using var foreign = await SendAsync(browser, HttpMethod.Get, handPath, otherToken, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var walletBodies = fault.SettlementBodies.ToArray();
        Assert.Equal(3, walletBodies.Length);
        Assert.Equal(walletBodies[0], walletBodies[1]);
        var storedWallet = (await receipts.GetSnapshotAsync()).Documents;
        Assert.Equal(2, storedWallet.Count);
        Assert.Equal(openingReceipt.UpdateTime, Assert.Single(storedWallet, receipt => receipt.Id == openingReceipt.Id).UpdateTime);
        var terminal = Assert.Single(storedWallet, receipt => receipt.Id != openingReceipt.Id);
        Assert.Equal(100L, terminal.GetValue<long>("payoutCents"));
        Assert.Equal(BodySha(walletBodies[0]), openingReceipt.GetValue<string>("requestBodySha256"), ignoreCase: true);
        Assert.Equal(BodySha(walletBodies[2]), terminal.GetValue<string>("requestBodySha256"), ignoreCase: true);
        var ledger = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(2, ledger.Count);
        Assert.Equal(-WagerCents, Assert.Single(ledger, row => row.Id == openingReceipt.Id + "-wager").GetValue<long>("amountCents"));
        Assert.Equal(100L, Assert.Single(ledger, row => row.Id == terminal.Id + "-payout").GetValue<long>("amountCents"));
        Assert.Equal(InitialCents - WagerCents + 100, await BalanceAsync(database, Player));
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));

        var audit = Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(audit.Id).GetSnapshotAsync();
        var payload = evidence.GetValue<string>("payloadJson");
        Assert.Equal(actionObservation.RequestId, evidence.GetValue<string>("requestId"));
        Assert.Equal(audit.GetValue<string>("evidenceSha256"), GameSecurityEventDocuments.Hash(payload));
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, FirestoreGameRoundAuditStore.Summary(audit), payload, default));
    }

    [Fact]
    public async Task Blackjack_mvc_gateway_signs_the_exact_delete_body_for_the_existing_queue_owner()
    {
        const string cancelKey = "blackjack-signed-queue-cancel-0001";
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(database, accounts, Player, "Player");
        var otherToken = await CreateAccountAsync(database, accounts, OtherPlayer, "Other Player");
        var walletKey = RandomNumberGenerator.GetBytes(32);
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var observed = new ConcurrentQueue<VerifiedWorkerRequest>();
        await using var wallet = await StartWalletAsync(database, BlackjackGameId, walletKey);
        await using var worker = await StartBlackjackWorkerAsync(database, new Uri(wallet.Urls.Single()), walletKey, workerKey, observed);
        var table = worker.Services.GetRequiredService<IBlackjackTableStore>();
        // Keep the existing queue ticket within its matchmaking grace throughout the HTTP requests.
        var joined = await table.JoinAsync(Player, "Player", 0, "blackjack-fixture-queue-join-0001", DateTime.UtcNow.AddHours(1), default);
        var queued = Assert.IsType<BlackjackTableQueueSessionResponse>(joined.Session);
        var ticket = database.Collection(BlackjackBackend.Tickets).Document(queued.TicketId);
        using (var savedTicket = JsonDocument.Parse((await ticket.GetSnapshotAsync()).GetValue<string>("ticketJson")))
            Assert.Equal(Player, savedTicket.RootElement.GetProperty("userId").GetString());
        await using var gateway = await StartBlackjackGatewayAsync(database, accounts, new Uri(worker.Urls.Single()), workerKey);
        using var browser = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
        var path = $"{BlackjackPath}/table/queue/{queued.TicketId}";
        var body = $"{{ \"expectedVersion\": {queued.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}\n";

        using var foreign = await SendAsync(browser, HttpMethod.Delete, path, otherToken, body, cancelKey, OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.True((await ticket.GetSnapshotAsync()).Exists);
        using var cancelled = await SendAsync(browser, HttpMethod.Delete, path, token, body, cancelKey, Player);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        AssertSignedBlackjack(observed.Last(), path, body, cancelKey);
        var archived = await cancelled.Content.ReadAsStringAsync();
        using var decoded = JsonDocument.Parse(archived);
        Assert.Equal("idle", decoded.RootElement.GetProperty("session").GetProperty("kind").GetString());
        Assert.False((await ticket.GetSnapshotAsync()).Exists);
        var command = database.Collection(BlackjackBackend.TableWalletCommands)
            .Document(BlackjackTableIds.Hash($"{Player}\n{cancelKey}"));
        var accepted = await command.GetSnapshotAsync();
        Assert.Equal(Player, accepted.GetValue<string>("userId"));
        using var retry = await SendAsync(browser, HttpMethod.Delete, path, token, body, cancelKey, Player);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(archived, await retry.Content.ReadAsStringAsync());
        using var different = await SendAsync(browser, HttpMethod.Delete, path, token,
            JsonSerializer.Serialize(new { expectedVersion = queued.Version + 1 }), cancelKey, Player);
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Equal(accepted.UpdateTime, (await command.GetSnapshotAsync()).UpdateTime);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    private static GameRequestObservation AssertSignedBlackjack(VerifiedWorkerRequest verified, string path, string body, string operation)
    {
        Assert.Equal((Player, path, operation, BodySha(body)), (verified.Actor, verified.Path, verified.OperationKey, verified.BodySha256));
        var observation = Assert.IsType<GameRequestObservation>(verified.Observation);
        Assert.Equal((Player, BlackjackGameId, path, operation), (observation.UserId, observation.GameId, observation.Path, observation.CommandId));
        return observation;
    }

    private static async Task<WebApplication> StartBlackjackWorkerAsync(FirestoreDb database, Uri wallet, byte[] walletKey,
        byte[] workerKey, ConcurrentQueue<VerifiedWorkerRequest> observed, DropFirstCommittedWalletReply? fault = null)
    {
        var builder = LoopbackBuilder();
        builder.Configuration["Features:BlackjackEnabled"] = "true";
        builder.Configuration["Features:BlackjackTableEnabled"] = "true";
        builder.Configuration["Features:BlackjackMultiplayerEnabled"] = "true";
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Configuration["GameWallet:Url"] = wallet.AbsoluteUri;
        builder.Configuration["GameWallet:SigningKey"] = Convert.ToBase64String(walletKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddBlackjackSolo();
        builder.Services.AddBlackjackTableBackend();
        builder.Services.AddTransient<IBlackjackStore>(services => new FixedBlackjackDeck(services.GetRequiredService<BlackjackSoloStore>()));
        if (fault is not null)
            builder.Services.AddGameWalletClient(BlackjackGameId).AddHttpMessageHandler(() => fault);
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(BlackjackGameId);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
                observed.Enqueue(new(GameWorkerAuthentication.GetAccountId(context), context.Request.Path.Value,
                    context.Request.Headers["Idempotency-Key"].ToString(), GameWorkerAuthentication.GetVerifiedBodySha256(context),
                    GameSecurityContext.Current));
            await next(context);
        });
        app.MapBlackjackSolo(GameWorkerAuthentication.GetAccountId);
        app.MapBlackjackTable(GameWorkerAuthentication.GetAccountId);
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartBlackjackGatewayAsync(FirestoreDb database, FirestoreAccountStore accounts,
        Uri worker, byte[] workerKey)
    {
        var builder = LoopbackBuilder();
        builder.Configuration["GameWorkers:blackjack:ForwardPublicRoutes"] = "true";
        builder.Configuration["GameWorkers:blackjack:Url"] = worker.AbsoluteUri;
        builder.Configuration["GameWorkers:blackjack:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(accounts, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        builder.Services.AddControllers().AddApplicationPart(typeof(BlackjackController).Assembly);
        builder.Services.AddRateLimiter(options =>
        {
            options.AddConcurrencyLimiter(RateLimitPolicies.SlotReads, limiter => limiter.PermitLimit = 100);
            options.AddConcurrencyLimiter(RateLimitPolicies.SlotSpins, limiter => limiter.PermitLimit = 100);
            options.AddConcurrencyLimiter(RateLimitPolicies.BlackjackTableReads, limiter => limiter.PermitLimit = 100);
            options.AddConcurrencyLimiter(RateLimitPolicies.BlackjackTableWrites, limiter => limiter.PermitLimit = 100);
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

    private sealed class FixedBlackjackDeck(BlackjackSoloStore store) : IBlackjackStore
    {
        public Task<BlackjackStoreResult> StartAsync(string userId, string key, long wager, IReadOnlyList<string> _,
            DateTime now, CancellationToken token)
        {
            string[] prefix = ["9|clubs", "7|clubs", "5|hearts", "9|diamonds", "6|spades", "K|hearts"];
            var deck = prefix.Concat(BlackjackRules.CreateShuffledDeck().Where(card => !prefix.Contains(card))).ToArray();
            return store.StartAsync(userId, key, wager, deck, now, token);
        }

        public Task<BlackjackStoreResult?> GetAsync(string userId, string gameId, CancellationToken token) =>
            store.GetAsync(userId, gameId, token);

        public Task<BlackjackStoreResult> ActAsync(string userId, string gameId, string key, int version, string action,
            DateTime now, CancellationToken token) => store.ActAsync(userId, gameId, key, version, action, now, token);
    }
}
