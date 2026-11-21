using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Keno;
using FortuneForge.Games.Keno.Server;
using FortuneForge.Games.SicBo.Server;
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
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

public sealed partial class SignedCardWorkerSmokeTests
{
    [Theory]
    [InlineData("keno", "{ \"ticket\": { \"numbers\": [1, 2, 3] }, \"wager\": 1 }\n",
        "{\"ticket\":{\"numbers\":[1,2,4]},\"wager\":1}", 100L)]
    [InlineData("sic-bo", "{ \"bets\": [{\"kind\":\"small\",\"stake\":1},{\"kind\":\"big\",\"stake\":1},{\"kind\":\"any-triple\",\"stake\":1}] }\n",
        "{\"bets\":[{\"kind\":\"odd\",\"stake\":1},{\"kind\":\"big\",\"stake\":1},{\"kind\":\"any-triple\",\"stake\":1}]}", 300L)]
    public async Task Number_games_mvc_gateway_recovers_one_signed_wallet_round_and_keeps_history_private(
        string game, string body, string changedBody, long wager)
    {
        var keno = game == "keno";
        var feature = keno ? "Features:KenoEnabled" : "Features:SicBoEnabled";
        var collection = keno ? "kenoRounds" : "sicBoRounds";
        var publicPath = $"/api/games/{game}/rounds";
        var roundId = GameSecurityEventDocuments.Hash($"{Player}\n{OperationKey}");
        var historyPath = $"{publicPath}/{roundId}";
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(database, accounts, Player, "Player");
        var otherToken = await CreateAccountAsync(database, accounts, OtherPlayer, "Other Player");
        var walletKey = RandomNumberGenerator.GetBytes(32);
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var fault = new DropFirstCommittedWalletReply(game);
        var observed = new ConcurrentQueue<VerifiedWorkerRequest>();
        await using var wallet = await StartWalletAsync(database, game, walletKey);
        await using var worker = await StartNumberWorkerAsync(database, game, feature, new Uri(wallet.Urls.Single()), walletKey, workerKey, fault, observed);
        await using var gateway = await StartPaidGameMvcGatewayAsync(database, accounts, game, feature, new Uri(worker.Urls.Single()), workerKey);
        using var browser = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
        using var direct = new HttpClient { BaseAddress = new Uri(worker.Urls.Single()) };
        var source = database.Collection(collection);
        var receipts = database.Collection("walletRoundSettlements");

        using var unsigned = await SendAsync(direct, HttpMethod.Post, publicPath, token, body, OperationKey);
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        using var switched = await SendAsync(browser, HttpMethod.Post, publicPath, otherToken, body, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Forbidden, switched.StatusCode);
        Assert.Empty(observed);
        Assert.Empty((await source.GetSnapshotAsync()).Documents);
        Assert.Empty((await receipts.GetSnapshotAsync()).Documents);

        using var lost = await SendAsync(browser, HttpMethod.Post, publicPath, token, body, OperationKey, Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        var verified = Assert.Single(observed);
        Assert.Equal((Player, publicPath, OperationKey, BodySha(body)),
            (verified.Actor, verified.Path, verified.OperationKey, verified.BodySha256));
        var request = Assert.IsType<GameRequestObservation>(verified.Observation);
        Assert.Equal((game, Player, OperationKey), (request.GameId, request.UserId, request.CommandId));

        var pending = Assert.Single((await source.GetSnapshotAsync()).Documents);
        Assert.Equal(roundId, pending.Id);
        Assert.Equal(Player, pending.GetValue<string>("userId"));
        Assert.False(pending.ContainsField("settledBalanceCents"));
        Assert.Empty((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        var receipt = Assert.Single((await receipts.GetSnapshotAsync()).Documents);
        var receiptId = GameWalletSettlementIdentity.CreateId(game, Player, roundId);
        Assert.Equal(receiptId, receipt.Id);
        Assert.Equal(roundId, receipt.GetValue<string>("operationId"));
        Assert.Equal(pending.GetValue<string>("outcomeSha256"), receipt.GetValue<string>("outcomeSha256"), ignoreCase: true);
        Assert.Equal(wager, receipt.GetValue<long>("wagerCents"));
        var payout = receipt.GetValue<long>("payoutCents");
        if (!keno) Assert.True(payout > 0);
        var balance = InitialCents - wager + payout;
        Assert.Equal(balance, receipt.GetValue<long>("balanceAfterCents"));
        Assert.Equal(balance, await BalanceAsync(database, Player));
        using var foreignPending = await SendAsync(browser, HttpMethod.Get, historyPath, otherToken, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreignPending.StatusCode);
        Assert.Single(fault.SettlementBodies);

        worker.Configuration[feature] = "false";
        using var recovered = await SendAsync(browser, HttpMethod.Post, publicPath, token, body, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        Assert.Equal(historyPath, recovered.Headers.Location?.OriginalString);
        var response = await recovered.Content.ReadAsStringAsync();
        using (var output = JsonDocument.Parse(response))
        {
            var result = output.RootElement;
            Assert.Equal(roundId, result.GetProperty("roundId").GetString());
            Assert.Equal(keno ? "completed" : "settled", result.GetProperty("phase").GetString());
            Assert.Equal(wager / 100m, result.GetProperty(keno ? "wager" : "totalStaked").GetDecimal());
            Assert.Equal(payout / 100m, result.GetProperty(keno ? "payout" : "totalReturn").GetDecimal());
            Assert.Equal(balance / 100m, result.GetProperty("balance").GetDecimal());
        }
        var attempts = observed.ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.Equal(verified.BodySha256, attempts[1].BodySha256);
        Assert.NotEqual(request.RequestId, attempts[1].Observation?.RequestId);
        using var retry = await SendAsync(browser, HttpMethod.Post, publicPath, token, body, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(response, await retry.Content.ReadAsStringAsync());
        using var changed = await SendAsync(browser, HttpMethod.Post, publicPath, token, changedBody, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var paused = await SendAsync(browser, HttpMethod.Post, publicPath, token, body, "number-game-paused-smoke-0001", Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, paused.StatusCode);
        using var pausedStatus = await SendAsync(browser, HttpMethod.Get, $"/api/games/{game}/status", token, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, pausedStatus.StatusCode);
        using (var status = JsonDocument.Parse(await pausedStatus.Content.ReadAsStringAsync()))
            Assert.False(status.RootElement.GetProperty("available").GetBoolean());
        using var history = await SendAsync(browser, HttpMethod.Get, historyPath, token, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Equal(response, await history.Content.ReadAsStringAsync());
        using var foreign = await SendAsync(browser, HttpMethod.Get, historyPath, otherToken, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var walletBodies = fault.SettlementBodies.ToArray();
        Assert.Equal(2, walletBodies.Length);
        Assert.Equal(walletBodies[0], walletBodies[1]);
        Assert.Equal(BodySha(walletBodies[0]), receipt.GetValue<string>("requestBodySha256"), ignoreCase: true);
        Assert.Equal(receipt.UpdateTime, Assert.Single((await receipts.GetSnapshotAsync()).Documents).UpdateTime);
        var saved = Assert.Single((await source.GetSnapshotAsync()).Documents);
        Assert.Equal(balance, saved.GetValue<long>("settledBalanceCents"));
        Assert.Equal(pending.GetValue<string>("outcomeSha256"), saved.GetValue<string>("outcomeSha256"));
        var ledger = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(payout > 0 ? 2 : 1, ledger.Count);
        Assert.All(ledger, row =>
        {
            Assert.Equal(receiptId, row.GetValue<string>("walletSettlementId"));
            Assert.Equal(Player, row.GetValue<string>("userId"));
        });
        Assert.Equal(-wager, Assert.Single(ledger, row => row.Id == receiptId + "-wager").GetValue<long>("amountCents"));
        if (payout > 0)
            Assert.Equal(payout, Assert.Single(ledger, row => row.Id == receiptId + "-payout").GetValue<long>("amountCents"));
        Assert.Equal(balance, await BalanceAsync(database, Player));
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));

        var audit = Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(audit.Id).GetSnapshotAsync();
        var payload = evidence.GetValue<string>("payloadJson");
        Assert.Equal(request.RequestId, evidence.GetValue<string>("requestId"));
        Assert.Equal(audit.GetValue<string>("evidenceSha256"), GameSecurityEventDocuments.Hash(payload));
        var summary = FirestoreGameRoundAuditStore.Summary(audit);
        Assert.Empty(NumericEvidenceReplayAudit.Inspect(summary, payload));
        Assert.Empty(await NumericEvidenceReplayAudit.InspectFinancialAsync(database, summary, default, payload));
        Assert.Single((await database.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);

        var outcomeField = keno ? "draw" : "dice";
        var originalOutcome = saved.GetValue<List<long>>(outcomeField);
        long[] changedOutcome;
        if (keno)
        {
            var ticket = saved.GetValue<List<long>>("ticket");
            changedOutcome = originalOutcome.ToArray();
            var index = Array.FindIndex(changedOutcome, number => !ticket.Contains(number));
            Assert.InRange(index, 0, changedOutcome.Length - 1);
            changedOutcome[index] = Enumerable.Range(1, KenoTicket.MaximumNumber)
                .Select(number => (long)number).First(number => !ticket.Contains(number) && !originalOutcome.Contains(number));
        }
        else
        {
            changedOutcome = originalOutcome.Distinct().Count() == 1
                ? Enumerable.Repeat(originalOutcome[0] == 1 ? 2L : 1L, 3).ToArray()
                : originalOutcome.SequenceEqual(new long[] { 1, 1, 2 }) ? [1, 1, 3] : [1, 1, 2];
        }
        try
        {
            await saved.Reference.UpdateAsync(outcomeField, changedOutcome);
            Assert.Contains(await NumericEvidenceReplayAudit.InspectFinancialAsync(database, summary, default, payload),
                finding => finding.Code == "numeric-signed-wallet-mismatch");
        }
        finally
        {
            await saved.Reference.UpdateAsync(outcomeField, originalOutcome);
        }
        Assert.Empty(await NumericEvidenceReplayAudit.InspectFinancialAsync(database, summary, default, payload));
        Assert.Equal(receipt.UpdateTime, Assert.Single((await receipts.GetSnapshotAsync()).Documents).UpdateTime);
        var unchangedLedger = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(ledger.Select(row => (row.Id, row.UpdateTime)).OrderBy(row => row.Id).ToArray(),
            unchangedLedger.Select(row => (row.Id, row.UpdateTime)).OrderBy(row => row.Id).ToArray());
        Assert.Equal(balance, await BalanceAsync(database, Player));

        await database.Collection("balanceTransactions").Document(receiptId + "-wager").UpdateAsync("amountCents", -wager - 1);
        Assert.Contains(await NumericEvidenceReplayAudit.InspectFinancialAsync(database, summary, default, payload),
            finding => finding.Code == "numeric-signed-wallet-mismatch");
    }

    private static async Task<WebApplication> StartNumberWorkerAsync(FirestoreDb database, string game, string feature,
        Uri wallet, byte[] walletKey, byte[] workerKey, DropFirstCommittedWalletReply fault, ConcurrentQueue<VerifiedWorkerRequest> observed)
    {
        var builder = LoopbackBuilder();
        builder.Configuration[feature] = "true";
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Configuration["GameWallet:Url"] = wallet.AbsoluteUri;
        builder.Configuration["GameWallet:SigningKey"] = Convert.ToBase64String(walletKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddGameWalletClient(game).AddHttpMessageHandler(() => fault);
        if (game == "keno") builder.Services.AddKenoWorker(builder.Configuration);
        else builder.Services.AddSicBoWorker(builder.Configuration);
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
        if (game == "keno") app.MapKenoWorker(GameWorkerAuthentication.GetAccountId);
        else app.MapSicBoWorker(GameWorkerAuthentication.GetAccountId);
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartPaidGameMvcGatewayAsync(FirestoreDb database, FirestoreAccountStore accounts,
        string game, string feature, Uri worker, byte[] workerKey)
    {
        var builder = LoopbackBuilder();
        builder.Configuration[feature] = "false";
        builder.Configuration[$"GameWorkers:{game}:ForwardPublicRoutes"] = "true";
        builder.Configuration[$"GameWorkers:{game}:Url"] = worker.AbsoluteUri;
        builder.Configuration[$"GameWorkers:{game}:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(accounts, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        builder.Services.AddControllers().AddApplicationPart(typeof(PackagedGameWorkerForwarding).Assembly);
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
}
