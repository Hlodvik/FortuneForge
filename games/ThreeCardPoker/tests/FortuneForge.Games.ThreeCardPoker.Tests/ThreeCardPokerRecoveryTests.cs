using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.ThreeCardPoker.Server;
using FortuneForge.Server.Bots.ThreeCardPoker;
using FortuneForge.Server.Cards.ThreeCardPoker.Table;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.ThreeCardPoker.Tests;

public sealed class ThreeCardPokerRecoveryTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private const string User = "alice";
    private const string JoinKey = "three_card_join_recovery_0001";
    private const string WagerKey = "three_card_wager_recovery_0001";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void MinimumOpeningFundsRespectGrossActionOrderRatherThanOnlyTheFinalNetDelta()
    {
        Assert.Equal(100, Minimum(-100, 500));
        Assert.Equal(0, Minimum(500, -100));
        Assert.Equal(50, Minimum(50, -100, 500));
        Assert.Equal(100, Minimum(-100, 100));
        Assert.Equal(200, Minimum(-100, -100, 700));
    }

    [Fact]
    public void FrozenWalletFundingRejectsAnUnreconciledGameJournalBeforeAnySettlement()
    {
        var ledger = new[] { new ThreeCardPokerTableLedgerEntry("wager", User, -100, 900, "test", "round", Now) };
        Assert.Throws<InvalidOperationException>(() => FirestoreThreeCardPokerTableStore.FundingFor(ledger,
            new Dictionary<string, long> { [User] = 1_000 }, new Dictionary<string, long> { [User] = 901 }));
    }

    [Fact]
    public void FrozenPublicRepliesRoundTripTheOriginalDerivedSessionAndVersion()
    {
        var table = Table();
        var state = State(table);
        var projected = ThreeCardPokerTableProjection.Session(state, User, Now);
        var reply = new ThreeCardPokerTableStoreResult(projected, 10_000);
        var restored = JsonSerializer.Deserialize<ThreeCardPokerTableStoreResult>(JsonSerializer.Serialize(reply, Json), Json)!;
        Assert.Equal(JsonSerializer.Serialize(reply, Json), JsonSerializer.Serialize(restored, Json));
        Assert.IsType<ThreeCardPokerTablePlaySessionResponse>(restored.Session);
        Assert.Equal(table.Version, restored.Session.Version);
    }

    [ThreeCardPokerEmulatorFact]
    public async Task LostWagerWalletReplyRecoversBeforePublicStateAndNeverRepeatsItsCharge()
    {
        var database = Connect();
        var wallet = new ThreeCardPokerTestWallet(database);
        var roster = new ThreeCardPokerTestRoster();
        await wallet.SetBalanceAsync(User, 10_000);
        var first = Build(database, wallet, roster);
        var joined = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await first.JoinAsync(User, "Saved Alice", 0, JoinKey, Now, default)).Session);
        wallet.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => first.WagerAsync(User, joined.Table.TableId, 500, 200, joined.Version, WagerKey, Now, default));
        Assert.Equal(9_300, await RealBalance(database));
        var privateBefore = await ReadTable(database, joined.Table.TableId);
        Assert.Equal(0, privateBefore.Players[0].NextWagerCents);
        Assert.Single((await database.Collection("threeCardPokerTablePendingAccounts").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("threeCardPokerTableLedger").GetSnapshotAsync()).Documents);

        var second = Build(database, wallet, roster);
        var recovered = await second.GetSessionAsync(User, Now, default);
        Assert.Equal(9_300, recovered.BalanceCents);
        Assert.Equal(500, (await ReadTable(database, joined.Table.TableId)).Players[0].NextWagerCents);
        var exact = await second.WagerAsync(User, joined.Table.TableId, 500, 200, joined.Version, WagerKey, Now, default);
        var originalJson = JsonSerializer.Serialize(exact, Json);
        await wallet.SetBalanceAsync(User, 42_000);
        await second.GetSessionAsync(User, Now.AddSeconds(2), default);
        var retry = await second.WagerAsync(User, joined.Table.TableId, 500, 200, joined.Version, WagerKey, Now.AddSeconds(3), default);
        Assert.Equal(originalJson, JsonSerializer.Serialize(retry, Json));
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("threeCardPokerTableLedger").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("threeCardPokerTablePendingAccounts").GetSnapshotAsync()).Documents);
    }

    [ThreeCardPokerEmulatorFact]
    public async Task StaleAccountBalanceDeclinesAtomicallyAndExactRetryRemainsDeclinedAfterTopUp()
    {
        var database = Connect();
        var wallet = new ThreeCardPokerTestWallet(database) { ReportedBalanceCents = 10_000 };
        var roster = new ThreeCardPokerTestRoster();
        await wallet.SetBalanceAsync(User, 0);
        var store = Build(database, wallet, roster);
        var joined = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.JoinAsync(User, "Saved Alice", 0, JoinKey, Now, default)).Session);
        await Assert.ThrowsAsync<ThreeCardPokerTableInsufficientCreditsException>(() => store.WagerAsync(User, joined.Table.TableId, 500, 200, joined.Version, WagerKey, Now, default));
        var before = await ReadTable(database, joined.Table.TableId);
        Assert.Equal(joined.Version, before.Version);
        Assert.Equal(0, before.Players[0].NextWagerCents);
        Assert.Empty(before.Players[0].Cards);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("threeCardPokerTablePendingAccounts").GetSnapshotAsync()).Documents);
        await wallet.SetBalanceAsync(User, 10_000);
        await Assert.ThrowsAsync<ThreeCardPokerTableInsufficientCreditsException>(() => store.WagerAsync(User, joined.Table.TableId, 500, 200, joined.Version, WagerKey, Now, default));
        Assert.Equal(1, wallet.Calls);
        await Assert.ThrowsAsync<ThreeCardPokerTableConflictException>(() => store.WagerAsync(User, joined.Table.TableId, 400, 200, joined.Version, WagerKey, Now, default));
    }

    [ThreeCardPokerEmulatorFact]
    public async Task LegacyRefundWithLostRosterReleaseRecoversAfterItsOriginalShardAndSessionWereRemoved()
    {
        var database = Connect();
        var wallet = new ThreeCardPokerTestWallet(database);
        var roster = new ThreeCardPokerTestRoster { LoseNextReleaseReply = true };
        await wallet.SetBalanceAsync(User, 9_500);
        var table = Table();
        table.Players[0].NextWagerCents = 500;
        table.Players.Add(new ThreeCardPokerTablePlayer
        {
            ActorId = "managed-legacy-player", PublicSeatId = "managed-seat", DisplayName = "Saved Guest", Seat = 1,
            SessionId = "legacy-managed-session", SessionStartedAtUtc = Now.AddMinutes(-5), Status = "awaiting-wager",
            HostMetadata = ThreeCardPokerManagedSeat.Create(ManagedPlayerSkillLevels.Average, 500, 600)
        });
        var stateId = await SeedState(database, table);
        var first = Build(database, wallet, roster);
        await Assert.ThrowsAsync<ManagedPlayerRosterUnavailableException>(() => first.GetSessionAsync(User, Now, default));
        Assert.Equal(10_000, await RealBalance(database));
        Assert.False((await database.Collection("threeCardPokerTableState").Document(stateId).GetSnapshotAsync()).Exists);
        Assert.Single((await database.Collection("threeCardPokerTablePendingAccounts").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("threeCardPokerTablePendingShards").GetSnapshotAsync()).Documents);
        var second = Build(database, wallet, roster);
        await second.SweepAsync(Now.AddSeconds(3), default);
        Assert.Equal(1, wallet.Calls);
        Assert.Equal(2, roster.Releases);
        Assert.Empty((await database.Collection("threeCardPokerTablePendingAccounts").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("threeCardPokerTablePendingShards").GetSnapshotAsync()).Documents);
        Assert.IsType<ThreeCardPokerTableIdleSessionResponse>((await second.GetSessionAsync(User, Now.AddSeconds(4), default)).Session);
        Assert.Empty(await second.GetHistoryAsync(User, 10, default));
    }

    [ThreeCardPokerEmulatorFact]
    public async Task ConcurrentDifferentJoinKeysCannotAdmitTheSameAccountTwice()
    {
        var database = Connect();
        var wallet = new ThreeCardPokerTestWallet(database);
        var roster = new ThreeCardPokerTestRoster();
        await wallet.SetBalanceAsync(User, 10_000);
        var first = Build(database, wallet, roster);
        var second = Build(database, wallet, roster);
        async Task<bool> Join(FirestoreThreeCardPokerTableStore store, string key)
        {
            try { await store.JoinAsync(User, "Saved Alice", 0, key, Now, default); return true; }
            catch (ThreeCardPokerTableConflictException) { return false; }
        }
        var outcomes = await Task.WhenAll(Join(first, JoinKey), Join(second, JoinKey + "_second"));
        Assert.Single(outcomes, success => success);
        Assert.Single((await database.Collection("threeCardPokerTables").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("threeCardPokerTableSessions").GetSnapshotAsync()).Documents);
        Assert.Equal(0, wallet.Calls);
        Assert.Equal(0, roster.Reservations);
    }

    [ThreeCardPokerEmulatorTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedLegacyHandCompletesWithoutRechargingAndProtectsAvailableOrArchivedOpeningEvidence(bool archived)
    {
        var database = Connect();
        var wallet = new ThreeCardPokerTestWallet(database);
        var roster = new ThreeCardPokerTestRoster();
        await wallet.SetBalanceAsync(User, 10_000);
        var store = Build(database, wallet, roster);
        var joined = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.JoinAsync(User, "Saved Alice", 0, JoinKey, Now, default)).Session);
        await store.WagerAsync(User, joined.Table.TableId, 100, 0, joined.Version, WagerKey, Now, default);
        var opening = Assert.Single((await database.Collection(ThreeCardPokerBackend.TableLedgerEvidence).GetSnapshotAsync()).Documents);
        if (!archived)
        {
            var legacy = opening.ToDictionary();
            legacy.Remove("walletMutationId");
            legacy.Remove("walletSettlementId");
            legacy.Remove("backendOwner");
            await database.Collection("balanceTransactions").Document("legacy-opening-wager").SetAsync(legacy);
        }
        await opening.Reference.DeleteAsync();
        var now = Now;
        ThreeCardPokerTablePlaySessionResponse session = joined;
        for (var step = 0; step < 40; step++)
        {
            now = now.AddSeconds(1);
            session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(User, now, default)).Session);
            if (session.Table.Phase == ThreeCardPokerTablePhases.Active && session.Table.Transition is null) break;
        }
        Assert.Equal(ThreeCardPokerTablePhases.Active, session.Table.Phase);
        Assert.Null(session.Table.Transition);
        await store.ActionAsync(User, joined.Table.TableId, "play", session.Version, WagerKey + "_play", now, default);
        for (var step = 0; step < 40; step++)
        {
            now = now.AddSeconds(1);
            session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(User, now, default)).Session);
            if (session.Table.Phase == ThreeCardPokerTablePhases.Settled) break;
        }
        Assert.Equal(ThreeCardPokerTablePhases.Settled, session.Table.Phase);
        Assert.Equal(3, wallet.Calls); // Opening wager, Play, then one payout; legacy opening is never charged again.
        Assert.Single(await store.GetHistoryAsync(User, 10, default));
        var evidence = Assert.Single((await database.Collection("gameRoundEvidence").GetSnapshotAsync()).Documents);
        using var capture = JsonDocument.Parse(evidence.GetValue<string>("payloadJson"));
        var proof = capture.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName);
        Assert.Equal(RoundAuditFinancialProof.MixedLegacyAndSignedWallet, proof.GetProperty("provenance").GetString());
        var entry = Assert.Single(proof.GetProperty("entries").EnumerateArray(), value => value.GetProperty("provenance").GetString()!.StartsWith("legacy-", StringComparison.Ordinal));
        Assert.Equal(-100, entry.GetProperty("amountCents").GetInt64());
        Assert.Equal(!archived, entry.GetProperty("evidenceAvailable").GetBoolean());
        Assert.Equal(archived ? ThreeCardPokerBackend.TableState : "balanceTransactions", entry.GetProperty("collection").GetString());
        if (!archived) Assert.Equal("legacy-opening-wager", entry.GetProperty("documentId").GetString());
        await store.GetSessionAsync(User, now, default);
        Assert.Equal(3, wallet.Calls);
    }

    private static long Minimum(params long[] amounts)
    {
        var ledger = amounts.Select((amount, index) => new ThreeCardPokerTableLedgerEntry(index.ToString(), User, amount, 0, "test", "round", Now));
        return Assert.Single(FirestoreThreeCardPokerTableStore.FundingFor(ledger,
            new Dictionary<string, long> { [User] = 100_000 }, new Dictionary<string, long> { [User] = 100_000 + amounts.Sum() })).MinimumBalanceBeforeCents;
    }
    private static FirestoreDb Connect() => new FirestoreDbBuilder
    { ProjectId = $"three-card-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
    private static FirestoreThreeCardPokerTableStore Build(FirestoreDb database, ThreeCardPokerTestWallet wallet, ThreeCardPokerTestRoster roster) =>
        new(database, wallet, wallet, roster, new ManagedTablePopulationDirector(), Deck, () => 3UL);
    private static async Task<long> RealBalance(FirestoreDb database)
    {
        var value = await database.Collection("userBalances").Document(User + "_slotsCredits").GetSnapshotAsync();
        return value.GetValue<long>("available") * 100 + value.GetValue<long>("availableFractionalCents");
    }
    private static async Task<ThreeCardPokerTableState> ReadTable(FirestoreDb database, string tableId)
    {
        var value = await database.Collection("threeCardPokerTables").Document(tableId).GetSnapshotAsync();
        return JsonSerializer.Deserialize<ThreeCardPokerTableState>(value.GetValue<string>("tableJson"), Json)!;
    }
    private static ThreeCardPokerTableState Table() => new()
    {
        TableId = ThreeCardPokerTableIds.Hash("legacy-table"), CreatedAtUtc = Now.AddMinutes(-5), UpdatedAtUtc = Now,
        Phase = ThreeCardPokerTablePhases.Betting, Version = 7, RoundNumber = 0, RoundAccountingSettled = true,
        ActiveSeat = 0, PendingSeat = 0, Transition = "human-wager",
        Players = [new ThreeCardPokerTablePlayer
        { ActorId = User, PublicSeatId = "saved-alice-seat", DisplayName = "Saved Alice", Seat = 0,
            SessionId = "saved-session", SessionStartedAtUtc = Now.AddMinutes(-5), Status = "awaiting-wager" }]
    };
    private static ThreeCardPokerTableLobbyState State(ThreeCardPokerTableState table) => new()
    {
        Sessions = new(StringComparer.Ordinal) { [User] = new(ThreeCardPokerTableSessionKinds.Table, null, table.TableId) },
        Tables = new(StringComparer.Ordinal) { [table.TableId] = table }
    };
    private static async Task<string> SeedState(FirestoreDb database, ThreeCardPokerTableState table)
    {
        var stateId = ThreeCardPokerTableIds.Hash("legacy-shard");
        await database.Collection("threeCardPokerTableState").Document(stateId).CreateAsync(new Dictionary<string, object>
        { ["stateJson"] = JsonSerializer.Serialize(State(table), Json), ["schemaVersion"] = 2L });
        await database.Collection("threeCardPokerTables").Document(table.TableId).CreateAsync(new Dictionary<string, object>
        { ["tableJson"] = JsonSerializer.Serialize(table, Json), ["stateId"] = stateId, ["schemaVersion"] = 2L });
        await database.Collection("threeCardPokerTableSessions").Document(ThreeCardPokerTableIds.Hash(User)).CreateAsync(new Dictionary<string, object>
        { ["stateId"] = stateId, ["schemaVersion"] = 2L });
        return stateId;
    }
    private static IReadOnlyList<string> Deck() => new[] { "clubs", "diamonds", "hearts", "spades" }.SelectMany(suit =>
        new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" }.Select(rank => $"{rank}|{suit}")).ToArray();
}
