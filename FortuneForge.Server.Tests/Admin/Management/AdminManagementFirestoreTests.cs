using System.Text.Json;
using FortuneForge.Server.Admin.Management;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Cards.Blackjack.Table;
using FortuneForge.Server.Cards.Solitaire;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.Games.Roulette;
using FortuneForge.Server.Matchmaking;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Admin.Management;

[Collection("Blackjack table Firestore emulator")]
public sealed class AdminManagementFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 21, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Writes_persist_with_atomic_sanitized_audit_and_retry_receipts()
    {
        var db = Database();
        var store = new FirestoreAdminManagementStore(db);
        var first = Mutation("catalog-visibility", "baccarat", false, 0, "visibility-request");
        Assert.Equal(0, (await store.ReadAsync(default)).Version);
        var hidden = await store.MutateAsync(first, default);
        Assert.False(hidden.Games.Single(game => game.Id == "baccarat").Visible);
        Assert.False((await new FirestoreAdminManagementStore(db).ReadAsync(default)).IsVisible(AdminManagementCatalog.Require("baccarat")));
        await store.MutateAsync(Mutation("bots-global", null, false, 1, "global-off-request"), default);
        await store.MutateAsync(Mutation("bots-game", "hearts", false, 2, "hearts-off-request"), default);
        var final = await store.MutateAsync(Mutation("bots-global", null, true, 3, "global-on-request"), default);
        Assert.False(final.Games.Single(game => game.Id == "hearts").EffectiveBotsEnabled);
        Assert.True(final.Games.Single(game => game.Id == "blackjack").EffectiveBotsEnabled);
        Assert.False(final.Games.Single(game => game.Id == "baccarat").Visible);
        Assert.False(final.Games.Single(game => game.Id == "horse-flight").Visible);
        Assert.Equal(1, (await store.MutateAsync(first, default)).Version);
        await Assert.ThrowsAsync<AdminManagementConflictException>(() => store.MutateAsync(first with { Value = true }, default));
        await Assert.ThrowsAsync<AdminManagementConflictException>(() => store.MutateAsync(first with { IdempotencyKey = "new-stale-request" }, default));

        var audit = await store.AuditAsync(10, null, default);
        Assert.Equal([4L, 3L, 2L, 1L], audit.Select(item => item.Version));
        Assert.All(audit, item => { Assert.Equal(64, item.ActorId.Length); Assert.Equal("Controlled operational test", item.Reason); });
        Assert.DoesNotContain("actual-admin-user", JsonSerializer.Serialize(audit));
        Assert.True(audit[^1].Before);
        Assert.False(audit[^1].After);

        var service = new AdminManagementService(store, Options.Create(new AdminManagementOptions { CursorSigningKey = new string('c', 40) }));
        var page = await service.AuditAsync(2, null, default);
        Assert.NotNull(page.NextCursor);
        var next = await service.AuditAsync(2, page.NextCursor, default);
        Assert.Equal([2L, 1L], next.Items.Select(item => item.Version));
        Assert.Null(next.NextCursor);
    }

    [Fact]
    public async Task Concurrent_identical_requests_commit_one_change_and_one_audit()
    {
        var db = Database();
        var store = new FirestoreAdminManagementStore(db);
        var mutation = Mutation("bots-global", null, false, 0, "identical-retry-key");
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.MutateAsync(mutation, default)));
        Assert.All(results, response => Assert.Equal(1, response.Version));
        Assert.Single(await store.AuditAsync(10, null, default));
        Assert.Equal(1, (await store.ReadAsync(default)).Version);
    }

    [Fact]
    public async Task Concurrent_distinct_writes_at_the_same_revision_cannot_overwrite_each_other()
    {
        var db = Database();
        var store = new FirestoreAdminManagementStore(db);
        async Task<bool> Attempt(bool value, string key)
        {
            try { await store.MutateAsync(Mutation("bots-global", null, value, 0, key), default); return true; }
            catch (AdminManagementConflictException exception) { Assert.Equal("version-conflict", exception.Code); return false; }
        }
        var results = await Task.WhenAll(Attempt(false, "first-change-request"), Attempt(true, "second-change-request"));
        Assert.Single(results, value => value);
        Assert.Single(await store.AuditAsync(10, null, default));
        Assert.Equal(1, (await store.ReadAsync(default)).Version);
    }

    [Fact]
    public async Task Disabled_switch_blocks_new_durable_leases_but_not_renewal_heartbeat_or_release()
    {
        var db = Database();
        var controls = new FirestoreAdminManagementStore(db);
        var assignments = new FirestoreManagedPlayerAssignmentStore(db);
        var ids = Enumerable.Range(0, 100).Select(index => $"managed-control-{index}")
            .Where(id => !ManagedPlayerAvailabilityPolicy.IsSleeping(id, Now)).Take(2).ToArray();
        foreach (var id in ids) await SeedManaged(db, id);
        Assert.True(await assignments.TryReserveAsync(ids[0], "hearts", "existing-table", Now, default));
        await controls.MutateAsync(Mutation("bots-global", null, false, 0, "pause-global-bots"), default);
        Assert.False(await assignments.TryReserveAsync(ids[1], "blackjack", "new-table", Now, default));
        Assert.False(await assignments.AllowsNewAssignmentsAsync("hearts", default));
        Assert.Empty(await Queuer(db).ReserveAsync("hearts", "fresh-table", 3, [], Now, default));
        Assert.True(await assignments.TryRenewAsync("hearts", "existing-table", [ids[0]], Now.AddMinutes(1), default));
        await assignments.HeartbeatAsync(ids[0], "hearts", "existing-table", Now.AddMinutes(2), default);
        Assert.True(await assignments.ReleaseAsync(ids[0], "existing-table", Now.AddMinutes(3), default));

        await controls.MutateAsync(Mutation("bots-game", "hearts", false, 1, "pause-hearts-bots"), default);
        await controls.MutateAsync(Mutation("bots-global", null, true, 2, "resume-global-bots"), default);
        Assert.False(await assignments.AllowsNewAssignmentsAsync("hearts", default));
        Assert.True(await assignments.AllowsNewAssignmentsAsync("blackjack", default));
        Assert.False(await assignments.TryReserveAsync(ids[1], "hearts", "another-table", Now, default));
        Assert.True(await assignments.TryReserveAsync(ids[1], "blackjack", "another-table", Now, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completed_queuer_cohort_is_resumable_without_extra_profiles_or_accounting_reset(bool provisionalBatches)
    {
        var db = Database();
        var ids = AvailableIds(3);
        foreach (var id in ids) await SeedManaged(db, id);
        var queuer = Queuer(db, provisionalBatches);
        var first = await queuer.ReserveAsync("hearts", "complete-table", 3, [], Now, default);
        var second = await queuer.ReserveAsync("hearts", "complete-table", 3, [], Now.AddMinutes(1), default);
        Assert.Equal(3, first.Count);
        Assert.Equal(first.Select(profile => profile.UserId), second.Select(profile => profile.UserId));
        var leases = (await db.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents;
        Assert.Equal(3, leases.Count);
        Assert.All(leases, lease =>
        {
            Assert.False(lease.GetValue<bool>("reservationPending"));
            Assert.Equal(Now, lease.GetValue<Timestamp>("reservedAt").ToDateTime());
        });
    }

    [Fact]
    public async Task Generation_failure_cleans_new_durable_provisional_leases_without_masking_failure()
    {
        var db = Database();
        await SeedManaged(db, AvailableIds(1)[0]);
        var failure = new InvalidOperationException("Deliberate generation failure");
        var profiles = new FirestoreManagedPlayerProfileRepository(db);
        var queuer = new ManagedPlayerQueuer(new FailingGenerator(failure), profiles, new FirestoreManagedPlayerAssignmentStore(db, true));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queuer.ReserveAsync("hearts", "failed-table", 3, [], Now, default)));
        Assert.Empty((await db.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabling_after_one_reservation_returns_no_opponents_with_cleanup_only_for_opted_in_owned_batches(bool provisionalBatches)
    {
        var db = Database();
        var ids = AvailableIds(2);
        foreach (var id in ids) await SeedManaged(db, id);
        var assignments = new DisableAfterFirstReservation(db, provisionalBatches);
        var profiles = new FirestoreManagedPlayerProfileRepository(db);
        var queuer = new ManagedPlayerQueuer(new ManagedPlayerProfileGenerator(profiles), profiles, assignments);
        Assert.Empty(await queuer.ReserveAsync("hearts", "interrupted-cohort", 3, [], Now, default));
        Assert.Equal(1, assignments.Reservations);
        Assert.Equal(provisionalBatches ? 1 : 0, assignments.Releases);
        var leases = (await db.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents;
        if (provisionalBatches) Assert.Empty(leases);
        else
        {
            var lease = Assert.Single(leases);
            Assert.False(lease.GetValue<bool>("reservationPending"));
            Assert.Equal(Now.AddMinutes(30), lease.GetValue<Timestamp>("leaseUntil").ToDateTime());
        }
    }

    [Fact]
    public async Task Concurrent_same_assignment_completion_protects_shared_leases_from_interrupted_cleanup()
    {
        var db = Database();
        var ids = AvailableIds(2);
        foreach (var id in ids) await SeedManaged(db, id);
        var assignments = new FirestoreManagedPlayerAssignmentStore(db, true);
        Assert.True(await assignments.TryReserveBatchAsync(ids[0], "hearts", "shared-table", "batch-a", Now, default));
        Assert.True(await assignments.TryReserveBatchAsync(ids[0], "hearts", "shared-table", "batch-b", Now, default));
        Assert.True(await assignments.TryReserveBatchAsync(ids[1], "hearts", "shared-table", "batch-b", Now, default));
        Assert.True(await assignments.CompleteBatchAsync("hearts", "shared-table", "batch-b", ids, Now, default));
        await Disable(db);
        Assert.False(await assignments.ReleaseIncompleteBatchAsync(ids[0], "shared-table", "batch-a", Now, default));
        Assert.False(await assignments.ReleaseIncompleteBatchAsync(ids[1], "shared-table", "batch-b", Now, default));
        Assert.True(await assignments.CompleteBatchAsync("hearts", "shared-table", "batch-a", ids, Now, default));
        Assert.True(await assignments.TryRenewAsync("hearts", "shared-table", ids, Now.AddMinutes(1), default));
        Assert.Equal(2, (await db.Collection("managedPlayerAssignments").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task Cleanup_winning_before_shared_batch_completion_cannot_return_a_missing_lease()
    {
        var db = Database();
        var ids = AvailableIds(2);
        foreach (var id in ids) await SeedManaged(db, id);
        var assignments = new FirestoreManagedPlayerAssignmentStore(db, true);
        Assert.True(await assignments.TryReserveBatchAsync(ids[0], "hearts", "shared-table", "batch-a", Now, default));
        Assert.True(await assignments.TryReserveBatchAsync(ids[0], "hearts", "shared-table", "batch-b", Now, default));
        Assert.True(await assignments.TryReserveBatchAsync(ids[1], "hearts", "shared-table", "batch-b", Now, default));
        Assert.True(await assignments.ReleaseIncompleteBatchAsync(ids[0], "shared-table", "batch-a", Now, default));
        Assert.False(await assignments.CompleteBatchAsync("hearts", "shared-table", "batch-b", ids, Now, default));
        Assert.True(await assignments.ReleaseIncompleteBatchAsync(ids[1], "shared-table", "batch-b", Now, default));
        Assert.Empty((await db.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Completion_and_cleanup_race_has_only_a_complete_cohort_or_clean_retry_outcome()
    {
        var db = Database();
        var ids = AvailableIds(2);
        foreach (var id in ids) await SeedManaged(db, id);
        var assignments = new FirestoreManagedPlayerAssignmentStore(db, true);
        Assert.True(await assignments.TryReserveBatchAsync(ids[0], "hearts", "racing-table", "batch-a", Now, default));
        Assert.True(await assignments.TryReserveBatchAsync(ids[0], "hearts", "racing-table", "batch-b", Now, default));
        Assert.True(await assignments.TryReserveBatchAsync(ids[1], "hearts", "racing-table", "batch-b", Now, default));
        var outcomes = await Task.WhenAll(
            assignments.CompleteBatchAsync("hearts", "racing-table", "batch-b", ids, Now, default),
            assignments.ReleaseIncompleteBatchAsync(ids[0], "racing-table", "batch-a", Now, default));
        Assert.NotEqual(outcomes[0], outcomes[1]);
        if (outcomes[0])
        {
            Assert.True(await assignments.TryRenewAsync("hearts", "racing-table", ids, Now.AddMinutes(1), default));
            Assert.False(await assignments.ReleaseIncompleteBatchAsync(ids[1], "racing-table", "batch-b", Now, default));
            Assert.Equal(2, (await db.Collection("managedPlayerAssignments").GetSnapshotAsync()).Count);
        }
        else
        {
            Assert.True(await assignments.ReleaseIncompleteBatchAsync(ids[1], "racing-table", "batch-b", Now, default));
            Assert.Empty((await db.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);
        }
    }

    [Fact]
    public async Task Legacy_leases_and_direct_reservations_remain_committed_without_resetting_accounting()
    {
        var db = Database();
        var ids = AvailableIds(2);
        foreach (var id in ids) await SeedManaged(db, id);
        var assignments = new FirestoreManagedPlayerAssignmentStore(db, true);
        Assert.True(await assignments.TryReserveAsync(ids[0], "hearts", "legacy-table", Now, default));
        var legacy = db.Collection("managedPlayerAssignments").Document(ids[0]);
        await legacy.UpdateAsync(new Dictionary<string, object> { ["reservationPending"] = FieldValue.Delete, ["reservationBatchId"] = FieldValue.Delete });
        Assert.True(await assignments.TryReserveBatchAsync(ids[0], "hearts", "legacy-table", "new-attempt", Now.AddMinutes(1), default));
        Assert.False(await assignments.ReleaseIncompleteBatchAsync(ids[0], "legacy-table", "new-attempt", Now, default));
        Assert.True(await assignments.CompleteBatchAsync("hearts", "legacy-table", "new-attempt", [ids[0]], Now.AddMinutes(1), default));
        Assert.True(await assignments.TryReserveAsync(ids[0], "hearts", "legacy-table", Now.AddMinutes(2), default));
        Assert.Equal(Now, (await legacy.GetSnapshotAsync()).GetValue<Timestamp>("reservedAt").ToDateTime());
        Assert.False(await assignments.TryReserveAsync(ids[0], "blackjack", "legacy-table", Now, default));
        Assert.True(await assignments.TryReserveBatchAsync(ids[1], "hearts", "direct-table", "provisional", Now, default));
        Assert.True(await assignments.TryReserveAsync(ids[1], "hearts", "direct-table", Now.AddMinutes(1), default));
        Assert.False(await assignments.ReleaseIncompleteBatchAsync(ids[1], "direct-table", "provisional", Now, default));
    }

    [Fact]
    public async Task Blackjack_opens_without_new_bots_instead_of_spinning_its_supply_retry_loop()
    {
        var db = Database();
        await Disable(db);
        await SeedBalance(db, "alice");
        var store = new FirestoreBlackjackTableStore(db, null, () => 1931UL, new MultiplayerMatchmaker(Queuer(db)));
        await store.JoinAsync("alice", "Alice", 0, "admin-blackjack-join", Now, default);
        var session = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync("alice", Now.AddSeconds(6), default)
            .WaitAsync(TimeSpan.FromSeconds(15))).Session);
        Assert.Single(session.Table.Seats);
        Assert.True(session.Table.Seats[0].IsCurrentPlayer);
        Assert.Equal(10_000, (await store.GetSessionAsync("alice", Now.AddSeconds(7), default)).BalanceCents);
        Assert.Empty((await db.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Holdem_waits_for_the_required_human_cohort_then_starts_normally_without_new_bots()
    {
        var db = Database();
        await Disable(db);
        await SeedBalance(db, "alice"); await SeedBalance(db, "bruno"); await SeedBalance(db, "cara");
        var store = new FirestoreCreditHoldemStore(db, true, new MultiplayerMatchmaker(Queuer(db)));
        await store.JoinAsync("alice", "Alice", 0, "admin-holdem-first", 1931, Now, default);
        Assert.IsType<CreditHoldemQueueSessionResponse>((await store.GetSessionAsync("alice", Now.AddSeconds(6), default)).Session);
        await store.JoinAsync("bruno", "Bruno", 0, "admin-holdem-second", 1942, Now.AddSeconds(7), default);
        Assert.IsType<CreditHoldemQueueSessionResponse>((await store.GetSessionAsync("alice", Now.AddSeconds(13), default)).Session);
        await store.JoinAsync("cara", "Cara", 0, "admin-holdem-third", 1943, Now.AddSeconds(14), default);
        var match = Assert.IsType<CreditHoldemMatchSessionResponse>((await store.GetSessionAsync("alice", Now.AddSeconds(20), default)).Session);
        Assert.Equal(CreditHoldemMoney.MinimumStartPlayers, match.Table.Seats.Count);
        Assert.Empty((await db.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Solitaire_hourly_admission_is_skipped_and_human_entry_is_not_charged_twice()
    {
        var db = Database();
        await Disable(db); await SeedBalance(db, "alice");
        var store = new FirestoreCompetitiveSolitaireStore(db, new CompetitiveSolitaireOptions { AllowSingleHumanBotFill = true });
        var started = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync("alice", "Alice", 4, 500, 3,
            "admin-solitaire-start", 1931, Now, default)).Session);
        await store.SweepAsync(Now.AddHours(1), default);
        var players = (await db.Collection("solitaireMatchPlayers").WhereEqualTo("matchId", started.MatchId).GetSnapshotAsync()).Documents;
        Assert.Single(players);
        Assert.Empty((await db.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Paid_Hearts_stays_human_first_with_open_seats_when_new_bots_are_off()
    {
        var db = Database();
        await Disable(db); await SeedBalance(db, "alice");
        var store = new FirestorePaidHeartsStore(db, new MultiplayerMatchmaker(Queuer(db)));
        var joined = await store.JoinAsync("alice", "Alice", 1m, "admin-hearts-start", Now, default);
        var later = await store.GetAsync("alice", joined.Queue!.MatchId, Now.AddSeconds(20), default);
        Assert.Equal("queue", later.Kind);
        Assert.Single(later.Queue!.Players);
        Assert.Equal(99m, later.BalanceCredits);
        Assert.Empty((await db.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Roulette_stays_playable_with_one_human_when_no_new_bots_are_available()
    {
        var db = Database();
        await Disable(db);
        var service = new RouletteGameService(new MultiplayerMatchmaker(Queuer(db)));
        var started = service.Start("alice", "Alice");
        Assert.Single(started.Players);
        Assert.Equal("open", started.Phase);
        Assert.Empty((await db.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);
    }

    private FirestoreDb Database() => new FirestoreDbBuilder
    { ProjectId = $"demo-admin-management-{Guid.NewGuid():N}", Endpoint = fixture.Endpoint, ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None }.Build();
    private static AdminManagementMutation Mutation(string operation, string? id, bool value, long version, string key) =>
        new("actual-admin-user", key, operation, id, value, version, "Controlled operational test", Now);
    private static Task<AdminManagementResponse> Disable(FirestoreDb db) => new FirestoreAdminManagementStore(db)
        .MutateAsync(Mutation("bots-global", null, false, 0, "disable-for-game-test"), default);
    private static ManagedPlayerQueuer Queuer(FirestoreDb db, bool provisionalBatches = false)
    {
        var profiles = new FirestoreManagedPlayerProfileRepository(db);
        return new(new ManagedPlayerProfileGenerator(profiles), profiles, new FirestoreManagedPlayerAssignmentStore(db, provisionalBatches));
    }
    private static string[] AvailableIds(int count) => Enumerable.Range(0, 100).Select(index => $"managed-batch-{index}")
        .Where(id => !ManagedPlayerAvailabilityPolicy.IsSleeping(id, Now)).Take(count).ToArray();

    private sealed class FailingGenerator(Exception failure) : IManagedPlayerProfileGenerator
    {
        public Task<ManagedPlayerProfile> GenerateAsync(string gameId, DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromException<ManagedPlayerProfile>(failure);
    }

    private sealed class DisableAfterFirstReservation(FirestoreDb db, bool provisionalBatches) : IManagedPlayerAssignmentStore,
        IManagedPlayerAdmissionPolicy, IManagedPlayerReservationBatchStore
    {
        private readonly FirestoreManagedPlayerAssignmentStore inner = new(db, provisionalBatches);
        public bool ProvisionalBatchesEnabled => provisionalBatches;
        public int Reservations { get; private set; }
        public int Releases { get; private set; }
        public Task<bool> AllowsNewAssignmentsAsync(string gameId, CancellationToken cancellationToken) => inner.AllowsNewAssignmentsAsync(gameId, cancellationToken);
        public Task<bool> TryReserveAsync(string profileId, string gameId, string assignmentId, DateTime nowUtc, CancellationToken cancellationToken) => AfterReserve(inner.TryReserveAsync(profileId, gameId, assignmentId, nowUtc, cancellationToken));
        public Task<bool> TryReserveBatchAsync(string profileId, string gameId, string assignmentId, string batchId, DateTime nowUtc, CancellationToken cancellationToken) => AfterReserve(inner.TryReserveBatchAsync(profileId, gameId, assignmentId, batchId, nowUtc, cancellationToken));
        private async Task<bool> AfterReserve(Task<bool> pending)
        {
            var acquired = await pending;
            if (acquired && ++Reservations == 1) await Disable(db);
            return acquired;
        }
        public Task<bool> CompleteBatchAsync(string gameId, string assignmentId, string batchId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken) => inner.CompleteBatchAsync(gameId, assignmentId, batchId, profileIds, nowUtc, cancellationToken);
        public async Task<bool> ReleaseIncompleteBatchAsync(string profileId, string assignmentId, string batchId, DateTime nowUtc, CancellationToken cancellationToken)
        { var released = await inner.ReleaseIncompleteBatchAsync(profileId, assignmentId, batchId, nowUtc, cancellationToken); if (released) Releases++; return released; }
        public Task HeartbeatAsync(string profileId, string gameId, string assignmentId, DateTime nowUtc, CancellationToken cancellationToken) => inner.HeartbeatAsync(profileId, gameId, assignmentId, nowUtc, cancellationToken);
        public Task<bool> TryRenewAsync(string gameId, string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken) => inner.TryRenewAsync(gameId, assignmentId, profileIds, nowUtc, cancellationToken);
        public Task<bool> ReleaseAsync(string profileId, string assignmentId, DateTime nowUtc, CancellationToken cancellationToken) => inner.ReleaseAsync(profileId, assignmentId, nowUtc, cancellationToken);
    }
    private static Task SeedBalance(FirestoreDb db, string id) => db.Collection("userBalances").Document($"{id}_slotsCredits")
        .SetAsync(new Dictionary<string, object> { ["available"] = 100L, ["availableFractionalCents"] = 0L, ["reserved"] = 0L, ["version"] = 1L, ["updatedAt"] = Timestamp.FromDateTime(Now) });
    private static Task SeedManaged(FirestoreDb db, string id) => db.Collection("users").Document(id)
        .SetAsync(new Dictionary<string, object> { ["userId"] = id, ["playerName"] = id, ["authProvider"] = ManagedPlayerProfileSchema.AuthenticationProvider,
            [ManagedPlayerProfileSchema.ProfileTagsField] = new[] { ManagedPlayerProfileSchema.BotTag }, ["skillLevel"] = 3L,
            ["supportedGames"] = ManagedPlayerGames.All.ToArray(), ["createdAt"] = Timestamp.FromDateTime(Now) });
}
