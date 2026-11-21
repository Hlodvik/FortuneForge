using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.HouseHoldem;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Cards.HouseHoldem;
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
    private const string HouseHoldemFinalKey = "house-holdem-smoke-final-0001";

    [Theory]
    [InlineData(HouseHoldemActions.Fold)]
    [InlineData(HouseHoldemActions.Play1X)]
    public async Task House_holdem_mvc_gateway_recovers_frozen_opening_and_can_finish_while_paused(string finalAction)
    {
        const string game = HouseHoldemBackend.GameId;
        const string starts = HouseHoldemBackend.ApiPath + "/rounds";
        const string openingBody = "{ \"ante\": 1, \"trips\": 0 }\n";
        const long openingStake = 200;
        var roundId = GameSecurityEventDocuments.Hash($"{Player}\n{OperationKey}");
        var roundPath = $"{starts}/{roundId}";
        var actionPath = roundPath + "/actions";
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(database, accounts, Player, "Player");
        var otherToken = await CreateAccountAsync(database, accounts, OtherPlayer, "Other Player");
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var walletKey = RandomNumberGenerator.GetBytes(32);
        var fault = new DropFirstCommittedWalletReply(game);
        var observed = new ConcurrentQueue<VerifiedWorkerRequest>();
        await using var wallet = await StartWalletAsync(database, game, walletKey);
        await using var worker = await StartHouseHoldemWorkerAsync(database, new Uri(wallet.Urls.Single()), walletKey, workerKey, fault, observed);
        await using var gateway = await StartHouseHoldemGatewayAsync(database, accounts, new Uri(worker.Urls.Single()), workerKey);
        using var browser = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
        using var direct = new HttpClient { BaseAddress = new Uri(worker.Urls.Single()) };
        var rounds = database.Collection("houseHoldemRounds");
        var commands = database.Collection("houseHoldemPreparedCommands");
        var active = database.Collection("houseHoldemActiveRounds").Document(Player);
        var receipts = database.Collection("walletRoundSettlements");

        using var unsigned = await SendAsync(direct, HttpMethod.Post, starts, token, openingBody, OperationKey);
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        using var switched = await SendAsync(browser, HttpMethod.Post, starts, otherToken, openingBody, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Forbidden, switched.StatusCode);
        Assert.Empty(observed);
        Assert.Empty((await receipts.GetSnapshotAsync()).Documents);

        using var lost = await SendAsync(browser, HttpMethod.Post, starts, token, openingBody, OperationKey, Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        var openingObservation = AssertHouseHoldemRequest(Assert.Single(observed), starts, openingBody, OperationKey);
        var prepared = Assert.Single((await commands.GetSnapshotAsync()).Documents);
        var frozen = prepared.GetValue<string>("payloadJson");
        var hash = prepared.GetValue<string>("payloadSha256");
        var frozenRound = HouseHoldemPreparedStore.ReadCommand(prepared).Prepared.After.Round;
        Assert.Equal(GameSecurityEventDocuments.Hash(frozen), hash);
        Assert.Equal("pending", prepared.GetValue<string>("status"));
        Assert.Equal(prepared.Id, (await active.GetSnapshotAsync()).GetValue<string>("pendingOperation"));
        Assert.Empty((await rounds.GetSnapshotAsync()).Documents);
        var opening = Assert.Single((await receipts.GetSnapshotAsync()).Documents);
        Assert.Equal(GameWalletSettlementIdentity.CreateId(game, Player, $"{game}-{prepared.Id}"), opening.Id);
        Assert.Equal(hash, opening.GetValue<string>("outcomeSha256"), ignoreCase: true);
        Assert.Equal(openingStake, opening.GetValue<long>("wagerCents"));
        Assert.Equal(openingStake + 100, opening.GetValue<long>("minimumBalanceBeforeCents"));
        Assert.Equal(InitialCents - openingStake, await BalanceAsync(database, Player));
        using var foreignPending = await SendAsync(browser, HttpMethod.Get, roundPath, otherToken, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreignPending.StatusCode);
        Assert.Single(fault.SettlementBodies);

        worker.Configuration[HouseHoldemBackend.EnabledSetting] = "false";
        using var retry = await SendAsync(browser, HttpMethod.Post, starts, token, openingBody, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(roundPath, retry.Headers.Location?.OriginalString);
        var opened = Assert.IsType<HouseHoldemRoundResponse>(await retry.Content.ReadFromJsonAsync<HouseHoldemRoundResponse>());
        var frozenView = HouseHoldemService.ToResponse(new(roundId, frozenRound, InitialCents - openingStake));
        Assert.Equal(roundId, opened.RoundId);
        Assert.Equal(HouseHoldemPhases.Preflop, opened.Phase);
        Assert.Equal(frozenView.PlayerCards.ToArray(), opened.PlayerCards.ToArray());
        Assert.Null(opened.DealerCards);
        Assert.Empty(opened.Board);
        var replayObservation = AssertHouseHoldemRequest(observed.Last(), starts, openingBody, OperationKey);
        Assert.NotEqual(openingObservation.RequestId, replayObservation.RequestId);
        var savedOpening = await prepared.Reference.GetSnapshotAsync();
        Assert.Equal(frozen, savedOpening.GetValue<string>("payloadJson"));
        Assert.Equal("applied", savedOpening.GetValue<string>("status"));
        Assert.Equal(roundId, (await active.GetSnapshotAsync()).GetValue<string>("roundId"));
        using var changed = await SendAsync(browser, HttpMethod.Post, starts, token, "{\"ante\":2,\"trips\":0}", OperationKey, Player);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var paused = await SendAsync(browser, HttpMethod.Post, starts, token, openingBody, "house-holdem-smoke-paused-0001", Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, paused.StatusCode);

        foreach (var phase in new[] { HouseHoldemPhases.Flop, HouseHoldemPhases.River })
        {
            using var check = await SendAsync(browser, HttpMethod.Post, actionPath, token,
                "{\"action\":\"check\"}", $"house-holdem-smoke-check-{phase}-0001", Player);
            Assert.Equal(HttpStatusCode.OK, check.StatusCode);
            var next = Assert.IsType<HouseHoldemRoundResponse>(await check.Content.ReadFromJsonAsync<HouseHoldemRoundResponse>());
            Assert.Equal(phase, next.Phase);
            Assert.Null(next.DealerCards);
        }
        var finalBody = $"{{ \"action\": \"{finalAction}\" }}\n";
        using var final = await SendAsync(browser, HttpMethod.Post, actionPath, token, finalBody, HouseHoldemFinalKey, Player);
        Assert.Equal(HttpStatusCode.OK, final.StatusCode);
        var finalJson = await final.Content.ReadAsStringAsync();
        var resultHand = Assert.IsType<HouseHoldemRoundResponse>(await final.Content.ReadFromJsonAsync<HouseHoldemRoundResponse>());
        Assert.Equal(HouseHoldemPhases.Settled, resultHand.Phase);
        Assert.Equal(2, resultHand.DealerCards!.Count);
        Assert.Equal(5, resultHand.Board.Count);
        var terminalObservation = AssertHouseHoldemRequest(observed.Last(), actionPath, finalBody, HouseHoldemFinalKey);
        var play = finalAction == HouseHoldemActions.Fold ? 0L : 100L;
        var payout = checked((long)(resultHand.Payout * 100m));
        Assert.Equal((openingStake + play) / 100m, resultHand.TotalWager);
        using var repeated = await SendAsync(browser, HttpMethod.Post, actionPath, token, finalBody, HouseHoldemFinalKey, Player);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(finalJson, await repeated.Content.ReadAsStringAsync());
        var otherAction = finalAction == HouseHoldemActions.Fold ? HouseHoldemActions.Play1X : HouseHoldemActions.Fold;
        using var conflicting = await SendAsync(browser, HttpMethod.Post, actionPath, token,
            JsonSerializer.Serialize(new { action = otherAction }), HouseHoldemFinalKey, Player);
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        using var archived = await SendAsync(browser, HttpMethod.Get, roundPath, token, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal(finalJson, await archived.Content.ReadAsStringAsync());
        using var foreign = await SendAsync(browser, HttpMethod.Get, roundPath, otherToken, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var walletBodies = fault.SettlementBodies.ToArray();
        Assert.Equal(5, walletBodies.Length);
        Assert.Equal(walletBodies[0], walletBodies[1]);
        Assert.Equal(BodySha(walletBodies[0]), opening.GetValue<string>("requestBodySha256"), ignoreCase: true);
        var allReceipts = (await receipts.GetSnapshotAsync()).Documents;
        Assert.Equal(4, allReceipts.Count);
        Assert.Equal(opening.UpdateTime, Assert.Single(allReceipts, item => item.Id == opening.Id).UpdateTime);
        var ledger = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(1 + (play > 0 ? 1 : 0) + (payout > 0 ? 1 : 0), ledger.Count);
        Assert.Equal(-openingStake, Assert.Single(ledger, row => row.Id == opening.Id + "-wager").GetValue<long>("amountCents"));
        Assert.Equal(InitialCents - openingStake - play + payout, await BalanceAsync(database, Player));
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));

        var audit = Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(audit.Id).GetSnapshotAsync();
        var payload = evidence.GetValue<string>("payloadJson");
        var summary = FirestoreGameRoundAuditStore.Summary(audit);
        Assert.Equal(terminalObservation.RequestId, evidence.GetValue<string>("requestId"));
        Assert.Equal(audit.GetValue<string>("evidenceSha256"), GameSecurityEventDocuments.Hash(payload));
        Assert.Empty(PaidCardEvidenceReplayAudit.Inspect(summary, payload));
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));
        using var completion = JsonDocument.Parse(payload);
        var proof = completion.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName);
        Assert.Equal(RoundAuditFinancialProof.SignedWallet, proof.GetProperty("provenance").GetString());
        var entries = proof.GetProperty("entries").Deserialize<RoundAuditLedgerEntry[]>(RoundAuditCapture.Json)!;
        var operations = proof.GetProperty("walletOperations").Deserialize<RoundAuditWalletOperation[]>(RoundAuditCapture.Json)!;
        Assert.Equal(2, operations.Length);
        Assert.Equal(ledger.Select(row => (row.Id, row.GetValue<long>("amountCents"))).OrderBy(row => row.Id).ToArray(),
            entries.Select(entry => (entry.DocumentId, entry.AmountCents)).OrderBy(entry => entry.DocumentId).ToArray());
        Assert.All(operations, operation => Assert.Equal(operation.LedgerEvidenceIds.Order(StringComparer.Ordinal).ToArray(),
            entries.Where(entry => entry.WalletOperationId == operation.DocumentId && entry.WalletSettlementId == operation.ReceiptsByAccount[Player].SettlementId)
                .Select(entry => entry.DocumentId).Order(StringComparer.Ordinal).ToArray()));
        var openingProof = Assert.Single(operations, operation => operation.DocumentId == prepared.Id);
        Assert.Equal(hash, openingProof.OutcomeSha256);
        Assert.Equal(opening.Id, openingProof.ReceiptsByAccount[Player].SettlementId);
        var terminalProof = Assert.Single(operations, operation => operation.DocumentId != prepared.Id);
        var terminal = Assert.Single(allReceipts, item => item.Id == terminalProof.ReceiptsByAccount[Player].SettlementId);
        Assert.Equal(terminalProof.OutcomeSha256, terminal.GetValue<string>("outcomeSha256"), ignoreCase: true);
        Assert.Equal(play, terminal.GetValue<long>("wagerCents"));
        Assert.Equal(payout, terminal.GetValue<long>("payoutCents"));
        await Assert.Single(ledger, row => row.Id == opening.Id + "-wager").Reference
            .UpdateAsync("amountCents", -openingStake - 1);
        Assert.Contains(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default),
            finding => finding.Code == "paid-card-protected-wallet-mismatch");
    }

    private static GameRequestObservation AssertHouseHoldemRequest(VerifiedWorkerRequest verified, string path, string body, string key)
    {
        Assert.Equal((Player, path, key, BodySha(body)), (verified.Actor, verified.Path, verified.OperationKey, verified.BodySha256));
        var request = Assert.IsType<GameRequestObservation>(verified.Observation);
        Assert.Equal((Player, HouseHoldemBackend.GameId, key), (request.UserId, request.GameId, request.CommandId));
        return request;
    }

    private static async Task<WebApplication> StartHouseHoldemWorkerAsync(FirestoreDb database, Uri wallet, byte[] walletKey,
        byte[] workerKey, DropFirstCommittedWalletReply fault, ConcurrentQueue<VerifiedWorkerRequest> observed)
    {
        var builder = LoopbackBuilder();
        builder.Configuration[HouseHoldemBackend.EnabledSetting] = "true";
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Configuration["GameWallet:Url"] = wallet.AbsoluteUri;
        builder.Configuration["GameWallet:SigningKey"] = Convert.ToBase64String(walletKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddGameWalletClient(HouseHoldemBackend.GameId).AddHttpMessageHandler(() => fault);
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(HouseHoldemBackend.GameId);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method))
                observed.Enqueue(new(GameWorkerAuthentication.GetAccountId(context), context.Request.Path.Value,
                    context.Request.Headers["Idempotency-Key"].ToString(), GameWorkerAuthentication.GetVerifiedBodySha256(context),
                    GameSecurityContext.Current));
            await next(context);
        });
        app.MapHouseHoldem(GameWorkerAuthentication.GetAccountId);
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartHouseHoldemGatewayAsync(FirestoreDb database, FirestoreAccountStore accounts,
        Uri worker, byte[] workerKey)
    {
        var builder = LoopbackBuilder();
        builder.Configuration["GameWorkers:house-holdem:ForwardPublicRoutes"] = "true";
        builder.Configuration["GameWorkers:house-holdem:Url"] = worker.AbsoluteUri;
        builder.Configuration["GameWorkers:house-holdem:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(accounts, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        builder.Services.AddControllers().AddApplicationPart(typeof(HouseHoldemController).Assembly);
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
