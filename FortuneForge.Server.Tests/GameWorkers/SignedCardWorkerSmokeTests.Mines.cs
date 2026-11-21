using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Mines.Server;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Activity;
using FortuneForge.Server.GameWorkers;
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
    private const string MinesGame = "mines";
    private const string MinesOrigin = "https://fortuneforgegame.web.app";

    [Fact]
    public async Task Mines_signed_gateway_recovers_lost_reservation_and_cashout_without_repeating_money_movement()
    {
        const string startKey = "mines-smoke-start-0001";
        const string revealKey = "mines-smoke-reveal-0001";
        const string cashoutKey = "mines-smoke-cashout-0001";
        const long wager = 100;
        const string roundsPath = "/api/games/mines/rounds";
        const string sessionPath = "/api/games/mines/session";
        var startBody = JsonSerializer.Serialize(new StartMinesRequest(startKey, wager, 3), JsonSerializerOptions.Web);
        var cashoutBody = JsonSerializer.Serialize(new CashOutMinesRequest(cashoutKey), JsonSerializerOptions.Web);
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(database, accounts, Player, "Player");
        var otherToken = await CreateAccountAsync(database, accounts, OtherPlayer, "Other Player");
        var walletKey = RandomNumberGenerator.GetBytes(32);
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var observed = new ConcurrentQueue<VerifiedWorkerRequest>();
        var fault = new DropMinesWalletReplies();
        await using var wallet = await StartWalletAsync(database, MinesGame, walletKey);
        wallet.Configuration[$"GameWallet:Games:{MinesGame}:AllowWagerReservations"] = "true";
        await using var worker = await StartMinesWorkerAsync(database, wallet.Urls.Single(), walletKey, workerKey, fault, observed);
        var workerUrl = worker.Urls.Single();
        await using var gateway = await StartMinesGatewayAsync(database, accounts, workerUrl, workerKey);
        using var browser = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
        browser.DefaultRequestHeaders.Add("Origin", MinesOrigin);
        Task<HttpResponseMessage> Post(string path, string json) => SendAsync(browser, HttpMethod.Post, path, token, json, expectedAccount: Player);
        Task<HttpResponseMessage> Session() => SendAsync(browser, HttpMethod.Get, sessionPath, token, expectedAccount: Player);

        using var unsignedBrowser = new HttpClient { BaseAddress = new Uri(workerUrl) };
        using var direct = await SendAsync(unsignedBrowser, HttpMethod.Post, roundsPath, token, startBody);
        Assert.Equal(HttpStatusCode.Forbidden, direct.StatusCode);
        using var staleAccount = await SendAsync(browser, HttpMethod.Post, roundsPath, token, startBody, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.Forbidden, staleAccount.StatusCode);
        Assert.Empty(observed);

        using var interrupted = await Post(roundsPath, startBody);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, interrupted.StatusCode);
        Assert.DoesNotContain("minePositions", await interrupted.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var startProof = AssertMinesCommand(Assert.Single(observed), roundsPath, startKey, startBody);
        var roundId = MinesService.Hash($"{Player}\n{startKey}");
        var reserveId = GameWalletSettlementIdentity.CreateId(MinesGame, Player, roundId);
        var completionId = GameWalletSettlementIdentity.CreateCompletionId(MinesGame, Player, roundId);
        var receipts = database.Collection("walletRoundSettlements");
        var reserve = Assert.Single((await receipts.GetSnapshotAsync()).Documents);
        Assert.Equal(reserveId, reserve.Id);
        Assert.Equal(InitialCents - wager, await BalanceAsync(database, Player));
        var roundDocument = database.Collection("minesRounds").Document(roundId);
        Assert.False((await roundDocument.GetSnapshotAsync()).Exists);

        gateway.Configuration["Features:MinesEnabled"] = worker.Configuration["Features:MinesEnabled"] = "false";
        gateway.Configuration["Features:MinesPaidEnabled"] = worker.Configuration["Features:MinesPaidEnabled"] = "false";
        using var startRetry = await Post(roundsPath, startBody);
        Assert.Equal(HttpStatusCode.Created, startRetry.StatusCode);
        var originalStart = await startRetry.Content.ReadAsStringAsync();
        var started = ReadMinesSession(originalStart);
        Assert.False(started.Practice);
        Assert.Equal(InitialCents - wager, started.BalanceCents);
        var active = Assert.IsType<MinesRoundResponse>(started.Round);
        Assert.Equal(roundId, active.RoundId);
        Assert.Equal("active", active.Status);
        Assert.Equal(25, active.Tiles.Count);
        Assert.All(active.Tiles, tile => Assert.Equal("hidden", tile.Kind));
        Assert.DoesNotContain("minePositions", originalStart, StringComparison.OrdinalIgnoreCase);
        Assert.Empty((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        using var changed = await Post(roundsPath, startBody.Replace("100", "200"));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        // The emulator-only test reads the frozen board so this case always reaches a positive cashout.
        var board = Assert.IsType<MinesStoredRound>(JsonSerializer.Deserialize<MinesStoredRound>(
            (await roundDocument.GetSnapshotAsync()).GetValue<string>("privateRoundJson"), JsonSerializerOptions.Web));
        var tile = active.Tiles.First(tile => !board.State.MinePositions.Contains(tile.Index)).Index;
        var revealBody = JsonSerializer.Serialize(new RevealMinesRequest(revealKey, tile), JsonSerializerOptions.Web);
        var revealPath = $"{roundsPath}/{roundId}/reveal";
        using var reveal = await Post(revealPath, revealBody);
        Assert.Equal(HttpStatusCode.OK, reveal.StatusCode);
        var revealed = Assert.IsType<MinesRoundResponse>(ReadMinesSession(await reveal.Content.ReadAsStringAsync()).Round);
        Assert.Equal("active", revealed.Status);
        Assert.Equal(1, revealed.SafeReveals);
        Assert.True(revealed.CanCashOut);
        Assert.All(revealed.Tiles.Where(item => item.Index != tile), item => Assert.Equal("hidden", item.Kind));
        var payout = revealed.CashOutCents;
        Assert.True(payout > 0);
        Assert.Equal(2, fault.Bodies.Count);

        var cashoutPath = $"{roundsPath}/{roundId}/cashout";
        using var foreign = await SendAsync(browser, HttpMethod.Post, cashoutPath, otherToken, cashoutBody, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var lostCashout = await Post(cashoutPath, cashoutBody);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lostCashout.StatusCode);
        var terminalProof = AssertMinesCommand(Assert.Single(observed, item => item.Actor == Player && item.OperationKey == cashoutKey),
            cashoutPath, cashoutKey, cashoutBody);
        var completion = await receipts.Document(completionId).GetSnapshotAsync();
        Assert.True(completion.Exists);
        Assert.Equal(InitialCents - wager + payout, await BalanceAsync(database, Player));
        Assert.Empty((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);

        gateway.Configuration[$"GameWorkers:{MinesGame}:Url"] = null;
        using var unavailable = await Session();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        using var unavailableBody = JsonDocument.Parse(await unavailable.Content.ReadAsStringAsync());
        Assert.Equal("game-worker-unavailable", unavailableBody.RootElement.GetProperty("code").GetString());
        gateway.Configuration[$"GameWorkers:{MinesGame}:Url"] = workerUrl;
        using var recovered = await Session();
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        var finalJson = await recovered.Content.ReadAsStringAsync();
        var final = ReadMinesSession(finalJson);
        Assert.Equal(InitialCents - wager + payout, final.BalanceCents);
        var closed = Assert.IsType<MinesRoundResponse>(final.Round);
        Assert.Equal(roundId, closed.RoundId);
        Assert.Equal("cashed-out", closed.Status);
        Assert.Equal(payout, closed.PayoutCents);
        using var replay = await Post(cashoutPath, cashoutBody);
        using var oldStart = await Post(roundsPath, startBody);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Created, oldStart.StatusCode);
        Assert.Equal(finalJson, await replay.Content.ReadAsStringAsync());
        Assert.Equal(originalStart, await oldStart.Content.ReadAsStringAsync());
        using var paused = await Post(roundsPath, startBody.Replace(startKey, "mines-smoke-paused-0001"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, paused.StatusCode);
        using var foreignSession = await SendAsync(browser, HttpMethod.Get, sessionPath, otherToken, expectedAccount: OtherPlayer);
        Assert.Equal(HttpStatusCode.OK, foreignSession.StatusCode);
        Assert.Null(ReadMinesSession(await foreignSession.Content.ReadAsStringAsync()).Round);
        Assert.Equal(InitialCents, await BalanceAsync(database, OtherPlayer));

        var bodies = fault.Bodies.ToArray();
        Assert.Equal(4, bodies.Length);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Equal(bodies[2], bodies[3]);
        await AssertMinesSettlementEvidenceAsync(database, roundId, wager, payout, reserve, completion, bodies[0], bodies[2],
            startProof, terminalProof);
    }

    private static async Task AssertMinesSettlementEvidenceAsync(FirestoreDb database, string roundId, long wager, long payout,
        DocumentSnapshot reserve, DocumentSnapshot completion, string reserveBody, string completionBody,
        GameRequestObservation start, GameRequestObservation terminal)
    {
        var receipts = (await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents;
        Assert.Equal(2, receipts.Count);
        Assert.Equal(reserve.UpdateTime, Assert.Single(receipts, doc => doc.Id == reserve.Id).UpdateTime);
        Assert.Equal(completion.UpdateTime, Assert.Single(receipts, doc => doc.Id == completion.Id).UpdateTime);
        Assert.Equal(nameof(GameWalletSettlementPhase.Reserve), reserve.GetValue<string>("phase"));
        Assert.Equal(nameof(GameWalletSettlementPhase.CompleteReservation), completion.GetValue<string>("phase"));
        Assert.Equal(BodySha(reserveBody).ToLowerInvariant(), reserve.GetValue<string>("requestBodySha256"));
        Assert.Equal(BodySha(completionBody).ToLowerInvariant(), completion.GetValue<string>("requestBodySha256"));
        Assert.Equal(reserve.GetValue<string>("outcomeSha256"), completion.GetValue<string>("reservationSha256"));
        Assert.Equal(roundId, reserve.GetValue<string>("operationId"));
        Assert.Equal(roundId, completion.GetValue<string>("operationId"));
        Assert.Equal(wager, reserve.GetValue<long>("wagerCents"));
        Assert.Equal(0, reserve.GetValue<long>("payoutCents"));
        Assert.Equal(0, completion.GetValue<long>("wagerCents"));
        Assert.Equal(payout, completion.GetValue<long>("payoutCents"));
        var ledger = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(2, ledger.Count);
        var debit = Assert.Single(ledger, doc => doc.Id == reserve.Id + "-wager");
        var credit = Assert.Single(ledger, doc => doc.Id == reserve.Id + "-payout");
        Assert.Equal((reserve.Id, -wager), (debit.GetValue<string>("walletSettlementId"), debit.GetValue<long>("amountCents")));
        Assert.Equal((completion.Id, payout), (credit.GetValue<string>("walletSettlementId"), credit.GetValue<long>("amountCents")));

        var history = Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Equal($"mines-{roundId}", history.Id);
        Assert.Equal(Player, history.GetValue<string>("userId"));
        Assert.Equal((wager, payout, payout - wager), (history.GetValue<long>("wagerCents"),
            history.GetValue<long>("payoutCents"), history.GetValue<long>("netCents")));
        var journal = (await database.Collection(GameSecurityEventDocuments.CollectionName)
            .WhereEqualTo("instanceId", roundId).GetSnapshotAsync()).Documents;
        Assert.Equal(3, journal.Count);
        Assert.Contains(journal, doc => doc.GetValue<string>("requestId") == start.RequestId);
        Assert.Contains(journal, doc => doc.GetValue<string>("requestId") == terminal.RequestId);
        var audit = Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        Assert.Equal(roundId, audit.GetValue<string>("roundId"));
        Assert.Equal("passed", audit.GetValue<string>("auditStatus"));
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(audit.Id).GetSnapshotAsync();
        Assert.Equal(terminal.RequestId, evidence.GetValue<string>("requestId"));
        var payload = evidence.GetValue<string>("payloadJson");
        Assert.Equal(audit.GetValue<string>("evidenceSha256"), GameSecurityEventDocuments.Hash(payload));
        var summary = FirestoreGameRoundAuditStore.Summary(audit);
        Assert.Empty(MinesSequenceReplayAudit.Inspect(summary, payload,
            journal.Select(document => document.GetValue<string>("payloadJson")).ToArray()));
        Assert.Empty(await MinesSequenceReplayAudit.InspectFinancialAsync(database, summary, payload, default));
    }

    private static async Task<WebApplication> StartMinesWorkerAsync(FirestoreDb database, string walletUrl, byte[] walletKey,
        byte[] workerKey, DropMinesWalletReplies fault, ConcurrentQueue<VerifiedWorkerRequest> observed)
    {
        var builder = MinesLoopback(database);
        builder.Configuration["GameWorker:Mode"] = "hosted";
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Configuration["GameWallet:Url"] = walletUrl;
        builder.Configuration["GameWallet:SigningKey"] = Convert.ToBase64String(walletKey);
        builder.Services.AddGameWalletClient(MinesGame).AddHttpMessageHandler(() => fault);
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(MinesGame);
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                var observation = GameSecurityContext.Current;
                observed.Enqueue(new(GameWorkerAuthentication.GetAccountId(context), context.Request.Path.Value,
                    observation?.CommandId ?? "", GameWorkerAuthentication.GetVerifiedBodySha256(context), observation));
            }
            await next(context);
        });
        app.MapMines((context, _) => Task.FromResult<MinesAccount?>(
            GameWorkerAuthentication.GetAccountId(context) is { } actor ? new MinesAccount(actor) : null));
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartMinesGatewayAsync(FirestoreDb database, FirestoreAccountStore store, string workerUrl, byte[] workerKey)
    {
        var builder = MinesLoopback(database);
        builder.Configuration[$"GameWorkers:{MinesGame}:ForwardPublicRoutes"] = "true";
        builder.Configuration[$"GameWorkers:{MinesGame}:Url"] = workerUrl;
        builder.Configuration[$"GameWorkers:{MinesGame}:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(provider => new AccountService(store, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddScoped<IGameActivityAccountResolver, GameActivityAccountResolver>();
        builder.Services.AddGameSecurityRequests(builder.Configuration);
        builder.Services.AddHttpClient<GameWorkerClient>();
        var app = builder.Build();
        app.UseRouting();
        app.UseGameSecurityRequests();
        app.UsePackagedGameWorkerForwarding();
        app.MapMines((_, _) => throw new InvalidOperationException("The paid request bypassed the signed Mines worker."));
        await app.StartAsync();
        return app;
    }

    private static WebApplicationBuilder MinesLoopback(FirestoreDb database)
    {
        var builder = LoopbackBuilder();
        builder.Configuration["Features:MinesEnabled"] = "true";
        builder.Configuration["Features:MinesPaidEnabled"] = "true";
        builder.Services.AddSingleton(database);
        builder.Services.AddMinesServer(options => options.AllowedOrigins = [MinesOrigin]);
        return builder;
    }

    private static MinesSessionResponse ReadMinesSession(string body) => Assert.IsType<MinesSessionResponse>(
        JsonSerializer.Deserialize<MinesSessionResponse>(body, JsonSerializerOptions.Web));

    private static GameRequestObservation AssertMinesCommand(VerifiedWorkerRequest proof, string path, string key, string body)
    {
        Assert.Equal((Player, path, key, BodySha(body)), (proof.Actor, proof.Path, proof.OperationKey, proof.BodySha256));
        var observation = Assert.IsType<GameRequestObservation>(proof.Observation);
        Assert.Equal((MinesGame, Player, key), (observation.GameId, observation.UserId, observation.CommandId));
        return observation;
    }

    private sealed class DropMinesWalletReplies : DelegatingHandler
    {
        private int reservePending = 1;
        private int completionPending = 1;
        internal ConcurrentQueue<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method != HttpMethod.Post || request.RequestUri?.AbsolutePath != GameWalletAuthentication.SettlementRoute(MinesGame))
                return await base.SendAsync(request, token);
            var body = await request.Content!.ReadAsStringAsync(token);
            Bodies.Enqueue(body);
            var phase = JsonSerializer.Deserialize<GameWalletSettlementRequest>(body, GameWalletAuthentication.WireJson)?.Phase;
            var response = await base.SendAsync(request, token);
            if (!response.IsSuccessStatusCode) return response;
            var drop = phase switch
            {
                GameWalletSettlementPhase.Reserve => Interlocked.Exchange(ref reservePending, 0) == 1,
                GameWalletSettlementPhase.CompleteReservation => Interlocked.Exchange(ref completionPending, 0) == 1,
                _ => false,
            };
            if (!drop) return response;
            response.Dispose();
            throw new HttpRequestException("Lost the Mines wallet reply after its successful commit.");
        }
    }
}
