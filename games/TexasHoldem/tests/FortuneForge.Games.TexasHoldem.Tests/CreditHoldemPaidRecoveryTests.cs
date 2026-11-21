using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using System.Net.Http.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.RoundAudit;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Games.TexasHoldem.Tests;

public sealed class CreditHoldemPaidRecoveryTests
{
    private static readonly DateTime Start = new(2026, 11, 21, 6, 0, 0, DateTimeKind.Utc);
    private static string Key(string label) => $"holdem_paid_{label}_operation";

    [TexasPaidEmulatorFact]
    public async Task FinalizedJoinReplyBypassesLaterPendingRefundAndOfflineWallet()
    {
        var database = Database(); var bank = new Bank(); bank.Set("alice", 10_000);
        var store = Store(database, bank);
        var original = await store.JoinAsync("alice", "Alice", 0, Key("cached_join"), 42, Start, default, buyInCents: 2_500);
        var queue = Assert.IsType<CreditHoldemQueueSessionResponse>(original.Session);
        bank.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.CancelAsync("alice", queue.TicketId,
            queue.Version, Key("pending_refund"), Start, default));
        bank.FailSettlements = true; bank.FailProfileReads = true;
        var replay = await Store(database, bank).JoinAsync("alice", "", 0, Key("cached_join"), 99, Start.AddHours(1), default, buyInCents: 2_500);
        Assert.Equal(Json(original.Session), Json(replay.Session));
        Assert.Equal(original.BalanceCents, replay.BalanceCents);
        Assert.NotEmpty((await database.Collection(CreditHoldemBackend.Locks).GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Store(database, bank).GetSessionAsync("alice", Start, default));
        bank.FailSettlements = false; bank.FailProfileReads = false;
        Assert.IsType<CreditHoldemIdleSessionResponse>((await Store(database, bank).GetSessionAsync("alice", Start, default)).Session);
        Assert.Equal(2, bank.Approvals);
    }

    [TexasPaidEmulatorFact]
    public async Task LostBuyInAndRefundRepliesRecoverAcrossInstancesAndReplayOriginalResponses()
    {
        var database = Database();
        var bank = new Bank();
        bank.Set("alice", 10_000);
        bank.LoseNextReply = true;
        var store = Store(database, bank);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() =>
            store.JoinAsync("alice", "Alice Display", 0, Key("join"), 42, Start, default, buyInCents: 2_500));
        Assert.Equal(7_500, bank.Balance("alice"));
        Assert.Empty((await database.Collection("creditHoldemSessions").GetSnapshotAsync()).Documents);
        Assert.NotEmpty((await database.Collection(CreditHoldemBackend.Locks).GetSnapshotAsync()).Documents);
        await database.Document("adminRuntimeConfiguration/current").SetAsync(new Dictionary<string, object>
        { ["botsEnabled"] = false, ["gameBots"] = new Dictionary<string, object> { [CreditHoldemBackend.GameId] = false } });
        var after = await Store(database, bank).GetSessionAsync("alice", Start, default);
        var queue = Assert.IsType<CreditHoldemQueueSessionResponse>(after.Session);
        Assert.Equal("Alice Display", Assert.Single(queue.Players).DisplayName);
        Assert.Equal(7_500, after.BalanceCents);
        Assert.Equal(1, bank.Approvals);
        Assert.Empty((await database.Collection(CreditHoldemBackend.Locks).GetSnapshotAsync()).Documents);
        var firstReply = await store.JoinAsync("alice", "Ignored Retry Name", 0, Key("join"), 99, Start.AddSeconds(1), default, buyInCents: 2_500);
        bank.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.CancelAsync("alice", queue.TicketId,
            queue.Version, Key("cancel"), Start.AddSeconds(1), default));
        Assert.Equal(10_000, bank.Balance("alice"));
        Assert.IsType<CreditHoldemIdleSessionResponse>((await Store(database, bank).GetSessionAsync("alice", Start.AddSeconds(1), default)).Session);
        var calls = bank.Calls;
        var replay = await store.JoinAsync("alice", "Another Name", 0, Key("join"), 123, Start.AddHours(1), default, buyInCents: 2_500);
        Assert.Equal(Json(firstReply.Session), Json(replay.Session));
        Assert.Equal(firstReply.BalanceCents, replay.BalanceCents);
        Assert.Equal(calls, bank.Calls);
        Assert.Equal(2, (await database.Collection(CreditHoldemBackend.WalletJournal).GetSnapshotAsync()).Count);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [TexasPaidEmulatorFact]
    public async Task LegacyNextHandBlindAdmissionDeclinesEveryAccountAtomicallyAndFreezesTheDecline()
    {
        var database = Database();
        var bank = new Bank();
        var legacy = await SeedLegacy(database, bank, complete: true);
        string? shortAccount = null;
        bank.BeforeNextBatch = request =>
        {
            var debits = request.Effects.Where(value => value.WagerCents > 0).ToArray();
            Assert.True(debits.Length >= 2);
            shortAccount = debits[^1].AccountId;
            bank.Set(shortAccount, 0);
        };
        var before = bank.Snapshot();
        var store = Store(database, bank);
        await Assert.ThrowsAsync<CreditHoldemInsufficientCreditsException>(() => store.NextHandAsync("alice", legacy.MatchId,
            legacy.Version, Key("legacy_next_declined"), 123, Start.AddSeconds(2), default));
        foreach (var account in before.Keys.Where(value => value != shortAccount)) Assert.Equal(before[account], bank.Balance(account));
        Assert.Equal(0, bank.Balance(shortAccount!));
        Assert.Equal(1, ReadMatch(await database.Collection("creditHoldemMatches").Document(legacy.MatchId).GetSnapshotAsync()).HandNumber);
        Assert.Empty((await database.Collection(CreditHoldemBackend.WalletJournal).GetSnapshotAsync()).Documents);
        bank.Set(shortAccount!, 10_000);
        var calls = bank.Calls;
        await Assert.ThrowsAsync<CreditHoldemInsufficientCreditsException>(() => Store(database, bank).NextHandAsync("alice", legacy.MatchId,
            legacy.Version, Key("legacy_next_declined"), 456, Start.AddSeconds(2), default));
        Assert.Equal(calls, bank.Calls);
        var accepted = await store.NextHandAsync("alice", legacy.MatchId, legacy.Version, Key("legacy_next_funded"), 123,
            Start.AddSeconds(2), default);
        Assert.Equal(2, Assert.IsType<CreditHoldemMatchSessionResponse>(accepted.Session).Table.HandNumber);
        Assert.True(bank.Last!.Effects.Count(value => value.WagerCents > 0) >= 2);
    }

    [TexasPaidEmulatorFact]
    public async Task LegacyActionPayoutReplyLossRecoversFrozenHandHistoryAuditAndWalletOnce()
    {
        var database = Database();
        var bank = new Bank();
        var legacy = await SeedLegacy(database, bank, complete: false);
        var store = Store(database, bank);
        var current = legacy;
        string finalActor = "";
        string finalKey = "";
        var finalVersion = 0;
        while (current.Status == "active")
        {
            var actor = current.Players.Single(value => value.Seat == current.ActiveSeat).ActorId;
            var final = current.Players.Count(value => value.Status == "active") == 2;
            var key = Key($"fold_{current.Version}");
            if (final)
            {
                bank.LoseNextReply = true;
                finalActor = actor; finalKey = key; finalVersion = current.Version;
                await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.ActionAsync(actor, current.MatchId,
                    new("fold", current.Version), key, Start.AddSeconds(1), default));
                break;
            }
            await store.ActionAsync(actor, current.MatchId, new("fold", current.Version), key, Start.AddSeconds(1), default);
            current = ReadMatch(await database.Collection("creditHoldemMatches").Document(current.MatchId).GetSnapshotAsync());
        }
        Assert.NotEmpty(finalActor);
        Assert.Empty((await database.Collection("creditHoldemMatchRevenue").GetSnapshotAsync()).Documents);
        var approvals = bank.Approvals;
        var archived = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents
            .Select(value => (value.Reference, Fields: value.ToDictionary())).ToArray();
        foreach (var original in archived) await original.Reference.DeleteAsync();
        var recovered = await Store(database, bank).GetSessionAsync(finalActor, Start.AddSeconds(1), default);
        Assert.IsType<CreditHoldemResultSessionResponse>(recovered.Session);
        foreach (var original in archived) await original.Reference.SetAsync(original.Fields);
        var replay = await store.ActionAsync(finalActor, legacy.MatchId, new("fold", finalVersion), finalKey, Start.AddHours(1), default);
        Assert.Equal(Json(recovered.Session), Json(replay.Session));
        Assert.Equal(approvals, bank.Approvals);
        var settled = ReadMatch(await database.Collection("creditHoldemMatches").Document(legacy.MatchId).GetSnapshotAsync());
        Assert.True(settled.AccountingSettled);
        Assert.Single((await database.Collection("creditHoldemMatchRevenue").GetSnapshotAsync()).Documents);
        Assert.Equal(3, (await database.Collection("cardGameResults").GetSnapshotAsync()).Count);
        var history = await store.HistoryAsync(finalActor, 50, default);
        var item = Assert.Single(history.Items);
        Assert.Equal("completed", item.Status);
        Assert.True((await store.MarkHistorySeenAsync(finalActor, item.EventId, Start.AddSeconds(2), default)).Seen);
        var findings = await HoldemFinancialAudit.InspectAsync(database, HoldemRoundAudit.Capture(settled, Start.AddSeconds(1)).Summary, default);
        Assert.DoesNotContain(findings, finding => finding.Severity == "error");
        Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        var protectedEvidence = Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var protectedJson = protectedEvidence.GetValue<string>("payloadJson");
        using (var payload = JsonDocument.Parse(protectedJson))
        {
            var proof = payload.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName);
            Assert.Equal(CreditHoldemBackend.Owner, proof.GetProperty("backendOwner").GetString());
            Assert.Equal(RoundAuditFinancialProof.MixedLegacyAndSignedWallet, proof.GetProperty("provenance").GetString());
            var signed = Assert.Single(proof.GetProperty("walletOperations").EnumerateArray(), operation =>
                operation.GetProperty("batchReceipt").GetProperty("accounts").EnumerateArray().Any(account =>
                    account.TryGetProperty("payoutTransactionId", out var payout) && payout.ValueKind == JsonValueKind.String));
            Assert.NotEmpty(signed.GetProperty("batchRequest").GetProperty("effects").EnumerateArray());
            Assert.Contains(proof.GetProperty("entries").EnumerateArray(), entry =>
                entry.GetProperty("provenance").GetString() == RoundAuditFinancialProof.LegacyHost);
        }
        foreach (var original in (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents)
            await original.Reference.DeleteAsync();
        Assert.Equal(protectedJson, (await protectedEvidence.Reference.GetSnapshotAsync()).GetValue<string>("payloadJson"));
        Assert.Equal(GameSecurityEventDocuments.Hash(protectedJson), protectedEvidence.GetValue<string>("sha256"));
        Assert.Empty((await database.Collection(CreditHoldemBackend.Locks).GetSnapshotAsync()).Documents);
    }

    [TexasPaidEmulatorFact]
    public async Task LegacyCommandGuardImportsAResponseWithoutRechargingOrChangingHistoricalRules()
    {
        var database = Database();
        var bank = new Bank();
        var legacy = await SeedLegacy(database, bank, complete: false);
        var actor = legacy.Players.Single(value => value.Seat == legacy.ActiveSeat).ActorId;
        var key = Key("old_guard");
        await database.Collection("creditHoldemCommandGuards").Document(CreditHoldemIds.Hash($"{actor}\n{key}"))
            .CreateAsync(new Dictionary<string, object>
            {
                ["userId"] = actor, ["operation"] = "action", ["target"] = legacy.MatchId,
                ["detail"] = $"fold:{legacy.Version}:", ["createdAt"] = Timestamp.FromDateTime(Start)
            });
        var first = await Store(database, bank).ActionAsync(actor, legacy.MatchId, new("fold", legacy.Version), key, Start, default);
        var replay = await Store(database, bank).ActionAsync(actor, legacy.MatchId, new("fold", legacy.Version), key, Start.AddHours(1), default);
        Assert.Equal(Json(first.Session), Json(replay.Session));
        Assert.Equal(0, bank.Calls);
        Assert.Equal(legacy.DealSeed, ReadMatch(await database.Collection("creditHoldemMatches").Document(legacy.MatchId).GetSnapshotAsync()).DealSeed);
    }

    [Fact]
    public void NetWalletEffectsRetainTheMinimumOriginalPrefixBalanceForEachAccount()
    {
        var plan = new CreditHoldemMutationPlan("prefix-proof", "alice", Start, [], [],
            [new("alice", -100, "a-debit", "wager", "source", Start), new("alice", 150, "a-payout", "payout", "source", Start),
             new("bob", 150, "b-payout", "payout", "source", Start), new("bob", -100, "b-debit", "wager", "source", Start)], [], [], [], null);
        var request = plan.WalletRequest();
        Assert.True(GameWalletBatchVerifier.IsValidRequest(request));
        Assert.Equal(new GameWalletBatchEffect("alice", 0, 50, 100), request.Effects[0]);
        Assert.Equal(new GameWalletBatchEffect("bob", 0, 50, 0), request.Effects[1]);
    }

    [Fact]
    public void FrozenQueueReplyRetainsTheInheritedOptimisticVersionAndPublicShape()
    {
        CreditHoldemSessionResponse session = new CreditHoldemQueueSessionResponse(new string('a', 64), 1,
            Start, Start.AddSeconds(3), [], 17);
        var json = Json(session);
        var restored = CreditHoldemMutation.RestoreReply(new(session.Kind, json, 123));
        Assert.Equal(17, restored.Session.Version);
        Assert.Equal(json, Json(restored.Session));
        Assert.Equal(123, restored.BalanceCents);
    }

    [Fact]
    public async Task OriginalCreditUrlsRequireTheVerifiedGatewayAccount()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapCreditHoldem(_ => null);
        await app.StartAsync();
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(CreditHoldemBackend.ApiPath + "/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(CreditHoldemBackend.ApiPath + "/session")).StatusCode);
        using var cancel = new HttpRequestMessage(HttpMethod.Delete, CreditHoldemBackend.ApiPath + "/queue/" + new string('a', 64))
        {
            Content = JsonContent.Create(new { expectedVersion = 1 })
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(cancel)).StatusCode);
    }

    [TexasPaidEmulatorFact]
    public async Task WorkerUsesTrustedDisplayNameAndPreservesDeleteBodiesAndPausedRetries()
    {
        var database = Database();
        var bank = new Bank(); bank.Set("alice", 10_000);
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Configuration[CreditHoldemBackend.EnabledSetting] = "true";
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<IGameWalletBatchClient>(bank);
        builder.Services.AddSingleton<IGameWalletAccountReader>(bank);
        builder.Services.AddSingleton<IManagedPlayerRosterClient>(new Roster());
        builder.Services.AddSingleton<IManagedTablePopulationDirector, ManagedTablePopulationDirector>();
        await using var app = builder.Build(); app.MapCreditHoldem(_ => "alice"); await app.StartAsync();
        var client = app.GetTestClient();
        async Task<HttpResponseMessage> Join(string key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, CreditHoldemBackend.ApiPath + "/queue")
            {
                Content = JsonContent.Create(new { expectedVersion = 0, buyInCredits = 25, tableRuleId = "standard" })
            };
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }
        var joined = await Join(Key("http_join"));
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        using var body = JsonDocument.Parse(await joined.Content.ReadAsStringAsync());
        var session = body.RootElement.GetProperty("session");
        Assert.Equal("Display alice", session.GetProperty("players")[0].GetProperty("displayName").GetString());
        using var cancel = new HttpRequestMessage(HttpMethod.Delete, CreditHoldemBackend.ApiPath + "/queue/" + session.GetProperty("ticketId").GetString())
        {
            Content = JsonContent.Create(new { expectedVersion = session.GetProperty("version").GetInt32() })
        };
        cancel.Headers.Add("Idempotency-Key", Key("http_cancel"));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(cancel)).StatusCode);
        Assert.Equal(10_000, bank.Balance("alice"));
        app.Configuration[CreditHoldemBackend.EnabledSetting] = "false";
        Assert.Equal(HttpStatusCode.OK, (await Join(Key("http_join"))).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Join(Key("http_paused_new"))).StatusCode);
    }

    private static FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = $"holdem-paid-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();
    private static FirestoreCreditHoldemStore Store(FirestoreDb database, Bank bank) => new(database, false, bank, bank, new Roster());
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, CreditHoldemMutation.Json);
    private static CreditHoldemMatch ReadMatch(DocumentSnapshot snapshot) => CreditHoldemMatchPersistence.Deserialize(snapshot.GetValue<string>("matchJson"));

    private static async Task<CreditHoldemMatch> SeedLegacy(FirestoreDb database, Bank bank, bool complete)
    {
        var id = new string('b', 64);
        var people = new[] { "alice", "bob", "carol" };
        var seats = people.Select((account, seat) => new CreditHoldemSeatAssignment(account, $"seat-{account}",
            $"Display {account}", true, seat, 1_000)).ToArray();
        var match = CreditHoldemEngine.Deal(id, seats, "historical-per-hand-partition", 42, Start, usesBuyInEscrow: false);
        foreach (var player in match.Players)
        {
            bank.Set(player.ActorId, 10_000 - player.CommittedHand);
            if (player.CommittedHand > 0)
                await database.Collection("balanceTransactions").Document($"{id}-hand-1-blind-{CreditHoldemIds.Hash(player.ActorId)}-{match.Version}")
                    .CreateAsync(new Dictionary<string, object> { ["userId"] = player.ActorId, ["currencyId"] = "slotsCredits", ["type"] = "texas-holdem-blind", ["amount"] = -player.CommittedHand / 100d });
        }
        if (complete)
        {
            CreditHoldemEngine.ForceComplete(match, Start.AddSeconds(1));
            var settlement = CreditHoldemEngine.ApplyFinancialSettlement(match, Start.AddSeconds(1));
            foreach (var payout in settlement.HumanPayoutsCents) bank.Set(payout.Key, bank.Balance(payout.Key) + payout.Value);
        }
        var node = JsonSerializer.SerializeToNode(match, CreditHoldemMutation.Json)!.AsObject();
        foreach (var player in node["players"]!.AsArray().OfType<JsonObject>())
        {
            player["isBot"] = !player["isAccountBacked"]!.GetValue<bool>();
            player.Remove("isAccountBacked");
        }
        await database.Collection("creditHoldemMatches").Document(id).CreateAsync(new Dictionary<string, object>
        {
            ["matchJson"] = node.ToJsonString(), ["matchId"] = id, ["status"] = match.Status,
            ["partitionKey"] = match.PartitionKey, ["acceptingHumans"] = true,
            ["nextDeadlineAt"] = Timestamp.FromDateTime(Start.AddSeconds(30))
        });
        foreach (var account in people)
            await database.Collection("creditHoldemSessions").Document(CreditHoldemIds.Hash(account)).CreateAsync(new Dictionary<string, object>
            {
                ["userId"] = account, ["kind"] = complete ? "result" : "match", ["matchId"] = id, ["version"] = (long)match.Version
            });
        return match;
    }

    private sealed class Roster : IManagedPlayerRosterClient
    {
        public Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveAsync(string assignmentId, int count, IReadOnlyCollection<string> excludedProfileIds,
            DateTime nowUtc, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ManagedPlayerRosterProfile>>(
            Enumerable.Range(0, count).Select(index => new ManagedPlayerRosterProfile($"managed-{index}", $"House {index}", 1)).ToArray());
        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Bank : IGameWalletBatchClient, IGameWalletAccountReader
    {
        private readonly Dictionary<string, long> balances = [];
        private readonly Dictionary<string, (GameWalletBatchRequest Request, GameWalletBatchReceipt Receipt)> receipts = [];
        public bool LoseNextReply { get; set; }
        public bool FailSettlements { get; set; }
        public bool FailProfileReads { get; set; }
        public Action<GameWalletBatchRequest>? BeforeNextBatch { get; set; }
        public int Calls { get; private set; }
        public int Approvals { get; private set; }
        public GameWalletBatchRequest? Last { get; private set; }
        public long Balance(string account) { lock (receipts) return balances.GetValueOrDefault(account); }
        public void Set(string account, long value) { lock (receipts) balances[account] = value; }
        public Dictionary<string, long> Snapshot() { lock (receipts) return new(balances); }
        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken) => FailProfileReads
            ? Task.FromException<GameWalletAccountState>(new GameWalletUnavailableException())
            : Task.FromResult(new GameWalletAccountState(0, Balance(accountId), $"Display {accountId}"));
        public Task<GameWalletBatchReceipt> SettleBatchAsync(string actorAccountId, GameWalletBatchRequest request, CancellationToken cancellationToken)
        {
            lock (receipts)
            {
                if (FailSettlements) throw new GameWalletUnavailableException();
                Calls++; Last = request;
                if (receipts.TryGetValue(request.OperationId, out var prior))
                {
                    Assert.Equal(Json(prior.Request), Json(request));
                    return Task.FromResult(prior.Receipt with { WasAlreadySettled = true });
                }
                var interleave = BeforeNextBatch; BeforeNextBatch = null; interleave?.Invoke(request);
                var declined = request.Effects.Any(effect => Balance(effect.AccountId) < GameWalletBalanceRequirement.RequiredCents(effect.WagerCents, effect.MinimumBalanceBeforeCents));
                var id = GameWalletBatchIdentity.CreateId(CreditHoldemBackend.GameId, request.OperationId);
                var entries = request.Effects.Select(effect =>
                {
                    var before = Balance(effect.AccountId);
                    var after = declined ? before : checked(before - effect.WagerCents + effect.PayoutCents);
                    balances[effect.AccountId] = after;
                    var prefix = GameWalletBatchIdentity.AccountLedgerPrefix(id, effect.AccountId);
                    return new GameWalletBatchAccountReceipt(effect.AccountId, before, after,
                        before < GameWalletBalanceRequirement.RequiredCents(effect.WagerCents, effect.MinimumBalanceBeforeCents),
                        !declined && effect.WagerCents > 0 ? $"{prefix}-wager" : null,
                        !declined && effect.PayoutCents > 0 ? $"{prefix}-payout" : null);
                }).ToArray();
                var receipt = new GameWalletBatchReceipt(id, actorAccountId, entries, DateTimeOffset.UtcNow, false, declined);
                receipts.Add(request.OperationId, (request, receipt));
                if (!declined) Approvals++;
                if (LoseNextReply) { LoseNextReply = false; throw new GameWalletUnavailableException(); }
                return Task.FromResult(receipt);
            }
        }
    }
}

public sealed class TexasPaidEmulatorFactAttribute : FactAttribute
{
    public TexasPaidEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A localhost Firestore emulator is required for Texas Hold'em paid recovery checks.";
    }
}
