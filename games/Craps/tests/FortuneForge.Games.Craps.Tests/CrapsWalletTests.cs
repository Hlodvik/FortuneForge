using System.Net;
using System.Net.Http.Json;
using System.Text;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.PackagedGame.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Craps;
using FortuneForge.Games.Dice;
using FortuneForge.Games.Firestore;
using FortuneForge.Server.Games.Craps;
using FortuneForge.Server.SecurityAudit;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Games.Craps.Tests;
public sealed class CrapsWalletTests
{
    [Fact]
    public async Task SignedWorkerRecoversLostRollWithNewStartsPausedAndHidesPrivateState()
    {
        var wallet = new Wallet(); var db = Database(); var key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(key); builder.Configuration["Features:CrapsEnabled"] = "true";
        builder.Services.AddSingleton(db); GameWorkerModule.Configure(builder);
        builder.Services.AddSingleton<IGameWalletClient>(wallet); builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
        builder.Services.AddSingleton(new FirestorePaidCrapsStore(db, wallet, wallet, () => Dice(3, 4), () => builder.Configuration.GetValue("Features:CrapsEnabled", false)));
        await using var app = builder.Build(); app.UseRouting(); app.UseGameWorkerAuthentication("craps");
        GameWorkerModule.Map(app, GameWorkerAuthentication.GetAccountId); await app.StartAsync(); var client = app.GetTestClient();
        HttpRequestMessage Signed(HttpMethod method, string path, string body = "", string user = "alice")
        {
            var request = new HttpRequestMessage(method, path);
            if (body.Length > 0) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.Add(GameWorkerAuthentication.AssertionHeader, GameWorkerAuthentication.CreateAssertion("craps", user, method.Method,
                path, Encoding.UTF8.GetBytes(body), key, DateTimeOffset.UtcNow, new GameRequestObservation(Guid.NewGuid().ToString("N"), user, null, null, DateTime.UtcNow, method.Method, path, "craps", null, null, null, null, "sanitized", "direct"))); return request;
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/games/craps/status")).StatusCode);
        var body = JsonSerializer.Serialize(new StartCrapsRoundRequest(1m, null, Key()));
        var first = await client.SendAsync(Signed(HttpMethod.Post, "/api/games/craps/rounds", body));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); var original = await first.Content.ReadAsStringAsync();
        var opened = JsonSerializer.Deserialize<CrapsRoundResponse>(original, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var source = db.Collection("crapsPaidRounds").Document(opened.RoundId.ToString("N"));
        var validJson = (await source.GetSnapshotAsync()).GetValue<string>("roundJson");
        await source.UpdateAsync("roundJson", "{");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(Signed(HttpMethod.Get, $"/api/games/craps/rounds/{opened.RoundId}"))).StatusCode);
        await source.UpdateAsync("roundJson", validJson);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Signed(HttpMethod.Post, "/api/games/craps/rounds", JsonSerializer.Serialize(new StartCrapsRoundRequest(0m, null, Key()))))).StatusCode);
        builder.Configuration["Features:CrapsEnabled"] = "false";
        Assert.Equal(original, await (await client.SendAsync(Signed(HttpMethod.Post, "/api/games/craps/rounds", body))).Content.ReadAsStringAsync());
        wallet.LoseNext = true; var rollBody = JsonSerializer.Serialize(Roll(opened)); var path = $"/api/games/craps/rounds/{opened.RoundId}/roll";
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(Signed(HttpMethod.Post, path, rollBody))).StatusCode);
        var recovered = await client.SendAsync(Signed(HttpMethod.Get, $"/api/games/craps/rounds/{opened.RoundId}"));
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode); var terminal = await recovered.Content.ReadAsStringAsync();
        Assert.Equal(terminal, await (await client.SendAsync(Signed(HttpMethod.Post, path, rollBody))).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Signed(HttpMethod.Get, $"/api/games/craps/rounds/{opened.RoundId}", user: "mallory"))).StatusCode);
        Assert.Equal(2, wallet.Commits); Assert.Equal(10100, wallet.Balance);
        foreach (var field in new[] { "walletReceipt", "preparedJson", "userId", "fingerprint" }) Assert.DoesNotContain(field, terminal);
    }

    [Fact]
    public async Task LostStartAndLostRollRepliesRecoverOnceAcrossReplicasAndPause()
    {
        var db = Database(); var wallet = new Wallet { LoseNext = true }; var enabled = true; var draws = 0;
        FirestorePaidCrapsStore Store() => new(db, wallet, wallet, () => { draws++; return Dice(3, 4); }, () => enabled);
        var first = Store(); var start = new StartCrapsRoundRequest(1m, [new("field", 1m), new("any-seven", 1m)], Key());
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => first.StartAsync("alice", start));
        Assert.Equal(9700, wallet.Balance); Assert.Equal(1, wallet.Commits);
        await Assert.ThrowsAsync<CrapsPaidException>(() => first.StartAsync("alice", start with { OperationId = Key() }));
        enabled = false;
        var restored = (await Store().ActiveAsync("alice"))!;
        Assert.Equal(9700, restored.WalletBalanceCents); Assert.Equal("come-out", restored.Phase);
        Assert.Equal(JsonSerializer.Serialize(restored), JsonSerializer.Serialize(await first.StartAsync("alice", start)));
        Assert.Equal(1, wallet.Commits);
        wallet.LoseNext = true;
        var roll = Roll(restored);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => first.RollAsync("alice", restored.RoundId, roll));
        Assert.Equal(10400, wallet.Balance); Assert.Equal(1, draws);
        var replica = new FirestorePaidCrapsStore(db, wallet, wallet, () => throw new InvalidOperationException("A saved roll must not draw again."), () => false);
        var terminal = await replica.GetAsync("alice", restored.RoundId);
        Assert.Equal("resolved", terminal.Phase); Assert.Equal(7, terminal.LastOutcome!.Total);
        Assert.Equal(10400, terminal.WalletBalanceCents); Assert.Null(await replica.ActiveAsync("alice"));
        Assert.Equal(JsonSerializer.Serialize(terminal), JsonSerializer.Serialize(await replica.RollAsync("alice", restored.RoundId, roll)));
        Assert.Equal(2, wallet.Commits); Assert.Equal(1, draws);
        await Assert.ThrowsAsync<CrapsAccessException>(() => replica.GetAsync("mallory", restored.RoundId));
        await CheckAudit(db, terminal.RoundId);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("userBalances").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task OddsSideBetReplacementsAndRefundsSettleWithTheirOriginalFinancialIdentity()
    {
        var db = Database(); var wallet = new Wallet(); var dice = new Queue<DicePair>([Dice(1, 3), Dice(1, 3)]);
        var store = new FirestorePaidCrapsStore(db, wallet, wallet, dice.Dequeue);
        var start = await store.StartAsync("alice", new(1m, [new("field", 1m)], Key()));
        var point = await store.RollAsync("alice", start.RoundId, Roll(start));
        Assert.Equal("point", point.Phase); Assert.Equal(10000, point.WalletBalanceCents); // Field pays R2 before the pass resolves.
        var oddsRequest = new PlaceCrapsOddsRequest(2m, Key(), point.Revision, 2);
        var odds = await store.PlaceOddsAsync("alice", start.RoundId, oddsRequest);
        Assert.Equal(9800, odds.WalletBalanceCents);
        var cancel = new SetCrapsWagerRequest("odds", 0, Key(), odds.Revision, 2);
        wallet.LoseNext = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.SetWagerAsync("alice", start.RoundId, cancel));
        var refunded = await new FirestorePaidCrapsStore(db, wallet, wallet).GetAsync("alice", start.RoundId);
        Assert.Equal(10000, refunded.WalletBalanceCents); Assert.True(refunded.ExtraBets.Single(extra => extra.Kind == "odds").Cancelled);
        Assert.Equal(JsonSerializer.Serialize(refunded), JsonSerializer.Serialize(await store.SetWagerAsync("alice", start.RoundId, cancel)));
        var replacement = await store.PlaceOddsAsync("alice", start.RoundId, new(3m, Key(), refunded.Revision, 2));
        Assert.Equal(9700, replacement.WalletBalanceCents);
        var terminal = await store.RollAsync("alice", start.RoundId, Roll(replacement));
        Assert.Equal("point-hit", terminal.LastOutcome!.Result); Assert.Equal(10800, terminal.WalletBalanceCents);
        Assert.Equal(500, terminal.TotalWagerCents); Assert.Equal(1300, terminal.TotalReturnCents);
        Assert.Equal(9m, terminal.ExtraBets.Single(extra => extra.Kind == "odds" && !extra.Cancelled).TotalReturn);
        await CheckAudit(db, terminal.RoundId);
    }

    [Fact]
    public async Task ConcurrentOpeningRetriesDebitOnceAndRejectChangedRequest()
    {
        var db = Database(); var wallet = new Wallet(); var request = new StartCrapsRoundRequest(2m, null, Key());
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => new FirestorePaidCrapsStore(db, wallet, wallet).StartAsync("alice", request)));
        Assert.Single(results.Select(result => JsonSerializer.Serialize(result)).Distinct()); Assert.Equal(9800, wallet.Balance); Assert.Equal(1, wallet.Commits);
        var conflict = await Assert.ThrowsAsync<CrapsPaidException>(() => new FirestorePaidCrapsStore(db, wallet, wallet).StartAsync("alice", request with { Stake = 3m }));
        Assert.Equal("craps-operation-conflict", conflict.Code);
    }

    [Fact]
    public async Task InsufficientOpeningLeavesNoActiveRoundAndKeepsTheOriginalDecline()
    {
        var db = Database(); var wallet = new Wallet { Balance = 0 }; var store = new FirestorePaidCrapsStore(db, wallet, wallet);
        var request = new StartCrapsRoundRequest(1m, null, Key());
        await Assert.ThrowsAsync<CrapsPaidException>(() => store.StartAsync("alice", request));
        wallet.Balance = 10000;
        await Assert.ThrowsAsync<CrapsPaidException>(() => store.StartAsync("alice", request));
        Assert.Null(await store.ActiveAsync("alice")); Assert.Equal(0, wallet.Commits); Assert.Equal(1, wallet.Calls);
        Assert.Empty((await db.Collection("crapsPaidRounds").GetSnapshotAsync()).Documents);
        var accepted = await store.StartAsync("alice", request with { OperationId = Key() }); Assert.Equal(9900, accepted.WalletBalanceCents);
    }

    [Fact]
    public async Task LegacyV1OpenRoundContinuesWithSignedPayoutAndPreservesOldJournalAndLedger()
    {
        var db = Database(); var wallet = new Wallet { Balance = 9900 }; var id = Guid.NewGuid(); var started = DateTime.UtcNow.AddMinutes(-1);
        var round = new PaidCrapsRound(id, "alice", CrapsEngine.StartPassLine(new("alice", 1m)), [], 100, 0, started, null, 0);
        await db.Collection("crapsPaidRounds").Document(id.ToString("N")).SetAsync(new Dictionary<string, object>
        { ["roundId"] = id.ToString("N"), ["userId"] = "alice", ["roundJson"] = JsonSerializer.Serialize(round), ["phase"] = "ComeOut", ["revision"] = 0L, ["schemaVersion"] = 1L });
        await db.Collection("crapsPaidPlayers").Document(Hash("alice")).SetAsync(new Dictionary<string, object>
        { ["userId"] = "alice", ["activeRoundId"] = id.ToString("N"), ["schemaVersion"] = 1L });
        var observation = GameSecurityEventDocuments.Capture("craps", id.ToString("N"), 0, started,
            new { round.State, round.Extras, round.TotalWagerCents, round.TotalReturnCents, round.Actions, round.SchemaVersion }, ["alice"], "started");
        await db.Collection(GameSecurityEventDocuments.CollectionName).Document(observation.Id).SetAsync(observation.Fields);
        await db.Collection("balanceTransactions").Document($"craps-{id:N}-entry").SetAsync(new Dictionary<string, object>
        { ["transactionId"] = $"craps-{id:N}-entry", ["userId"] = "alice", ["currencyId"] = "slotsCredits", ["type"] = "craps-wager", ["amount"] = -1d, ["balanceAfter"] = 99d, ["idempotencyKey"] = Key() });
        var store = new FirestorePaidCrapsStore(db, wallet, wallet, () => Dice(3, 4), () => false);
        var restored = await store.GetAsync("alice", id);
        var terminal = await store.RollAsync("alice", id, Roll(restored));
        Assert.Equal(10100, wallet.Balance); Assert.Equal(1, wallet.Commits);
        var stored = FirestorePaidCrapsStore.ReadRound(await db.Collection("crapsPaidRounds").Document(id.ToString("N")).GetSnapshotAsync(), "alice");
        Assert.Equal(started, stored.StartedAtUtc); Assert.Equal(2, stored.SchemaVersion); Assert.Single(stored.Actions!);
        Assert.Equal(observation.Fields["payloadJson"], (await db.Collection(GameSecurityEventDocuments.CollectionName).Document(observation.Id).GetSnapshotAsync()).GetValue<string>("payloadJson"));
        await CheckAudit(db, terminal.RoundId);
    }

    [Fact]
    public async Task LegacyOperationReceiptReplaysWithoutAChargeOrFabricatedSideWagerIdentities()
    {
        var db = Database(); var wallet = new Wallet(); var operation = Key(); var id = Guid.NewGuid();
        var old = new { RoundId = id, Stake = 1m, Phase = "come-out", Point = (int?)null, Rolls = Array.Empty<CrapsRollResponse>(),
            LastOutcome = (CrapsOutcomeResponse?)null, ExtraBets = Array.Empty<CrapsExtraBetResponse>(), WalletBalanceCents = 9900L };
        var fingerprint = Hash(JsonSerializer.Serialize(new object?[] { "start", 1m, Array.Empty<object>() }));
        await db.Collection("crapsPaidOperations").Document(Hash("alice\n" + operation)).SetAsync(new Dictionary<string, object>
        { ["userId"] = "alice", ["fingerprint"] = fingerprint, ["responseJson"] = JsonSerializer.Serialize(old) });
        var result = await new FirestorePaidCrapsStore(db, wallet, wallet, acceptsNewRounds: () => false).StartAsync("alice", new(1m, null, operation));
        Assert.Equal(id, result.RoundId); Assert.Equal(9900, result.WalletBalanceCents); Assert.Equal(0, result.Revision); Assert.Equal(100, result.TotalWagerCents); Assert.Equal(0, wallet.Calls);
    }

    [Fact]
    public async Task LegacyRollReceiptWithNullableWireRevisionReplaysBeforeFreshAdmissionGuards()
    {
        var db = Database(); var wallet = new Wallet(); var operation = Key(); var id = Guid.NewGuid();
        var dice = new CrapsRollResponse(1, 1, 3, 4, "point-established");
        var old = new { RoundId = id, Stake = 1m, Phase = "point", Point = 4, Rolls = new[] { dice },
            LastOutcome = new CrapsOutcomeResponse(1, 3, 4, "point-established", false, null), ExtraBets = Array.Empty<CrapsExtraBetResponse>(), WalletBalanceCents = 9900L };
        await db.Collection("crapsPaidOperations").Document(Hash("alice\n" + operation)).SetAsync(new Dictionary<string, object>
        { ["userId"] = "alice", ["fingerprint"] = Hash(JsonSerializer.Serialize(new object?[] { "roll", id })), ["responseJson"] = JsonSerializer.Serialize(old) });
        var restored = await new FirestorePaidCrapsStore(db, wallet, wallet).RollAsync("alice", id, new(operation));
        Assert.Equal(1, restored.Revision); Assert.Equal(4, restored.Point); Assert.Equal(0, wallet.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrphanedLegacyLedgerOrActionWitnessCannotBecomeANewRoll(bool witnessOnly)
    {
        var db = Database(); var wallet = new Wallet { Balance = 9900 }; var id = Guid.NewGuid(); var operation = Key(); var now = DateTime.UtcNow;
        var pass = CrapsEngine.Roll(CrapsEngine.StartPassLine(new("alice", 1m)), Dice(1, 3)).State;
        var round = new PaidCrapsRound(id, "alice", pass, [], 100, 0, now, null, 1);
        await db.Collection("crapsPaidRounds").Document(id.ToString("N")).SetAsync(new Dictionary<string, object>
        { ["roundId"] = id.ToString("N"), ["userId"] = "alice", ["roundJson"] = JsonSerializer.Serialize(round), ["phase"] = "Point", ["revision"] = 1L, ["schemaVersion"] = 1L });
        await db.Collection("crapsPaidPlayers").Document(Hash("alice")).SetAsync(new Dictionary<string, object> { ["userId"] = "alice", ["activeRoundId"] = id.ToString("N") });
        if (witnessOnly)
        {
            using var context = GameSecurityContext.Push(new("orphan", "alice", null, null, now, "POST", $"/api/games/craps/rounds/{id}/roll", "craps", id.ToString("N"), operation, 0, null, "sanitized", "direct"));
            var observation = GameSecurityEventDocuments.Capture("craps", id.ToString("N"), 1, now, new { round.State, round.Extras }, ["alice"], "rolled");
            await db.Collection(GameSecurityEventDocuments.CollectionName).Document(observation.Id).SetAsync(observation.Fields);
        }
        else await db.Collection("balanceTransactions").Document($"craps-{id:N}-{operation}-payout").SetAsync(new Dictionary<string, object>
        { ["userId"] = "alice", ["type"] = "craps-payout", ["idempotencyKey"] = operation, ["amount"] = 2d });
        var draws = 0; var store = new FirestorePaidCrapsStore(db, wallet, wallet, () => { draws++; return Dice(1, 3); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RollAsync("alice", id, new(operation, 1, 2)));
        Assert.Equal(0, draws); Assert.Equal(0, wallet.Calls); Assert.Equal(9900, wallet.Balance);
        Assert.Empty((await db.Collection("crapsPaidOperations").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task CorruptPendingJsonNeverReentersTheWalletAndCanResumeAfterRepair()
    {
        var db = Database(); var wallet = new Wallet { LoseNext = true }; var store = new FirestorePaidCrapsStore(db, wallet, wallet);
        var request = new StartCrapsRoundRequest(1m, null, Key());
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.StartAsync("alice", request));
        var operation = Assert.Single((await db.Collection("crapsPaidOperations").GetSnapshotAsync()).Documents);
        var original = operation.GetValue<string>("preparedJson"); await operation.Reference.UpdateAsync("preparedJson", "{");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StartAsync("alice", request)); Assert.Equal(1, wallet.Calls);
        await operation.Reference.UpdateAsync("preparedJson", original);
        var restored = await store.ActiveAsync("alice"); Assert.Equal(9900, restored!.WalletBalanceCents); Assert.Equal(1, wallet.Commits);
    }

    private static async Task CheckAudit(FirestoreDb db, Guid id)
    {
        var round = FirestorePaidCrapsStore.ReadRound(await db.Collection("crapsPaidRounds").Document(id.ToString("N")).GetSnapshotAsync(), "alice");
        var audit = SinglePlayerRoundRecord.Create("craps", id.ToString("N"), "alice", round.TotalWagerCents, round.TotalReturnCents,
            round.StartedAtUtc, round.CompletedAtUtc!.Value, "crapsPaidRounds", id.ToString("N"), round, "craps-private-dice-and-bets-v1").Audit;
        var snapshots = await db.Collection(GameSecurityEventDocuments.CollectionName).WhereEqualTo("gameId", "craps").WhereEqualTo("instanceId", id.ToString("N")).GetSnapshotAsync();
        Assert.Empty(PaidCrapsReplayAudit.Inspect(audit.Summary, audit.PayloadJson, snapshots.Documents.Select(item => item.GetValue<string>("payloadJson")).ToArray()));
        Assert.Empty(await PaidCrapsReplayAudit.InspectFinancialAsync(db, audit.Summary, audit.PayloadJson, default));
        Assert.Single((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection("gameAnalyticsSettlementOutbox").GetSnapshotAsync()).Documents);
    }
    private static CrapsOperationRequest Roll(CrapsRoundResponse round) => new(Key(), round.Revision, round.Rolls.Count + 1);
    private static DicePair Dice(int first, int second) => new(new DieValue(first), new DieValue(second));
    private static string Key() => Guid.NewGuid().ToString("N");
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static FirestoreDb Database() => new FirestoreDbBuilder { ProjectId = "demo-craps-wallet-" + Key(), Endpoint = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") ?? "127.0.0.1:8085",
        ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None }.Build();
    private sealed class Wallet : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly Lock gate = new(); private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> receipts = [];
        public long Balance = 10000; public int Calls; public int Commits; public bool LoseNext;
        public Task<GameWalletAccountState> GetStateAsync(string account, CancellationToken token) => Task.FromResult(new GameWalletAccountState(0, Balance));
        public Task<GameWalletSettlementReceipt> SettleAsync(string account, GameWalletSettlementRequest request, CancellationToken token)
        {
            lock (gate)
            {
                Calls++;
                if (receipts.TryGetValue(account + request.OperationId, out var existing)) { Assert.Equal(existing.Request, request); return Task.FromResult(existing.Receipt); }
                var before = Balance; var declined = before < request.WagerCents;
                if (!declined) { Balance = before - request.WagerCents + request.PayoutCents; Commits++; }
                var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId("craps", account, request.OperationId),
                    declined || request.WagerCents == 0 ? null : "wager", declined || request.PayoutCents == 0 ? null : "payout", before,
                    declined ? before : before - request.WagerCents, Balance, DateTimeOffset.UtcNow, false, declined);
                receipts.Add(account + request.OperationId, (request, receipt));
                if (LoseNext) { LoseNext = false; throw new GameWalletUnavailableException(); }
                return Task.FromResult(receipt);
            }
        }
    }
}
