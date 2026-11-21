using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Bots;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.Games.Roulette;
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
    private const string RouletteGame = "roulette";
    private const string RoulettePrefix = "/api/games/roulette";
    private const string RoulettePlayerNameGrant = "GameWallet:Games:roulette:AllowPlayerNameRead";

    [Fact]
    public async Task Roulette_mvc_gateway_uses_signed_names_and_roster_and_preserves_free_play_history()
    {
        const string trustedName = "Trusted Player";
        const string joinKey = "roulette-smoke-join-0001";
        const string joinBody = "{\"playerName\":\"Forged Name\"}";
        const string roundsPath = RoulettePrefix + "/rounds";
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(database, accounts, Player, trustedName);
        var otherToken = await CreateAccountAsync(database, accounts, OtherPlayer, "Other Player");
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var profileKey = RandomNumberGenerator.GetBytes(32);
        var rosterKey = RandomNumberGenerator.GetBytes(32);
        var observed = new ConcurrentQueue<VerifiedWorkerRequest>();
        await using var platform = await StartRoulettePlatformAsync(database, profileKey, rosterKey);
        await using var worker = await StartRouletteWorkerAsync(database, platform.Urls.Single(), workerKey, profileKey, rosterKey, observed);
        await using var gateway = await StartRouletteGatewayAsync(database, accounts, worker.Urls.Single(), workerKey);
        using var browser = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
        Task<HttpResponseMessage> Call(HttpMethod method, string path, string? body = null, string? key = null) =>
            SendAsync(browser, method, path, token, body, key, Player);

        using var staleAccount = await SendAsync(browser, HttpMethod.Post, roundsPath, token, joinBody, joinKey, OtherPlayer);
        Assert.Equal(HttpStatusCode.Forbidden, staleAccount.StatusCode);
        Assert.Empty(observed);
        using var noProfileGrant = await Call(HttpMethod.Post, roundsPath, joinBody, joinKey);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, noProfileGrant.StatusCode);
        await AssertRouletteErrorAsync(noProfileGrant, "roulette-account-unavailable");
        Assert.Empty((await database.Collection("rouletteOperations").GetSnapshotAsync()).Documents);

        platform.Configuration[RoulettePlayerNameGrant] = "true";
        var reader = worker.Services.GetRequiredService<IGameWalletAccountReader>();
        Assert.Equal(new GameWalletAccountState(0, PlayerName: trustedName), await reader.GetStateAsync(Player, default));
        using var joined = await Call(HttpMethod.Post, roundsPath, joinBody, joinKey);
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        var joinJson = await joined.Content.ReadAsStringAsync();
        var round = ReadRouletteRound(joinJson);
        Assert.Equal(1000m, round.Balance);
        Assert.Equal(trustedName, Assert.Single(round.Players).DisplayName);
        Assert.DoesNotContain("Forged Name", joinJson, StringComparison.Ordinal);
        var tableId = round.RoundId;
        var tablePath = $"{roundsPath}/{tableId:D}";

        platform.Configuration[RoulettePlayerNameGrant] = "false";
        using var savedJoin = await Call(HttpMethod.Post, roundsPath, joinBody, joinKey);
        Assert.Equal(HttpStatusCode.OK, savedJoin.StatusCode);
        Assert.Equal(joinJson, await savedJoin.Content.ReadAsStringAsync());
        platform.Configuration[RoulettePlayerNameGrant] = "true";

        string Bet(RouletteRoundResponse state) => JsonSerializer.Serialize(new PlaceRouletteBetRequest("red", 5m, null, [], state.Revision), JsonSerializerOptions.Web);
        string Revision(RouletteRoundResponse state) => JsonSerializer.Serialize(new RouletteActionRequest(state.Revision), JsonSerializerOptions.Web);
        using var placed = await Call(HttpMethod.Post, tablePath + "/bets", Bet(round), "roulette-smoke-bet-0001");
        var bet = await ReadRouletteRoundAsync(placed);
        Assert.Equal(995m, bet.Balance);
        var deleteBody = $"{{ \"expectedRevision\": {bet.Revision} }}\r\n";
        const string deleteKey = "roulette-smoke-delete-0001";
        var deletePath = tablePath + "/bets/0";
        using var removed = await Call(HttpMethod.Delete, deletePath, deleteBody, deleteKey);
        var removeJson = await removed.Content.ReadAsStringAsync();
        var afterRemoval = await ReadRouletteRoundAsync(removed);
        Assert.Empty(afterRemoval.Bets);
        Assert.Equal(1000m, afterRemoval.Balance);
        var deleteProof = Assert.Single(observed, request => request.OperationKey == deleteKey);
        Assert.Equal((Player, deletePath, BodySha(deleteBody)), (deleteProof.Actor, deleteProof.Path, deleteProof.BodySha256));
        Assert.Equal("DELETE", Assert.IsType<GameRequestObservation>(deleteProof.Observation).Method);

        using var newBet = await Call(HttpMethod.Post, tablePath + "/bets", Bet(afterRemoval), "roulette-smoke-bet-0002");
        var newWager = await ReadRouletteRoundAsync(newBet);
        var spinBody = Revision(newWager);
        const string spinKey = "roulette-smoke-spin-0001";
        using var spun = await Call(HttpMethod.Post, tablePath + "/spin", spinBody, spinKey);
        var spinJson = await spun.Content.ReadAsStringAsync();
        round = await ReadRouletteRoundAsync(spun);
        Assert.Equal("settled", round.Phase);
        Assert.InRange(Assert.IsType<int>(round.WinningPocket), 0, 36);
        Assert.Equal(995m + Assert.Single(round.Settlements).TotalReturn, round.Balance);
        var historyPath = $"{tablePath}/history/{round.RoundNumber}";

        // The real population policy schedules the first arrival between rounds 3 and 6.
        for (var i = 2; i <= 6; i++)
        {
            using var next = await Call(HttpMethod.Post, roundsPath, "{}", $"roulette-smoke-next-{i:D2}");
            round = await ReadRouletteRoundAsync(next);
            Assert.Equal(tableId, round.RoundId);
            Assert.Equal(i, round.RoundNumber);
            if (round.Players.Count > 1) break;
            using var wager = await Call(HttpMethod.Post, tablePath + "/bets", Bet(round), $"roulette-smoke-cadence-bet-{i:D2}");
            round = await ReadRouletteRoundAsync(wager);
            using var spin = await Call(HttpMethod.Post, tablePath + "/spin", Revision(round), $"roulette-smoke-cadence-spin-{i:D2}");
            round = await ReadRouletteRoundAsync(spin);
        }
        var managed = Assert.Single(round.Players, player => !player.IsCurrentPlayer);
        var assignments = database.Collection("managedPlayerAssignments");
        var lease = Assert.Single((await assignments.GetSnapshotAsync()).Documents);
        Assert.Equal(RouletteGame, lease.GetValue<string>("gameId"));
        Assert.Equal($"roulette:{tableId:N}", lease.GetValue<string>("assignmentId"));
        var managedProfile = await database.Collection("users").Document(lease.Id).GetSnapshotAsync();
        Assert.True(FirestoreManagedPlayerProfileRepository.IsManagedPlayer(managedProfile));
        Assert.Equal(managed.DisplayName, managedProfile.GetValue<string>("playerName"));
        using var current = await Call(HttpMethod.Get, tablePath);
        Assert.Contains((await ReadRouletteRoundAsync(current)).Players, player => player == managed);
        var renewed = await assignments.Document(lease.Id).GetSnapshotAsync();
        Assert.True(renewed.GetValue<Timestamp>("heartbeatAt").ToDateTime() > lease.GetValue<Timestamp>("heartbeatAt").ToDateTime());

        worker.Configuration["Features:RouletteEnabled"] = "false";
        gateway.Configuration["Features:RouletteEnabled"] = "false";
        using var pausedStatus = await Call(HttpMethod.Get, RoulettePrefix + "/status");
        Assert.Equal(HttpStatusCode.OK, pausedStatus.StatusCode);
        var status = Assert.IsType<RouletteStatusResponse>(JsonSerializer.Deserialize<RouletteStatusResponse>(
            await pausedStatus.Content.ReadAsStringAsync(), JsonSerializerOptions.Web));
        Assert.False(status.Available);
        Assert.Equal("free-play-shared-single-zero", status.Mode);
        using var pausedJoin = await Call(HttpMethod.Post, roundsPath, "{}", "roulette-smoke-paused-join");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, pausedJoin.StatusCode);
        await AssertRouletteErrorAsync(pausedJoin, "roulette-disabled");
        using var savedSpin = await Call(HttpMethod.Post, tablePath + "/spin", spinBody, spinKey);
        Assert.Equal(HttpStatusCode.OK, savedSpin.StatusCode);
        Assert.Equal(spinJson, await savedSpin.Content.ReadAsStringAsync());
        using var savedRemoval = await Call(HttpMethod.Delete, deletePath, deleteBody, deleteKey);
        Assert.Equal(HttpStatusCode.OK, savedRemoval.StatusCode);
        Assert.Equal(removeJson, await savedRemoval.Content.ReadAsStringAsync());
        using var history = await Call(HttpMethod.Get, historyPath);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Equal(spinJson, await history.Content.ReadAsStringAsync());
        using var privateHistory = await SendAsync(browser, HttpMethod.Get, historyPath, otherToken, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, privateHistory.StatusCode);

        Assert.Empty((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(InitialCents, await BalanceAsync(database, Player));
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));
    }

    private static async Task<WebApplication> StartRoulettePlatformAsync(FirestoreDb database, byte[] profileKey, byte[] rosterKey)
    {
        var builder = LoopbackBuilder();
        builder.Configuration[$"GameWallet:Games:{RouletteGame}:SigningKey"] = Convert.ToBase64String(profileKey);
        builder.Configuration[$"ManagedPlayerRoster:Games:{RouletteGame}:Enabled"] = "true";
        builder.Configuration[$"ManagedPlayerRoster:Games:{RouletteGame}:SigningKey"] = Convert.ToBase64String(rosterKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<IWalletRoundSettlementStore>(new FirestoreWalletRoundSettlementStore(database));
        var profiles = new FirestoreManagedPlayerProfileRepository(database);
        builder.Services.AddSingleton<IManagedPlayerQueuer>(new ManagedPlayerQueuer(new ManagedPlayerProfileGenerator(profiles),
            profiles, new FirestoreManagedPlayerAssignmentStore(database)));
        var app = builder.Build();
        app.MapGameWalletSettlement();
        app.MapGameManagedPlayerRoster();
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartRouletteWorkerAsync(FirestoreDb database, string platformUrl, byte[] workerKey,
        byte[] profileKey, byte[] rosterKey, ConcurrentQueue<VerifiedWorkerRequest> observed)
    {
        var builder = LoopbackBuilder();
        builder.Configuration["Features:RouletteEnabled"] = "true";
        builder.Configuration["GameWorker:Mode"] = "hosted";
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Configuration["GameWallet:Url"] = platformUrl;
        builder.Configuration["GameWallet:SigningKey"] = Convert.ToBase64String(profileKey);
        builder.Configuration["ManagedPlayerRoster:Url"] = platformUrl;
        builder.Configuration["ManagedPlayerRoster:SigningKey"] = Convert.ToBase64String(rosterKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddGameWalletClient(RouletteGame);
        builder.Services.AddManagedPlayerRosterClient(RouletteGame);
        builder.Services.AddRouletteServer();
        // Drive the real population policy by advancing rounds in order without a competing scheduler tick.
        builder.Services.Remove(builder.Services.Single(service => service.ImplementationType == typeof(RoulettePackageWorker)));
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(RouletteGame);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
                observed.Enqueue(new(GameWorkerAuthentication.GetAccountId(context), context.Request.Path.Value,
                    context.Request.Headers["Idempotency-Key"].ToString(), GameWorkerAuthentication.GetVerifiedBodySha256(context),
                    GameSecurityContext.Current));
            await next(context);
        });
        app.MapRoulette(GameWorkerAuthentication.GetAccountId);
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartRouletteGatewayAsync(FirestoreDb database, FirestoreAccountStore store,
        string workerUrl, byte[] workerKey)
    {
        var builder = LoopbackBuilder();
        builder.Configuration["Features:RouletteEnabled"] = "true";
        builder.Configuration[$"GameWorkers:{RouletteGame}:ForwardPublicRoutes"] = "true";
        builder.Configuration[$"GameWorkers:{RouletteGame}:Url"] = workerUrl;
        builder.Configuration[$"GameWorkers:{RouletteGame}:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(store, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        builder.Services.AddControllers().AddApplicationPart(typeof(RouletteController).Assembly);
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

    private static async Task<RouletteRoundResponse> ReadRouletteRoundAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ReadRouletteRound(await response.Content.ReadAsStringAsync());
    }

    private static RouletteRoundResponse ReadRouletteRound(string json) => Assert.IsType<RouletteRoundResponse>(
        JsonSerializer.Deserialize<RouletteRoundResponse>(json, JsonSerializerOptions.Web));

    private static async Task AssertRouletteErrorAsync(HttpResponseMessage response, string code) =>
        Assert.Equal(code, Assert.IsType<RouletteErrorResponse>(JsonSerializer.Deserialize<RouletteErrorResponse>(
            await response.Content.ReadAsStringAsync(), JsonSerializerOptions.Web)).Code);
}
