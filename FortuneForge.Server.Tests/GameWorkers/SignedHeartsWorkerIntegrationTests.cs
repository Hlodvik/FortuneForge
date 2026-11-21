using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Activity;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class SignedHeartsWorkerIntegrationTests(SolitaireFirestoreEmulatorFixture fixture)
{
    private const string Game = "hearts";
    private const string Paid = "/api/games/hearts/paid";
    private const string Join = Paid + "/join";
    private const string Player = "signed-hearts-player";
    private const string Other = "signed-hearts-other";
    private const string PlayerName = "Signed Hearts Player";
    private const string JoinKey = "signed-hearts-join-0001";
    private const string LeaveKey = "signed-hearts-leave-0001";

    [Fact]
    public async Task Free_start_replays_through_the_signed_gateway_while_new_matches_are_paused()
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-signed-hearts-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateSessionAsync(accounts, Player, PlayerName);
        var otherToken = await CreateSessionAsync(accounts, Other, "Other Hearts Player");
        var keys = new SigningKeys(NewKey(), NewKey(), NewKey());
        var observed = new ConcurrentQueue<VerifiedCommand>();
        await using var platform = await StartPlatformAsync(database, accounts, keys);
        await using var worker = await StartWorkerAsync(database, platform.Urls.Single(), keys, new(), observed);
        platform.Configuration[$"GameWorkers:{Game}:Url"] = worker.Urls.Single();
        using var browser = Client(platform);
        const string path = "/api/games/hearts/matches";
        const string key = "signed-hearts-free-0001";
        const string body = "{ \"seed\": 1931, \"targetScore\": 100 }";

        using var first = await ReadAsync(await SendAsync(browser, HttpMethod.Post, path, token, Player, key, body));
        var match = first.RootElement.GetProperty("matchId").GetString();
        Assert.Equal(new VerifiedCommand(Player, key, WireDigest(body)), Assert.Single(observed));
        worker.Configuration["Features:HeartsEnabled"] = "false";
        using var replay = await ReadAsync(await SendAsync(browser, HttpMethod.Post, path, token, Player, key, body));
        Assert.Equal(first.RootElement.GetRawText(), replay.RootElement.GetRawText());
        using var paused = await SendAsync(browser, HttpMethod.Post, path, token, Player, key + "-new", body);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, paused.StatusCode);
        using var foreign = await SendAsync(browser, HttpMethod.Get, $"{path}/{match}", otherToken, Other);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Single(await DocumentsAsync(database, FirestoreHeartsFreeStore.Sessions));
        Assert.Empty(await DocumentsAsync(database, "balanceTransactions"));
    }

    [Fact]
    public async Task Paid_join_and_queue_refund_recover_after_real_signed_batch_replies_are_lost()
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-signed-hearts-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateSessionAsync(accounts, Player, PlayerName);
        var otherToken = await CreateSessionAsync(accounts, Other, "Other Hearts Player");
        await database.Collection("userBalances").Document(Player + "_slotsCredits").UpdateAsync(new Dictionary<string, object>
            { ["available"] = 100L, ["availableFractionalCents"] = 0L });
        var keys = new SigningKeys(NewKey(), NewKey(), NewKey());
        var fault = new DropCommittedBatchReply();
        var observed = new ConcurrentQueue<VerifiedCommand>();
        await using var platform = await StartPlatformAsync(database, accounts, keys);
        await using var worker = await StartWorkerAsync(database, platform.Urls.Single(), keys, fault, observed);
        platform.Configuration[$"GameWorkers:{Game}:Url"] = worker.Urls.Single();
        using var browser = Client(platform);
        using var direct = Client(worker);
        const string joinBody = "{ \"entryCredits\": 1, \"playerName\": \"Untrusted\" }\r\n";

        using var unsigned = await SendAsync(direct, HttpMethod.Post, Join, token, Player, JoinKey, joinBody);
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        using var switched = await SendAsync(browser, HttpMethod.Post, Join, otherToken, Player, JoinKey, joinBody);
        Assert.Equal(HttpStatusCode.Forbidden, switched.StatusCode);
        Assert.Empty(await DocumentsAsync(database, GameWalletBatchFirestoreStore.ReceiptCollection));

        fault.Arm();
        using var lostJoin = await SendAsync(browser, HttpMethod.Post, Join, token, Player, JoinKey, joinBody);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lostJoin.StatusCode);
        Assert.Equal(1, fault.Dropped);
        Assert.Equal(new VerifiedCommand(Player, JoinKey, WireDigest(joinBody)), Assert.Single(observed));
        var debit = Assert.Single(await DocumentsAsync(database, "balanceTransactions"));
        Assert.Equal(-100L, debit.GetValue<long>("amountCents"));
        Assert.Equal(Player, debit.GetValue<string>("userId"));
        Assert.Equal(9_900L, await BalanceAsync(database));
        Assert.Empty(await DocumentsAsync(database, "heartsPaidMatches"));
        Assert.Empty(await DocumentsAsync(database, "heartsPaidWalletJournal"));
        var prepared = Assert.Single(await DocumentsAsync(database, "heartsPaidPreparedMutations"));
        Assert.Equal("pending", prepared.GetValue<string>("status"));
        var originalBatch = Assert.Single(await DocumentsAsync(database, GameWalletBatchFirestoreStore.ReceiptCollection));
        Assert.Equal(prepared.Id, originalBatch.GetValue<string>("operationId"));
        Assert.Equal(Player, originalBatch.GetValue<string>("actorAccountId"));
        Assert.Equal(originalBatch.Id, debit.GetValue<string>("walletBatchId"));

        worker.Configuration["Features:HeartsEnabled"] = "false";
        using var recovered = await ReadAsync(await SendAsync(browser, HttpMethod.Get, Paid + "/session", token, Player));
        Assert.Equal("queue", recovered.RootElement.GetProperty("kind").GetString());
        var queue = recovered.RootElement.GetProperty("queue");
        var matchId = queue.GetProperty("matchId").GetString();
        Assert.Equal(PlayerName, Assert.Single(queue.GetProperty("players").EnumerateArray()).GetProperty("displayName").GetString());
        using var exactJoin = await ReadAsync(await SendAsync(browser, HttpMethod.Post, Join, token, Player, JoinKey, joinBody));
        using var repeatedJoin = await ReadAsync(await SendAsync(browser, HttpMethod.Post, Join, token, Player, JoinKey, joinBody));
        Assert.Equal(exactJoin.RootElement.GetRawText(), repeatedJoin.RootElement.GetRawText());
        Assert.Equal(matchId, exactJoin.RootElement.GetProperty("queue").GetProperty("matchId").GetString());
        Assert.Equal(99m, exactJoin.RootElement.GetProperty("balanceCredits").GetDecimal());
        using var changed = await SendAsync(browser, HttpMethod.Post, Join, token, Player, JoinKey, "{\"entryCredits\":2}");
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var paused = await SendAsync(browser, HttpMethod.Post, Join, token, Player, JoinKey + "-new", joinBody);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, paused.StatusCode);
        using var foreign = await SendAsync(browser, HttpMethod.Get, $"{Paid}/matches/{matchId}", otherToken, Other);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var version = recovered.RootElement.GetProperty("version").GetInt32();
        var leaveBody = $"{{ \"expectedVersion\": {version} }}\r\n";
        var leave = $"{Paid}/matches/{matchId}/leave";
        fault.Arm();
        using var lostLeave = await SendAsync(browser, HttpMethod.Post, leave, token, Player, LeaveKey, leaveBody);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lostLeave.StatusCode);
        Assert.Equal(2, fault.Dropped);
        Assert.Contains(new VerifiedCommand(Player, LeaveKey, WireDigest(leaveBody)), observed);
        Assert.Equal(10_000L, await BalanceAsync(database));
        using var idle = await ReadAsync(await SendAsync(browser, HttpMethod.Get, Paid + "/session", token, Player));
        Assert.Equal("idle", idle.RootElement.GetProperty("kind").GetString());
        using var exactLeave = await ReadAsync(await SendAsync(browser, HttpMethod.Post, leave, token, Player, LeaveKey, leaveBody));
        using var repeatedLeave = await ReadAsync(await SendAsync(browser, HttpMethod.Post, leave, token, Player, LeaveKey, leaveBody));
        Assert.Equal(exactLeave.RootElement.GetRawText(), repeatedLeave.RootElement.GetRawText());
        Assert.Equal(100m, exactLeave.RootElement.GetProperty("balanceCredits").GetDecimal());

        var batches = await DocumentsAsync(database, GameWalletBatchFirestoreStore.ReceiptCollection);
        Assert.Equal(2, batches.Count);
        Assert.All(batches, batch => Assert.Equal(Player, batch.GetValue<string>("actorAccountId")));
        var ledger = await DocumentsAsync(database, "balanceTransactions");
        Assert.Equal(new[] { -100L, 100L }, ledger.Select(row => row.GetValue<long>("amountCents")).Order().ToArray());
        var journal = await DocumentsAsync(database, "heartsPaidWalletJournal");
        Assert.Equal(batches.Select(batch => batch.Id).Order(), journal.Select(row => row.GetValue<string>("walletBatchId")).Order());
        Assert.Equal(10_000L, await BalanceAsync(database));
        Assert.Empty(await DocumentsAsync(database, "heartsPaidMutationLocks"));
    }

    private static async Task<WebApplication> StartPlatformAsync(FirestoreDb database, FirestoreAccountStore accounts, SigningKeys keys)
    {
        var builder = Loopback();
        builder.Configuration["Features:HeartsEnabled"] = "true";
        builder.Configuration[$"GameWorkers:{Game}:ForwardPublicRoutes"] = "true";
        builder.Configuration[$"GameWorkers:{Game}:SigningKey"] = keys.Worker;
        builder.Configuration[$"GameWallet:Games:{Game}:SigningKey"] = keys.Wallet;
        builder.Configuration[$"GameWallet:Games:{Game}:AllowAccountBalanceRead"] = "true";
        builder.Configuration[$"GameWallet:Games:{Game}:AllowPlayerNameRead"] = "true";
        builder.Configuration[$"GameWallet:Games:{Game}:BatchSigningKey"] = keys.Batch;
        builder.Configuration[$"GameWallet:Games:{Game}:AllowMultiAccountBatch"] = "true";
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<IWalletRoundSettlementStore>(new FirestoreWalletRoundSettlementStore(database));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(accounts, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        builder.Services.AddHttpClient(GameWorkerClient.ExactResponseClient).ConfigurePrimaryHttpMessageHandler(() =>
            new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false });
        builder.Services.AddControllers().AddApplicationPart(typeof(PaidHeartsController).Assembly);
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
        app.MapControllers().WithHeartsWorkerRoutes();
        app.MapGameWalletSettlement();
        app.MapGameWalletBatchSettlement();
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartWorkerAsync(FirestoreDb database, string walletUrl, SigningKeys keys,
        DropCommittedBatchReply fault, ConcurrentQueue<VerifiedCommand> observed)
    {
        var builder = Loopback();
        builder.Configuration["Features:HeartsEnabled"] = "true";
        builder.Configuration["GameWorker:SigningKey"] = keys.Worker;
        builder.Configuration["GameWallet:Url"] = walletUrl;
        builder.Configuration["GameWallet:SigningKey"] = keys.Wallet;
        builder.Configuration["GameWallet:BatchSigningKey"] = keys.Batch;
        builder.Services.AddSingleton(database);
        builder.Services.AddHeartsFreeBackend();
        builder.Services.AddHeartsPaidBackend();
        builder.Services.AddGameWalletBatchClient(Game).AddHttpMessageHandler(() => fault);
        builder.Services.RemoveAll<IHostedService>(); // Recovery is driven through the public requests in this test.
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(Game);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method))
                observed.Enqueue(new(GameWorkerAuthentication.GetAccountId(context), context.Request.Headers["Idempotency-Key"].ToString(),
                    GameWorkerAuthentication.GetVerifiedBodySha256(context)));
            await next(context);
        });
        app.MapHeartsFree(GameWorkerAuthentication.GetAccountId);
        app.MapHeartsPaid(GameWorkerAuthentication.GetAccountId);
        await app.StartAsync();
        return app;
    }

    private static WebApplicationBuilder Loopback()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static string WireDigest(string body) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    private static async Task<string> CreateSessionAsync(FirestoreAccountStore accounts, string player, string displayName)
    {
        Assert.NotNull((await accounts.CreateAsync(player, displayName, displayName.ToUpperInvariant(), player + "@example.test",
            "emulator-test", "active", default)).Value);
        var token = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        await accounts.CreateSessionAsync(GameSecurityEventDocuments.Hash(token), player, now, now.AddHours(1), null, default);
        return token;
    }

    private static HttpClient Client(WebApplication app) => new(new SocketsHttpHandler { UseProxy = false, UseCookies = false })
        { BaseAddress = new Uri(app.Urls.Single()) };

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string route,
        string token, string expectedAccount, string? key = null, string? body = null)
    {
        using var request = new HttpRequestMessage(method, route);
        request.Headers.Add("Cookie", $"{AccountSessionCookie.Name}={token}");
        request.Headers.Add("X-FortuneForge-Expected-Account", expectedAccount);
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected OK, received {(int)response.StatusCode}: {body}");
            return JsonDocument.Parse(body);
        }
    }

    private static Task<long> BalanceAsync(FirestoreDb db) =>
        FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(db, Player, false, default);

    private static async Task<IReadOnlyList<DocumentSnapshot>> DocumentsAsync(FirestoreDb db, string collection) =>
        (await db.Collection(collection).GetSnapshotAsync()).Documents;

    private sealed record SigningKeys(string Worker, string Wallet, string Batch);
    private sealed record VerifiedCommand(string? Actor, string Key, string? BodySha256);

    private sealed class DropCommittedBatchReply : DelegatingHandler
    {
        private int armed;
        private int dropped;
        public int Dropped => Volatile.Read(ref dropped);
        public void Arm() => Volatile.Write(ref armed, 1);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (!response.IsSuccessStatusCode || Interlocked.Exchange(ref armed, 0) == 0) return response;
            Interlocked.Increment(ref dropped);
            response.Dispose();
            throw new HttpRequestException("Lost the signed Hearts wallet reply after the batch committed.");
        }
    }
}
