using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.Cards;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Games.AndarBahar.Tests;

[Collection("Andar Bahar Firestore")]
public sealed class AndarBaharWalletStoreTests(AndarBaharEmulator emulator)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PaidHttp_UnknownWalletReplyRecoversWhileNewWagersArePaused()
    {
        var db = Database(); var wallet = new Wallet { LoseNextResponse = true };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Features:AndarBaharEnabled"] = "true";
        builder.Services.AddAndarBaharServer();
        builder.Services.AddSingleton(db);
        builder.Services.AddSingleton<IGameWalletClient>(wallet);
        builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
        await using var app = builder.Build();
        app.MapAndarBahar((_, _) => Task.FromResult<AndarBaharAccount?>(new("player", 0)));
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "andar_paid_http_replay_01");
        var request = new CreateAndarBaharRoundRequest("andar", .5m);
        var busy = await client.PostAsJsonAsync("/api/games/andar-bahar/rounds", request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), busy.Headers.RetryAfter!.Delta);
        builder.Configuration["Features:AndarBaharEnabled"] = "false";
        var recovered = await client.GetFromJsonAsync<AndarBaharRoundResponse>("/api/games/andar-bahar/rounds/latest");
        var replay = await client.PostAsJsonAsync("/api/games/andar-bahar/rounds", request);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var result = await replay.Content.ReadFromJsonAsync<AndarBaharRoundResponse>();
        Assert.Equal(recovered!.RoundId, result!.RoundId);
        Assert.Equal(recovered.Balance, result.Balance);
        Assert.Equal(recovered.Winner, result.Winner);
        Assert.False((await client.GetFromJsonAsync<AndarBaharStatusResponse>("/api/games/andar-bahar/status"))!.Available);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "andar_paid_http_replay_02");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/games/andar-bahar/rounds", request)).StatusCode);
        Assert.Equal(1, wallet.Committed);
    }

    [Theory]
    [InlineData(true, "andar", .50, .95, 100.45)]
    [InlineData(false, "bahar", .50, 1.0, 100.50)]
    [InlineData(true, "bahar", .50, 0, 99.50)]
    public async Task SignedSettlement_UsesRulesAndWritesHistoryWithoutDirectWalletAccess(bool andarWins, string side, double stake, double returned, double balance)
    {
        var db = Database(); var wallet = new Wallet(); var service = Service(db, wallet, andarWins);
        var result = await service.StartAsync("player", new(side, (decimal)stake), "andar_signed_rules_0001");
        Assert.Equal((decimal)returned, result.TotalReturn);
        Assert.Equal((decimal)balance, result.Balance);
        var replay = await service.StartAsync("player", new(side, (decimal)stake), "andar_signed_rules_0001");
        Assert.Equal(result.RoundId, replay.RoundId);
        Assert.Equal(result.Balance, replay.Balance);
        Assert.Equal(1, wallet.Committed);
        var history = Assert.Single((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Equal(result.RoundId, history.GetValue<string>("matchId"));
        Assert.Equal((long)(result.TotalReturn * 100), history.GetValue<long>("payoutCents"));
        Assert.Single((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("userBalances").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task LostWalletResponse_RestartAndPausedLatestRecoverOneFrozenDeal()
    {
        var db = Database(); var wallet = new Wallet { LoseNextResponse = true };
        const string key = "andar_lost_response_0001";
        using (GameSecurityContext.Push(Observation("original-wager", "POST", "/api/games/andar-bahar/rounds")))
            await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(db, wallet).StartAsync("player", new("andar", .5m), key));
        Assert.Equal(10045, wallet.Balance);
        Assert.Empty((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        using var recovery = GameSecurityContext.Push(Observation("recovery-request", "GET", "/api/games/andar-bahar/rounds/latest"));
        var paused = Service(db, wallet, andarWins: false, enabled: false);
        var recovered = await paused.LatestAsync("player");
        Assert.Equal("andar", recovered!.Winner);
        Assert.Equal(.95m, recovered.TotalReturn);
        Assert.Equal(100.45m, recovered.Balance);
        Assert.Equal(recovered.RoundId, (await paused.StartAsync("player", new("andar", .5m), key)).RoundId);
        Assert.Equal(1, wallet.Committed);
        Assert.Single((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Equal("original-wager", Assert.Single((await db.Collection(GameSecurityEventDocuments.CollectionName).GetSnapshotAsync()).Documents).GetValue<string>("requestId"));
        await Assert.ThrowsAsync<AndarBaharDisabledException>(() => paused.StartAsync("player", new("andar", .5m), "andar_paused_new_00001"));
        await Assert.ThrowsAsync<AndarBaharRoundNotFoundException>(() => paused.GetAsync("other", recovered.RoundId));
        Assert.Null(await paused.LatestAsync("other"));
    }

    [Fact]
    public async Task PendingDeal_RejectsDifferentWagerUntilOriginalIsRecovered()
    {
        var db = Database(); var wallet = new Wallet { LoseNextResponse = true }; var service = Service(db, wallet);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.StartAsync("player", new("andar", 1), "andar_pending_original_01"));
        await Assert.ThrowsAsync<AndarBaharRoundConflictException>(() => service.StartAsync("player", new("andar", 2), "andar_pending_original_01"));
        await Assert.ThrowsAsync<AndarBaharRoundConflictException>(() => service.StartAsync("player", new("andar", 1), "andar_pending_second_01"));
        Assert.Equal(1, wallet.Committed);
        var result = await service.GetAsync("player", AndarBaharService.LookupKey("player", "andar_pending_original_01"));
        Assert.Equal(100.9m, result.Balance);
        await service.StartAsync("player", new("andar", 1), "andar_pending_second_01");
        Assert.Equal(2, wallet.Committed);
    }

    [Fact]
    public async Task ConcurrentInstances_SettleOneDealWithExactResponse()
    {
        var db = Database(); var wallet = new Wallet();
        var first = Service(db, wallet); var second = Service(db, wallet, andarWins: false);
        var results = await Task.WhenAll(first.StartAsync("player", new("andar", .5m), "andar_parallel_same_0001"),
            second.StartAsync("player", new("andar", .5m), "andar_parallel_same_0001"));
        Assert.Single(results.Select(result => result.RoundId).Distinct());
        Assert.Single(results.Select(result => result.Winner).Distinct());
        Assert.Single(results.Select(result => result.Balance).Distinct());
        Assert.Equal(1, wallet.Committed);
        Assert.Single((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task DeclinedWager_RemainsDeclinedAfterTopupAndDoesNotPublishHistory()
    {
        var db = Database(); var wallet = new Wallet { Balance = 0 }; var service = Service(db, wallet);
        await Assert.ThrowsAsync<AndarBaharInsufficientCreditsException>(() => service.StartAsync("player", new("andar", .5m), "andar_declined_exact_001"));
        wallet.Balance = 10000;
        await Assert.ThrowsAsync<AndarBaharInsufficientCreditsException>(() => service.StartAsync("player", new("andar", .5m), "andar_declined_exact_001"));
        Assert.Equal(0, wallet.Committed);
        Assert.Empty((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync("player", new("andar", 100), "andar_historical_cap_01"));
        Assert.Equal(.95m, (await service.StartAsync("player", new("andar", .5m), "andar_after_decline_0001")).TotalReturn);
    }

    [Theory]
    [InlineData("intentSha256")]
    [InlineData("receiptSha256")]
    public async Task CorruptSavedProof_FailsClosedWithoutAnotherCharge(string field)
    {
        var db = Database(); var wallet = new Wallet(); var service = Service(db, wallet);
        var result = await service.StartAsync("player", new("andar", .5m), "andar_integrity_check_01");
        await db.Collection("andarBaharRounds").Document(result.RoundId).UpdateAsync(field, "corrupt");
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.GetAsync("player", result.RoundId));
        Assert.Equal(1, wallet.Committed);
    }

    [Fact]
    public async Task PersistedIntentBytes_KeepOriginalWalletFingerprintAcrossRecovery()
    {
        var db = Database(); var wallet = new Wallet { FailBeforeNextCommit = true };
        const string key = "andar_frozen_fingerprint_01";
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(db, wallet).StartAsync("player", new("andar", .5m), key));
        Assert.Equal(0, wallet.Committed);
        var id = AndarBaharService.LookupKey("player", key);
        var record = db.Collection("andarBaharRounds").Document(id);
        var pending = await record.GetSnapshotAsync();
        using var parsed = JsonDocument.Parse(pending.GetValue<string>("intentJson"));
        var originalBytes = JsonSerializer.Serialize(parsed.RootElement, new JsonSerializerOptions { WriteIndented = true });
        var digest = GameSecurityEventDocuments.Hash(originalBytes);
        await record.UpdateAsync(new Dictionary<string, object> { ["intentJson"] = originalBytes, ["intentSha256"] = digest });
        // Model an older serializer's successful wallet call whose response was lost.
        await wallet.SettleAsync("player", new(id, digest, 50, 95), default);
        var recovered = await Service(db, wallet).LatestAsync("player");
        Assert.Equal(id, recovered!.RoundId);
        Assert.Equal(100.45m, recovered.Balance);
        Assert.Equal(1, wallet.Committed);
        Assert.Single((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData("wager")]
    [InlineData("payout")]
    [InlineData("event")]
    [InlineData("history")]
    public async Task OrphanedLegacyProof_BlocksNewChargeForTheSameKey(string kind)
    {
        var db = Database(); var wallet = new Wallet();
        const string key = "andar_legacy_orphaned_01";
        var id = AndarBaharService.LookupKey("player", key);
        var proof = kind switch
        {
            "event" => db.Collection("andarBaharRoundEvents").Document(id + "-settled"),
            "history" => db.Collection("cardGameResults").Document("andar-bahar-" + id),
            _ => db.Collection("balanceTransactions").Document($"andar-bahar-{id}-{kind}"),
        };
        await proof.SetAsync(new Dictionary<string, object> { ["legacyProof"] = true });
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(db, wallet).StartAsync("player", new("andar", .5m), key));
        Assert.Equal(0, wallet.Committed);
        Assert.Empty((await db.Collection("andarBaharRounds").GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task HistoricalSchemas_PreserveFormerR100WagerWithoutChargeOrBackfill(int schema)
    {
        var db = Database(); var wallet = new Wallet { Balance = 12345 };
        const string key = "andar_legacy_hundred_01";
        var id = AndarBaharService.LookupKey("player", key);
        var prefix = AndarBaharServerTests.Deck(true).Take(2).Select(CardCode.Format).ToArray();
        var record = new
        {
            SchemaVersion = 1, RoundId = id, UserId = "player", IdempotencyKey = key, Side = "andar", StakeCents = 10000L,
            RevealedCards = prefix, Winner = "andar", ProfitCents = 9000L, TotalReturnCents = 19000L,
            BalanceAfterWagerCents = 90000L, BalanceAfterSettlementCents = 109000L, CreatedAtUtc = Now,
        };
        await db.Collection("andarBaharRounds").Document(id).SetAsync(new Dictionary<string, object>
        { ["roundId"] = id, ["userId"] = "player", ["schemaVersion"] = (long)schema, ["recordJson"] = JsonSerializer.Serialize(record) });
        foreach (var kind in new[] { "wager", "payout" })
        {
            var ledgerId = $"andar-bahar-{id}-{kind}";
            var cents = kind == "wager" ? -10000L : 19000L;
            var after = kind == "wager" ? 90000L : 109000L;
            var ledger = new Dictionary<string, object>
            {
                ["transactionId"] = ledgerId, ["userId"] = "player", ["currencyId"] = "slotsCredits", ["type"] = "andar-bahar-" + kind,
                ["idempotencyKey"] = kind == "wager" ? key : key + "-payout", ["amountCents"] = cents, ["balanceAfterCents"] = after,
                ["amount"] = cents / 100d, ["balanceAfter"] = after / 100d,
            };
            if (schema == 2) { ledger["roundId"] = id; ledger["gameId"] = "andar-bahar"; }
            await db.Collection("balanceTransactions").Document(ledgerId).SetAsync(ledger);
        }
        await db.Collection("andarBaharRoundEvents").Document(id + "-settled").SetAsync(new Dictionary<string, object>
        { ["roundId"] = id, ["userId"] = "player", ["idempotencyKey"] = key, ["event"] = "settled", ["schemaVersion"] = 1L });
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("player")));
        await db.Collection("andarBaharAccounts").Document(hash).SetAsync(new Dictionary<string, object>
        { ["userId"] = "player", ["roundId"] = id, ["schemaVersion"] = 1L });
        if (schema == 2)
        {
            await db.Collection("cardGameResults").Document("andar-bahar-" + id).SetAsync(new Dictionary<string, object>
            {
                ["resultId"] = "andar-bahar-" + id, ["game"] = "andar-bahar", ["mode"] = "credit", ["userId"] = "player",
                ["matchId"] = id, ["wagerCents"] = 10000L, ["payoutCents"] = 19000L, ["netCents"] = 9000L,
                ["outcome"] = "win", ["side"] = "andar", ["winner"] = "andar", ["settlementStatus"] = "paid",
                ["claimStatus"] = "completed", ["startedAt"] = Timestamp.FromDateTime(Now.UtcDateTime),
                ["completedAt"] = Timestamp.FromDateTime(Now.UtcDateTime), ["schemaVersion"] = 1L,
            });
        }
        var paused = Service(db, wallet, enabled: false);
        var result = await paused.StartAsync("player", new("andar", 100), key);
        Assert.Equal(190m, result.TotalReturn);
        Assert.Equal(123.45m, result.Balance);
        Assert.Equal(id, (await paused.GetAsync("player", id)).RoundId);
        Assert.Equal(id, (await paused.LatestAsync("player"))!.RoundId);
        Assert.Equal(0, wallet.Committed);
        Assert.Equal(schema == 2 ? 1 : 0, (await db.Collection("cardGameResults").GetSnapshotAsync()).Count);
        await db.Collection("balanceTransactions").Document($"andar-bahar-{id}-wager").UpdateAsync("amountCents", -50L);
        await Assert.ThrowsAsync<InvalidOperationException>(() => paused.StartAsync("player", new("andar", 100), key));
        Assert.Equal(0, wallet.Committed);
    }

    private static GameRequestObservation Observation(string id, string method, string path) => new(id, "player", "session", "127.0.0.1",
        Now.UtcDateTime, method, path, "andar-bahar", null, null, null, null, "not-retained", "test");
    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-andar-bahar-{Guid.NewGuid():N}", Endpoint = emulator.Endpoint,
        ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None,
    }.Build();
    private static AndarBaharService Service(FirestoreDb db, Wallet wallet, bool andarWins = true, bool enabled = true) =>
        new(new AndarBaharWalletStore(db, wallet, wallet), new FixedTimeProvider(), () => AndarBaharServerTests.Deck(andarWins), enabled);
    private sealed class FixedTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Wallet : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> receipts = new();
        public long Balance { get; set; } = 10000;
        public int Committed { get; private set; }
        public bool LoseNextResponse { get; set; }
        public bool FailBeforeNextCommit { get; set; }
        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken token) => Task.FromResult(new GameWalletAccountState(0, Balance));
        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken token)
        {
            lock (receipts)
            {
                if (FailBeforeNextCommit) { FailBeforeNextCommit = false; throw new GameWalletUnavailableException(); }
                var identity = accountId + "/" + request.OperationId;
                if (receipts.TryGetValue(identity, out var prior))
                {
                    if (prior.Request != request) throw new GameWalletConflictException();
                    return Task.FromResult(prior.Receipt with { WasAlreadySettled = true });
                }
                var decline = Balance < request.WagerCents;
                var before = Balance; var afterWager = decline ? before : before - request.WagerCents;
                Balance = decline ? before : afterWager + request.PayoutCents;
                var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId("andar-bahar", accountId, request.OperationId),
                    decline ? null : GameWalletSettlementIdentity.CreateId("andar-bahar", accountId, request.OperationId) + "-wager", !decline && request.PayoutCents > 0 ? GameWalletSettlementIdentity.CreateId("andar-bahar", accountId, request.OperationId) + "-payout" : null,
                    before, afterWager, Balance, Now, false, decline);
                receipts.Add(identity, (request, receipt));
                if (!decline) Committed++;
                if (LoseNextResponse) { LoseNextResponse = false; throw new GameWalletUnavailableException(); }
                return Task.FromResult(receipt);
            }
        }
    }
}
