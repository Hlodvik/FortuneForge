using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Cards;
using FortuneForge.Games.HouseHoldem;
using FortuneForge.Server.Cards.HouseHoldem;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Games.HouseHoldem.Tests;

public sealed class HouseHoldemRoundTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static string Key(string label) => $"house_holdem_{label}_operation";
    private static string Json<T>(T value) => HouseHoldemRoundCodec.Serialize(value);
    private static string[] Deck() => StandardDeck.Create().Select(card => card.Code).ToArray();

    [HouseHoldemEmulatorFact]
    public async Task ConcurrentStartsAndLaterRetriesReplayOriginalResponseWithoutAnotherWalletEffect()
    {
        var database = Database();
        var wallet = new Wallet(10_000);
        var first = Store(database, wallet);
        var second = Store(database, wallet);
        var starts = await Task.WhenAll(
            first.StartAsync("alice", Key("start"), 100, 100, Deck(), Now, default),
            second.StartAsync("alice", Key("start"), 100, 100, Deck().Reverse().ToArray(), Now.AddSeconds(1), default));
        Assert.Equal(Json(starts[0]), Json(starts[1]));
        Assert.Equal(1, wallet.Applied);
        Assert.Equal(9_700, wallet.Balance);
        var id = starts[0].RoundId;
        await second.TransitionAsync("alice", id, Key("check"), "check", Now.AddSeconds(2), default);
        var calls = wallet.Calls;
        var replay = await Store(database, wallet).StartAsync("alice", Key("start"), 100, 100, [], Now.AddHours(1), default);
        Assert.Equal(Json(starts[0]), Json(replay));
        Assert.Equal(calls, wallet.Calls);
        Assert.Equal("flop", (await first.GetAsync("alice", id, default))!.Round.Phase);
        await Assert.ThrowsAsync<HouseHoldemRoundConflictException>(() =>
            first.StartAsync("alice", Key("start"), 200, 100, Deck(), Now, default));
        await Assert.ThrowsAsync<HouseHoldemRoundConflictException>(() =>
            first.TransitionAsync("alice", id, Key("check"), "play-2x", Now, default));
    }

    [HouseHoldemEmulatorFact]
    public async Task LostWalletStartReplyRecoversFromStatusOnAnotherInstanceWithoutDrawingAnotherHand()
    {
        var database = Database();
        var wallet = new Wallet(10_000) { LoseNextReply = true };
        var store = Store(database, wallet);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() =>
            store.StartAsync("alice", Key("lost_start"), 100, 0, Deck(), Now, default));
        Assert.Equal(9_800, wallet.Balance);
        Assert.Empty((await database.Collection("houseHoldemRounds").GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<HouseHoldemRoundConflictException>(() =>
            Store(database, wallet).StartAsync("alice", Key("other_start"), 100, 0, Deck(), Now, default));
        var recoveredId = await Store(database, wallet).GetActiveRoundIdAsync("alice", default);
        var recovered = await Store(database, wallet).GetAsync("alice", recoveredId!, default);
        Assert.NotNull(recovered);
        Assert.Equal(HouseHoldemRules.StartRound(Deck(), 100).PlayerCards, recovered.Round.PlayerCards);
        Assert.Equal(1, wallet.Applied);
        Assert.Single((await database.Collection("houseHoldemRounds").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("houseHoldemRoundEvents").GetSnapshotAsync()).Documents);
    }

    [HouseHoldemEmulatorFact]
    public async Task LostFinalPayoutReplyRecoversHistoryAuditAndRedactedResponseExactlyOnce()
    {
        var database = Database();
        var wallet = new Wallet(10_000);
        var store = Store(database, wallet);
        var start = await store.StartAsync("alice", Key("paid"), 100, 100, Deck(), Now, default);
        var id = start.RoundId;
        Assert.Null(HouseHoldemService.ToResponse(start).DealerCards);
        var played = await store.TransitionAsync("alice", id, Key("play"), "play-4x", Now, default);
        Assert.Null(HouseHoldemService.ToResponse(played).DealerCards);
        await store.TransitionAsync("alice", id, Key("advance_flop"), "advance", Now, default);
        wallet.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() =>
            store.TransitionAsync("alice", id, Key("advance_river"), "advance", Now, default));
        var applied = wallet.Applied;
        var recovered = (await Store(database, wallet).GetAsync("alice", id, default))!;
        Assert.Equal("settled", recovered.Round.Phase);
        Assert.NotNull(HouseHoldemService.ToResponse(recovered).DealerCards);
        Assert.Null(await store.GetActiveRoundIdAsync("alice", default));
        var replay = await store.TransitionAsync("alice", id, Key("advance_river"), "advance", Now.AddHours(1), default);
        Assert.Equal(Json(recovered), Json(replay));
        Assert.Equal(applied, wallet.Applied);
        var result = Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Equal(recovered.Round.PayoutCents, result.GetValue<long>("payoutCents"));
        Assert.Equal(recovered.Round.TotalWagerCents, result.GetValue<long>("wagerCents"));
        Assert.Single((await database.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var evidence = Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        using (var payload = JsonDocument.Parse(evidence.GetValue<string>("payloadJson")))
        {
            var proof = payload.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName);
            Assert.Equal(HouseHoldemBackend.Owner, proof.GetProperty("backendOwner").GetString());
            Assert.Equal(RoundAuditFinancialProof.SignedWallet, proof.GetProperty("provenance").GetString());
            Assert.Contains(proof.GetProperty("entries").EnumerateArray(), entry =>
                entry.GetProperty("kind").GetString() == "house-holdem-wager");
            Assert.Contains(proof.GetProperty("entries").EnumerateArray(), entry =>
                entry.GetProperty("kind").GetString() == "house-holdem-play-wager");
            Assert.NotEmpty(proof.GetProperty("walletOperations").EnumerateArray());
        }
        Assert.Equal(4, (await database.Collection("gameSecurityEvents").GetSnapshotAsync()).Count);
        Assert.Null(await store.GetAsync("mallory", id, default));
        await Assert.ThrowsAsync<HouseHoldemRoundNotFoundException>(() =>
            store.TransitionAsync("mallory", id, Key("steal"), "advance", Now, default));
    }

    [HouseHoldemEmulatorFact]
    public async Task AtomicAdmissionKeepsOneAnteAvailableAndDeclineDoesNotCreateAHand()
    {
        var database = Database();
        var wallet = new Wallet(250);
        var store = Store(database, wallet);
        await Assert.ThrowsAsync<HouseHoldemInsufficientCreditsException>(() =>
            store.StartAsync("alice", Key("insufficient"), 100, 0, Deck(), Now, default));
        Assert.Equal(250, wallet.Balance);
        Assert.Equal(300, wallet.Last!.MinimumBalanceBeforeCents);
        Assert.Null(await store.GetActiveRoundIdAsync("alice", default));
        Assert.Empty((await database.Collection("houseHoldemRounds").GetSnapshotAsync()).Documents);
        wallet.Balance = 10_000;
        var calls = wallet.Calls;
        await Assert.ThrowsAsync<HouseHoldemInsufficientCreditsException>(() =>
            store.StartAsync("alice", Key("insufficient"), 100, 0, Deck(), Now, default));
        Assert.Equal(calls, wallet.Calls);
        await store.StartAsync("alice", Key("funded"), 100, 0, Deck(), Now, default);
        Assert.Equal(9_800, wallet.Balance);
    }

    [HouseHoldemEmulatorFact]
    public async Task HistoricalR500DeckAndStepsResumeAndRetryWithoutRechargingTheInitialWager()
    {
        var database = Database();
        var wallet = new Wallet(200_000);
        var key = Key("legacy_start");
        var id = HouseHoldemRoundCodec.Hash($"alice\n{key}");
        var round = HouseHoldemRules.RestoreRound(Deck(), 50_000, 100);
        HouseHoldemRules.ApplyAction(round, "check");
        HouseHoldemRules.ApplyAction(round, "check");
        var legacy = new HouseHoldemStoredRound(id, "alice", round, Deck(), ["check", "check"], Now);
        var fields = HouseHoldemRoundCodec.Write(legacy, Now, key);
        fields.Remove("privateRoundJson");
        fields.Remove("backendOwner");
        await database.Collection("houseHoldemRounds").Document(id).CreateAsync(fields);
        await database.Collection("houseHoldemActiveRounds").Document("alice").CreateAsync(new Dictionary<string, object> { ["roundId"] = id });
        await database.Collection("houseHoldemRoundEvents").Document($"{id}-{HouseHoldemRoundCodec.Hash(key)}")
            .CreateAsync(new Dictionary<string, object> { ["userId"] = "alice", ["command"] = "start" });
        var store = Store(database, wallet, allowNew: false);
        var service = new HouseHoldemService(store, TimeProvider.System);
        var retry = await service.StartAsync("alice", new(500, 1), key, default);
        Assert.Equal(500, retry.Ante);
        Assert.Equal("river", retry.Phase);
        Assert.Equal(0, wallet.Calls);
        var settled = await service.ActionAsync("alice", id, new("play-1x"), Key("legacy_play"), default);
        Assert.Equal(1_501, settled.TotalWager);
        Assert.Equal("settled", settled.Phase);
        Assert.Equal(50_000, wallet.Last!.WagerCents);
        Assert.Equal(1, wallet.Applied);
        Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<HouseHoldemRoundPausedException>(() =>
            store.StartAsync("alice", Key("paused"), 100, 0, Deck(), Now, default));
    }

    [HouseHoldemEmulatorFact]
    public async Task ProtectedCompletionKeepsMixedLegacyAndSignedEvidenceAfterOriginalLedgerChanges()
    {
        var database = Database();
        var wallet = new Wallet(9_800);
        var key = Key("legacy_proof");
        var id = HouseHoldemRoundCodec.Hash($"alice\n{key}");
        var stored = new HouseHoldemStoredRound(id, "alice", HouseHoldemRules.StartRound(Deck(), 100), Deck(), [], Now);
        await database.Collection(HouseHoldemBackend.RoundsCollection).Document(id).CreateAsync(HouseHoldemRoundCodec.Write(stored, Now, key));
        await database.Collection(HouseHoldemBackend.ActiveCollection).Document("alice").CreateAsync(new Dictionary<string, object> { ["roundId"] = id });
        await database.Collection(HouseHoldemBackend.EventsCollection).Document($"{id}-{HouseHoldemRoundCodec.Hash(key)}")
            .CreateAsync(new Dictionary<string, object> { ["userId"] = "alice", ["command"] = "start" });
        var legacyId = $"house-holdem-{id}-initial-wager";
        var legacy = database.Collection("balanceTransactions").Document(legacyId);
        await legacy.CreateAsync(new Dictionary<string, object>
        {
            ["userId"] = "alice", ["currencyId"] = "slotsCredits", ["type"] = "house-holdem-wager", ["amount"] = -2d
        });
        var store = Store(database, wallet);
        await store.TransitionAsync("alice", id, Key("proof_play"), "play-4x", Now, default);
        await store.TransitionAsync("alice", id, Key("proof_flop"), "advance", Now, default);
        await store.TransitionAsync("alice", id, Key("proof_river"), "advance", Now, default);
        var evidence = Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var json = evidence.GetValue<string>("payloadJson");
        using (var payload = JsonDocument.Parse(json))
        {
            var proof = payload.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName);
            Assert.Equal(RoundAuditFinancialProof.MixedLegacyAndSignedWallet, proof.GetProperty("provenance").GetString());
            Assert.Contains(proof.GetProperty("entries").EnumerateArray(), entry =>
                entry.GetProperty("documentId").GetString() == legacyId && entry.GetProperty("amountCents").GetInt64() == -200);
            Assert.Contains(proof.GetProperty("entries").EnumerateArray(), entry =>
                entry.GetProperty("provenance").GetString() == RoundAuditFinancialProof.SignedWallet);
        }
        await legacy.SetAsync(new Dictionary<string, object> { ["amount"] = -999d }, SetOptions.MergeAll);
        Assert.Equal(json, (await evidence.Reference.GetSnapshotAsync()).GetValue<string>("payloadJson"));
        Assert.Equal(GameSecurityEventDocuments.Hash(json), evidence.GetValue<string>("sha256"));
    }

    [Fact]
    public void LegacyCodecRetainsOldWagersAndRejectsSnapshotsThatDoNotReplay()
    {
        var round = HouseHoldemRules.RestoreRound(Deck(), 50_000, 100);
        HouseHoldemRules.ApplyAction(round, "play-4x");
        var stored = new HouseHoldemStoredRound(new string('a', 64), "alice", round, Deck(), ["play-4x"], Now);
        var fields = HouseHoldemRoundCodec.Write(stored, Now, Key("codec"));
        fields.Remove("privateRoundJson");
        Assert.Equal(300_100, HouseHoldemRoundCodec.Read(stored.RoundId, fields).Round.TotalWagerCents);
        fields["privateRoundJson"] = Json(HouseHoldemRules.RestoreRound(Deck(), 50_000, 100));
        Assert.Throws<InvalidOperationException>(() => HouseHoldemRoundCodec.Read(stored.RoundId, fields));
    }

    [Fact]
    public void PublicProjectionRedactsCardsUntilSettlementAndNewWagersUseCurrentLimit()
    {
        var round = HouseHoldemRules.StartRound(Deck(), 100);
        var start = HouseHoldemService.ToResponse(new(new string('a', 64), round, 10_000));
        Assert.Null(start.DealerCards);
        Assert.Empty(start.Board);
        Assert.Equal(2, start.PlayerCards.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => HouseHoldemMoney.ToAnteCents(500));
        Assert.Equal(10_000, HouseHoldemMoney.ToAnteCents(100));
        HouseHoldemRules.ApplyAction(round, "play-4x");
        HouseHoldemRules.Advance(round);
        HouseHoldemRules.Advance(round);
        Assert.Equal(2, HouseHoldemService.ToResponse(new(new string('a', 64), round, 10_000)).DealerCards!.Count);
    }

    [Fact]
    public async Task ExistingPublicUrlsRequireTheGatewayAccountBeforeReadingGameState()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapHouseHoldem(_ => null);
        await app.StartAsync();
        var client = app.GetTestClient();
        var id = new string('a', 64);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/games/house-holdem/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/games/house-holdem/rounds/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/games/house-holdem/rounds", new { ante = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/games/house-holdem/rounds/{id}/actions", new { action = "check" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/games/house-holdem/rounds/{id}/advance", null)).StatusCode);
    }

    [HouseHoldemEmulatorFact]
    public async Task PausedWorkerStillRecoversAndFinishesExistingHandsThroughOriginalUrls()
    {
        var database = Database();
        var wallet = new Wallet(10_000) { LoseNextReply = true };
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() =>
            Store(database, wallet).StartAsync("alice", Key("http_recovery"), 100, 0, Deck(), Now, default));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Features:HouseHoldemEnabled"] = "false";
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<IGameWalletClient>(wallet);
        builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
        await using var app = builder.Build();
        app.MapHouseHoldem(_ => "alice");
        await app.StartAsync();
        var client = app.GetTestClient();
        var status = await client.GetFromJsonAsync<HouseHoldemStatusResponse>("/api/games/house-holdem/status");
        Assert.NotNull(status);
        Assert.False(status.Available);
        Assert.NotNull(status.ActiveRoundId);
        Assert.Equal(98, status.Balance);
        var path = $"/api/games/house-holdem/rounds/{status.ActiveRoundId}";
        foreach (var (action, operation) in new[] { ("check", "http_check_one"), ("check", "http_check_two"), ("fold", "http_fold") })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{path}/actions") { Content = JsonContent.Create(new { action }) };
            request.Headers.Add("Idempotency-Key", Key(operation));
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        }
        Assert.Equal("settled", (await client.GetFromJsonAsync<HouseHoldemRoundResponse>(path))!.Phase);
        using var paused = new HttpRequestMessage(HttpMethod.Post, "/api/games/house-holdem/rounds")
        {
            Content = JsonContent.Create(new { ante = 1 })
        };
        paused.Headers.Add("Idempotency-Key", Key("new_paused"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(paused)).StatusCode);
    }

    private static FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = $"house-holdem-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static HouseHoldemFirestoreStore Store(FirestoreDb database, Wallet wallet, bool allowNew = true) =>
        new(database, wallet, wallet, allowNew);

    private sealed class Wallet(long balance) : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> receipts = [];
        public long Balance { get; set; } = balance;
        public int Applied { get; private set; }
        public int Calls { get; private set; }
        public bool LoseNextReply { get; set; }
        public GameWalletSettlementRequest? Last { get; private set; }

        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult(new GameWalletAccountState(0, Balance));

        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken cancellationToken)
        {
            lock (receipts)
            {
                Calls++;
                Last = request;
                if (receipts.TryGetValue(request.OperationId, out var prior))
                {
                    Assert.Equal(prior.Request, request);
                    return Task.FromResult(prior.Receipt with { WasAlreadySettled = true });
                }
                var before = Balance;
                var declined = before < GameWalletBalanceRequirement.RequiredCents(request.WagerCents, request.MinimumBalanceBeforeCents);
                if (!declined)
                {
                    Balance = checked(before - request.WagerCents + request.PayoutCents);
                    Applied++;
                }
                var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId("house-holdem", accountId, request.OperationId),
                    !declined && request.WagerCents > 0 ? $"{request.OperationId}-wager" : null,
                    !declined && request.PayoutCents > 0 ? $"{request.OperationId}-payout" : null,
                    before, declined ? before : before - request.WagerCents, Balance, DateTimeOffset.UtcNow, false, declined);
                receipts.Add(request.OperationId, (request, receipt));
                if (LoseNextReply)
                {
                    LoseNextReply = false;
                    throw new GameWalletUnavailableException();
                }
                return Task.FromResult(receipt);
            }
        }
    }
}

public sealed class HouseHoldemEmulatorFactAttribute : FactAttribute
{
    public HouseHoldemEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A localhost Firestore emulator is required for House Hold'em durable lifecycle checks.";
    }
}
