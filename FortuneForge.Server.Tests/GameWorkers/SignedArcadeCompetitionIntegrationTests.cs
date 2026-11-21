using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.Asteroids;
using FortuneForge.Games.Asteroids.Server;
using FortuneForge.Games.Flappy.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.HorseFlight;
using FortuneForge.Games.HorseFlight.Server;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Arcade.Competition;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed partial class SignedArcadeCompetitionIntegrationTests(SolitaireFirestoreEmulatorFixture fixture)
{
    private const string Player = "signed-arcade-player";
    private const string OtherPlayer = "signed-arcade-other";
    private const string Operation = "signed-arcade-entry-0001";

    [Theory]
    [InlineData("asteroids", AsteroidsPaidRuns.CollectionName, "seedHex")]
    [InlineData("flappy", FlappyPaidRuns.CollectionName, "seed")]
    [InlineData("horse-flight", HorseFlightPaidRuns.CollectionName, "seed")]
    public async Task SignedPublicAdmissionAndRunStatusRecoverOneCommittedWalletDebit(
        string game, string runCollection, string seedField)
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-signed-arcade-{Guid.NewGuid():N}"[..29],
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var clock = new CompetitionClock(new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));
        var accounts = new FirestoreAccountStore(database);
        var playerToken = await CreateAccountAsync(accounts, Player);
        var otherToken = await CreateAccountAsync(accounts, OtherPlayer);
        await database.Collection("userBalances").Document(Player + "_slotsCredits")
            .UpdateAsync(new Dictionary<string, object> { ["available"] = 5L, ["availableFractionalCents"] = 0L });
        var workerKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var walletKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var platform = await StartPlatformAsync(database, accounts, game, workerKey, walletKey, clock);
        var fault = new LoseWalletResponses(game);
        await using var worker = await StartWorkerAsync(database, game, platform.Urls.Single(), workerKey, walletKey, clock, fault);
        platform.Configuration[$"GameWorkers:{game}:Url"] = worker.Urls.Single();
        using var browser = new HttpClient(new SocketsHttpHandler { UseProxy = false, UseCookies = false })
            { BaseAddress = new Uri(platform.Urls.Single()) };
        var path = $"/api/arcade-competitions/{game}/daily";

        using var board = await ReadAsync(await SendAsync(browser, HttpMethod.Get, path, token: null));
        var starts = board.RootElement.GetProperty("startsAtUtc").GetDateTimeOffset();
        var ends = board.RootElement.GetProperty("endsAtUtc").GetDateTimeOffset();
        var body = $$"""
            {
              "expectedWindowStartsAtUtc": "{{starts:O}}",
              "expectedWindowEndsAtUtc": "{{ends:O}}"
            }
            """ + "\r\n";
        var startPath = path + "/attempts";
        var recoverPath = startPath + "?recoverOnly=true";

        using var wrongAccount = await SendAsync(browser, HttpMethod.Post, startPath, otherToken, Operation, body);
        Assert.Equal(HttpStatusCode.Forbidden, wrongAccount.StatusCode);
        Assert.Empty(await DocumentsAsync(database, "balanceTransactions"));

        using var lost = await SendAsync(browser, HttpMethod.Post, startPath, playerToken, Operation, body);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        Assert.True(fault.LostEntry);
        var debit = Assert.Single(await DocumentsAsync(database, "balanceTransactions"));
        Assert.Equal("arcade-competition-entry", debit.GetValue<string>("type"));
        Assert.Equal(Player, debit.GetValue<string>("userId"));
        Assert.Equal(Operation, debit.GetValue<string>("idempotencyKey"));
        Assert.Equal(-100L, debit.GetValue<long>("amountCents"));
        Assert.Single(await DocumentsAsync(database, "walletRoundSettlements"));
        Assert.Empty(await DocumentsAsync(database, runCollection));
        var pending = Assert.Single(await DocumentsAsync(database, ArcadeCompetitionPaidEntries<AsteroidsPaidRun>.PreparedCollection));
        Assert.Equal("pending", pending.GetValue<string>("status"));
        using var saved = JsonDocument.Parse(pending.GetValue<string>("intentJson"));
        Assert.Equal(GameSecurityEventDocuments.Hash(body),
            saved.RootElement.GetProperty("request").GetProperty("requestBodySha256").GetString());

        // A previous client must not bypass the prepared intent through the host after a flag change.
        var forwardingFlag = $"GameWorkers:{game}:ForwardPaidCompetitionRoutes";
        platform.Configuration[forwardingFlag] = "false";
        using var legacyRetry = await SendAsync(browser, HttpMethod.Post, startPath, playerToken, Operation);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, legacyRetry.StatusCode);
        Assert.Equal(debit.Id, Assert.Single(await DocumentsAsync(database, "balanceTransactions")).Id);
        Assert.Empty(await DocumentsAsync(database, runCollection));
        platform.Configuration[forwardingFlag] = "true";

        using var switched = await SendAsync(browser, HttpMethod.Post, recoverPath, otherToken, Operation, body);
        Assert.Equal(HttpStatusCode.Forbidden, switched.StatusCode);
        using var changed = await SendAsync(browser, HttpMethod.Post, recoverPath, playerToken, Operation, body + " ");
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var recovered = await ReadAsync(await SendAsync(browser, HttpMethod.Post, recoverPath, playerToken, Operation, body));
        var runId = recovered.RootElement.GetProperty("runId").GetString();
        var seed = recovered.RootElement.GetProperty(seedField).GetRawText();
        Assert.Equal(ends, recovered.RootElement.GetProperty("endsAtUtc").GetDateTimeOffset());
        Assert.Equal(100L, recovered.RootElement.GetProperty("entryFeeCents").GetInt64());
        var statusPath = $"{path}/runs/{runId}";
        using var status = await ReadAsync(await SendAsync(browser, HttpMethod.Get, statusPath, playerToken));
        Assert.Equal(runId, status.RootElement.GetProperty("runId").GetString());
        Assert.Equal("started", status.RootElement.GetProperty("status").GetString());
        Assert.Equal(seed, status.RootElement.GetProperty(seedField).GetRawText());

        clock.Now = clock.Now.AddSeconds(1);
        var replayPath = statusPath + "/replay";
        var replayBody = TerminalReplay(game, recovered.RootElement);
        using var completed = await ReadAsync(await SendAsync(browser, HttpMethod.Post, replayPath, playerToken, body: replayBody));
        Assert.False(completed.RootElement.GetProperty("wasReplay").GetBoolean());
        using var repeated = await ReadAsync(await SendAsync(browser, HttpMethod.Post, replayPath, playerToken, body: replayBody));
        Assert.True(repeated.RootElement.GetProperty("wasReplay").GetBoolean());
        var score = completed.RootElement.GetProperty("score").GetInt64();
        Assert.Equal(score, repeated.RootElement.GetProperty("score").GetInt64());
        using var history = await ReadAsync(await SendAsync(browser, HttpMethod.Get, statusPath, playerToken));
        Assert.Equal("completed", history.RootElement.GetProperty("status").GetString());
        Assert.Equal(score, history.RootElement.GetProperty("score").GetInt64());

        clock.Now = ends.AddSeconds(1);
        using var rolled = await ReadAsync(await SendAsync(browser, HttpMethod.Post, recoverPath, playerToken, Operation, body));
        Assert.Equal(runId, rolled.RootElement.GetProperty("runId").GetString());
        Assert.Equal(seed, rolled.RootElement.GetProperty(seedField).GetRawText());
        Assert.True(rolled.RootElement.GetProperty("wasReplay").GetBoolean());
        using var freshOldWindow = await SendAsync(browser, HttpMethod.Post, recoverPath, playerToken, Operation + "-unknown", body);
        Assert.Equal(HttpStatusCode.Conflict, freshOldWindow.StatusCode);

        Assert.Equal(400L, await FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, Player, false, default));
        Assert.Single(await DocumentsAsync(database, runCollection));
        Assert.Single(await DocumentsAsync(database, ArcadeCompetitionFirestoreDocuments.AttemptsCollection));
        Assert.Single(await DocumentsAsync(database, "walletRoundSettlements"));
        Assert.Equal(debit.Id, Assert.Single(await DocumentsAsync(database, "balanceTransactions")).Id);

        var services = worker.Services;
        var store = services.GetRequiredService<IArcadeCompetitionStore>();
        var planner = new ArcadeCompetitionSettlementPlanningService(store, clock, services.GetRequiredService<ArcadeCompetitionRulesOptions>());
        var executor = new ArcadeCompetitionSettlementExecutor(store, planner, services.GetRequiredService<IArcadeCompetitionPayoutCreditor>(), clock);
        var competition = new ArcadeCompetitionIdentity(game, ArcadeCompetitionWindowKind.Daily, starts, ends);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => executor.ExecuteAsync(competition, default));
        Assert.True(fault.LostPayout);
        Assert.Equal(500L, await FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, Player, false, default));
        var settled = await executor.ExecuteAsync(competition, default);
        Assert.True(settled.SettlementState.IsCompleted);
        var refund = Assert.Single(settled.PayoutPlan.Instructions);
        Assert.Equal(ArcadeCompetitionPayoutKind.Refund, refund.Kind);
        Assert.Equal(Player, refund.PlayerId);
        Assert.Equal(100L, refund.AmountCents);
        Assert.True((await executor.ExecuteAsync(competition, default)).WasAlreadyCompleted);
        Assert.Equal(500L, await FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, Player, false, default));
        var ledger = await DocumentsAsync(database, "balanceTransactions");
        Assert.Equal(2, ledger.Count);
        var credit = Assert.Single(ledger, row => row.GetValue<string>("type") == "arcade-competition-payout");
        Assert.Equal("refund", credit.GetValue<string>("payoutKind"));
        Assert.Equal(100L, credit.GetValue<long>("amountCents"));
        if (game == "horse-flight") await AssertHorseFlightFreeRoutesAsync(browser, playerToken);
    }

    private static async Task<WebApplication> StartPlatformAsync(FirestoreDb database, FirestoreAccountStore accounts,
        string game, string workerKey, string walletKey, CompetitionClock clock)
    {
        var builder = Loopback();
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32 * 1024);
        builder.Configuration[$"GameWorkers:{game}:SigningKey"] = workerKey;
        builder.Configuration[$"GameWorkers:{game}:ForwardPaidCompetitionRoutes"] = "true";
        builder.Configuration[$"GameWorkers:{game}:ForwardFreeRoutes"] = "true";
        builder.Configuration[$"GameWallet:Games:{game}:SigningKey"] = walletKey;
        builder.Configuration[$"GameWallet:Games:{game}:AllowArcadeCompetition"] = "true";
        builder.Configuration[$"GameWallet:Games:{game}:AllowAccountBalanceRead"] = "true";
        builder.Configuration["Features:HorseFlightEnabled"] = "true";
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(accounts, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddSingleton<IWalletRoundSettlementStore>(new FirestoreWalletRoundSettlementStore(database));
        builder.Services.AddSingleton<IWalletCreditStore>(new FirestoreWalletCreditStore(database));
        builder.Services.AddArcadeCompetitions(builder.Configuration);
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddHttpClient(GameWorkerClient.ExactResponseClient).ConfigurePrimaryHttpMessageHandler(() =>
            new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false });
        builder.Services.AddSingleton(provider =>
        {
            var http = provider.GetRequiredService<IHttpClientFactory>();
            return new GameWorkerClient(http.CreateClient(), builder.Configuration, provider.GetRequiredService<IHostEnvironment>(),
                TimeProvider.System, provider.GetRequiredService<ILogger<GameWorkerClient>>(), http);
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(ArcadeCompetitionController).Assembly);
        builder.Services.AddRateLimiter(options =>
        {
            options.AddConcurrencyLimiter(RateLimitPolicies.SlotReads, limiter => limiter.PermitLimit = 100);
            options.AddConcurrencyLimiter(RateLimitPolicies.SlotSpins, limiter => limiter.PermitLimit = 100);
        });
        var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.UsePackagedGameWorkerForwarding();
        app.MapControllers();
        app.MapGameWalletSettlement();
        app.MapArcadeCompetitionWallet();
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartWorkerAsync(FirestoreDb database, string game, string walletUrl,
        string workerKey, string walletKey, CompetitionClock clock, LoseWalletResponses fault)
    {
        var builder = Loopback();
        builder.Configuration["GameWorker:SigningKey"] = workerKey;
        builder.Configuration["GameWallet:Url"] = walletUrl;
        builder.Configuration["GameWallet:SigningKey"] = walletKey;
        var services = builder.Services;
        services.AddSingleton(database);
        services.AddSingleton<TimeProvider>(clock);
        services.AddArcadeCompetitionStandings();
        services.AddArcadeCompetitionWallet(game).AddHttpMessageHandler(() => fault);
        switch (game)
        {
            case "asteroids": Register<AsteroidsPaidRun, AsteroidsPaidRuns, AsteroidsPaidRunService>(services, game, new(() => 42)); break;
            case "flappy": Register<FlappyPaidRun, FlappyPaidRuns, FlappyPaidRunService>(services, game, new(() => 41)); break;
            case "horse-flight":
                Register<HorseFlightPaidRun, HorseFlightPaidRuns, HorseFlightPaidRunService>(services, game, new());
                services.AddGameWalletClient(game);
                services.AddSingleton<HorseFlightFreeRunStore>();
                break;
            default: throw new ArgumentOutOfRangeException(nameof(game));
        }
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(game);
        switch (game)
        {
            case "asteroids": app.MapAsteroidsPaidRuns(GameWorkerAuthentication.GetAccountId, GameWorkerAuthentication.GetCommandBodySha256); break;
            case "flappy": app.MapFlappyPaidRuns(GameWorkerAuthentication.GetAccountId, GameWorkerAuthentication.GetCommandBodySha256); break;
            case "horse-flight":
                app.MapHorseFlightPaidRuns(GameWorkerAuthentication.GetAccountId, () => true, GameWorkerAuthentication.GetCommandBodySha256);
                app.MapHorseFlightFreeRuns(GameWorkerAuthentication.GetAccountId, () => true);
                break;
        }
        app.MapArcadeCompetitionStandings(game);
        await app.StartAsync();
        return app;
    }

    private static void Register<TRun, TRuns, TService>(IServiceCollection services, string game, TRuns runs)
        where TRun : class where TRuns : class, IArcadeCompetitionRunAdapter<TRun> where TService : class
    {
        services.AddSingleton(runs);
        services.AddSingleton(provider => new ArcadeCompetitionPaidEntries<TRun>(provider.GetRequiredService<FirestoreDb>(),
            game, provider.GetRequiredService<ArcadeCompetitionRulesOptions>(), provider.GetRequiredService<IArcadeCompetitionEntryWallet>(), runs));
        services.AddSingleton<TService>();
    }

    private static WebApplicationBuilder Loopback()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }

    private static async Task<string> CreateAccountAsync(FirestoreAccountStore accounts, string player)
    {
        Assert.NotNull((await accounts.CreateAsync(player, player, player.ToUpperInvariant(), player + "@example.test",
            "emulator-test", "active", default)).Value);
        var token = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        await accounts.CreateSessionAsync(GameSecurityEventDocuments.Hash(token), player, now, now.AddHours(1), null, default);
        return token;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token,
        string? operation = null, string? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Add("Cookie", $"{AccountSessionCookie.Name}={token}");
            request.Headers.Add("X-FortuneForge-Expected-Account", Player);
        }
        if (operation is not null) request.Headers.Add("Idempotency-Key", operation);
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

    private static string TerminalReplay(string game, JsonElement entered)
    {
        if (game == "asteroids")
            return JsonSerializer.Serialize(new AsteroidsReplayRequest(AsteroidsReplayEvaluator.RulesVersion,
                AsteroidsReplayEvaluator.MaximumReplaySteps, []));
        if (game == "flappy")
            return JsonSerializer.Serialize(new FlappyRecordedReplayRequest(36, []));

        var state = HorseFlightEngine.Start(entered.GetProperty("seed").GetUInt32(),
            rulesVersion: entered.GetProperty("rulesVersion").GetInt32());
        while (state.Phase == HorseFlightPhase.Running && state.Tick < HorseFlightReplayEvaluator.MaximumTicks)
            state = HorseFlightEngine.Step(state, HorseFlightInput.None).State;
        Assert.NotEqual(HorseFlightPhase.Running, state.Phase);
        var replay = JsonSerializer.Serialize(new CompleteHorseFlightRunRequest(state.Tick, [], []));
        // Exercise a payload above the platform default without changing the recorded inputs.
        return replay.PadRight(36 * 1024);
    }

    private static async Task AssertHorseFlightFreeRoutesAsync(HttpClient browser, string token)
    {
        const string direct = "/api/games/horse-flight";
        const string competition = "/api/arcade-competitions/horse-flight/free/runs";
        const string key = "signed-horse-flight-free-0001";
        using var status = await ReadAsync(await SendAsync(browser, HttpMethod.Get, direct + "/status", token));
        Assert.True(status.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal(5m, status.RootElement.GetProperty("balance").GetDecimal());
        using var started = await ReadAsync(await SendAsync(browser, HttpMethod.Post, direct + "/runs", token, key));
        using var replayed = await ReadAsync(await SendAsync(browser, HttpMethod.Post, competition, token, key));
        var runId = started.RootElement.GetProperty("runId").GetString();
        Assert.Equal(runId, replayed.RootElement.GetProperty("runId").GetString());
        Assert.True(replayed.RootElement.GetProperty("wasReplay").GetBoolean());
        var body = TerminalReplay("horse-flight", started.RootElement);
        using var complete = await ReadAsync(await SendAsync(browser, HttpMethod.Post, $"{direct}/runs/{runId}/complete",
            token, $"horse-flight-free-complete-{runId}", body));
        using var retry = await ReadAsync(await SendAsync(browser, HttpMethod.Post, $"{competition}/{runId}/replay", token, body: body));
        Assert.Equal(complete.RootElement.GetProperty("score").GetInt64(), retry.RootElement.GetProperty("score").GetInt64());
        Assert.True(retry.RootElement.GetProperty("wasReplay").GetBoolean());
    }

    private static async Task<IReadOnlyList<DocumentSnapshot>> DocumentsAsync(FirestoreDb database, string collection) =>
        (await database.Collection(collection).GetSnapshotAsync()).Documents;

    private sealed class CompetitionClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class LoseWalletResponses(string game) : DelegatingHandler
    {
        private readonly string entryPath = ArcadeCompetitionWalletProtocol.EntryRoute(game);
        private readonly string payoutPath = ArcadeCompetitionWalletProtocol.PayoutRoute(game);
        private int lostEntry;
        private int lostPayout;
        public bool LostEntry => Volatile.Read(ref lostEntry) != 0;
        public bool LostPayout => Volatile.Read(ref lostPayout) != 0;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (!response.IsSuccessStatusCode) return response;
            var path = request.RequestUri?.AbsolutePath;
            if (!(path == entryPath && Interlocked.Exchange(ref lostEntry, 1) == 0) &&
                !(path == payoutPath && Interlocked.Exchange(ref lostPayout, 1) == 0))
                return response;
            response.Dispose();
            throw new HttpRequestException("Lost the signed arcade wallet response after it committed the transaction.");
        }
    }
}
