using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Cards.Baccarat;
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
    [Fact]
    public async Task Baccarat_mvc_gateway_recovers_the_original_signed_wager_and_private_history()
    {
        const string game = "baccarat";
        const string roundsPath = "/api/games/baccarat/rounds";
        const string body = "{ \"betSide\": \"player\", \"stake\": 0.50 }";
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(database, accounts, Player, "Player");
        var otherToken = await CreateAccountAsync(database, accounts, OtherPlayer, "Other Player");
        var walletKey = RandomNumberGenerator.GetBytes(32);
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var observed = new ConcurrentQueue<VerifiedWorkerRequest>();
        var fault = new DropFirstCommittedWalletReply(game);
        await using var wallet = await StartWalletAsync(database, game, walletKey);
        await using var worker = await StartWorkerAsync(database, game, "Baccarat", workerKey, new Uri(wallet.Urls.Single()),
            walletKey, fault, observed);
        await using var gateway = await StartBaccaratMvcGatewayAsync(database, accounts, new Uri(worker.Urls.Single()), workerKey);
        using var browser = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };

        using var wrongAccount = await SendAsync(browser, HttpMethod.Post, roundsPath, token, body, OperationKey, OtherPlayer);
        Assert.Equal(HttpStatusCode.Forbidden, wrongAccount.StatusCode);
        Assert.Empty(observed);

        using var lostReply = await SendAsync(browser, HttpMethod.Post, roundsPath, token, body, OperationKey, Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lostReply.StatusCode);
        var signed = Assert.Single(observed);
        Assert.Equal((Player, roundsPath, OperationKey, BodySha(body)),
            (signed.Actor, signed.Path, signed.OperationKey, signed.BodySha256));
        var request = Assert.IsType<GameRequestObservation>(signed.Observation);
        Assert.Equal((game, Player, OperationKey), (request.GameId, request.UserId, request.CommandId));

        var receipts = database.Collection("walletRoundSettlements");
        var receipt = Assert.Single((await receipts.GetSnapshotAsync()).Documents);
        var receiptId = GameWalletSettlementIdentity.CreateId(game, Player, OperationKey);
        Assert.Equal(receiptId, receipt.Id);
        Assert.Equal(WagerCents, receipt.GetValue<long>("wagerCents"));
        var payout = receipt.GetValue<long>("payoutCents");
        var finalBalance = InitialCents - WagerCents + payout;
        Assert.Equal(finalBalance, receipt.GetValue<long>("balanceAfterCents"));
        Assert.Empty((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);

        worker.Configuration["Features:BaccaratEnabled"] = "false";
        using var retry = await SendAsync(browser, HttpMethod.Post, roundsPath, token, body, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var roundId = Assert.IsType<string>(await ReadRoundIdAsync(retry));
        Assert.Equal($"{roundsPath}/{roundId}", retry.Headers.Location?.OriginalString);
        var savedResponse = await retry.Content.ReadAsStringAsync();
        using var latest = await SendAsync(browser, HttpMethod.Get, roundsPath + "/latest", token, expectedAccount: Player);
        using var byId = await SendAsync(browser, HttpMethod.Get, $"{roundsPath}/{roundId}", token, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
        Assert.Equal(savedResponse, await latest.Content.ReadAsStringAsync());
        Assert.Equal(savedResponse, await byId.Content.ReadAsStringAsync());
        using var foreign = await SendAsync(browser, HttpMethod.Get, $"{roundsPath}/{roundId}", otherToken, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var changed = await SendAsync(browser, HttpMethod.Post, roundsPath, token, body.Replace("0.50", "0.75"), OperationKey, Player);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var paused = await SendAsync(browser, HttpMethod.Post, roundsPath, token, body, "baccarat-paused-smoke-0001", Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, paused.StatusCode);

        var walletBodies = fault.SettlementBodies.ToArray();
        Assert.Equal(2, walletBodies.Length);
        Assert.Equal(walletBodies[0], walletBodies[1]);
        Assert.Equal(receipt.UpdateTime, Assert.Single((await receipts.GetSnapshotAsync()).Documents).UpdateTime);
        Assert.Equal(finalBalance, await BalanceAsync(database, Player));
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));
        var ledger = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(payout > 0 ? 2 : 1, ledger.Count);
        Assert.All(ledger, leg => Assert.Equal(receiptId, leg.GetValue<string>("walletSettlementId")));
        Assert.Equal(-WagerCents, Assert.Single(ledger, leg => leg.Id == receiptId + "-wager").GetValue<long>("amountCents"));
        if (payout > 0)
            Assert.Equal(payout, Assert.Single(ledger, leg => leg.Id == receiptId + "-payout").GetValue<long>("amountCents"));

        var prepared = Assert.Single((await database.Collection(GameSecurityEventDocuments.CollectionName)
            .WhereEqualTo("instanceId", roundId).GetSnapshotAsync()).Documents);
        Assert.Equal(request.RequestId, prepared.GetValue<string>("requestId"));
        var audit = Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        Assert.Equal(game, audit.GetValue<string>("gameId"));
        Assert.Equal(roundId, audit.GetValue<string>("roundId"));
        Assert.Equal("passed", audit.GetValue<string>("auditStatus"));
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(audit.Id).GetSnapshotAsync();
        var payload = evidence.GetValue<string>("payloadJson");
        Assert.Equal(audit.GetValue<string>("evidenceSha256"), GameSecurityEventDocuments.Hash(payload));
        using var details = JsonDocument.Parse(payload);
        Assert.Equal(WagerCents, details.RootElement.GetProperty("wagerCents").GetInt64());
        Assert.Equal(payout, details.RootElement.GetProperty("payoutCents").GetInt64());
        var summary = FirestoreGameRoundAuditStore.Summary(audit);
        Assert.Empty(PaidCardEvidenceReplayAudit.Inspect(summary, payload));
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));
    }

    private static async Task<WebApplication> StartBaccaratMvcGatewayAsync(FirestoreDb database, FirestoreAccountStore store,
        Uri worker, byte[] workerKey)
    {
        var builder = LoopbackBuilder();
        builder.Configuration["Features:BaccaratEnabled"] = "false";
        builder.Configuration["GameWorkers:baccarat:ForwardPublicRoutes"] = "true";
        builder.Configuration["GameWorkers:baccarat:Url"] = worker.AbsoluteUri;
        builder.Configuration["GameWorkers:baccarat:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(store, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        builder.Services.AddSingleton<PracticeBaccaratStore>();
        builder.Services.AddControllers().AddApplicationPart(typeof(BaccaratController).Assembly);
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
