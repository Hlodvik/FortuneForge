using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Cards;
using FortuneForge.Server.Cards.VideoPoker;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FortuneForge.Games.VideoPoker.Tests;

public sealed class VideoPokerPaidRoundTests
{
    private const string User = "video-poker-package-player";
    private const string DealKey = "video_poker_deal_test_0001";
    private const string DrawKey = "video_poker_draw_test_0001";

    [Fact]
    public void MultiHandLegacyRoundJsonRetainsReplacementDecksAndPublicProjectionHidesThem()
    {
        var round = VideoPokerRoundEngine.Deal([Royal(), Royal(), Royal()], 5);
        var restored = JsonSerializer.Deserialize<VideoPokerRound>(JsonSerializer.Serialize(round, RoundAuditCapture.Json), RoundAuditCapture.Json)!;
        Assert.Equal(3, restored.HandCount);
        Assert.All(restored.RemainingDecks, deck => Assert.Equal(47, deck.Length));
        var completed = VideoPokerRoundEngine.Draw(restored, AllHeld());
        var publicReply = VideoPokerService.ToResponse(new(new string('a', 64), completed, 1_209_500));
        Assert.Equal(12_000m, publicReply.Payout);
        var json = JsonSerializer.Serialize(publicReply, RoundAuditCapture.Json);
        Assert.DoesNotContain("remainingDeck", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("decks", json, StringComparison.OrdinalIgnoreCase);
    }

    [VideoPokerEmulatorFact]
    public async Task PublicRoutesPreserveCreatedLocationAndAllowRecoveryAndDrawWhileNewAdmissionIsPaused()
    {
        var database = Connect();
        var wallet = new Wallet();
        var original = await Service(database, wallet).StartAsync(User, new(5), DealKey, default);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["Features:VideoPokerEnabled"] = "false";
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<IGameWalletClient>(wallet);
        builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<VideoPokerFirestoreStore>();
        await using var app = builder.Build();
        app.MapVideoPoker(context => context.Request.Headers["X-Test-Account"].ToString() == User ? User : null);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient { BaseAddress = new Uri(address) };
        using var unauthorized = await http.GetAsync("/api/games/video-poker/status");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        http.DefaultRequestHeaders.Add("X-Test-Account", User);
        var status = await http.GetFromJsonAsync<VideoPokerStatusResponse>("/api/games/video-poker/status");
        Assert.False(status!.Available);

        using var retry = new HttpRequestMessage(HttpMethod.Post, "/api/games/video-poker/rounds")
        { Content = JsonContent.Create(new CreateVideoPokerRoundRequest(5)) };
        retry.Headers.Add("Idempotency-Key", DealKey);
        using var retried = await http.SendAsync(retry);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.Equal($"/api/games/video-poker/rounds/{original.RoundId}", retried.Headers.Location!.OriginalString);
        Assert.Equal(Json(original), Json(await retried.Content.ReadFromJsonAsync<VideoPokerRoundResponse>()));
        using var fresh = new HttpRequestMessage(HttpMethod.Post, "/api/games/video-poker/rounds")
        { Content = JsonContent.Create(new CreateVideoPokerRoundRequest(5)) };
        fresh.Headers.Add("Idempotency-Key", "video_poker_paused_fresh_0001");
        using var declined = await http.SendAsync(fresh);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, declined.StatusCode);
        using var draw = new HttpRequestMessage(HttpMethod.Post, $"/api/games/video-poker/rounds/{original.RoundId}/draw")
        { Content = JsonContent.Create(new DrawVideoPokerRoundRequest([0, 1, 2, 3, 4])) };
        draw.Headers.Add("Idempotency-Key", DrawKey);
        using var completed = await http.SendAsync(draw);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal("completed", (await completed.Content.ReadFromJsonAsync<VideoPokerRoundResponse>())!.Phase);
        Assert.Equal(1, wallet.Wagers);
        Assert.Equal(1, wallet.Payouts);
        await app.StopAsync();
    }

    [VideoPokerEmulatorFact]
    public async Task LostDealReplyRecoversOnGetAndExactDealRetryNeverRedrawsOrRecharges()
    {
        var database = Connect();
        var wallet = new Wallet { LoseNextReply = true };
        var service = Service(database, wallet);
        var request = new CreateVideoPokerRoundRequest(5, 3);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.StartAsync(User, request, DealKey, default));
        Assert.Equal(98_500, wallet.Balance);
        var pending = await database.Collection(VideoPokerFirestoreStore.RoundCollection)
            .Document(Id(User, DealKey)).GetSnapshotAsync();
        Assert.Equal(DealKey, pending.GetValue<string>("pendingOperationKey"));
        Assert.Empty((await database.Collection("videoPokerRoundEvents").GetSnapshotAsync()).Documents);

        var recovered = await Service(database, wallet, NoDeck).GetAsync(User, Id(User, DealKey), default);
        Assert.Equal(985m, recovered.Balance);
        var retry = await Service(database, wallet, () => StandardDeck.Create().Reverse().ToArray()).StartAsync(User, request, DealKey, default);
        Assert.Equal(Json(recovered), Json(retry));
        Assert.Equal(1, wallet.Wagers);
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("videoPokerRoundEvents").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(GameSecurityEventDocuments.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("userBalances").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [VideoPokerEmulatorFact]
    public async Task LostDrawReplyRecoversOnReadWithOnePayoutAndPreservesExactReceiptsAfterOtherWalletActivity()
    {
        var database = Connect();
        var wallet = new Wallet();
        var service = Service(database, wallet);
        var started = await service.StartAsync(User, new(5, 3), DealKey, default);
        wallet.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.DrawAsync(User, started.RoundId,
            new([0, 1, 2, 3, 4]), DrawKey, default));
        Assert.Equal(1_298_500, wallet.Balance);
        var recovered = await Service(database, wallet, NoDeck).GetAsync(User, started.RoundId, default);
        Assert.Equal(12_000m, recovered.Payout);
        var calls = wallet.Calls;
        wallet.Balance += 12_345;
        var replay = await service.DrawAsync(User, started.RoundId, new([4, 3, 2, 1, 0]), DrawKey, default);
        var duplicate = await service.DrawAsync(User, started.RoundId, new([0, 1, 2, 3, 4]), "video_poker_draw_other_0002", default);
        Assert.Equal(Json(recovered), Json(replay));
        Assert.Equal(Json(replay), Json(duplicate));
        Assert.Equal(1, wallet.Wagers);
        Assert.Equal(1, wallet.Payouts);
        Assert.Equal(calls, wallet.Calls);
        Assert.Equal(2, (await database.Collection("videoPokerRoundEvents").GetSnapshotAsync()).Count);
        Assert.Single((await database.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var evidence = Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        using var protectedCapture = JsonDocument.Parse(evidence.GetValue<string>("payloadJson"));
        var proof = protectedCapture.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName);
        Assert.Equal(VideoPokerBackend.FinancialEvidenceContract, proof.GetProperty("contractVersion").GetString());
        Assert.Equal(VideoPokerBackend.Owner, proof.GetProperty("backendOwner").GetString());
        Assert.Equal(RoundAuditFinancialProof.SignedWallet, proof.GetProperty("provenance").GetString());
        var entries = proof.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal(-1_500, Assert.Single(entries, entry => entry.GetProperty("kind").GetString() == "video-poker-wager").GetProperty("amountCents").GetInt64());
        Assert.Equal(1_200_000, Assert.Single(entries, entry => entry.GetProperty("kind").GetString() == "video-poker-payout").GetProperty("amountCents").GetInt64());
        var operations = proof.GetProperty("walletOperations").EnumerateArray().ToArray();
        Assert.Equal(2, operations.Length);
        foreach (var operation in operations)
        {
            var operationId = operation.GetProperty("documentId").GetString()!;
            var savedOperation = await database.Collection(VideoPokerBackend.OperationCollection).Document(operationId).GetSnapshotAsync();
            Assert.Equal(savedOperation.GetValue<string>("preparedSha256"), operation.GetProperty("outcomeSha256").GetString());
            var walletProof = operation.GetProperty("receiptsByAccount").GetProperty(User);
            Assert.Contains(entries, entry => entry.GetProperty("walletSettlementId").GetString() == walletProof.GetProperty("settlementId").GetString());
        }
        var raw = await database.Collection(VideoPokerFirestoreStore.RoundCollection).Document(started.RoundId).GetSnapshotAsync();
        Assert.Equal(GameWalletSettlementIdentity.CreateId("video-poker", User, $"video-poker-{started.RoundId}-deal"), raw.GetValue<string>("dealWalletSettlementId"));
        Assert.Equal(GameWalletSettlementIdentity.CreateId("video-poker", User, $"video-poker-{started.RoundId}-draw"), raw.GetValue<string>("drawWalletSettlementId"));
        Assert.Equal(64, raw.GetValue<string>("drawOutcomeSha256").Length);
        var history = await Store(database, wallet).HistoryAsync(User, 10, default);
        Assert.Equal("completed", Assert.Single(history).Phase);
        Assert.Equal(12_000m, history[0].Payout);
    }

    [VideoPokerEmulatorFact]
    public async Task ConcurrentDrawRetriesPublishOneOutcomeAndRejectChangedHoldsAndCrossRoundKeyReuse()
    {
        var database = Connect();
        var wallet = new Wallet();
        var service = Service(database, wallet);
        var started = await service.StartAsync(User, new(5), DealKey, default);
        var replies = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            service.DrawAsync(User, started.RoundId, new([0, 1, 2, 3, 4]), DrawKey, default)));
        Assert.Single(replies.Select(Json).Distinct());
        Assert.Equal(1, wallet.Payouts);
        await Assert.ThrowsAsync<VideoPokerRoundConflictException>(() => service.DrawAsync(User, started.RoundId,
            new([]), "video_poker_changed_holds_0001", default));
        await Assert.ThrowsAsync<VideoPokerRoundConflictException>(() => service.StartAsync(User, new(4), DealKey, default));
        var second = await service.StartAsync(User, new(1), "video_poker_second_deal_0001", default);
        await Assert.ThrowsAsync<VideoPokerRoundConflictException>(() => service.DrawAsync(User, second.RoundId,
            new([0, 1, 2, 3, 4]), DrawKey, default));
        await Assert.ThrowsAsync<VideoPokerRoundNotFoundException>(() => service.GetAsync("another-player", started.RoundId, default));
    }

    [VideoPokerEmulatorFact]
    public async Task WalletDeclineIsDurableHidesTheDealAndExactRetryCannotSpendAfterTopUp()
    {
        var database = Connect();
        var wallet = new Wallet { Balance = 0 };
        var service = Service(database, wallet);
        await Assert.ThrowsAsync<VideoPokerInsufficientCreditsException>(() => service.StartAsync(User, new(5), DealKey, default));
        wallet.Balance = 100_000;
        await Assert.ThrowsAsync<VideoPokerInsufficientCreditsException>(() => service.StartAsync(User, new(5), DealKey, default));
        Assert.Equal(1, wallet.Calls);
        Assert.Equal(0, wallet.Wagers);
        Assert.False((await database.Collection(VideoPokerFirestoreStore.RoundCollection).Document(Id(User, DealKey)).GetSnapshotAsync()).Exists);
        Assert.Empty((await database.Collection(GameSecurityEventDocuments.CollectionName).GetSnapshotAsync()).Documents);
        var another = await service.StartAsync(User, new(5), "video_poker_after_topup_0001", default);
        Assert.Equal("awaiting-draw", another.Phase);
    }

    [VideoPokerEmulatorFact]
    public async Task LegacySingleHandAwaitingDrawResumesWithoutChargingAndKeepsSavedCards()
    {
        var database = Connect();
        var wallet = new Wallet();
        var reference = await SeedLegacy(database, 1, false);
        var service = Service(database, wallet, NoDeck);
        var resumed = await service.GetAsync(User, reference.Id, default);
        Assert.Equal("awaiting-draw", resumed.Phase);
        var completed = await service.DrawAsync(User, reference.Id, new([0, 1, 2, 3, 4]), DrawKey, default);
        Assert.Equal(resumed.InitialCards, completed.InitialCards);
        Assert.Equal(4_000m, completed.Payout);
        Assert.Equal(0, wallet.Wagers);
        Assert.Equal(1, wallet.Payouts);
        Assert.Equal(1, wallet.Calls);
        var raw = await reference.GetSnapshotAsync();
        Assert.Equal(1, raw.GetValue<long>("schemaVersion"));
        Assert.Equal("completed", raw.GetValue<string>("status"));
        Assert.Equal(400_000, raw.GetValue<long>("payoutCents"));
    }

    [VideoPokerEmulatorFact]
    public async Task LegacyAcceptedDrawDoesNotRequireAnArchivedOpeningLedgerOrRechargeItsSavedWager()
    {
        var database = Connect();
        var wallet = new Wallet();
        var reference = await SeedLegacy(database, 1, false);
        await database.Collection("balanceTransactions").Document($"video-poker-{reference.Id}-wager").DeleteAsync();
        await database.Collection("videoPokerRoundEvents").Document($"{reference.Id}-deal").DeleteAsync();
        var completed = await Service(database, wallet, NoDeck).DrawAsync(User, reference.Id, new([0, 1, 2, 3, 4]), DrawKey, default);
        Assert.Equal(4_000m, completed.Payout);
        Assert.Equal(0, wallet.Wagers);
        Assert.Equal(1, wallet.Payouts);
        var evidence = Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        using var capture = JsonDocument.Parse(evidence.GetValue<string>("payloadJson"));
        var proof = capture.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName);
        Assert.Equal(RoundAuditFinancialProof.MixedLegacyAndSignedWallet, proof.GetProperty("provenance").GetString());
        Assert.Contains(proof.GetProperty("entries").EnumerateArray(), entry => !entry.GetProperty("evidenceAvailable").GetBoolean());
    }

    [VideoPokerEmulatorFact]
    public async Task PreparedDrawFromBeforeFinancialProofUpgradeRetainsItsOriginalWalletHashAcrossReplyLoss()
    {
        var database = Connect();
        var wallet = new Wallet();
        var started = await Service(database, wallet).StartAsync(User, new(5), DealKey, default);
        var source = database.Collection(VideoPokerBackend.RoundCollection).Document(started.RoundId);
        var saved = await source.GetSnapshotAsync();
        var completed = VideoPokerRoundEngine.Draw(VideoPokerRoundEngine.Deal(Royal(), 5), AllHeld());
        var positions = new[] { 0, 1, 2, 3, 4 };
        var requestHash = GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(new
        { kind = "draw", roundId = started.RoundId, coins = 5, hands = 1, holds = positions }, RoundAuditCapture.Json));
        var oldJson = JsonSerializer.Serialize(new
        {
            userId = User, roundId = started.RoundId, idempotencyKey = DrawKey, kind = "draw", requestSha256 = requestHash,
            round = completed, startedAtUtc = saved.GetValue<Timestamp>("createdAt").ToDateTimeOffset(),
            occurredAtUtc = DateTimeOffset.UtcNow, observation = (GameRequestObservation?)null
        }, RoundAuditCapture.Json);
        var oldHash = GameSecurityEventDocuments.Hash(oldJson);
        await database.Collection(VideoPokerBackend.OperationCollection).Document(Id(User, DrawKey)).CreateAsync(new Dictionary<string, object>
        {
            ["userId"] = User, ["roundId"] = started.RoundId, ["kind"] = "draw", ["requestSha256"] = requestHash,
            ["preparedJson"] = oldJson, ["preparedSha256"] = oldHash, ["status"] = "prepared", ["schemaVersion"] = 1L
        });
        await source.UpdateAsync("pendingOperationKey", DrawKey);
        wallet.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(database, wallet, NoDeck).GetAsync(User, started.RoundId, default));
        var recovered = await Service(database, wallet, NoDeck).GetAsync(User, started.RoundId, default);
        Assert.Equal(4_000m, recovered.Payout);
        Assert.Equal(1, wallet.Wagers);
        Assert.Equal(1, wallet.Payouts);
        Assert.Equal(oldHash, (await database.Collection(VideoPokerBackend.OperationCollection).Document(Id(User, DrawKey)).GetSnapshotAsync()).GetValue<string>("preparedSha256"));
    }

    [VideoPokerEmulatorFact]
    public async Task LegacyCompletedMultiHandHistoryAndDrawRetryNeverChargeOrPayAgain()
    {
        var database = Connect();
        var wallet = new Wallet();
        var reference = await SeedLegacy(database, 2, true);
        var service = Service(database, wallet, NoDeck);
        var read = await service.GetAsync(User, reference.Id, default);
        Assert.Equal(3, read.HandCount);
        Assert.Equal(12_000m, read.Payout);
        var replay = await service.DrawAsync(User, reference.Id, new([0, 1, 2, 3, 4]), DrawKey, default);
        Assert.Equal(Json(read), Json(replay));
        Assert.Equal(0, wallet.Calls);
        Assert.Equal(12_000m, Assert.Single(await Store(database, wallet).HistoryAsync(User, 10, default)).Payout);
        Assert.Empty((await database.Collection(VideoPokerFirestoreStore.OperationCollection).GetSnapshotAsync()).Documents);
    }

    [VideoPokerEmulatorFact]
    public async Task CorruptSavedPayoutAndReplacementDecksFailBeforeWalletUse()
    {
        var database = Connect();
        var wallet = new Wallet();
        var reference = await SeedLegacy(database, 2, true);
        await reference.UpdateAsync("payoutCents", 1L);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(database, wallet).GetAsync(User, reference.Id, default));
        Assert.Equal(0, wallet.Calls);
    }

    private static VideoPokerService Service(FirestoreDb database, Wallet wallet, Func<IReadOnlyList<PlayingCard>>? decks = null) =>
        new(Store(database, wallet), TimeProvider.System, decks ?? Royal);
    private static VideoPokerFirestoreStore Store(FirestoreDb database, Wallet wallet) => new(database, wallet, wallet);
    private static string Id(string user, string key) => VideoPokerFirestoreStore.CreateLookupKey($"{user}\n{key}");
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, RoundAuditCapture.Json);
    private static VideoPokerHeldCardPositions AllHeld() => new(Enum.GetValues<VideoPokerCardPosition>());
    private static IReadOnlyList<PlayingCard> NoDeck() => throw new InvalidOperationException("A read or draw must use the saved deck.");
    private static IReadOnlyList<PlayingCard> Royal()
    {
        var royal = new[] { CardRank.Ace, CardRank.King, CardRank.Queen, CardRank.Jack, CardRank.Ten }
            .Select(rank => new PlayingCard(rank, CardSuit.Spades)).ToArray();
        return royal.Concat(StandardDeck.Create().Except(royal)).ToArray();
    }
    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"video-poker-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static async Task<DocumentReference> SeedLegacy(FirestoreDb database, int schema, bool completed)
    {
        var id = Id(User, "video_poker_legacy_deal_0001");
        var deck = Royal().Select(CardCode.Format).ToArray();
        var fields = new Dictionary<string, object>
        {
            ["roundId"] = id, ["userId"] = User, ["coinsWagered"] = 5L, ["schemaVersion"] = (long)schema,
            ["status"] = completed ? "completed" : "awaiting-draw", ["startIdempotencyKey"] = "video_poker_legacy_deal_0001",
            ["createdAt"] = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(-1)), ["updatedAt"] = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(-1))
        };
        if (schema == 1) fields["deck"] = deck;
        else
        {
            fields["handCount"] = 3L;
            fields["decks"] = Enumerable.Range(0, 3).SelectMany(_ => deck).ToArray();
        }
        if (completed)
        {
            fields["heldPositions"] = new long[] { 0, 1, 2, 3, 4 };
            fields["drawIdempotencyKey"] = "video_poker_legacy_draw_0001";
            fields["payoutCents"] = schema == 1 ? 400_000L : 1_200_000L;
            fields["handRank"] = "royal-flush";
            if (schema == 2)
            {
                fields["handRanks"] = new[] { "royal-flush", "royal-flush", "royal-flush" };
                fields["handPayoutCents"] = new long[] { 400_000, 400_000, 400_000 };
            }
        }
        var reference = database.Collection(VideoPokerFirestoreStore.RoundCollection).Document(id);
        await reference.CreateAsync(fields);
        await database.Collection("balanceTransactions").Document($"video-poker-{id}-wager")
            .CreateAsync(new Dictionary<string, object>
            { ["userId"] = User, ["currencyId"] = "slotsCredits", ["type"] = "video-poker-wager", ["amount"] = schema == 1 ? -5d : -15d });
        await database.Collection("videoPokerRoundEvents").Document($"{id}-deal")
            .CreateAsync(new Dictionary<string, object> { ["userId"] = User });
        if (completed)
        {
            await database.Collection("balanceTransactions").Document($"video-poker-{id}-payout")
                .CreateAsync(new Dictionary<string, object>
                { ["userId"] = User, ["currencyId"] = "slotsCredits", ["type"] = "video-poker-payout", ["amount"] = schema == 1 ? 4_000d : 12_000d });
            await database.Collection("videoPokerRoundEvents").Document($"{id}-draw")
                .CreateAsync(new Dictionary<string, object> { ["userId"] = User });
        }
        return reference;
    }

    private sealed class Wallet : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly Lock sync = new();
        private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> receipts = [];
        public long Balance { get; set; } = 100_000;
        public bool LoseNextReply { get; set; }
        public int Calls { get; private set; }
        public int Wagers { get; private set; }
        public int Payouts { get; private set; }
        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult(new GameWalletAccountState(0, Balance));
        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken cancellationToken)
        {
            lock (sync)
            {
                Calls++;
                if (receipts.TryGetValue(request.OperationId, out var previous))
                {
                    Assert.Equal(previous.Request, request);
                    return Task.FromResult(previous.Receipt with { WasAlreadySettled = true });
                }
                var before = Balance;
                var declined = before < request.WagerCents;
                if (!declined)
                {
                    Balance = checked(before - request.WagerCents + request.PayoutCents);
                    if (request.WagerCents > 0) Wagers++;
                    if (request.PayoutCents > 0) Payouts++;
                }
                var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId("video-poker", accountId, request.OperationId),
                    !declined && request.WagerCents > 0 ? request.OperationId + "-wager" : null,
                    !declined && request.PayoutCents > 0 ? request.OperationId + "-payout" : null,
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

public sealed class VideoPokerEmulatorFactAttribute : FactAttribute
{
    public VideoPokerEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "Run with a localhost Firestore emulator to verify paid Video Poker persistence.";
        else if (!Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")!.StartsWith("127.0.0.1:", StringComparison.Ordinal) &&
            !Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")!.StartsWith("localhost:", StringComparison.Ordinal))
            throw new InvalidOperationException("Paid Video Poker tests require a localhost emulator.");
    }
}
