using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.FreeSolitaire.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Cards.Solitaire;
using FortuneForge.Server.GameWorkers;
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
public sealed class SignedCompetitiveSolitaireWorkerIntegrationTests(SolitaireFirestoreEmulatorFixture fixture)
{
    private const string Game = CompetitiveSolitaireEndpoints.GameId;
    private const string Queue = CompetitiveSolitaireEndpoints.PathPrefix + "/queue";
    private const string Session = CompetitiveSolitaireEndpoints.PathPrefix + "/session";
    private const string Player = "signed-solitaire-player";
    private const string Other = "signed-solitaire-other";
    private const string PlayerName = "Signed Solitaire Player";
    private const string Entry = "signed-solitaire-queue-0001";

    [Fact]
    public async Task LostPublicJoinReusesSignedWalletBuyInAndTheWorkerReservesAHouseFundedSeat()
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-signed-solitaire-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateSessionAsync(accounts, Player, PlayerName);
        var otherToken = await CreateSessionAsync(accounts, Other, "Other Solitaire Player");
        await CompetitiveSolitaireTestStore.SeedBalanceAsync(database, Player, DateTime.UtcNow, available: 100);
        var keys = new SigningKeys(NewKey(), NewKey(), NewKey(), NewKey());
        var clock = new TestClock();
        var fault = new LoseCommittedJoin();
        var observed = new ConcurrentQueue<VerifiedJoin>();
        await using var platform = await StartPlatformAsync(database, accounts, keys, clock, fault);
        await using var worker = await StartWorkerAsync(database, platform.Urls.Single(), keys, clock, observed);
        platform.Configuration[$"GameWorkers:{Game}:Url"] = worker.Urls.Single();
        using var browser = Client(platform);
        using var direct = Client(worker);
        const string body = "{ \"playerCount\": 4, \"buyInCredits\": 5, \"drawCount\": 3, \"idempotencyKey\": \"" + Entry + "\" }\r\n";

        using var unsigned = await SendAsync(direct, HttpMethod.Post, Queue, token, Player, body);
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        using var switched = await SendAsync(browser, HttpMethod.Post, Queue, otherToken, Player, body);
        Assert.Equal(HttpStatusCode.Forbidden, switched.StatusCode);
        Assert.Empty(await DocumentsAsync(database, GameWalletBatchFirestoreStore.ReceiptCollection));

        using var lost = await SendAsync(browser, HttpMethod.Post, Queue, token, Player, body);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        Assert.NotNull(fault.Reply);
        Assert.Equal(new VerifiedJoin(Player, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)))), Assert.Single(observed));
        var ticket = Assert.Single(await DocumentsAsync(database, "solitaireTickets"));
        Assert.Equal(Player, ticket.GetValue<string>("userId"));
        Assert.Equal(PlayerName, ticket.GetValue<string>("displayName"));
        var batch = Assert.Single(await DocumentsAsync(database, GameWalletBatchFirestoreStore.ReceiptCollection));
        var debit = Assert.Single(await DocumentsAsync(database, "balanceTransactions"));
        var operation = Assert.Single(await DocumentsAsync(database, FirestoreCompetitiveSolitaireStore.FinancialOperations));
        Assert.Equal(batch.Id, ticket.GetValue<string>("buyInWalletBatchId"));
        Assert.Equal(batch.Id, debit.GetValue<string>("walletBatchId"));
        Assert.Equal(operation.Id, batch.GetValue<string>("operationId"));
        Assert.Equal(operation.GetValue<string>("payloadSha256"), batch.GetValue<string>("outcomeSha256"));
        Assert.Equal(Game, batch.GetValue<string>("gameId"));
        Assert.Equal(Player, batch.GetValue<string>("actorAccountId"));
        Assert.Equal(-500L, debit.GetValue<long>("amountCents"));
        Assert.Equal(Player, debit.GetValue<string>("userId"));
        Assert.Equal(9_500L, await FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, Player, false, default));

        platform.Configuration[$"GameWorkers:{Game}:ForwardPublicRoutes"] = "false";
        using var disabled = await SendAsync(browser, HttpMethod.Post, Queue, token, Player, body);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
        platform.Configuration[$"GameWorkers:{Game}:ForwardPublicRoutes"] = "true";
        using var retry = await SendAsync(browser, HttpMethod.Post, Queue, token, Player, body);
        var retried = await ReadAsync(retry);
        Assert.Equal(fault.Reply, retried);
        using var entry = JsonDocument.Parse(retried);
        Assert.Equal(95m, entry.RootElement.GetProperty("balanceCredits").GetDecimal());
        var match = entry.RootElement.GetProperty("session");
        Assert.Equal("match", match.GetProperty("kind").GetString());
        var matchId = match.GetProperty("matchId").GetString()!;
        var startedAtUtc = match.GetProperty("startedAtUtc").GetDateTimeOffset();
        var seats = match.GetProperty("players").EnumerateArray().ToArray();
        var firstPlayer = Assert.Single(seats, seat => seat.GetProperty("status").GetString() != SolitairePlayerStatuses.Open);
        Assert.Equal(3, seats.Count(seat => seat.GetProperty("status").GetString() == SolitairePlayerStatuses.Open));
        Assert.Equal(PlayerName, firstPlayer.GetProperty("displayName").GetString());
        Assert.True(firstPlayer.GetProperty("isCurrentPlayer").GetBoolean());

        using var changed = await SendAsync(browser, HttpMethod.Post, Queue, token, Player,
            body.Replace("\"buyInCredits\": 5", "\"buyInCredits\": 10", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var unrelatedSession = await SendAsync(browser, HttpMethod.Get, Session, otherToken, Other);
        using var unrelated = JsonDocument.Parse(await ReadAsync(unrelatedSession));
        Assert.Equal("idle", unrelated.RootElement.GetProperty("kind").GetString());
        Assert.Single(await DocumentsAsync(database, "solitaireMatches"));
        var humanSeat = Assert.Single(await DocumentsAsync(database, "solitaireMatchPlayers"));
        Assert.Equal(Player, humanSeat.GetValue<string>("userId"));
        Assert.Empty(await DocumentsAsync(database, "managedPlayerAssignments"));

        // Run the same due-match sweep as the package scheduler while keeping roster signing clocks in sync.
        var dueAt = startedAtUtc.AddHours(1).AddSeconds(1);
        clock.Now = dueAt;
        await worker.Services.GetRequiredService<FirestoreCompetitiveSolitaireStore>().SweepAsync(dueAt.UtcDateTime, default);
        var players = await DocumentsAsync(database, "solitaireMatchPlayers");
        Assert.Equal(2, players.Count);
        var bot = Assert.Single(players, row => row.GetValue<bool>("isSynthetic"));
        var lease = Assert.Single(await DocumentsAsync(database, "managedPlayerAssignments"));
        Assert.Equal(bot.GetValue<string>("userId"), lease.Id);
        Assert.Equal(Game, lease.GetValue<string>("gameId"));
        Assert.True(lease.GetValue<Timestamp>("leaseUntil").ToDateTime() > dueAt.UtcDateTime);
        var funding = await database.Collection("solitaireHouseFunding").Document(matchId).GetSnapshotAsync();
        Assert.Equal(500L, funding.GetValue<long>("humanEntryCents"));
        Assert.Equal(500L, funding.GetValue<long>("houseFundedEntryCents"));
        Assert.Equal(batch.Id, Assert.Single(await DocumentsAsync(database, GameWalletBatchFirestoreStore.ReceiptCollection)).Id);
        Assert.Equal(debit.Id, Assert.Single(await DocumentsAsync(database, "balanceTransactions")).Id);
    }

    private static async Task<WebApplication> StartPlatformAsync(FirestoreDb database, FirestoreAccountStore accounts,
        SigningKeys keys, TestClock clock, LoseCommittedJoin fault)
    {
        var builder = Loopback();
        builder.Configuration[$"GameWorkers:{Game}:ForwardPublicRoutes"] = "true";
        builder.Configuration[$"GameWorkers:{Game}:SigningKey"] = keys.Worker;
        builder.Configuration[$"GameWallet:Games:{Game}:SigningKey"] = keys.Wallet;
        builder.Configuration[$"GameWallet:Games:{Game}:AllowAccountBalanceRead"] = "true";
        builder.Configuration[$"GameWallet:Games:{Game}:AllowPlayerNameRead"] = "true";
        builder.Configuration[$"GameWallet:Games:{Game}:BatchSigningKey"] = keys.Batch;
        builder.Configuration[$"GameWallet:Games:{Game}:AllowMultiAccountBatch"] = "true";
        builder.Configuration[$"ManagedPlayerRoster:Games:{Game}:Enabled"] = "true";
        builder.Configuration[$"ManagedPlayerRoster:Games:{Game}:SigningKey"] = keys.Roster;
        builder.Configuration["Features:CompetitiveSolitaireEnabled"] = "true";
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton(new FirestoreSolitaireFreeRunService(database, clock));
        builder.Services.AddSingleton<IWalletRoundSettlementStore>(new FirestoreWalletRoundSettlementStore(database));
        builder.Services.AddManagedPlayers();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(accounts, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        builder.Services.AddHttpClient(GameWorkerClient.ExactResponseClient).ConfigurePrimaryHttpMessageHandler(() =>
                new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false })
            .AddHttpMessageHandler(() => fault);
        builder.Services.AddControllers().AddApplicationPart(typeof(SolitaireController).Assembly);
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
        app.MapControllers().WithFreeSolitaireWorkerRoutes().WithCompetitiveSolitaireWorkerRoutes();
        app.MapGameWalletSettlement();
        app.MapGameWalletBatchSettlement();
        app.MapGameManagedPlayerRoster();
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartWorkerAsync(FirestoreDb database, string platformUrl, SigningKeys keys,
        TestClock clock, ConcurrentQueue<VerifiedJoin> observed)
    {
        var builder = Loopback();
        builder.Configuration["GameWorker:SigningKey"] = keys.Worker;
        builder.Configuration["GameWallet:Url"] = platformUrl;
        builder.Configuration["GameWallet:SigningKey"] = keys.Wallet;
        builder.Configuration["GameWallet:BatchSigningKey"] = keys.Batch;
        builder.Configuration["ManagedPlayerRoster:Url"] = platformUrl;
        builder.Configuration["ManagedPlayerRoster:SigningKey"] = keys.Roster;
        builder.Configuration["Solitaire:AllowNewPaidRounds"] = "true";
        builder.Configuration["Cards:CompetitiveSolitaire:AllowSingleHumanBotFill"] = "true";
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddCompetitiveSolitaireBackend(builder.Configuration);
        builder.Services.RemoveAll<IHostedService>(); // Drive the production sweep explicitly at the due time.
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(Game);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path == Queue)
                observed.Enqueue(new(GameWorkerAuthentication.GetAccountId(context), GameWorkerAuthentication.GetVerifiedBodySha256(context)));
            await next(context);
        });
        app.MapCompetitiveSolitaire(GameWorkerAuthentication.GetAccountId);
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

    private static async Task<string> CreateSessionAsync(FirestoreAccountStore accounts, string player, string name)
    {
        Assert.NotNull((await accounts.CreateAsync(player, name, name.ToUpperInvariant(), player + "@example.test",
            "emulator-test", "active", default)).Value);
        var token = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        await accounts.CreateSessionAsync(GameSecurityEventDocuments.Hash(token), player, now, now.AddHours(1), null, default);
        return token;
    }

    private static HttpClient Client(WebApplication app) => new(new SocketsHttpHandler { UseProxy = false, UseCookies = false })
        { BaseAddress = new Uri(app.Urls.Single()) };

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
        string token, string expectedAccount, string? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{AccountSessionCookie.Name}={token}");
        request.Headers.Add("X-FortuneForge-Expected-Account", expectedAccount);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response)
    {
        var value = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected OK, received {(int)response.StatusCode}: {value}");
        return value;
    }

    private static async Task<IReadOnlyList<DocumentSnapshot>> DocumentsAsync(FirestoreDb database, string collection) =>
        (await database.Collection(collection).GetSnapshotAsync()).Documents;

    private sealed record SigningKeys(string Worker, string Wallet, string Batch, string Roster);
    private sealed record VerifiedJoin(string? Actor, string? BodySha256);

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset? Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now ?? DateTimeOffset.UtcNow;
    }

    private sealed class LoseCommittedJoin : DelegatingHandler
    {
        private int dropped;
        public string? Reply { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (request.RequestUri?.AbsolutePath != Queue || !response.IsSuccessStatusCode || Interlocked.Exchange(ref dropped, 1) != 0)
                return response;
            Reply = await response.Content.ReadAsStringAsync(token);
            response.Dispose();
            throw new HttpRequestException("Lost the public reply after the Solitaire worker committed its join.");
        }
    }
}
