using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.HiLo.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Activity;
using FortuneForge.Server.GameWorkers;
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
    private const string HiLoGame = "hi-lo";
    private const string HiLoOrigin = "https://fortuneforgegame.web.app";

    [Fact]
    public async Task HiLo_signed_gateway_recovers_a_reservation_and_cashout_once_across_worker_restart()
    {
        const long wager = 137;
        const string startKey = "table-hilo-smoke-start-01";
        const string cashoutKey = "table-hilo-smoke-cashout-01";
        const string startBody = "{\"operationId\":\"table-hilo-smoke-start-01\",\"wagerCents\":137}";
        const string cashoutBody = "{\"operationId\":\"table-hilo-smoke-cashout-01\",\"expectedRevision\":0,\"choice\":\"cashout\"}";
        const string roundsPath = "/api/games/hi-lo/rounds";
        const string sessionPath = "/api/games/hi-lo/session";
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var playerToken = await CreateAccountAsync(database, accounts, Player, "Player");
        var otherToken = await CreateAccountAsync(database, accounts, OtherPlayer, "Other Player");
        var walletKey = RandomNumberGenerator.GetBytes(32);
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var observed = new ConcurrentQueue<SignedHiLoCommand>();
        await using var wallet = await StartWalletAsync(database, HiLoGame, walletKey);
        wallet.Configuration[$"GameWallet:Games:{HiLoGame}:AllowWagerReservations"] = "true";
        wallet.Configuration[$"GameWallet:Games:{HiLoGame}:LegacySlotLedger"] = "false";
        var walletUrl = new Uri(wallet.Urls.Single());
        var fault = new DropFirstCommittedWalletReply(HiLoGame);
        await using var worker = await StartHiLoWorkerAsync(database, workerKey, walletUrl, walletKey, observed, enabled: true, fault: fault);
        await using var gateway = await StartHiLoGatewayAsync(database, accounts, worker.Urls.Single(), workerKey);
        using var browser = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
        browser.DefaultRequestHeaders.Add("Origin", HiLoOrigin);
        browser.DefaultRequestHeaders.Add("X-FortuneForge-Game", HiLoGame);

        using var wrongAccount = await SendAsync(browser, HttpMethod.Post, roundsPath, playerToken, startBody, startKey, OtherPlayer);
        Assert.Equal(HttpStatusCode.Forbidden, wrongAccount.StatusCode);
        Assert.Empty(observed);

        using var lostReply = await SendAsync(browser, HttpMethod.Post, roundsPath, playerToken, startBody, startKey, Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lostReply.StatusCode);
        var original = Assert.Single(observed);
        var originalRequest = AssertSignedHiLoCommand(original, roundsPath, startKey, startBody);
        var reserveBody = Assert.Single(fault.SettlementBodies);
        var reserveRequest = Assert.IsType<GameWalletSettlementRequest>(
            JsonSerializer.Deserialize<GameWalletSettlementRequest>(reserveBody, GameWalletAuthentication.WireJson));
        Assert.Equal(GameWalletSettlementPhase.Reserve, reserveRequest.Phase);
        Assert.Equal(wager, reserveRequest.WagerCents);
        Assert.Equal(0, reserveRequest.PayoutCents);
        var reserve = Assert.Single((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);
        Assert.Equal(BodySha(reserveBody).ToLowerInvariant(), reserve.GetValue<string>("requestBodySha256"));
        Assert.Equal(InitialCents - wager, await BalanceAsync(database, Player));

        gateway.Configuration["Features:HiLoEnabled"] = "false";
        gateway.Configuration[$"GameWorkers:{HiLoGame}:ForwardPublicRoutes"] = "false";
        await worker.StopAsync();
        using var unavailable = await SendAsync(browser, HttpMethod.Get, sessionPath, playerToken, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        using var unavailableBody = JsonDocument.Parse(await unavailable.Content.ReadAsStringAsync());
        Assert.Equal("game-worker-unavailable", unavailableBody.RootElement.GetProperty("code").GetString());

        await using var replica = await StartHiLoWorkerAsync(database, workerKey, walletUrl, walletKey, observed, enabled: false);
        gateway.Configuration[$"GameWorkers:{HiLoGame}:Url"] = replica.Urls.Single();
        using var restored = await SendAsync(browser, HttpMethod.Get, sessionPath, playerToken, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var session = ReadHiLoSession(await restored.Content.ReadAsStringAsync());
        Assert.False(session.Practice);
        Assert.False(session.Enabled);
        Assert.Equal(InitialCents - wager, session.WalletBalanceCents);
        var active = Assert.IsType<HiLoRoundResponse>(session.Round);
        Assert.Equal("active", active.Status);
        Assert.Equal(0, active.Revision);
        Assert.Equal(wager, active.WagerCents);
        Assert.Equal("round-" + active.RoundId, reserveRequest.OperationId);
        using var firstReplay = await SendAsync(browser, HttpMethod.Post, roundsPath, playerToken, startBody, startKey, Player);
        Assert.Equal(HttpStatusCode.OK, firstReplay.StatusCode);
        var exactStart = await firstReplay.Content.ReadAsStringAsync();
        Assert.Equal(active.RoundId, ReadHiLoSession(exactStart).Round?.RoundId);
        using var changed = await SendAsync(browser, HttpMethod.Post, roundsPath, playerToken, startBody.Replace("137", "139"), startKey, Player);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        var actionPath = $"{roundsPath}/{active.RoundId}/actions";
        using var foreign = await SendAsync(browser, HttpMethod.Post, actionPath, otherToken, cashoutBody, cashoutKey, OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Single((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);
        using var cashout = await SendAsync(browser, HttpMethod.Post, actionPath, playerToken, cashoutBody, cashoutKey, Player);
        Assert.Equal(HttpStatusCode.OK, cashout.StatusCode);
        var exactCashout = await cashout.Content.ReadAsStringAsync();
        var completed = ReadHiLoSession(exactCashout);
        Assert.Equal(InitialCents, completed.WalletBalanceCents);
        var closed = Assert.IsType<HiLoRoundResponse>(completed.Round);
        Assert.Equal(active.RoundId, closed.RoundId);
        Assert.Equal("cashedOut", closed.Status);
        Assert.Equal(wager, closed.CurrentReturnCents);
        var acceptedCashout = Assert.Single(observed, item => item.Actor == Player && item.Operation == cashoutKey);
        var cashoutRequest = AssertSignedHiLoCommand(acceptedCashout, actionPath, cashoutKey, cashoutBody);

        using var startReplay = await SendAsync(browser, HttpMethod.Post, roundsPath, playerToken, startBody, startKey, Player);
        using var cashoutReplay = await SendAsync(browser, HttpMethod.Post, actionPath, playerToken, cashoutBody, cashoutKey, Player);
        Assert.Equal(HttpStatusCode.OK, startReplay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cashoutReplay.StatusCode);
        Assert.Equal(exactStart, await startReplay.Content.ReadAsStringAsync());
        Assert.Equal(exactCashout, await cashoutReplay.Content.ReadAsStringAsync());
        const string pausedKey = "table-hilo-smoke-paused-01";
        using var paused = await SendAsync(browser, HttpMethod.Post, roundsPath, playerToken,
            startBody.Replace(startKey, pausedKey), pausedKey, Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, paused.StatusCode);
        using var final = await SendAsync(browser, HttpMethod.Get, sessionPath, playerToken, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, final.StatusCode);
        Assert.Equal(exactCashout, await final.Content.ReadAsStringAsync());
        Assert.Equal(InitialCents, await BalanceAsync(database, Player));
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));

        await AssertHiLoReservationEvidenceAsync(database, reserve, reserveRequest, active.RoundId, wager, originalRequest, cashoutRequest);

        gateway.Configuration[$"GameWorkers:{HiLoGame}:Url"] = null;
        using var missingWorker = await SendAsync(browser, HttpMethod.Get, sessionPath, playerToken, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, missingWorker.StatusCode);
        using var missingWorkerBody = JsonDocument.Parse(await missingWorker.Content.ReadAsStringAsync());
        Assert.Equal("game-worker-unavailable", missingWorkerBody.RootElement.GetProperty("code").GetString());
        Assert.Equal(InitialCents, await BalanceAsync(database, Player));
    }

    private static async Task AssertHiLoReservationEvidenceAsync(FirestoreDb database, DocumentSnapshot originalReserve,
        GameWalletSettlementRequest request, string roundId, long wager, GameRequestObservation start, GameRequestObservation cashout)
    {
        var reserveId = GameWalletSettlementIdentity.CreateId(HiLoGame, Player, request.OperationId);
        var completionId = GameWalletSettlementIdentity.CreateCompletionId(HiLoGame, Player, request.OperationId);
        var receipts = (await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents;
        Assert.Equal(2, receipts.Count);
        var reserve = Assert.Single(receipts, receipt => receipt.Id == reserveId);
        Assert.Equal(originalReserve.UpdateTime, reserve.UpdateTime);
        Assert.Equal(nameof(GameWalletSettlementPhase.Reserve), reserve.GetValue<string>("phase"));
        Assert.Equal(request.OutcomeSha256, reserve.GetValue<string>("outcomeSha256"));
        var completion = Assert.Single(receipts, receipt => receipt.Id == completionId);
        Assert.Equal(nameof(GameWalletSettlementPhase.CompleteReservation), completion.GetValue<string>("phase"));
        Assert.Equal(request.OperationId, completion.GetValue<string>("operationId"));
        Assert.Equal(request.OutcomeSha256, completion.GetValue<string>("reservationSha256"));
        Assert.Equal(0, completion.GetValue<long>("wagerCents"));
        Assert.Equal(wager, completion.GetValue<long>("payoutCents"));
        Assert.Equal(InitialCents, completion.GetValue<long>("balanceAfterCents"));

        var ledger = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(2, ledger.Count);
        var debit = Assert.Single(ledger, leg => leg.Id == reserveId + "-wager");
        Assert.Equal(reserveId, debit.GetValue<string>("walletSettlementId"));
        Assert.Equal(-wager, debit.GetValue<long>("amountCents"));
        var credit = Assert.Single(ledger, leg => leg.Id == reserveId + "-payout");
        Assert.Equal(completionId, credit.GetValue<string>("walletSettlementId"));
        Assert.Equal(wager, credit.GetValue<long>("amountCents"));

        var history = Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Equal($"{HiLoGame}-{roundId}", history.Id);
        Assert.Equal(Player, history.GetValue<string>("userId"));
        Assert.Equal(wager, history.GetValue<long>("wagerCents"));
        Assert.Equal(wager, history.GetValue<long>("payoutCents"));
        Assert.Equal(0, history.GetValue<long>("netCents"));
        var journal = (await database.Collection(GameSecurityEventDocuments.CollectionName)
            .WhereEqualTo("instanceId", roundId).GetSnapshotAsync()).Documents;
        Assert.Equal(new[] { start.RequestId, cashout.RequestId }.Order(StringComparer.Ordinal),
            journal.Select(entry => entry.GetValue<string>("requestId")).Order(StringComparer.Ordinal));
        var audit = Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        Assert.Equal(roundId, audit.GetValue<string>("roundId"));
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(audit.Id).GetSnapshotAsync();
        Assert.Equal(cashout.RequestId, evidence.GetValue<string>("requestId"));
        var payload = evidence.GetValue<string>("payloadJson");
        Assert.Equal(audit.GetValue<string>("evidenceSha256"), GameSecurityEventDocuments.Hash(payload));
        var summary = FirestoreGameRoundAuditStore.Summary(audit);
        Assert.Empty(PaidCardEvidenceReplayAudit.Inspect(summary, payload));
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));
    }

    private static async Task<WebApplication> StartHiLoWorkerAsync(FirestoreDb database, byte[] workerKey, Uri wallet,
        byte[] walletKey, ConcurrentQueue<SignedHiLoCommand> observed, bool enabled, DropFirstCommittedWalletReply? fault = null)
    {
        var builder = LoopbackBuilder();
        builder.Configuration["Features:HiLoEnabled"] = enabled.ToString();
        builder.Configuration["GameWorker:Mode"] = "hosted";
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Configuration["GameWorker:AllowedOrigins:0"] = HiLoOrigin;
        builder.Configuration["GameWallet:Url"] = wallet.AbsoluteUri;
        builder.Configuration["GameWallet:SigningKey"] = Convert.ToBase64String(walletKey);
        builder.Services.AddSingleton(database);
        var walletClient = builder.Services.AddGameWalletClient(HiLoGame);
        if (fault is not null) walletClient.AddHttpMessageHandler(() => fault);
        builder.Services.AddHiLoServer(options =>
            options.AllowedOrigins = builder.Configuration.GetSection("GameWorker:AllowedOrigins").Get<string[]>() ?? []);
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(HiLoGame);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method))
                observed.Enqueue(new(GameWorkerAuthentication.GetAccountId(context), context.Request.Path.Value,
                    context.Request.Headers["Idempotency-Key"].ToString(), context.Request.Headers.Origin.ToString(),
                    context.Request.Headers["X-FortuneForge-Game"].ToString(),
                    GameWorkerAuthentication.GetVerifiedBodySha256(context), GameSecurityContext.Current));
            await next(context);
        });
        app.MapHiLo((context, _) => Task.FromResult<HiLoAccount?>(
            GameWorkerAuthentication.GetAccountId(context) is { } user ? new HiLoAccount(user) : null));
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartHiLoGatewayAsync(FirestoreDb database, FirestoreAccountStore store,
        string workerUrl, byte[] workerKey)
    {
        var builder = LoopbackBuilder();
        builder.Configuration["Features:HiLoEnabled"] = "true";
        builder.Configuration[$"GameWorkers:{HiLoGame}:ForwardPublicRoutes"] = "true";
        builder.Configuration[$"GameWorkers:{HiLoGame}:Url"] = workerUrl;
        builder.Configuration[$"GameWorkers:{HiLoGame}:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(store, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        builder.Services.AddHiLoServer(options =>
        {
            options.ReadRateLimitPolicy = RateLimitPolicies.SlotReads;
            options.WriteRateLimitPolicy = RateLimitPolicies.SlotSpins;
            options.AllowedOrigins = [HiLoOrigin];
        });
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
        app.MapHiLo((_, _) => throw new InvalidOperationException("The paid request bypassed the signed Hi-Lo worker."));
        await app.StartAsync();
        return app;
    }

    private static HiLoSessionResponse ReadHiLoSession(string response) => Assert.IsType<HiLoSessionResponse>(
        JsonSerializer.Deserialize<HiLoSessionResponse>(response, JsonSerializerOptions.Web));

    private static GameRequestObservation AssertSignedHiLoCommand(SignedHiLoCommand command, string path, string operation, string body)
    {
        Assert.Equal((Player, path, operation, HiLoOrigin, HiLoGame, BodySha(body)),
            (command.Actor, command.Path, command.Operation, command.Origin, command.Game, command.BodySha256));
        var observation = Assert.IsType<GameRequestObservation>(command.Observation);
        Assert.Equal((Player, path, operation, HiLoGame), (observation.UserId, observation.Path, observation.CommandId, observation.GameId));
        return observation;
    }

    private sealed record SignedHiLoCommand(string? Actor, string? Path, string Operation, string Origin, string Game,
        string? BodySha256, GameRequestObservation? Observation);
}
