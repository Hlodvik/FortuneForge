using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.ThreeCardPoker;
using FortuneForge.Games.ThreeCardPoker.Server;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Cards.ThreeCardPoker.Table;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

public sealed partial class SignedCardWorkerSmokeTests
{
    private const string ThreeCardName = "Trusted Player";
    private const string ThreeCardJoinKey = "three-card-smoke-join-0001";
    private const string ThreeCardFoldKey = "three-card-smoke-fold-0001";

    [Fact]
    public async Task Three_card_poker_mvc_gateway_uses_trusted_name_and_recovers_the_original_wager_while_paused()
    {
        const string prefix = ThreeCardPokerBackend.PublicPath;
        const string joinBody = "{ \"expectedVersion\": 0, \"displayName\": \"Claimed Name\" }\n";
        const long stake = 200;
        var game = ThreeCardPokerBackend.GameId;
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(database, accounts, Player, ThreeCardName);
        var foreignToken = await CreateAccountAsync(database, accounts, OtherPlayer, "Other Player");
        var walletKey = RandomNumberGenerator.GetBytes(32);
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var fault = new DropFirstCommittedWalletReply(game);
        var observed = new ConcurrentQueue<VerifiedWorkerRequest>();
        await using var wallet = await StartWalletAsync(database, game, walletKey);
        wallet.Configuration[$"GameWallet:Games:{game}:AllowPlayerNameRead"] = "true";
        await using var worker = await StartThreeCardWorkerAsync(database, new Uri(wallet.Urls.Single()), walletKey, workerKey, fault, observed);
        await using var gateway = await StartThreeCardGatewayAsync(database, accounts, new Uri(worker.Urls.Single()), workerKey);
        using var browser = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
        using var direct = new HttpClient { BaseAddress = new Uri(worker.Urls.Single()) };
        var receipts = database.Collection("walletRoundSettlements");
        var operations = database.Collection(ThreeCardPokerBackend.TableOperations);

        using var unsigned = await SendAsync(direct, HttpMethod.Post, prefix + "/queue", token, joinBody, ThreeCardJoinKey);
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        using var switched = await SendAsync(browser, HttpMethod.Post, prefix + "/queue", foreignToken, joinBody, ThreeCardJoinKey, Player);
        Assert.Equal(HttpStatusCode.Forbidden, switched.StatusCode);
        Assert.Empty(observed);
        using var joined = await SendAsync(browser, HttpMethod.Post, prefix + "/queue", token, joinBody, ThreeCardJoinKey, Player);
        var seat = await ReadThreeCardMutationAsync(joined);
        Assert.Equal(ThreeCardPokerTablePhases.Betting, seat.Table.Phase);
        Assert.Equal(ThreeCardName, Assert.Single(seat.Table.Seats).DisplayName);
        AssertSignedThreeCard(Assert.Single(observed), prefix + "/queue", joinBody, ThreeCardJoinKey);
        Assert.Empty((await receipts.GetSnapshotAsync()).Documents);

        var tablePath = $"{prefix}/tables/{seat.Table.TableId}";
        var wagerPath = tablePath + "/wagers";
        var wagerBody = $"{{ \"ante\": 1, \"pairPlus\": 1, \"expectedVersion\": {seat.Version} }}\n";
        using var lost = await SendAsync(browser, HttpMethod.Post, wagerPath, token, wagerBody, OperationKey, Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        var openingObservation = AssertSignedThreeCard(observed.Last(), wagerPath, wagerBody, OperationKey);
        var frozen = Assert.Single((await operations.WhereEqualTo("status", "prepared").GetSnapshotAsync()).Documents);
        var preparedJson = frozen.GetValue<string>("preparedJson");
        var preparedHash = frozen.GetValue<string>("preparedSha256");
        Assert.Equal(GameSecurityEventDocuments.Hash(preparedJson), preparedHash);
        var wager = Assert.Single((await receipts.GetSnapshotAsync()).Documents);
        var walletId = GameSecurityEventDocuments.Hash($"{frozen.Id}\n{Player}");
        Assert.Equal(GameWalletSettlementIdentity.CreateId(game, Player, walletId), wager.Id);
        Assert.Equal(Player, wager.GetValue<string>("userId"));
        Assert.Equal(stake, wager.GetValue<long>("wagerCents"));
        Assert.Equal(stake, wager.GetValue<long>("minimumBalanceBeforeCents"));
        Assert.Equal(preparedHash, wager.GetValue<string>("outcomeSha256"), ignoreCase: true);
        Assert.Equal(InitialCents - stake, await BalanceAsync(database, Player));
        var debit = Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(wager.Id + "-wager", debit.Id);
        Assert.Equal(-stake, debit.GetValue<long>("amountCents"));

        worker.Configuration["Features:ThreeCardPokerTableEnabled"] = "false";
        using var recovery = await SendAsync(browser, HttpMethod.Post, wagerPath, token, wagerBody, OperationKey, Player);
        var entry = await ReadThreeCardMutationAsync(recovery);
        var exactReply = await recovery.Content.ReadAsStringAsync();
        var retryObservation = AssertSignedThreeCard(observed.Last(), wagerPath, wagerBody, OperationKey);
        Assert.NotEqual(openingObservation.RequestId, retryObservation.RequestId);
        Assert.Equal(seat.Table.TableId, entry.Table.TableId);
        Assert.Equal(preparedJson, (await frozen.Reference.GetSnapshotAsync()).GetValue<string>("preparedJson"));
        var savedBet = Assert.Single(entry.Table.Seats);
        Assert.Equal(1m, savedBet.Ante);
        Assert.Equal(1m, savedBet.PairPlus);
        using var duplicate = await SendAsync(browser, HttpMethod.Post, wagerPath, token, wagerBody, OperationKey, Player);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(exactReply, await duplicate.Content.ReadAsStringAsync());
        using var changed = await SendAsync(browser, HttpMethod.Post, wagerPath, token,
            JsonSerializer.Serialize(new { ante = 2, pairPlus = 1, expectedVersion = seat.Version }), OperationKey, Player);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var fresh = await SendAsync(browser, HttpMethod.Post, wagerPath, token, wagerBody, "three-card-smoke-paused-0001", Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, fresh.StatusCode);
        using var pause = await SendAsync(browser, HttpMethod.Get, prefix + "/status", token, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
        Assert.False(Assert.IsType<ThreeCardPokerTableStatusResponse>(
            await pause.Content.ReadFromJsonAsync<ThreeCardPokerTableStatusResponse>()).Available);
        var twoWalletBodies = fault.SettlementBodies.ToArray();
        Assert.Equal(2, twoWalletBodies.Length);
        Assert.Equal(twoWalletBodies[0], twoWalletBodies[1]);
        Assert.Equal(BodySha(twoWalletBodies[0]), wager.GetValue<string>("requestBodySha256"), ignoreCase: true);

        var turn = await WaitForThreeCardAsync(browser, token, table => table.LegalActions.Contains(ThreeCardPokerActions.Fold));
        Assert.All(turn.Table.Dealer.Cards, card => Assert.True(card.Hidden));
        var foldBody = $"{{ \"type\": \"fold\", \"expectedVersion\": {turn.Version} }}\n";
        using var folded = await SendAsync(browser, HttpMethod.Post, tablePath + "/actions", token, foldBody, ThreeCardFoldKey, Player);
        Assert.Equal(HttpStatusCode.OK, folded.StatusCode);
        AssertSignedThreeCard(observed.Last(), tablePath + "/actions", foldBody, ThreeCardFoldKey);
        var settled = await WaitForThreeCardAsync(browser, token, table => table.Phase == ThreeCardPokerTablePhases.Settled);
        var player = Assert.Single(settled.Table.Seats);
        Assert.Equal("fold", player.Outcome);
        Assert.Equal(stake / 100m, player.TotalWager);
        var payoutCents = checked((long)(player.Payout * 100m));
        Assert.Equal(200L, payoutCents);
        Assert.Equal(InitialCents - stake + payoutCents, await BalanceAsync(database, Player));
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));
        using var ownHistory = await SendAsync(browser, HttpMethod.Get, prefix + "/history", token, expectedAccount: Player);
        using var otherHistory = await SendAsync(browser, HttpMethod.Get, prefix + "/history", foreignToken, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.OK, ownHistory.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherHistory.StatusCode);
        var historic = Assert.Single((await ownHistory.Content.ReadFromJsonAsync<ThreeCardPokerTableHistoryItemResponse[]>())!);
        Assert.Equal((stake / 100m, payoutCents / 100m), (historic.WagerCredits, historic.PayoutCredits));
        Assert.Empty((await otherHistory.Content.ReadFromJsonAsync<ThreeCardPokerTableHistoryItemResponse[]>())!);

        var platform = (await receipts.GetSnapshotAsync()).Documents;
        Assert.Equal(2, platform.Count);
        Assert.Equal(wager.UpdateTime, Assert.Single(platform, receipt => receipt.Id == wager.Id).UpdateTime);
        var audit = Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(audit.Id).GetSnapshotAsync();
        var payload = evidence.GetValue<string>("payloadJson");
        var summary = FirestoreGameRoundAuditStore.Summary(audit);
        Assert.Equal(audit.GetValue<string>("evidenceSha256"), GameSecurityEventDocuments.Hash(payload));
        Assert.Empty(TableOutcomeReplayAudit.Inspect(summary, payload));
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));
        using var captured = JsonDocument.Parse(payload);
        var proof = captured.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName);
        Assert.Equal(ThreeCardPokerBackend.Owner, proof.GetProperty("backendOwner").GetString());
        Assert.Equal(ThreeCardPokerBackend.FinancialEvidenceContract, proof.GetProperty("contractVersion").GetString());
        Assert.Equal(RoundAuditFinancialProof.SignedWallet, proof.GetProperty("provenance").GetString());
        var ledger = proof.GetProperty("entries").Deserialize<RoundAuditLedgerEntry[]>(RoundAuditCapture.Json)!;
        var walletOps = proof.GetProperty("walletOperations").Deserialize<RoundAuditWalletOperation[]>(RoundAuditCapture.Json)!;
        Assert.Contains(walletOps, operation => Guid.TryParseExact(operation.DocumentId, "N", out _));
        var openingProof = Assert.Single(walletOps, operation => operation.DocumentId == frozen.Id);
        Assert.Equal(preparedHash, openingProof.OutcomeSha256);
        Assert.Equal(wager.Id, openingProof.ReceiptsByAccount[Player].SettlementId);
        Assert.Equal(payoutCents - stake, ledger.Sum(entry => entry.AmountCents));
        foreach (var leg in ledger)
        {
            Assert.Equal(Player, leg.UserId);
            var row = await database.Collection(ThreeCardPokerBackend.TableLedgerEvidence).Document(leg.DocumentId).GetSnapshotAsync();
            Assert.Equal(leg.AmountCents, FirestoreWalletEvidence.Legacy(row, leg.RoundReference).AmountCents);
            Assert.Contains(platform, receipt => receipt.Id == leg.WalletSettlementId);
            Assert.Contains(walletOps, operation => operation.DocumentId == leg.WalletOperationId &&
                operation.LedgerEvidenceIds.Contains(leg.DocumentId, StringComparer.Ordinal));
        }
        var wagerRow = Assert.Single(openingProof.LedgerEvidenceIds);
        await database.Collection(ThreeCardPokerBackend.TableLedgerEvidence).Document(wagerRow)
            .UpdateAsync("walletSettlementId", "changed-settlement");
        Assert.Contains(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default),
            finding => finding.Code == "three-card-poker-protected-wallet-mismatch");
    }

    private static GameRequestObservation AssertSignedThreeCard(VerifiedWorkerRequest verified, string path, string body, string key)
    {
        Assert.Equal((Player, path, key, BodySha(body)), (verified.Actor, verified.Path, verified.OperationKey, verified.BodySha256));
        var request = Assert.IsType<GameRequestObservation>(verified.Observation);
        Assert.Equal((Player, ThreeCardPokerBackend.GameId, key), (request.UserId, request.GameId, request.CommandId));
        return request;
    }

    private static async Task<ThreeCardPokerTablePlaySessionResponse> ReadThreeCardMutationAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mutation = Assert.IsType<ThreeCardPokerTableMutationResponse>(
            await response.Content.ReadFromJsonAsync<ThreeCardPokerTableMutationResponse>());
        return Assert.IsType<ThreeCardPokerTablePlaySessionResponse>(mutation.Session);
    }

    private static async Task<ThreeCardPokerTablePlaySessionResponse> WaitForThreeCardAsync(HttpClient browser, string token,
        Func<ThreeCardPokerTableResponse, bool> ready)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
        {
            using var response = await SendAsync(browser, HttpMethod.Get, ThreeCardPokerBackend.PublicPath + "/snapshot", token, expectedAccount: Player);
            var snapshot = await ReadThreeCardMutationAsync(response);
            if (ready(snapshot.Table)) return snapshot;
            await Task.Delay(150);
        }
        throw new TimeoutException("The Three Card Poker table did not reach the expected phase.");
    }

    private static async Task<WebApplication> StartThreeCardWorkerAsync(FirestoreDb database, Uri wallet, byte[] walletKey,
        byte[] workerKey, DropFirstCommittedWalletReply fault, ConcurrentQueue<VerifiedWorkerRequest> observed)
    {
        var builder = LoopbackBuilder();
        builder.Configuration["Features:ThreeCardPokerTableEnabled"] = "true";
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Configuration["GameWallet:Url"] = wallet.AbsoluteUri;
        builder.Configuration["GameWallet:SigningKey"] = Convert.ToBase64String(walletKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddThreeCardPokerBackend();
        builder.Services.AddSingleton(provider => new FirestoreThreeCardPokerTableStore(database,
            provider.GetRequiredService<IGameWalletClient>(), provider.GetRequiredService<IGameWalletAccountReader>(),
            provider.GetRequiredService<IManagedPlayerRosterClient>(), provider.GetRequiredService<IManagedTablePopulationDirector>(),
            ThreeCardPairDeck));
        // Run normal recovery and deadline transitions through public snapshots so a sweep cannot claim the injected failure.
        builder.Services.Remove(builder.Services.Single(service => service.ServiceType == typeof(IHostedService) &&
            service.ImplementationType == typeof(ThreeCardPokerDeadlineWorker)));
        builder.Services.AddGameWalletClient(ThreeCardPokerBackend.GameId).AddHttpMessageHandler(() => fault);
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(ThreeCardPokerBackend.GameId);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method))
                observed.Enqueue(new(GameWorkerAuthentication.GetAccountId(context), context.Request.Path.Value,
                    context.Request.Headers["Idempotency-Key"].ToString(), GameWorkerAuthentication.GetVerifiedBodySha256(context),
                    GameSecurityContext.Current));
            await next(context);
        });
        app.MapThreeCardPoker(GameWorkerAuthentication.GetAccountId);
        await app.StartAsync();
        return app;
    }

    private static IReadOnlyList<string> ThreeCardPairDeck()
    {
        // In the solo deal the first, third, and fifth cards are the player's hand.
        string[] prefix = ["Q|spades", "2|clubs", "Q|diamonds", "5|clubs", "4|spades", "9|diamonds"];
        return prefix.Concat(ThreeCardPokerRules.CreateShuffledDeck().Where(card => !prefix.Contains(card))).ToArray();
    }

    private static async Task<WebApplication> StartThreeCardGatewayAsync(FirestoreDb database, FirestoreAccountStore accounts,
        Uri worker, byte[] workerKey)
    {
        var builder = LoopbackBuilder();
        builder.Configuration["GameWorkers:three-card-poker:ForwardPublicRoutes"] = "true";
        builder.Configuration["GameWorkers:three-card-poker:Url"] = worker.AbsoluteUri;
        builder.Configuration["GameWorkers:three-card-poker:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(accounts, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        builder.Services.AddControllers().AddApplicationPart(typeof(ThreeCardPokerTableController).Assembly);
        builder.Services.AddRateLimiter(options =>
        {
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
}
