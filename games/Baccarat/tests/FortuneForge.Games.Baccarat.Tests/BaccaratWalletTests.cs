using System.Net;
using System.Net.Http.Json;
using System.Text;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.PackagedGame.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;
using FortuneForge.Server.Cards.Baccarat;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Games.Baccarat.Tests;

public sealed class BaccaratWalletTests
{
    [Fact]
    public async Task SignedWorkerRoutesPreserveExactRecoveryAndRejectForeignOrAlteredRequests()
    {
        var wallet = new Wallet(); var key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(key);
        builder.Configuration["Features:BaccaratEnabled"] = "true";
        var database = Database(); builder.Services.AddSingleton(database); GameWorkerModule.Configure(builder);
        builder.Services.AddSingleton<IGameWalletClient>(wallet); builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
        await using var app = builder.Build(); app.UseRouting(); app.UseGameWorkerAuthentication("baccarat");
        GameWorkerModule.Map(app, GameWorkerAuthentication.GetAccountId); await app.StartAsync();
        var client = app.GetTestClient();
        HttpRequestMessage Signed(HttpMethod method, string path, string body = "", string user = "alice", string? operation = null)
        {
            var request = new HttpRequestMessage(method, path);
            if (body.Length > 0) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            Dictionary<string, string>? headers = operation is null ? null : new() { ["Idempotency-Key"] = operation };
            if (operation is not null) request.Headers.Add("Idempotency-Key", operation);
            request.Headers.Add(GameWorkerAuthentication.AssertionHeader, GameWorkerAuthentication.CreateAssertion("baccarat", user, method.Method,
                path, Encoding.UTF8.GetBytes(body), key, DateTimeOffset.UtcNow, new FortuneForge.Games.Abstractions.Auditing.GameRequestObservation(Guid.NewGuid().ToString("N"), user, null, null, DateTime.UtcNow, method.Method, path, "baccarat", null, operation, null, null, "sanitized", "direct"), headers: headers));
            return request;
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/games/baccarat/status")).StatusCode);
        const string body = "{\"betSide\":\"banker\",\"stake\":0.25,\"playerPairStake\":0.25}";
        using var first = await client.SendAsync(Signed(HttpMethod.Post, "/api/games/baccarat/rounds", body, operation: "baccarat_http_original_0001"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); var original = await first.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BaccaratRoundResponse>(original, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var savedRound = database.Collection("baccaratRounds").Document(result.RoundId);
        var savedCards = (await savedRound.GetSnapshotAsync()).GetValue<string[]>("pendingShoeCards");
        var badCards = savedCards.ToArray(); badCards[0] = "not-a-card";
        await savedRound.UpdateAsync("pendingShoeCards", badCards);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(Signed(HttpMethod.Get, "/api/games/baccarat/rounds/" + result.RoundId))).StatusCode);
        await savedRound.UpdateAsync("pendingShoeCards", savedCards);
        builder.Configuration["Features:BaccaratEnabled"] = "false";
        Assert.Equal(original, await (await client.SendAsync(Signed(HttpMethod.Post, "/api/games/baccarat/rounds", body, operation: "baccarat_http_original_0001"))).Content.ReadAsStringAsync());
        Assert.Equal(original, await (await client.SendAsync(Signed(HttpMethod.Get, "/api/games/baccarat/rounds/" + result.RoundId))).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Signed(HttpMethod.Get, "/api/games/baccarat/rounds/" + result.RoundId, user: "mallory"))).StatusCode);
        using var altered = Signed(HttpMethod.Post, "/api/games/baccarat/rounds", body, operation: "baccarat_http_other_0001");
        altered.Content = new StringContent(body.Replace("0.25", "0.50"), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(altered)).StatusCode);
        Assert.Equal(1, wallet.Commits);
        foreach (var field in new[] { "walletReceipt", "outcomeSha256", "userId", "startIdempotencyKey" }) Assert.DoesNotContain(field, original);
    }

    [Fact]
    public async Task LostWalletResponseRecoversOriginalDealAfterRestartAndPause()
    {
        var db = Database(); var wallet = new Wallet { LoseNext = true }; var accepting = true;
        var store = new BaccaratFirestoreStore(db, wallet, wallet, () => accepting);
        var service = new BaccaratService(store, TimeProvider.System, Shoe);
        var request = new CreateBaccaratRoundRequest("banker", 0.25m, 0.25m, 0.50m);
        const string key = "baccarat_lost_response_0001";
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.StartAsync("player", request, key, default));
        Assert.Equal(1, wallet.Commits);
        Assert.Empty((await db.Collection("baccaratRoundEvents").GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<BaccaratRoundConflictException>(() => service.StartAsync("player", request, key + "other", default));
        accepting = false;
        var replica = new BaccaratService(new BaccaratFirestoreStore(db, wallet, wallet, () => accepting), TimeProvider.System,
            () => throw new InvalidOperationException("A saved retry must never require another shoe."));
        var replay = await replica.StartAsync("player", request, key, default);
        Assert.Equal(102.49m, replay.Balance); Assert.Equal(3.49m, replay.CombinedTotalReturn);
        Assert.Equal(4, replay.ShoeCardsUsed); Assert.Equal("win", replay.PlayerPairDisposition);
        var latest = await store.LatestAsync("player", default);
        Assert.Equal(replay.RoundId, latest!.Round.RoundId);
        Assert.Equal(JsonSerializer.Serialize(replay), JsonSerializer.Serialize(await replica.GetAsync("player", replay.RoundId, default)));
        Assert.Equal(JsonSerializer.Serialize(replay), JsonSerializer.Serialize(await replica.StartAsync("player", request, key, default)));
        Assert.Equal(1, wallet.Commits); Assert.Equal(2, wallet.Calls);
        await Assert.ThrowsAsync<BaccaratRoundConflictException>(() => replica.StartAsync("player", request with { Stake = 0.50m }, key, default));
        await Assert.ThrowsAsync<BaccaratUnavailableException>(() => replica.StartAsync("player", request, key + "new", default));
        await Assert.ThrowsAsync<BaccaratRoundNotFoundException>(() => replica.GetAsync("intruder", replay.RoundId, default));
        Assert.Single((await db.Collection("baccaratRoundEvents").GetSnapshotAsync()).Documents);
        var audit = Assert.Single((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        Assert.Equal("passed", audit.GetValue<string>("auditStatus"));
        Assert.Single((await db.Collection("gameAnalyticsSettlementOutbox").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("userBalances").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task ConcurrentReplicasChargeAndAdvanceShoeExactlyOnce()
    {
        var db = Database(); var wallet = new Wallet();
        var services = Enumerable.Range(0, 4).Select(_ => new BaccaratService(new BaccaratFirestoreStore(db, wallet, wallet), TimeProvider.System, Shoe)).ToArray();
        var request = new CreateBaccaratRoundRequest("banker", 0.25m);
        var results = await Task.WhenAll(services.Select(service => service.StartAsync("player", request, "baccarat_concurrent_0001", default)));
        Assert.Single(results.Select(value => JsonSerializer.Serialize(value)).Distinct()); Assert.Equal(1, wallet.Commits);
        var shoe = Assert.Single((await db.Collection("baccaratShoes").GetSnapshotAsync()).Documents);
        Assert.Equal(4, shoe.GetValue<long>("nextIndex"));
        Assert.Single((await db.Collection("baccaratRounds").GetSnapshotAsync()).Documents);
        await services[0].StartAsync("player", request, "baccarat_concurrent_0002", default);
        Assert.Equal(2, wallet.Commits);
        shoe = Assert.Single((await db.Collection("baccaratShoes").GetSnapshotAsync()).Documents);
        Assert.True(shoe.GetValue<long>("nextIndex") > 4);
    }

    [Fact]
    public async Task DeclinedWagerDoesNotAdvanceShoeOrPublishOutcome()
    {
        var db = Database(); var wallet = new Wallet { Balance = 0 };
        var store = new BaccaratFirestoreStore(db, wallet, wallet);
        var service = new BaccaratService(store, TimeProvider.System, Shoe);
        var request = new CreateBaccaratRoundRequest("banker", 0.25m);
        for (var attempt = 0; attempt < 2; attempt++)
            await Assert.ThrowsAsync<BaccaratInsufficientCreditsException>(() => service.StartAsync("player", request, "baccarat_declined_0001", default));
        Assert.Equal(1, wallet.Calls); Assert.Equal(0, wallet.Commits);
        var shoe = Assert.Single((await db.Collection("baccaratShoes").GetSnapshotAsync()).Documents);
        Assert.Equal(0, shoe.GetValue<long>("nextIndex")); Assert.Equal("", shoe.GetValue<string>("pendingRoundId"));
        Assert.Empty((await db.Collection("baccaratRoundEvents").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        wallet.Balance = 10000;
        var result = await service.StartAsync("player", request, "baccarat_after_decline_0001", default);
        Assert.Equal(4, result.ShoeCardsUsed); Assert.Equal(1, wallet.Commits);
    }

    [Fact]
    public async Task DeclinedShoeRolloverPreservesTheHistoricalShoeUntilAnAcceptedWager()
    {
        var db = Database(); var wallet = new Wallet { Balance = 0 }; var cards = Shoe().Select(CardCode.Format).ToArray();
        var shoe = db.Collection("baccaratShoes").Document(BaccaratFirestoreStore.CreateLookupKey("player"));
        await shoe.SetAsync(new Dictionary<string, object> { ["userId"] = "player", ["cards"] = cards, ["nextIndex"] = 312L, ["schemaVersion"] = 1L });
        var service = new BaccaratService(new BaccaratFirestoreStore(db, wallet, wallet), TimeProvider.System, Shoe);
        await Assert.ThrowsAsync<BaccaratInsufficientCreditsException>(() => service.StartAsync("player", new("banker", 0.25m), "baccarat_rollover_decline_0001", default));
        var unchanged = await shoe.GetSnapshotAsync(); Assert.Equal(312, unchanged.GetValue<long>("nextIndex")); Assert.Equal(cards, unchanged.GetValue<string[]>("cards"));
        wallet.Balance = 10000;
        var accepted = await service.StartAsync("player", new("banker", 0.25m), "baccarat_rollover_accept_0001", default);
        Assert.Equal(4, accepted.ShoeCardsUsed); Assert.Equal(4, (await shoe.GetSnapshotAsync()).GetValue<long>("nextIndex"));
    }

    [Theory]
    [InlineData(75, 146)]
    [InlineData(1234, 2406)]
    public async Task HistoricalV1RoundsReplayWithoutPairFieldsOrNewWalletCharge(long stakeCents, long returnCents)
    {
        var db = Database(); var wallet = new Wallet(); const string key = "baccarat_legacy_round_0001";
        var id = BaccaratFirestoreStore.CreateLookupKey("player\n" + key);
        var dealt = PuntoBancoRoundDealer.Deal(Shoe());
        await db.Collection("baccaratRounds").Document(id).SetAsync(new Dictionary<string, object>
        {
            ["roundId"] = id, ["userId"] = "player", ["betSide"] = "banker", ["stakeCents"] = stakeCents,
            ["consumedCards"] = dealt.ConsumedCards.Select(CardCode.Format).ToArray(), ["outcome"] = "banker",
            ["endedOnNatural"] = true, ["disposition"] = "win", ["profitCents"] = returnCents - stakeCents, ["totalReturnCents"] = returnCents,
            ["shoeCardsUsed"] = 4L, ["shoeCardsRemaining"] = 412L, ["startIdempotencyKey"] = key, ["schemaVersion"] = 1L
        });
        var service = new BaccaratService(new BaccaratFirestoreStore(db, wallet, wallet, () => false), TimeProvider.System, () => []);
        var recovered = await service.GetAsync("player", id, default);
        var replay = await service.StartAsync("player", new("banker", stakeCents / 100m), key, default);
        Assert.Equal(JsonSerializer.Serialize(recovered), JsonSerializer.Serialize(replay));
        Assert.Null(replay.PlayerPairDisposition); Assert.Equal(returnCents / 100m, replay.CombinedTotalReturn); Assert.Equal(0, wallet.Calls);
        await Assert.ThrowsAsync<BaccaratRoundConflictException>(() => service.StartAsync("player", new("banker", stakeCents / 100m, 0.25m), key, default));
        Assert.Empty((await db.Collection("baccaratShoes").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task CorruptSavedOutcomeCannotReachWallet()
    {
        var db = Database(); var wallet = new Wallet { FailBeforeCommit = true };
        var service = new BaccaratService(new BaccaratFirestoreStore(db, wallet, wallet), TimeProvider.System, Shoe);
        const string key = "baccarat_corrupt_round_0001";
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.StartAsync("player", new("banker", 1m), key, default));
        var round = Assert.Single((await db.Collection("baccaratRounds").GetSnapshotAsync()).Documents);
        await round.Reference.UpdateAsync("combinedTotalReturnCents", 999999L);
        wallet.FailBeforeCommit = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync("player", round.Id, default));
        Assert.Equal(0, wallet.Commits);
    }

    private static FirestoreDb Database()
    {
        var endpoint = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") ?? "127.0.0.1:8085";
        if (!endpoint.StartsWith("127.0.0.1:") && !endpoint.StartsWith("localhost:")) throw new InvalidOperationException("Tests require a loopback Firestore emulator.");
        return new FirestoreDbBuilder { ProjectId = "demo-baccarat-wallet-" + Guid.NewGuid().ToString("N"), Endpoint = endpoint,
            ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None }.Build();
    }
    private static IReadOnlyList<PlayingCard> Shoe()
    {
        var prefix = new[] { new PlayingCard(CardRank.Four, CardSuit.Clubs), new PlayingCard(CardRank.King, CardSuit.Clubs),
            new PlayingCard(CardRank.Four, CardSuit.Hearts), new PlayingCard(CardRank.Nine, CardSuit.Clubs) };
        var cards = Enumerable.Range(0, 8).SelectMany(_ => StandardDeck.Create()).ToList(); foreach (var card in prefix) cards.Remove(card);
        return [..prefix, ..cards];
    }
    private sealed class Wallet : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly Lock gate = new(); private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> receipts = [];
        public long Balance = 10000; public int Calls; public int Commits; public bool LoseNext; public bool FailBeforeCommit;
        public Task<GameWalletAccountState> GetStateAsync(string account, CancellationToken token) => Task.FromResult(new GameWalletAccountState(0, Balance));
        public Task<GameWalletSettlementReceipt> SettleAsync(string account, GameWalletSettlementRequest request, CancellationToken token)
        {
            lock (gate)
            {
                Calls++;
                if (FailBeforeCommit) throw new GameWalletUnavailableException();
                if (receipts.TryGetValue(account + ":" + request.OperationId, out var existing))
                {
                    Assert.Equal(existing.Request, request); return Task.FromResult(existing.Receipt);
                }
                var before = Balance; var declined = before < request.WagerCents;
                if (!declined) { Balance = before - request.WagerCents + request.PayoutCents; Commits++; }
                var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId("baccarat", account, request.OperationId),
                    declined ? null : "wager", declined || request.PayoutCents == 0 ? null : "payout", before,
                    declined ? before : before - request.WagerCents, Balance, DateTimeOffset.UtcNow, false, declined);
                receipts.Add(account + ":" + request.OperationId, (request, receipt));
                if (LoseNext) { LoseNext = false; throw new GameWalletUnavailableException(); }
                return Task.FromResult(receipt);
            }
        }
    }
}
