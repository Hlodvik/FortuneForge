using System.Text.Json;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.TexasHoldem;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using FortuneForge.Server.Matchmaking;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Cards.TexasHoldem.Credit;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class CreditHoldemFirestoreEmulatorTests : IClassFixture<CreditHoldemFirestoreEmulatorFixture>
{
    private static readonly DateTime Start = new(2026, 8, 16, 14, 0, 0, DateTimeKind.Utc);
    private readonly CreditHoldemFirestoreEmulatorFixture fixture;

    public CreditHoldemFirestoreEmulatorTests(CreditHoldemFirestoreEmulatorFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task ProductionStore_FreeJoinAndQueueLeaveNeverTouchMoney()
    {
        var (database, store, suffix) = CreateStore();
        var user = $"queue-{suffix}";
        await SeedBalanceAsync(database, user, 10_000);

        var calls = Enumerable.Range(0, 6).Select(_ => store.JoinAsync(
            user, "Alice", 0, "free-join-1", 101, Start, default));
        var results = await Task.WhenAll(calls);
        var queued = Assert.IsType<CreditHoldemQueueSessionResponse>(results[0].Session);
        await store.CancelAsync(user, queued.TicketId, queued.Version, "free-leave-1", Start, default);

        Assert.Equal(10_000, await ReadBalanceAsync(database, user));
        Assert.Empty(await LedgerAsync(database));
    }

    [Fact]
    public async Task ProductionStore_BlindsAndActionCommitExactlyOnceAfterValidation()
    {
        var (database, store, suffix) = CreateStore();
        var first = $"alice-{suffix}";
        var second = $"bruno-{suffix}";
        await SeedPlayersAsync(database, first, second);
        var match = await StartMatchAsync(store, first, second);
        var internalMatch = await ReadMatchAsync(database, match.Table.MatchId);
        var actor = internalMatch.Players.Single(player => player.Seat == match.Table.ActiveSeat);
        Assert.True(actor.IsAccountBacked);
        var actorView = Assert.IsType<CreditHoldemMatchSessionResponse>(
            (await store.GetSessionAsync(actor.ActorId, Start.AddSeconds(6), default)).Session);
        var publicActor = actorView.Table.Seats.Single(seat => seat.IsCurrentPlayer);
        var required = actorView.Table.CurrentBet - publicActor.CommittedRound;
        Assert.True(required > 0);
        var before = await ReadBalanceAsync(database, actor.ActorId);
        var request = new CreditHoldemActionRequest(CreditHoldemActions.Call, actorView.Version);

        var duplicate = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => store.ActionAsync(
            actor.ActorId, match.Table.MatchId, request, "call-once-1", Start.AddSeconds(7), default)));

        Assert.Single(duplicate.Select(value => value.Session.Version).Distinct());
        Assert.Equal(before - required, await ReadBalanceAsync(database, actor.ActorId));
        var ledger = await LedgerAsync(database);
        Assert.Equal(ledger.Count, ledger.Select(entry => Field<string>(entry, "transactionId")).Distinct().Count());
        Assert.Contains(ledger, entry => Field<string>(entry, "type") == "texas-holdem-blind");
        Assert.Contains(ledger, entry => Field<string>(entry, "type").StartsWith("texas-holdem-action-v", StringComparison.Ordinal));
        await Assert.ThrowsAsync<CreditHoldemConflictException>(() => store.ActionAsync(
            actor.ActorId, match.Table.MatchId, request, "stale-call-1", Start.AddSeconds(8), default));
    }

    [Fact]
    public async Task ProductionStore_FirstActionDeadlineKeepsHumanForTheNextHand()
    {
        var (database, store, suffix) = CreateStore();
        var first = $"inactive-a-{suffix}";
        var second = $"inactive-b-{suffix}";
        await SeedPlayersAsync(database, first, second);
        var session = await StartMatchAsync(store, first, second);
        var match = await ReadMatchAsync(database, session.Table.MatchId);
        var inactive = match.Players.Single(player => player.Seat == match.ActiveSeat);

        var timedOut = await store.GetSessionAsync(
            inactive.ActorId, match.ActionDeadlineAtUtc!.Value, default);

        Assert.False(timedOut.Session is CreditHoldemIdleSessionResponse);
        match = await ReadMatchAsync(database, session.Table.MatchId);
        Assert.DoesNotContain(inactive.ActorId, match.LeavingActorIds);
        var persisted = match.Players.Single(player => player.ActorId == inactive.ActorId);
        Assert.Equal(1, persisted.ConsecutiveMissedActionHands);
        Assert.Equal(match.HandNumber, persisted.LastMissedActionHand);
    }

    [Fact]
    public async Task ProductionStore_SettlesImmediatePayoutAndSignedHouseNetExactlyOnce()
    {
        var (database, store, suffix) = CreateStore();
        var first = $"settle-a-{suffix}";
        var second = $"settle-b-{suffix}";
        await SeedPlayersAsync(database, first, second);
        var match = await StartMatchAsync(store, first, second);
        var at = match.Table.MatchDeadlineAtUtc.AddSeconds(1);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => store.GetSessionAsync(
            index % 2 == 0 ? first : second, at, default)));
        var result = Assert.IsType<CreditHoldemResultSessionResponse>(
            (await store.GetSessionAsync(first, at, default)).Session);
        var balanceTotal = await ReadBalanceAsync(database, first) + await ReadBalanceAsync(database, second);
        var ledgerCount = (await LedgerAsync(database)).Count;
        _ = await store.GetSessionAsync(first, at.AddSeconds(1), default);

        Assert.Equal(20_000 - result.HumanCommittedCredits * 100 + result.HumanPayoutCredits * 100, balanceTotal);
        Assert.Equal(ledgerCount, (await LedgerAsync(database)).Count);
        Assert.Equal(result.HumanCommittedCredits - result.HumanPayoutCredits, result.HouseNetCredits);
        var revenue = Assert.Single((await database.Collection("creditHoldemMatchRevenue").GetSnapshotAsync()).Documents);
        Assert.Equal(Field<long>(revenue, "humanWagerCents") - Field<long>(revenue, "humanPayoutCents"),
            Field<long>(revenue, "houseNetCents"));
        Assert.Equal("real-human-wager-v2", Field<string>(revenue, "financialClassification"));
        Assert.Equal(0, Field<long>(revenue, "botFinancialContributionCents"));
    }

    [Fact]
    public async Task ProductionStore_AbandonedResultReleasesEveryHumanSession()
    {
        var (database, store, suffix) = CreateStore();
        var first = $"result-timeout-a-{suffix}";
        var second = $"result-timeout-b-{suffix}";
        await SeedPlayersAsync(database, first, second);
        var match = await StartMatchAsync(store, first, second);
        var completedAt = match.Table.MatchDeadlineAtUtc.AddSeconds(1);
        Assert.IsType<CreditHoldemResultSessionResponse>(
            (await store.GetSessionAsync(first, completedAt, default)).Session);

        var expiry = completedAt.Add(CreditHoldemTableCoordinator.ResultInactivityDuration);
        Assert.IsType<CreditHoldemIdleSessionResponse>(
            (await store.GetSessionAsync(first, expiry, default)).Session);
        Assert.IsType<CreditHoldemIdleSessionResponse>(
            (await store.GetSessionAsync(second, expiry, default)).Session);
    }

    [Fact]
    public async Task ProductionStore_RedactsPrivateCardsAndInternalPlayerMetadata()
    {
        var (database, store, suffix) = CreateStore();
        var first = $"redact-a-{suffix}";
        var second = $"redact-b-{suffix}";
        await SeedPlayersAsync(database, first, second);
        var match = await StartMatchAsync(store, first, second);

        var json = JsonSerializer.Serialize(match, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("dealSeed", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actorId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("botSkill", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isBot", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(match.Table.Seats.Where(seat => !seat.IsCurrentPlayer).SelectMany(seat => seat.HoleCards), card =>
        {
            Assert.True(card.Hidden);
            Assert.Null(card.Rank);
            Assert.Null(card.Suit);
        });
    }

    [Fact]
    public async Task ProductionStore_PersistentNextHandAcceptsLateHumanAndLeaveFoldsCurrentHand()
    {
        var (database, store, suffix) = CreateStore();
        var first = $"persist-a-{suffix}";
        var second = $"persist-b-{suffix}";
        var late = $"persist-c-{suffix}";
        await SeedPlayersAsync(database, first, second, late);
        var hand = await StartMatchAsync(store, first, second);
        var pending = Assert.IsType<CreditHoldemQueueSessionResponse>((await store.JoinAsync(
            late, "Casey", 0, "late-join-1", 303, Start.AddSeconds(7), default)).Session);
        Assert.Equal("pending-next-hand", pending.Players.Single().Status);
        var at = hand.Table.MatchDeadlineAtUtc.AddSeconds(1);
        var result = Assert.IsType<CreditHoldemResultSessionResponse>(
            (await store.GetSessionAsync(first, at, default)).Session);
        var next = Assert.IsType<CreditHoldemMatchSessionResponse>((await store.NextHandAsync(
            first, result.MatchId, result.Version, "next-hand-1", 404, at.AddSeconds(1), default)).Session);
        Assert.Equal(result.MatchId, next.Table.MatchId);
        Assert.Equal(result.HandNumber + 1, next.Table.HandNumber);
        Assert.Contains(next.Table.Seats, seat => seat.DisplayName == "Casey");

        var left = await store.LeaveAsync(
            first, next.Table.MatchId, next.Version, "leave-table-1", at.AddSeconds(2), default);
        Assert.IsType<CreditHoldemIdleSessionResponse>(left.Session);
        var stored = await ReadMatchAsync(database, next.Table.MatchId);
        Assert.Equal("folded", stored.Players.Single(player => player.ActorId == first).Status);
        Assert.Contains(first, stored.LeavingActorIds);
    }

    [Fact]
    public async Task ProductionStore_HistoryAndSharedResultArePaidSeenOnlyRecords()
    {
        var (database, store, suffix) = CreateStore();
        var first = $"history-a-{suffix}";
        var second = $"history-b-{suffix}";
        await SeedPlayersAsync(database, first, second);
        var hand = await StartMatchAsync(store, first, second);
        var active = Assert.Single((await store.HistoryAsync(first, 30, default)).Items);
        Assert.Equal(("active", true), (active.Status, active.Seen));
        var at = hand.Table.MatchDeadlineAtUtc.AddSeconds(1);
        _ = await store.GetSessionAsync(first, at, default);
        var completed = Assert.Single((await store.HistoryAsync(first, 30, default)).Items);
        var balanceAfterSettlement = await ReadBalanceAsync(database, first);
        Assert.Equal("completed", completed.Status);
        Assert.False(completed.Seen);
        var shared = await database.Collection("cardGameResults").Document(completed.EventId).GetSnapshotAsync();
        Assert.True(shared.Exists);
        Assert.Equal("texas-holdem", Field<string>(shared, "game"));
        Assert.Equal("credit-table", Field<string>(shared, "mode"));
        Assert.Equal("completed", Field<string>(shared, "claimStatus"));
        Assert.Equal("paid", Field<string>(shared, "settlementStatus"));
        Assert.Equal(first, Field<string>(shared, "userId"));

        var marked = await store.MarkHistorySeenAsync(first, completed.EventId, at.AddSeconds(1), default);
        Assert.True(marked.Seen);
        Assert.Equal(balanceAfterSettlement, await ReadBalanceAsync(database, first));
        shared = await database.Collection("cardGameResults").Document(completed.EventId).GetSnapshotAsync();
        Assert.True(shared.ContainsField("seenAt"));
    }

    [Fact]
    public async Task ProductionStore_LegacyMatchWithoutNewCollectionsReadsAndRewritesCompatibly()
    {
        var (database, store, suffix) = CreateStore();
        var first = $"legacy-a-{suffix}";
        var second = $"legacy-b-{suffix}";
        await SeedPlayersAsync(database, first, second);
        var match = await StartMatchAsync(store, first, second);
        var matchDocument = database.Collection("creditHoldemMatches").Document(match.Table.MatchId);
        var snapshot = await matchDocument.GetSnapshotAsync();
        var storedJson = Field<string>(snapshot, "matchJson");
        using var document = JsonDocument.Parse(storedJson);
        var legacyFields = document.RootElement.EnumerateObject()
            .Where(property => property.Name is not "leavingActorIds" and not "humanPayoutsCents")
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        var legacyJson = JsonSerializer.Serialize(
            legacyFields, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await matchDocument.SetAsync(new Dictionary<string, object> { ["matchJson"] = legacyJson }, SetOptions.MergeAll);

        var left = await store.LeaveAsync(
            first, match.Table.MatchId, match.Version, "legacy-leave-1", Start.AddSeconds(7), default);

        Assert.IsType<CreditHoldemIdleSessionResponse>(left.Session);
        snapshot = await matchDocument.GetSnapshotAsync();
        using var rewritten = JsonDocument.Parse(Field<string>(snapshot, "matchJson"));
        Assert.True(rewritten.RootElement.TryGetProperty("leavingActorIds", out var leaving));
        Assert.Contains(first, leaving.EnumerateArray().Select(value => value.GetString()));
        Assert.True(rewritten.RootElement.TryGetProperty("humanPayoutsCents", out var payouts));
        Assert.Equal(JsonValueKind.Object, payouts.ValueKind);
    }

    [Fact]
    public async Task ProductionStore_ManagedSeatsRemainAfterLastAccountLeavesWithoutImmediateReplacement()
    {
        var queuer = new RecordingManagedPlayerQueuer();
        var (database, _, suffix) = CreateStore();
        var store = new FirestoreCreditHoldemStore(
            database, allowSingleHumanBotFill: true, new MultiplayerMatchmaker(queuer));
        var user = $"managed-{suffix}";
        await SeedBalanceAsync(database, user, 10_000);

        _ = await store.JoinAsync(user, "Alice", 0, "managed-join-1", 701, Start, default);
        var session = Assert.IsType<CreditHoldemMatchSessionResponse>(
            (await store.GetSessionAsync(user, Start.Add(CreditHoldemEngine.HumanGrace), default)).Session);
        var assignmentId = $"texas-holdem:{session.Table.MatchId}";

        var reservation = Assert.Single(queuer.Reservations);
        Assert.Equal((ManagedPlayerGames.TexasHoldem, assignmentId, 2),
            (reservation.GameId, reservation.AssignmentId, reservation.Count));
        var heartbeat = Assert.Single(queuer.Heartbeats);
        Assert.Equal(assignmentId, heartbeat.AssignmentId);
        Assert.Equal(2, heartbeat.ProfileIds.Count);

        _ = await store.LeaveAsync(
            user,
            session.Table.MatchId,
            session.Version,
            "managed-leave-1",
            Start.Add(CreditHoldemEngine.HumanGrace).AddMilliseconds(100),
            default);

        Assert.Empty(queuer.Releases);
        var stored = await ReadMatchAsync(database, session.Table.MatchId);
        Assert.All(stored.Players.Where(TexasHoldemManagedPlayers.IsManaged),
            player => Assert.Contains(player.ActorId, heartbeat.ProfileIds));
    }

    [Fact]
    public async Task ProductionStore_UsesEveryQueuedHumanBeforeRequestingManagedSeats()
    {
        var queuer = new RecordingManagedPlayerQueuer();
        var (database, _, suffix) = CreateStore();
        var store = new FirestoreCreditHoldemStore(
            database, allowSingleHumanBotFill: true, new MultiplayerMatchmaker(queuer));
        var first = $"queue-first-{suffix}";
        var second = $"queue-second-{suffix}";
        var third = $"queue-third-{suffix}";
        await SeedPlayersAsync(database, first, second, third);

        _ = await store.JoinAsync(first, "Alice", 0, "queue-human-first", 711, Start, default);
        _ = await store.JoinAsync(second, "Bruno", 0, "queue-human-second", 712, Start.AddMilliseconds(1), default);
        _ = await store.JoinAsync(third, "Casey", 0, "queue-human-third", 713, Start.AddMilliseconds(2), default);
        var session = Assert.IsType<CreditHoldemMatchSessionResponse>(
            (await store.GetSessionAsync(first, Start.Add(CreditHoldemEngine.HumanGrace), default)).Session);
        var match = await ReadMatchAsync(database, session.Table.MatchId);

        Assert.Equal(3, match.Players.Count);
        Assert.All(match.Players, player => Assert.True(player.IsAccountBacked));
        Assert.Empty(queuer.Reservations);
    }

    [Fact]
    public async Task ProductionStore_SweepAdvancesPrivateManagedTurnsWithoutSessionPolling()
    {
        var queuer = new RecordingManagedPlayerQueuer();
        var (database, _, suffix) = CreateStore();
        var store = new FirestoreCreditHoldemStore(
            database, allowSingleHumanBotFill: true, new MultiplayerMatchmaker(queuer));
        var user = $"worker-{suffix}";
        await SeedBalanceAsync(database, user, 10_000);
        _ = await store.JoinAsync(user, "Alice", 0, "worker-join-1", 801, Start, default);
        var session = Assert.IsType<CreditHoldemMatchSessionResponse>(
            (await store.GetSessionAsync(user, Start.Add(CreditHoldemEngine.HumanGrace), default)).Session);
        var match = await ReadMatchAsync(database, session.Table.MatchId);
        var active = match.Players.First(TexasHoldemManagedPlayers.IsManaged);
        match.ActiveSeat = active.Seat;
        match.UpdatedAtUtc = Start.Add(CreditHoldemEngine.HumanGrace);
        match.ActionDeadlineAtUtc = match.UpdatedAtUtc.Add(CreditHoldemEngine.ActionDuration);
        var dueAt = TexasHoldemManagedPlayers.PrivateActionDueAt(match, active);
        await database.Collection("creditHoldemMatches").Document(match.MatchId).SetAsync(
            new Dictionary<string, object>
            {
                ["matchJson"] = JsonSerializer.Serialize(
                    match, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                ["nextDeadlineAt"] = Timestamp.FromDateTime(dueAt)
            }, SetOptions.MergeAll);

        await store.SweepAsync(dueAt.AddMilliseconds(1), default);
        match = await ReadMatchAsync(database, session.Table.MatchId);

        Assert.Contains(match.ActionLog, entry => entry.PublicSeatId == active.PublicSeatId);
        Assert.True(queuer.Heartbeats.Count >= 1);
    }

    [Fact]
    public async Task ProductionStore_ReleasesOnlyManagedSeatReplacedByQueuedAccount()
    {
        var queuer = new RecordingManagedPlayerQueuer();
        var (database, _, suffix) = CreateStore();
        var store = new FirestoreCreditHoldemStore(
            database, allowSingleHumanBotFill: true, new MultiplayerMatchmaker(queuer));
        var first = $"replace-a-{suffix}";
        var second = $"replace-b-{suffix}";
        var third = $"replace-c-{suffix}";
        var fourth = $"replace-d-{suffix}";
        await SeedPlayersAsync(database, first, second, third, fourth);
        _ = await store.JoinAsync(first, "Alice", 0, "replace-first", 901, Start, default);
        var started = Assert.IsType<CreditHoldemMatchSessionResponse>(
            (await store.GetSessionAsync(first, Start.Add(CreditHoldemEngine.HumanGrace), default)).Session);
        var before = await ReadMatchAsync(database, started.Table.MatchId);
        var managedBefore = before.Players.Where(TexasHoldemManagedPlayers.IsManaged)
            .Select(player => player.ActorId).ToHashSet(StringComparer.Ordinal);
        _ = await store.JoinAsync(
            second, "Bruno", 0, "replace-second", 902, Start.AddSeconds(6), default);
        _ = await store.JoinAsync(
            third, "Casey", 0, "replace-third", 903, Start.AddSeconds(6), default);
        _ = await store.JoinAsync(
            fourth, "Devon", 0, "replace-fourth", 904, Start.AddSeconds(6), default);
        var completed = Assert.IsType<CreditHoldemResultSessionResponse>(
            (await store.GetSessionAsync(first, before.MatchDeadlineAtUtc.AddMilliseconds(1), default)).Session);

        _ = await store.NextHandAsync(
            first,
            completed.MatchId,
            completed.Version,
            "replace-next-hand",
            905,
            before.MatchDeadlineAtUtc.AddSeconds(1),
            default);

        var release = Assert.Single(queuer.Releases);
        var removed = Assert.Single(release.ProfileIds);
        Assert.Contains(removed, managedBefore);
        var next = await ReadMatchAsync(database, completed.MatchId);
        Assert.DoesNotContain(next.Players, player => player.ActorId == removed);
        Assert.Contains(next.Players, player => player.ActorId == second && player.IsAccountBacked);
    }

    private static async Task<CreditHoldemMatchSessionResponse> StartMatchAsync(
        FirestoreCreditHoldemStore store, string first, string second)
    {
        await store.JoinAsync(first, "Alice", 0, "join-first", 201, Start, default);
        await store.JoinAsync(second, "Bruno", 0, "join-second", 202, Start, default);
        return Assert.IsType<CreditHoldemMatchSessionResponse>(
            (await store.GetSessionAsync(first, Start.Add(CreditHoldemEngine.HumanGrace), default)).Session);
    }

    private (FirestoreDb Database, FirestoreCreditHoldemStore Store, string Suffix) CreateStore()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-holdem-{suffix}",
            Endpoint = fixture.Endpoint,
            ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
            EmulatorDetection = EmulatorDetection.None
        }.Build();
        return (database, new FirestoreCreditHoldemStore(database), suffix);
    }

    private static Task SeedPlayersAsync(FirestoreDb database, params string[] users) =>
        Task.WhenAll(users.Select(user => SeedBalanceAsync(database, user, 10_000)));

    private static Task SeedBalanceAsync(FirestoreDb database, string userId, long cents) =>
        database.Collection("userBalances").Document($"{userId}_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["available"] = cents / 100,
            ["availableFractionalCents"] = cents % 100,
            ["version"] = 1L,
            ["updatedAt"] = Timestamp.FromDateTime(Start)
        });

    private static async Task<long> ReadBalanceAsync(FirestoreDb database, string userId)
    {
        var snapshot = await database.Collection("userBalances").Document($"{userId}_slotsCredits").GetSnapshotAsync();
        return checked(Field<long>(snapshot, "available") * 100 + Field<long>(snapshot, "availableFractionalCents"));
    }

    private static async Task<IReadOnlyList<DocumentSnapshot>> LedgerAsync(FirestoreDb database) =>
        (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;

    private static async Task<CreditHoldemMatch> ReadMatchAsync(FirestoreDb database, string matchId)
    {
        var document = await database.Collection("creditHoldemMatches").Document(matchId).GetSnapshotAsync();
        return JsonSerializer.Deserialize<CreditHoldemMatch>(
            Field<string>(document, "matchJson"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static async Task<string> ActorIdAtSeat(FirestoreDb database, string matchId, int seat)
    {
        var match = await ReadMatchAsync(database, matchId);
        return match.Players.Single(player => player.Seat == seat).ActorId;
    }

    private static T Field<T>(DocumentSnapshot snapshot, string field) => snapshot.GetValue<T>(field);

    private sealed class RecordingManagedPlayerQueuer : IManagedPlayerQueuer
    {
        public List<(string GameId, string AssignmentId, int Count)> Reservations { get; } = [];
        public List<(string GameId, string AssignmentId, IReadOnlyList<string> ProfileIds)> Heartbeats { get; } = [];
        public List<(string AssignmentId, IReadOnlyList<string> ProfileIds)> Releases { get; } = [];

        public Task<IReadOnlyList<ManagedPlayerProfile>> ReserveAsync(
            string gameId,
            string assignmentId,
            int count,
            IReadOnlyCollection<string> excludedProfileIds,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            Reservations.Add((gameId, assignmentId, count));
            return Task.FromResult<IReadOnlyList<ManagedPlayerProfile>>(
                Enumerable.Range(0, count).Select(index => new ManagedPlayerProfile(
                    $"managed-holdem-{index}",
                    $"Player {index}",
                    ManagedPlayerSkillLevels.Average,
                    new HashSet<string>([ManagedPlayerGames.TexasHoldem], StringComparer.Ordinal),
                    nowUtc)).ToArray());
        }

        public Task HeartbeatAsync(
            string gameId,
            string assignmentId,
            IReadOnlyCollection<string> profileIds,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            Heartbeats.Add((gameId, assignmentId, profileIds.Order(StringComparer.Ordinal).ToArray()));
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(
            string assignmentId,
            IReadOnlyCollection<string> profileIds,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            Releases.Add((assignmentId, profileIds.Order(StringComparer.Ordinal).ToArray()));
            return Task.CompletedTask;
        }
    }
}
