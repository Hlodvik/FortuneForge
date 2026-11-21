using System.Text.Json;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Bots.Blackjack;
using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Cards.Blackjack.Table;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.Blackjack.Tests;

[Collection("Blackjack table Firestore emulator")]
public sealed class BlackjackTableWalletRecoveryTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Lost_join_response_is_discovered_by_account_before_session_route_exists()
    {
        var db = Database(); var wallet = new Wallet { LoseNextReply = true }; var store = Store(db, wallet);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.JoinAsync("alice", "Alice", 0, "join-key", Now, default));
        Assert.Empty((await db.Collection(BlackjackBackend.Sessions).GetSnapshotAsync()).Documents);
        var recovered = await Store(db, wallet).GetSessionAsync("alice", Now, default);
        var table = Assert.IsType<BlackjackTablePlaySessionResponse>(recovered.Session);
        Assert.Equal("Alice", Assert.Single(table.Table.Seats).DisplayName);
        var replay = await store.JoinAsync("alice", "Alice", 0, "join-key", Now.AddHours(1), default);
        Assert.Equal(Serialize(recovered), Serialize(replay));
        Assert.Equal(0, wallet.Charges);
        Assert.Empty((await db.Collection(BlackjackBackend.PendingAccounts).GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection(BlackjackBackend.PendingShards).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Lost_wager_response_retains_exact_reply_and_commits_one_charge_and_one_game_ledger()
    {
        var db = Database(); var wallet = new Wallet(); var store = Store(db, wallet);
        var joined = Play(await store.JoinAsync("alice", "Alice", 0, "join-key", Now, default));
        wallet.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.WagerAsync("alice", joined.Table.TableId, 100,
            joined.Version, "wager-key", Now.AddSeconds(1), default));
        wallet.Unavailable = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Store(db, wallet).WagerAsync("alice", joined.Table.TableId, 200,
            joined.Version, "different-key", Now.AddSeconds(2), default));
        wallet.Unavailable = false;
        var recovered = await Store(db, wallet).GetSessionAsync("alice", Now.AddSeconds(2), default);
        var retries = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.WagerAsync("alice", joined.Table.TableId, 100,
            joined.Version, "wager-key", Now.AddHours(1), default)));
        Assert.All(retries, result => Assert.Equal(Serialize(retries[0]), Serialize(result)));
        Assert.Equal(9900, retries[0].BalanceCents);
        Assert.Equal(1, wallet.Charges);
        var entry = Assert.Single((await db.Collection(BlackjackBackend.TableLedgerEvidence).GetSnapshotAsync()).Documents);
        Assert.Equal("blackjack-table-wager", entry.GetValue<string>("type"));
        Assert.NotEmpty(entry.GetValue<string>("walletSettlementId"));
        Assert.True(Play(recovered).Version >= Play(retries[0]).Version);
        Assert.Empty((await db.Collection("userBalances").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<BlackjackTableConflictException>(() => store.WagerAsync("alice", joined.Table.TableId, 200,
            joined.Version, "wager-key", Now, default));
    }

    [Fact]
    public async Task Lost_payout_reply_recovers_after_restart_with_one_history_audit_and_award()
    {
        var db = Database(); var wallet = new Wallet(); var store = Store(db, wallet);
        var joined = Play(await store.JoinAsync("alice", "Alice", 0, "join-key", Now, default));
        await store.WagerAsync("alice", joined.Table.TableId, 100, joined.Version, "wager-key", Now.AddSeconds(1), default);
        var active = await WaitFor(store, Now.AddSeconds(2), session => session.Table.Phase == BlackjackTablePhases.Active && session.Table.Transition is null);
        await store.ActionAsync("alice", joined.Table.TableId, "stand", active.Version, "stand-key", Now.AddMinutes(1), default);
        wallet.LoseNextCreditReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => WaitFor(store, Now.AddMinutes(1).AddSeconds(1),
            session => session.Table.Phase == "settlement" && session.Table.Transition is null));
        var resumed = Store(db, wallet);
        await WaitFor(resumed, Now.AddMinutes(2), session => session.Table.Phase == "settlement" && session.Table.Transition is null);
        Assert.Equal(10100, wallet.Balance);
        Assert.Equal(1, wallet.Charges); Assert.Equal(1, wallet.Credits);
        var history = Assert.Single(await resumed.GetHistoryAsync("alice", 20, default));
        Assert.Equal((1m, 2m), (history.WagerCredits, history.PayoutCredits));
        Assert.Single((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        var evidence = Assert.Single((await db.Collection("gameRoundEvidence").GetSnapshotAsync()).Documents);
        await AssertFinancialProof(db, evidence, "blackjack-table-wager", "blackjack-table-payout");
        Assert.Single((await db.Collection("gameAnalyticsSettlementOutbox").GetSnapshotAsync()).Documents);
        Assert.True((await resumed.MarkHistorySeenAsync("alice", history.ResultId, Now.AddMinutes(3), default)).Seen);
        await Assert.ThrowsAsync<BlackjackTableNotFoundException>(() => resumed.MarkHistorySeenAsync("bob", history.ResultId, Now, default));
    }

    [Fact]
    public async Task Wallet_decline_after_stale_balance_read_keeps_original_hand_and_immutable_decline()
    {
        var db = Database(); var wallet = new Wallet(); var store = Store(db, wallet);
        var joined = Play(await store.JoinAsync("alice", "Alice", 0, "join-key", Now, default));
        wallet.Balance = 50; wallet.ReportedBalance = 10000;
        await Assert.ThrowsAsync<BlackjackTableInsufficientCreditsException>(() => store.WagerAsync("alice", joined.Table.TableId,
            100, joined.Version, "declined-key", Now.AddSeconds(1), default));
        wallet.Balance = 5000; wallet.ReportedBalance = null;
        await Assert.ThrowsAsync<BlackjackTableInsufficientCreditsException>(() => store.WagerAsync("alice", joined.Table.TableId,
            100, joined.Version, "declined-key", Now.AddSeconds(2), default));
        var unchanged = Play(await store.GetSessionAsync("alice", Now.AddSeconds(2), default));
        Assert.Equal(joined.Version, unchanged.Version);
        Assert.Equal("human-wager", unchanged.Table.Transition);
        await store.WagerAsync("alice", joined.Table.TableId, 100, unchanged.Version, "new-key", Now.AddSeconds(3), default);
        Assert.Equal(4900, wallet.Balance);
        Assert.Equal(1, wallet.Charges);
    }

    [Fact]
    public async Task Account_lock_prevents_two_concurrent_idle_joins_from_creating_two_tables()
    {
        var db = Database(); var wallet = new Wallet();
        async Task<BlackjackTableStoreResult?> Attempt(string key)
        {
            try { return await Store(db, wallet).JoinAsync("alice", "Alice", 0, key, Now, default); }
            catch (BlackjackTableConflictException) { return null; }
        }
        var joins = await Task.WhenAll(Attempt("first-join"), Attempt("second-join"));
        var accepted = Assert.Single(joins, result => result is not null)!;
        Assert.Single(joins, result => result is null);
        Assert.Single((await db.Collection(BlackjackBackend.Tables).GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<BlackjackTableNotFoundException>(() => Store(db, wallet).ActionAsync("bob",
            Play(accepted).Table.TableId, "stand", Play(accepted).Version, "stolen-key", Now, default));
        Assert.Equal(0, wallet.Charges);
    }

    [Fact]
    public async Task Historical_prepaid_table_finishes_without_recharging_its_old_wager()
    {
        var db = Database(); var wallet = new Wallet { Balance = 9900 };
        var memory = new InMemoryBlackjackTableStore(Deck, () => 1931UL, multiplayerEnabled: false);
        memory.SetBalance("alice", 10000);
        var joined = Play(await memory.JoinAsync("alice", "Alice", 0, "legacy-join", Now, default));
        await memory.WagerAsync("alice", joined.Table.TableId, 100, joined.Version, "legacy-wager", Now.AddSeconds(1), default);
        var stateId = BlackjackTableIds.Hash("historical-state");
        var state = memory.StateForTest;
        await db.Collection(BlackjackBackend.TableState).Document(stateId).SetAsync(new Dictionary<string, object>
        { ["stateJson"] = JsonSerializer.Serialize(state, Json), ["schemaVersion"] = 1L });
        await db.Collection(BlackjackBackend.Sessions).Document(BlackjackTableIds.Hash("alice")).SetAsync(new Dictionary<string, object>
        { ["stateId"] = stateId, ["kind"] = "table", ["tableId"] = joined.Table.TableId });
        await db.Collection(BlackjackBackend.Tables).Document(joined.Table.TableId).SetAsync(new Dictionary<string, object>
        { ["stateId"] = stateId, ["tableJson"] = JsonSerializer.Serialize(state.Tables[joined.Table.TableId], Json) });
        var store = Store(db, wallet);
        var active = await WaitFor(store, Now.AddSeconds(2), session => session.Table.Phase == BlackjackTablePhases.Active && session.Table.Transition is null);
        await store.ActionAsync("alice", joined.Table.TableId, "stand", active.Version, "finish-old", Now.AddMinutes(1), default);
        await WaitFor(store, Now.AddMinutes(2), session => session.Table.Phase == "settlement" && session.Table.Transition is null);
        Assert.Equal(10100, wallet.Balance); Assert.Equal(0, wallet.Charges); Assert.Equal(1, wallet.Credits);
        Assert.Single(await store.GetHistoryAsync("alice", 20, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_preserves_each_split_double_and_insurance_ledger_anchor(bool insurance)
    {
        var db = Database(); var wallet = new Wallet();
        string[] prefix = insurance ? ["9|spades", "A|hearts", "7|diamonds", "K|spades"]
            : ["8|spades", "6|hearts", "8|diamonds", "10|spades", "3|diamonds", "2|hearts", "10|clubs", "9|clubs"];
        IReadOnlyList<string> ActionDeck() => prefix.Concat(BlackjackRules.CreateShuffledDeck()
            .Where(card => !prefix.Contains(card)).Order(StringComparer.Ordinal)).ToArray();
        var store = new FirestoreBlackjackTableStore(db, wallet, wallet, new Roster(), ActionDeck, () => 1931UL, multiplayerEnabled: false);
        var joined = Play(await store.JoinAsync("alice", "Alice", 0, "join-key", Now, default));
        await store.WagerAsync("alice", joined.Table.TableId, 100, joined.Version, "wager-key", Now.AddSeconds(1), default);
        var active = await WaitFor(store, Now.AddSeconds(2), session => session.Table.Transition is null &&
            session.Table.Phase == (insurance ? BlackjackTablePhases.Insurance : BlackjackTablePhases.Active));
        await store.ActionAsync("alice", joined.Table.TableId, insurance ? "insurance" : "split", active.Version,
            "first-action", Now.AddMinutes(1), default);
        if (!insurance)
        {
            for (var hand = 0; hand < 2; hand++)
            {
                var ready = await WaitFor(store, Now.AddMinutes(1).AddSeconds(hand + 1), session =>
                    session.Table.Transition is null && session.Table.LegalActions.Contains("double"));
                await store.ActionAsync("alice", joined.Table.TableId, "double", ready.Version,
                    "double-" + hand, Now.AddMinutes(1).AddSeconds(hand + 1), default);
            }
        }
        await WaitFor(store, Now.AddMinutes(2), session => session.Table.Phase == "settlement" && session.Table.Transition is null);
        var evidence = Assert.Single((await db.Collection("gameRoundEvidence").GetSnapshotAsync()).Documents);
        await AssertFinancialProof(db, evidence, insurance
            ? ["blackjack-table-wager", "blackjack-table-insurance", "blackjack-table-payout"]
            : ["blackjack-table-wager", "blackjack-table-split", "blackjack-table-double", "blackjack-table-double", "blackjack-table-payout"]);
        var original = evidence.GetValue<string>("payloadJson");
        foreach (var table in (await db.Collection(BlackjackBackend.Tables).GetSnapshotAsync()).Documents)
            await table.Reference.UpdateAsync("walletCommandIds", Array.Empty<string>());
        Assert.Equal(original, (await evidence.Reference.GetSnapshotAsync()).GetValue<string>("payloadJson"));
    }

    [Fact]
    public async Task Cancelled_untransferred_reservation_is_released_with_an_uncancelled_cleanup_token()
    {
        var db = Database(); var wallet = new Wallet(); var roster = new TrackingRoster();
        var store = ManagedStore(db, wallet, roster);
        await store.JoinAsync("alice", "Alice", 0, "join-key", Now, default);
        using var cancelled = new CancellationTokenSource();
        roster.AfterReserve = () => { cancelled.Cancel(); return Task.CompletedTask; };
        await ExpectCancellation(() => store.GetSessionAsync("alice", Now.AddSeconds(6), cancelled.Token));
        Assert.Equal("managed-shared", Assert.Single(roster.Released));
        Assert.Empty((await db.Collection(BlackjackBackend.PendingShards).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Transferred_reservation_survives_unknown_wallet_outcome_and_is_recovered_without_a_new_reservation()
    {
        var db = Database(); var wallet = new Wallet(); var roster = new TrackingRoster();
        var store = ManagedStore(db, wallet, roster);
        await store.JoinAsync("alice", "Alice", 0, "join-key", Now, default);
        wallet.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.GetSessionAsync("alice", Now.AddSeconds(6), default));
        Assert.Empty(roster.Released);
        Assert.Single((await db.Collection(BlackjackBackend.PendingShards).GetSnapshotAsync()).Documents);
        await ManagedStore(db, wallet, roster).GetSessionAsync("alice", Now.AddSeconds(6), default);
        Assert.Empty(roster.Released);
        Assert.Equal(1, roster.ReserveCalls);
        Assert.Empty((await db.Collection(BlackjackBackend.PendingShards).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Concurrent_same_assignment_adoption_never_releases_the_winners_identical_profile()
    {
        var db = Database(); var wallet = new Wallet(); var roster = new TrackingRoster();
        await ManagedStore(db, wallet, roster).JoinAsync("alice", "Alice", 0, "join-key", Now, default);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        roster.AfterReserve = async () =>
        {
            if (roster.ReserveCalls >= 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        };
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => ManagedStore(db, wallet, roster)
            .GetSessionAsync("alice", Now.AddSeconds(6), default)));
        Assert.Empty(roster.Released);
        var state = JsonSerializer.Deserialize<BlackjackTableLobbyState>(Assert.Single(
            (await db.Collection(BlackjackBackend.TableState).GetSnapshotAsync()).Documents).GetValue<string>("stateJson"), Json)!;
        Assert.Equal("managed-shared", Assert.Single(state.Tables.Values.SelectMany(table => table.Players), BlackjackManagedSeat.IsManaged).ActorId);
        Assert.Empty((await db.Collection(FirestoreBlackjackTableStore.RetiredManagedLeases).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Retired_pair_fence_blocks_adoption_while_an_old_release_rpc_is_delayed()
    {
        var db = Database(); var wallet = new Wallet(); var roster = new TrackingRoster();
        var store = ManagedStore(db, wallet, roster);
        await store.JoinAsync("alice", "Alice", 0, "join-key", Now, default);
        using var cancelled = new CancellationTokenSource();
        roster.AfterReserve = () => { cancelled.Cancel(); return Task.CompletedTask; };
        roster.BeforeRelease = async () =>
        {
            roster.BeforeRelease = null;
            roster.AfterReserve = null;
            Assert.Single((await db.Collection(FirestoreBlackjackTableStore.RetiredManagedLeases).GetSnapshotAsync()).Documents);
            await ManagedStore(db, wallet, roster).GetSessionAsync("alice", Now.AddSeconds(6), default);
        };
        await ExpectCancellation(() => store.GetSessionAsync("alice", Now.AddSeconds(6), cancelled.Token));
        Assert.Equal("managed-shared", Assert.Single(roster.Released));
        var state = JsonSerializer.Deserialize<BlackjackTableLobbyState>(Assert.Single(
            (await db.Collection(BlackjackBackend.TableState).GetSnapshotAsync()).Documents).GetValue<string>("stateJson"), Json)!;
        var winner = Assert.Single(state.Tables.Values.SelectMany(table => table.Players), BlackjackManagedSeat.IsManaged);
        Assert.Equal("managed-replacement", winner.ActorId);
        Assert.DoesNotContain(winner.ActorId, roster.Released);
        Assert.Equal(2, roster.ReserveCalls);
    }

    [Fact]
    public async Task Unused_reservation_is_retired_when_a_concurrent_winner_makes_the_retry_read_only()
    {
        var db = Database(); var wallet = new Wallet(); var roster = new TrackingRoster { DistinctReservations = true };
        var store = ManagedStore(db, wallet, roster);
        await store.JoinAsync("alice", "Alice", 0, "join-key", Now, default);
        roster.AfterReserve = async () =>
        {
            roster.AfterReserve = null;
            await ManagedStore(db, wallet, roster).GetSessionAsync("alice", Now.AddSeconds(6), default);
        };
        await store.GetSessionAsync("alice", Now.AddSeconds(6), default);
        Assert.Equal("managed-1", Assert.Single(roster.Released));
        var state = JsonSerializer.Deserialize<BlackjackTableLobbyState>(Assert.Single(
            (await db.Collection(BlackjackBackend.TableState).GetSnapshotAsync()).Documents).GetValue<string>("stateJson"), Json)!;
        Assert.Equal("managed-2", Assert.Single(state.Tables.Values.SelectMany(table => table.Players), BlackjackManagedSeat.IsManaged).ActorId);
        Assert.Single((await db.Collection(FirestoreBlackjackTableStore.RetiredManagedLeases).GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Full_permanent_exclusion_budget_keeps_the_human_table_available_without_an_invalid_roster_request(bool longIds)
    {
        var db = Database(); var wallet = new Wallet(); var roster = new TrackingRoster();
        var store = ManagedStore(db, wallet, roster);
        await store.JoinAsync("alice", "Alice", 0, "join-key", Now, default);
        var state = JsonSerializer.Deserialize<BlackjackTableLobbyState>(Assert.Single(
            (await db.Collection(BlackjackBackend.TableState).GetSnapshotAsync()).Documents).GetValue<string>("stateJson"), Json)!;
        var assignment = BlackjackTableIds.Hash(Assert.Single(state.Tickets).TicketId);
        var batch = db.StartBatch();
        var count = GameManagedPlayerRosterProtocol.MaximumExclusions + (longIds ? 0 : 1);
        for (var index = 0; index < count; index++)
        {
            var profile = longIds ? ("managed-retired-" + index).PadRight(128, 'x') : "managed-retired-" + index;
            batch.Create(db.Collection(FirestoreBlackjackTableStore.RetiredManagedLeases).Document(
                BlackjackTableIds.Hash(BlackjackTableCoordinator.ManagedLeaseKey(assignment, profile))), new Dictionary<string, object>
            {
                ["assignmentId"] = assignment, ["profileId"] = profile, ["retiredAtUtc"] = Timestamp.FromDateTime(Now),
            });
        }
        await batch.CommitAsync();
        var available = Play(await store.GetSessionAsync("alice", Now.AddSeconds(6), default));
        Assert.True(Assert.Single(available.Table.Seats).IsCurrentPlayer);
        Assert.Equal(0, roster.ReserveCalls);
    }

    [Fact]
    public async Task Lease_loss_after_wallet_settlement_finalizes_original_terms_then_retires_before_new_progression()
    {
        var db = Database(); var wallet = new Wallet(); var roster = new TrackingRoster();
        var state = new BlackjackTableLobbyState();
        var tableId = BlackjackTableIds.Hash("managed-prepaid-table");
        var table = new BlackjackTableState
        {
            TableId = tableId, Phase = BlackjackTablePhases.Betting, Version = 1, CreatedAtUtc = Now, UpdatedAtUtc = Now,
            Transition = "human-wager", ActiveSeat = 0, PendingSeat = 0, WagerDeadlineAtUtc = Now.AddMinutes(1),
            Players =
            [
                new() { ActorId = "alice", PublicSeatId = "seat_alice", SessionId = "alice-session", DisplayName = "Alice", Seat = 0, SessionStartedAtUtc = Now, Status = "choosing-wager" },
                new() { ActorId = "managed-shared", PublicSeatId = "seat_managed", SessionId = "managed-session", DisplayName = "Managed", Seat = 1, SessionStartedAtUtc = Now,
                    HostMetadata = BlackjackManagedSeat.Create(3, 100, 0), NextWagerCents = 100, Status = "ready" },
            ],
        };
        state.Tables.Add(tableId, table); state.Sessions.Add("alice", new(BlackjackTableSessionKinds.Table, null, tableId));
        var stateId = BlackjackTableIds.Hash("managed-prepaid-state");
        await db.Collection(BlackjackBackend.TableState).Document(stateId).SetAsync(new Dictionary<string, object> { ["stateJson"] = JsonSerializer.Serialize(state, Json) });
        await db.Collection(BlackjackBackend.Sessions).Document(BlackjackTableIds.Hash("alice")).SetAsync(new Dictionary<string, object> { ["stateId"] = stateId });
        var store = ManagedStore(db, wallet, roster);
        wallet.AfterSettle = () => roster.Owned = false;
        wallet.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.WagerAsync("alice", tableId, 100, 1, "wager-key", Now.AddSeconds(1), default));
        var reply = await ManagedStore(db, wallet, roster).WagerAsync("alice", tableId, 100, 1, "wager-key", Now.AddSeconds(2), default);
        Assert.Equal(2, Play(reply).Table.Seats.Count);
        Assert.Equal(1, wallet.Charges);
        Assert.Empty((await db.Collection(BlackjackBackend.PendingShards).GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection(BlackjackBackend.PendingAccounts).GetSnapshotAsync()).Documents);
        var progressed = Play(await store.GetSessionAsync("alice", Now.AddSeconds(1), default));
        Assert.Single(progressed.Table.Seats);
        Assert.True(Assert.Single(progressed.Table.Seats).IsCurrentPlayer);
        Assert.Equal(1, wallet.Charges);
        Assert.Equal(9900, wallet.Balance);
    }

    [Fact]
    public async Task Roster_transport_outage_aborts_new_progression_without_retiring_a_seat_or_creating_a_wallet_operation()
    {
        var db = Database(); var wallet = new Wallet(); var roster = new TrackingRoster();
        var store = ManagedStore(db, wallet, roster);
        await store.JoinAsync("alice", "Alice", 0, "join-key", Now, default);
        await store.GetSessionAsync("alice", Now.AddSeconds(6), default);
        var before = Assert.Single((await db.Collection(BlackjackBackend.TableState).GetSnapshotAsync()).Documents).GetValue<string>("stateJson");
        var operations = (await db.Collection(BlackjackBackend.TableWalletCommands).GetSnapshotAsync()).Count;
        roster.Unavailable = true;
        await Assert.ThrowsAsync<ManagedPlayerRosterUnavailableException>(() => store.GetSessionAsync("alice", Now.AddSeconds(7), default));
        Assert.Equal(before, Assert.Single((await db.Collection(BlackjackBackend.TableState).GetSnapshotAsync()).Documents).GetValue<string>("stateJson"));
        Assert.Equal(operations, (await db.Collection(BlackjackBackend.TableWalletCommands).GetSnapshotAsync()).Count);
        Assert.Empty(roster.Released);
    }

    private static FirestoreBlackjackTableStore ManagedStore(FirestoreDb db, Wallet wallet, TrackingRoster roster) =>
        new(db, wallet, wallet, roster, Deck, () => 1931UL, new OneManagedPlayer());

    private static async Task ExpectCancellation(Func<Task> action)
    {
        var error = await Record.ExceptionAsync(action);
        Assert.True(error is OperationCanceledException or Grpc.Core.RpcException { StatusCode: Grpc.Core.StatusCode.Cancelled }, error?.ToString());
    }

    private sealed class OneManagedPlayer : IManagedTablePopulationDirector
    {
        public ManagedTablePopulationDecision Observe(ManagedTablePopulationObservation observation) =>
            new(true, observation.ManagedPlayers == 0 ? 1 : 0, false);
        public void Close(string gameId, string tableId) { }
    }

    private sealed class TrackingRoster : IManagedPlayerRosterClient
    {
        private int reserveCalls;
        internal int ReserveCalls => Volatile.Read(ref reserveCalls);
        internal List<string> Released { get; } = [];
        internal bool Owned { get; set; } = true;
        internal bool Unavailable { get; set; }
        internal bool DistinctReservations { get; set; }
        internal Func<Task>? AfterReserve { get; set; }
        internal Func<Task>? BeforeRelease { get; set; }
        public async Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveAsync(string assignmentId, int count,
            IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken token)
        {
            var call = Interlocked.Increment(ref reserveCalls);
            if (AfterReserve is { } callback) await callback();
            var profile = DistinctReservations ? "managed-" + call
                : excludedProfileIds.Contains("managed-shared") ? "managed-replacement" : "managed-shared";
            return [new(profile, "Managed", 3)];
        }
        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profiles, DateTime nowUtc, CancellationToken token) =>
            throw new InvalidOperationException("Use confirmed renewal.");
        public Task<bool> TryRenewAsync(string assignmentId, IReadOnlyCollection<string> profiles, DateTime nowUtc, CancellationToken token) =>
            Unavailable ? Task.FromException<bool>(new ManagedPlayerRosterUnavailableException()) : Task.FromResult(Owned);
        public async Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profiles, DateTime nowUtc, CancellationToken token)
        {
            if (BeforeRelease is { } callback) await callback();
            token.ThrowIfCancellationRequested();
            lock (Released) Released.AddRange(profiles);
        }
    }

    private static async Task AssertFinancialProof(FirestoreDb db, DocumentSnapshot evidence, params string[] expectedKinds)
    {
        using var payload = JsonDocument.Parse(evidence.GetValue<string>("payloadJson"));
        var proof = payload.RootElement.GetProperty("financialProof");
        Assert.Equal(BlackjackBackend.Owner, proof.GetProperty("backendOwner").GetString());
        Assert.Equal("signed-wallet", proof.GetProperty("provenance").GetString());
        var entries = proof.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(expectedKinds.Order(), entries.Select(entry => entry.GetProperty("kind").GetString()).Order());
        foreach (var entry in entries)
        {
            var archived = await db.Collection(BlackjackBackend.TableLedgerEvidence).Document(entry.GetProperty("documentId").GetString()!).GetSnapshotAsync();
            Assert.True(archived.Exists);
            Assert.Equal(FortuneForge.Games.Firestore.FirestoreWalletEvidence.Legacy(archived, entry.GetProperty("roundReference").GetString()!).AmountCents, entry.GetProperty("amountCents").GetInt64());
            Assert.Equal(archived.GetValue<string>("walletMutationId"), entry.GetProperty("walletOperationId").GetString());
            Assert.Equal(archived.GetValue<string>("walletSettlementId"), entry.GetProperty("walletSettlementId").GetString());
        }
        var ids = proof.GetProperty("walletOperations").EnumerateArray().SelectMany(operation =>
            operation.GetProperty("ledgerEvidenceIds").EnumerateArray().Select(id => id.GetString())).ToHashSet(StringComparer.Ordinal);
        Assert.All(entries, entry => Assert.Contains(entry.GetProperty("documentId").GetString(), ids));
    }

    private FirestoreDb Database() => new FirestoreDbBuilder
    { ProjectId = "demo-blackjack-tables-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None }.Build();
    private static FirestoreBlackjackTableStore Store(FirestoreDb db, Wallet wallet) =>
        new(db, wallet, wallet, new Roster(), Deck, () => 1931UL, multiplayerEnabled: false);
    private static BlackjackTablePlaySessionResponse Play(BlackjackTableStoreResult result) => Assert.IsType<BlackjackTablePlaySessionResponse>(result.Session);
    private static string Serialize(BlackjackTableStoreResult result) => JsonSerializer.Serialize(result, Json);
    private static IReadOnlyList<string> Deck()
    {
        string[] prefix = ["10|spades", "6|hearts", "9|diamonds", "10|clubs", "8|spades"];
        return prefix.Concat(BlackjackRules.CreateShuffledDeck().Where(card => !prefix.Contains(card)).Order(StringComparer.Ordinal)).ToArray();
    }
    private static async Task<BlackjackTablePlaySessionResponse> WaitFor(FirestoreBlackjackTableStore store, DateTime start,
        Func<BlackjackTablePlaySessionResponse, bool> done)
    {
        for (var step = 0; step < 80; step++)
        {
            var session = Play(await store.GetSessionAsync("alice", start.AddMilliseconds(step * 500), default));
            if (done(session)) return session;
        }
        throw new InvalidOperationException("The saved table did not reach the expected phase.");
    }
    private sealed class Roster : IManagedPlayerRosterClient
    {
        public Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveAsync(string assignmentId, int count, IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken token) => throw new InvalidOperationException("Solo tables cannot reserve managed players.");
        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profiles, DateTime nowUtc, CancellationToken token) => throw new InvalidOperationException("Solo tables cannot heartbeat managed players.");
        public Task<bool> TryRenewAsync(string assignmentId, IReadOnlyCollection<string> profiles, DateTime nowUtc, CancellationToken token) => throw new InvalidOperationException("Solo tables cannot renew managed players.");
        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profiles, DateTime nowUtc, CancellationToken token) => throw new InvalidOperationException("Solo tables cannot release managed players.");
    }
    private sealed class Wallet : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> receipts = [];
        public long Balance { get; set; } = 10000;
        public long? ReportedBalance { get; set; }
        public bool LoseNextReply { get; set; }
        public bool LoseNextCreditReply { get; set; }
        public bool Unavailable { get; set; }
        public Action? AfterSettle { get; set; }
        public int Charges { get; private set; }
        public int Credits { get; private set; }
        public Task<GameWalletAccountState> GetStateAsync(string account, CancellationToken token) => Task.FromResult(new GameWalletAccountState(0, ReportedBalance ?? Balance, "Alice"));
        public Task<GameWalletSettlementReceipt> SettleAsync(string account, GameWalletSettlementRequest request, CancellationToken token)
        {
            lock (receipts)
            {
                if (Unavailable) throw new GameWalletUnavailableException();
                if (receipts.TryGetValue(request.OperationId, out var prior))
                {
                    if (prior.Request != request) throw new GameWalletConflictException();
                    return Task.FromResult(prior.Receipt with { WasAlreadySettled = true });
                }
                var before = Balance;
                var declined = before < GameWalletBalanceRequirement.RequiredCents(request.WagerCents, request.MinimumBalanceBeforeCents);
                var afterWager = declined ? before : before - request.WagerCents;
                Balance = declined ? before : afterWager + request.PayoutCents;
                var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId("blackjack", account, request.OperationId),
                    !declined && request.WagerCents > 0 ? request.OperationId + "-wager" : null,
                    !declined && request.PayoutCents > 0 ? request.OperationId + "-payout" : null,
                    before, afterWager, Balance, DateTimeOffset.UtcNow, false, declined);
                receipts.Add(request.OperationId, (request, receipt));
                AfterSettle?.Invoke();
                if (!declined && request.WagerCents > 0) Charges++;
                if (!declined && request.PayoutCents > 0) Credits++;
                if (LoseNextReply || LoseNextCreditReply && request.PayoutCents > 0)
                { LoseNextReply = false; LoseNextCreditReply = false; throw new GameWalletUnavailableException(); }
                return Task.FromResult(receipt);
            }
        }
    }
}
