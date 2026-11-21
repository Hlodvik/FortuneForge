using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Cards.CasinoWar;
using FortuneForge.Server.Cards.VideoPoker;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

public sealed partial class SignedCardWorkerSmokeTests
{
    [Theory]
    [InlineData("casino-war", "CasinoWar:AllowNewPaidRounds", "{ \"primaryStake\": 2, \"tieStake\": 1 }",
        "decision", "{ \"decision\": \"surrender\" }", "{ \"decision\": \"go-to-war\" }", "awaiting-tie-decision", 300L)]
    [InlineData("video-poker", "Features:VideoPokerEnabled", "{ \"coinsWagered\": 2, \"handCount\": 3 }",
        "draw", "{ \"heldPositions\": [0, 1, 2, 3, 4] }", "{ \"heldPositions\": [] }", "awaiting-draw", 600L)]
    public async Task Draw_games_mvc_gateway_recovers_the_original_paid_deal_and_finishes_while_paused(
        string game, string feature, string openingBody, string action, string actionBody, string changedActionBody,
        string openingPhase, long wagerCents)
    {
        const string actionKey = "signed-draw-games-final-command-0001";
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(database, accounts, Player, "Player");
        var otherToken = await CreateAccountAsync(database, accounts, OtherPlayer, "Other Player");
        var walletKey = RandomNumberGenerator.GetBytes(32);
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var fault = new DropFirstCommittedWalletReply(game);
        var observations = new ConcurrentQueue<VerifiedWorkerRequest>();
        var path = $"/api/games/{game}/rounds";
        var receipts = database.Collection("walletRoundSettlements");
        await using var wallet = await StartWalletAsync(database, game, walletKey);
        await using var worker = await StartDrawGameWorkerAsync(database, game, feature, workerKey, wallet, walletKey,
            fault, observations, admitNewRounds: true);
        await using var gateway = await StartPaidGameMvcGatewayAsync(database, accounts, game, feature, new Uri(worker.Urls.Single()), workerKey);
        using var browser = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };

        using var wrong = await SendAsync(browser, HttpMethod.Post, path, otherToken, openingBody, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Empty(observations);
        using var lost = await SendAsync(browser, HttpMethod.Post, path, token, openingBody, OperationKey, Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        var openingObservation = Assert.Single(observations);
        Assert.Equal((Player, path, OperationKey, BodySha(openingBody)),
            (openingObservation.Actor, openingObservation.Path, openingObservation.OperationKey, openingObservation.BodySha256));
        var request = Assert.IsType<GameRequestObservation>(openingObservation.Observation);
        Assert.Equal(game, request.GameId);
        var openingReceipt = Assert.Single((await receipts.GetSnapshotAsync()).Documents);
        Assert.Equal(wagerCents, openingReceipt.GetValue<long>("wagerCents"));
        Assert.Empty((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);

        await worker.StopAsync();
        var pausedRpc = new DropFirstCommittedWalletReply(game, loseFirst: false);
        await using var replica = await StartDrawGameWorkerAsync(database, game, feature, workerKey, wallet, walletKey,
            pausedRpc, observations, admitNewRounds: false);
        gateway.Configuration[$"GameWorkers:{game}:Url"] = replica.Urls.Single();
        using var recovered = await SendAsync(browser, HttpMethod.Post, path, token, openingBody, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        var rawOpening = await recovered.Content.ReadAsStringAsync();
        using var opening = JsonDocument.Parse(rawOpening);
        var roundId = opening.RootElement.GetProperty("roundId").GetString();
        Assert.False(string.IsNullOrEmpty(roundId));
        Assert.Equal(openingPhase, opening.RootElement.GetProperty("phase").GetString());
        var savedPath = $"{path}/{roundId}";
        Assert.Equal(savedPath, recovered.Headers.Location?.OriginalString);
        Assert.Equal(Assert.Single(fault.SettlementBodies), Assert.Single(pausedRpc.SettlementBodies));
        using var paused = await SendAsync(browser, HttpMethod.Post, path, token, openingBody, "signed-draw-games-paused-new-0001", Player);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, paused.StatusCode);
        using var foreign = await SendAsync(browser, HttpMethod.Get, savedPath, otherToken, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var openingAgain = await SendAsync(browser, HttpMethod.Post, path, token, openingBody, OperationKey, Player);
        Assert.Equal(HttpStatusCode.Created, openingAgain.StatusCode);
        Assert.Equal(rawOpening, await openingAgain.Content.ReadAsStringAsync());

        var actionPath = $"{savedPath}/{action}";
        using var played = await SendAsync(browser, HttpMethod.Post, actionPath, token, actionBody, actionKey, Player);
        Assert.Equal(HttpStatusCode.OK, played.StatusCode);
        var rawResult = await played.Content.ReadAsStringAsync();
        using var result = JsonDocument.Parse(rawResult);
        Assert.Equal("completed", result.RootElement.GetProperty("phase").GetString());
        using var retry = await SendAsync(browser, HttpMethod.Post, actionPath, token, actionBody, actionKey, Player);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(rawResult, await retry.Content.ReadAsStringAsync());
        using var changed = await SendAsync(browser, HttpMethod.Post, actionPath, token, changedActionBody, actionKey, Player);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var saved = await SendAsync(browser, HttpMethod.Get, savedPath, token, expectedAccount: Player);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(rawResult, await saved.Content.ReadAsStringAsync());

        var settlements = (await receipts.GetSnapshotAsync()).Documents;
        Assert.Equal(2, settlements.Count);
        Assert.Equal(openingReceipt.UpdateTime, Assert.Single(settlements, row => row.Id == openingReceipt.Id).UpdateTime);
        Assert.Equal(wagerCents, settlements.Sum(row => row.GetValue<long>("wagerCents")));
        var payout = settlements.Sum(row => row.GetValue<long>("payoutCents"));
        Assert.Equal(InitialCents - wagerCents + payout, await BalanceAsync(database, Player));
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));

        var auditDocument = Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        var summary = FirestoreGameRoundAuditStore.Summary(auditDocument);
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(summary.Id).GetSnapshotAsync();
        var payload = evidence.GetValue<string>("payloadJson");
        Assert.Equal(roundId, summary.RoundId);
        Assert.Equal((wagerCents, payout), (Assert.Single(summary.Participants).WagerCents, summary.Participants[0].PayoutCents));
        Assert.Equal(summary.EvidenceSha256, GameSecurityEventDocuments.Hash(payload));
        Assert.Empty(PaidCardEvidenceReplayAudit.Inspect(summary, payload));
        using var capture = JsonDocument.Parse(payload);
        var proof = capture.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName);
        Assert.Equal(RoundAuditFinancialProof.SignedWallet, proof.GetProperty("provenance").GetString());
        Assert.Equal(2, proof.GetProperty("walletOperations").GetArrayLength());
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));
    }

    private static async Task<WebApplication> StartDrawGameWorkerAsync(FirestoreDb database, string game, string feature,
        byte[] workerKey, WebApplication wallet, byte[] walletKey, DropFirstCommittedWalletReply fault,
        ConcurrentQueue<VerifiedWorkerRequest> observations, bool admitNewRounds)
    {
        var builder = LoopbackBuilder();
        builder.Configuration[feature] = admitNewRounds.ToString();
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Configuration["GameWallet:Url"] = wallet.Urls.Single();
        builder.Configuration["GameWallet:SigningKey"] = Convert.ToBase64String(walletKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton(TimeProvider.System);
        if (game == "casino-war")
        {
            builder.Services.AddCasinoWarBackend(builder.Configuration);
            builder.Services.AddSingleton(provider => new CasinoWarService(provider.GetRequiredService<ICasinoWarStore>(),
                provider.GetRequiredService<TimeProvider>(), () => new[] { "5|clubs", "5|diamonds", "2|clubs", "3|clubs",
                    "4|clubs", "A|spades", "6|clubs", "7|clubs", "8|clubs", "K|hearts" }.Select(CardCode.Parse).ToArray()));
        }
        else builder.Services.AddSingleton<VideoPokerFirestoreStore>();
        builder.Services.AddGameWalletClient(game).AddHttpMessageHandler(() => fault);
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(game);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method))
                observations.Enqueue(new(GameWorkerAuthentication.GetAccountId(context), context.Request.Path.Value,
                    context.Request.Headers["Idempotency-Key"].ToString(), GameWorkerAuthentication.GetVerifiedBodySha256(context),
                    GameSecurityContext.Current));
            await next(context);
        });
        if (game == "casino-war") app.MapCasinoWar(GameWorkerAuthentication.GetAccountId, admitNewRounds);
        else app.MapVideoPoker(GameWorkerAuthentication.GetAccountId);
        await app.StartAsync();
        return app;
    }
}
