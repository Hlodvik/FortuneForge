using System.Text.Json;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Cards.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Solitaire;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class CompetitiveSolitaireBotFillFirestoreTests
{
    private static readonly DateTime Start = new(2026, 8, 15, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FirstJoinStartsImmediately_AndHistoryShowsVacantSeats()
    {
        var (database, store) = CreateStore();
        await SeedBalanceAsync(database, "alice");

        var joins = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => JoinAsync(store, "alice", Start)));
        var first = Assert.IsType<SolitaireMatchSessionResponse>(joins[0].Session);
        Assert.All(joins, joined => Assert.Equal(first.MatchId,
            Assert.IsType<SolitaireMatchSessionResponse>(joined.Session).MatchId));
        Assert.Equal(Start, first.StartedAtUtc);
        Assert.Equal(Start.AddMinutes(10), first.DeadlineAtUtc);
        Assert.Equal(1, first.Players.Count(player => player.Status == SolitairePlayerStatuses.Playing));
        Assert.Equal(3, first.Players.Count(player => player.Status == SolitairePlayerStatuses.Open));
        Assert.Equal(9_500, await ReadBalanceAsync(database, "alice"));
        Assert.Single(await LedgerAsync(database));
        Assert.Empty(await AssignmentsAsync(database));

        var history = Assert.Single(await store.GetHistoryAsync("alice", 20, Start, default));
        Assert.Equal("playing", history.Status);
        Assert.Equal(Start, history.StartedAtUtc);
        Assert.Null(history.CompletedAtUtc);
        Assert.Equal(3, history.Players.Count(player =>
            player.DisplayName == "Waiting for players..." && player.Status == SolitairePlayerStatuses.Open));
        Assert.False(history.CanClaim);
        AssertAutomationNeutral(first);
    }

    [Fact]
    public async Task LaterHumanJoinsSameDeck_WithIndependentTenMinuteClock()
    {
        var (database, store) = CreateStore(allowManagedPlayers: false);
        await Task.WhenAll(SeedBalanceAsync(database, "alice"), SeedBalanceAsync(database, "bruno"));
        var alice = await JoinStartedAsync(store, "alice", Start);
        var joinedAt = Start.AddMinutes(7);
        var bruno = await JoinStartedAsync(store, "bruno", joinedAt);

        Assert.Equal(alice.MatchId, bruno.MatchId);
        Assert.Equal(joinedAt, bruno.StartedAtUtc);
        Assert.Equal(joinedAt.AddMinutes(10), bruno.DeadlineAtUtc);
        Assert.Equal(JsonSerializer.Serialize(alice.Game), JsonSerializer.Serialize(bruno.Game));
        var aliceLater = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("alice", joinedAt, default)).Session);
        Assert.Equal(3 * 60_000, aliceLater.RemainingMilliseconds);
        Assert.Equal(Start.AddMinutes(10), aliceLater.DeadlineAtUtc);
        Assert.Equal(2, bruno.Players.Count(player => player.Status == SolitairePlayerStatuses.Playing));
        Assert.Equal((10m, 9m), (bruno.PrizePoolCredits, bruno.WinnerPayoutCredits));
        Assert.Empty(await AssignmentsAsync(database));
        var funding = Assert.Single((await database.Collection("solitaireHouseFunding").GetSnapshotAsync()).Documents);
        Assert.Equal(1_000, Field<long>(funding, "humanEntryCents"));
        Assert.Equal(0, Field<long>(funding, "houseFundedEntryCents"));
        await store.SweepAsync(Start.AddHours(1), default);
        Assert.Equal(2, (await StoredPlayersAsync(database, alice.MatchId)).Count);
        Assert.Empty(await AssignmentsAsync(database));
    }

    [Fact]
    public async Task HumansCanFillRemainingSeatsBeforeHourlyBotAdmission()
    {
        var (database, store) = CreateStore();
        var users = new[] { "alice", "bruno", "cara", "drew" };
        await Task.WhenAll(users.Select(user => SeedBalanceAsync(database, user)));
        await SeedProfilesAsync(database, 3);
        var first = await JoinStartedAsync(store, users[0], Start);
        var others = await Task.WhenAll(users.Skip(1).Select((user, index) =>
            JoinAsync(store, user, Start.AddMinutes(58).AddSeconds(index))));

        Assert.All(others, joined => Assert.Equal(first.MatchId,
            Assert.IsType<SolitaireMatchSessionResponse>(joined.Session).MatchId));
        Assert.Equal(4, (await StoredPlayersAsync(database, first.MatchId)).Count);
        await store.SweepAsync(Start.AddHours(1), default);
        Assert.Empty(await AssignmentsAsync(database));
        Assert.Equal(4, (await StoredPlayersAsync(database, first.MatchId)).Count);
        var funding = Assert.Single((await database.Collection("solitaireHouseFunding").GetSnapshotAsync()).Documents);
        Assert.Equal(2_000, Field<long>(funding, "humanEntryCents"));
        Assert.Equal(0, Field<long>(funding, "houseFundedEntryCents"));
        Assert.Equal(4, Field<long>(funding, "humanPlayerCount"));
    }

    [Fact]
    public async Task BotArrivalsAreHourly_AndEachPlaysTheSharedDeckOnItsOwnClock()
    {
        var (database, store) = CreateStore();
        await SeedBalanceAsync(database, "alice");
        await SeedProfilesAsync(database, 3);
        var match = await JoinStartedAsync(store, "alice", Start);
        _ = await store.CommandAsync("alice", match.MatchId,
            Command(SolitaireCommandTypes.Submit, match.Version),
            "alice-submits", Start.AddMinutes(2), default);

        await store.SweepAsync(Start.AddHours(1).AddMilliseconds(-1), default);
        Assert.Single(await StoredPlayersAsync(database, match.MatchId));
        await store.SweepAsync(Start.AddHours(1), default);
        var firstBot = Assert.Single(await StoredPlayersAsync(database, match.MatchId),
            player => Field<bool>(player, "isSynthetic"));
        Assert.Equal(Timestamp.FromDateTime(Start.AddHours(1)), Field<Timestamp>(firstBot, "startedAt"));
        Assert.Equal(Timestamp.FromDateTime(Start.AddHours(1).AddMinutes(10)),
            Field<Timestamp>(firstBot, "deadlineAt"));
        Assert.Equal(JsonSerializer.Serialize(match.Game),
            JsonSerializer.Serialize(SolitaireEngine.ToResponse(ReadGame(firstBot))));
        Assert.Equal("playing", Field<string>(
            await database.Collection("solitaireMatches").Document(match.MatchId).GetSnapshotAsync(), "status"));
        await store.SweepAsync(Start.AddHours(1).AddMinutes(1), default);
        Assert.Equal(2, (await StoredPlayersAsync(database, match.MatchId)).Count);
        await store.SweepAsync(Start.AddHours(2), default);
        Assert.Equal(3, (await StoredPlayersAsync(database, match.MatchId)).Count);
        await store.SweepAsync(Start.AddHours(3), default);
        var players = await StoredPlayersAsync(database, match.MatchId);
        Assert.Equal(4, players.Count);
        Assert.Equal(3, players.Count(player => Field<bool>(player, "isSynthetic")));
        // Earlier bot runs have ended and their profiles are already free;
        // only the bot admitted at hour three still holds an assignment.
        Assert.Single(await AssignmentsAsync(database));
        Assert.Equal(1_500, Field<long>(Assert.Single((await database.Collection("solitaireHouseFunding")
            .GetSnapshotAsync()).Documents), "houseFundedEntryCents"));

        await store.SweepAsync(Start.AddHours(3).AddMinutes(5), default);
        var lateBot = Assert.Single(await StoredPlayersAsync(database, match.MatchId),
            player => Field<Timestamp>(player, "startedAt") ==
                Timestamp.FromDateTime(Start.AddHours(3)));
        Assert.True(Field<long>(lateBot, "version") > 1);
        Assert.Equal("playing", Field<string>(lateBot, "status"));
        Assert.Equal("playing", Field<string>(await database.Collection("solitaireMatches")
            .Document(match.MatchId).GetSnapshotAsync(), "status"));
    }

    [Fact]
    public async Task BotCanWinFundedPool_WithoutChangingBotBalances()
    {
        var (database, store) = CreateStore();
        await SeedBalanceAsync(database, "alice");
        var profiles = await SeedProfilesAsync(database, 3);
        var started = await JoinStartedAsync(store, "alice", Start);
        _ = await store.ForfeitAsync("alice", started.MatchId, started.Version,
            "alice-forfeits", Start.AddMinutes(1), default);
        foreach (var hour in new[] { 1, 2, 3 })
            await store.SweepAsync(Start.AddHours(hour), default);
        await store.SweepAsync(Start.AddHours(3).AddMinutes(11), default);

        var result = Assert.IsType<SolitaireResultSessionResponse>(
            (await store.GetSessionAsync("alice", Start.AddHours(3).AddMinutes(11), default)).Session);
        Assert.Equal((20m, 18m, 2m),
            (result.PrizePoolCredits, result.WinnerPayoutCredits, result.PlatformFeeCredits));
        var winner = Assert.Single(result.Standings, standing => standing.PayoutCredits > 0);
        Assert.False(winner.IsCurrentPlayer);
        Assert.True(winner.Score > 0);
        Assert.Empty(await AssignmentsAsync(database));
        var revenue = Assert.Single((await database.Collection("solitaireMatchRevenue").GetSnapshotAsync()).Documents);
        Assert.Equal(500, Field<long>(revenue, "humanEntryCents"));
        Assert.Equal(1_500, Field<long>(revenue, "houseFundedEntryCents"));
        Assert.Equal(1_800, Field<long>(revenue, "botPrizeCents"));
        Assert.Equal(500, Field<long>(revenue, "houseNetCents"));
        Assert.Equal(9_500, await ReadBalanceAsync(database, "alice"));
        await AssertBotBalancesUntouchedAsync(database, profiles);
    }

    [Fact]
    public async Task FinishedBotReleasesItsProfileWhileTheMatchRemainsOpen()
    {
        var (database, store) = CreateStore();
        await SeedBalanceAsync(database, "alice");
        var profile = Assert.Single(await SeedProfilesAsync(database, 1));
        var started = await JoinStartedAsync(store, "alice", Start);

        await store.SweepAsync(Start.AddHours(1), default);
        Assert.Single(await AssignmentsAsync(database));
        await store.SweepAsync(Start.AddHours(1).AddMinutes(11), default);

        var bot = Assert.Single(await StoredPlayersAsync(database, started.MatchId),
            player => Field<bool>(player, "isSynthetic"));
        Assert.Equal(SolitairePlayerStatuses.Finished, Field<string>(bot, "status"));
        Assert.Empty(await AssignmentsAsync(database));
        Assert.Equal("playing", Field<string>(
            await database.Collection("solitaireMatches").Document(started.MatchId).GetSnapshotAsync(),
            "status"));
        var profileDocument = await database.Collection("users").Document(profile.UserId)
            .GetSnapshotAsync();
        Assert.InRange(Field<long>(profileDocument, "managedPlaySecondsInWindow"), 1, 900);
    }

    [Fact]
    public async Task WaitingMatchPersistsItsNextDueTimeInsteadOfRewritingEverySweep()
    {
        var (database, store) = CreateStore();
        await SeedBalanceAsync(database, "alice");
        var started = await JoinStartedAsync(store, "alice", Start);
        var matchReference = database.Collection("solitaireMatches").Document(started.MatchId);
        var initial = await matchReference.GetSnapshotAsync();
        Assert.Equal(Timestamp.FromDateTime(Start.AddMinutes(10)),
            Field<Timestamp>(initial, "nextSweepAt"));

        await store.SweepAsync(Start.AddMinutes(10), default);
        var waiting = await matchReference.GetSnapshotAsync();
        Assert.Equal(Timestamp.FromDateTime(Start.AddHours(1)),
            Field<Timestamp>(waiting, "nextSweepAt"));
        await store.SweepAsync(Start.AddMinutes(30), default);
        var stillWaiting = await matchReference.GetSnapshotAsync();
        Assert.Equal(waiting.UpdateTime, stillWaiting.UpdateTime);
        Assert.Single(await StoredPlayersAsync(database, started.MatchId));
    }

    [Fact]
    public async Task FailedBotAdmissionDoesNotStopOtherMatchesOrTheirDeadlines()
    {
        var (database, store) = CreateStore();
        await Task.WhenAll(SeedBalanceAsync(database, "alice"), SeedBalanceAsync(database, "bruno"));
        await SeedProfilesAsync(database, 2);
        var first = await JoinStartedAsync(store, "alice", Start);
        var second = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
            "bruno", "bruno", 6, 500, 3, "bruno-six-seat-game", 1002,
            Start.AddSeconds(1), default)).Session);
        await database.Collection("solitaireHouseFunding").Document(first.MatchId).DeleteAsync();

        await store.SweepAsync(Start.AddHours(1).AddSeconds(2), default);

        Assert.Equal(SolitairePlayerStatuses.Finished, Field<string>(
            Assert.Single(await StoredPlayersAsync(database, first.MatchId),
                player => !Field<bool>(player, "isSynthetic")), "status"));
        Assert.Equal(SolitairePlayerStatuses.Finished, Field<string>(
            Assert.Single(await StoredPlayersAsync(database, second.MatchId),
                player => !Field<bool>(player, "isSynthetic")), "status"));
        Assert.Contains(await StoredPlayersAsync(database, second.MatchId),
            player => Field<bool>(player, "isSynthetic"));
    }

    [Fact]
    public async Task LostManagedAssignmentEndsOnlyThatBotsRun()
    {
        var (database, store) = CreateStore();
        await SeedBalanceAsync(database, "alice");
        await SeedProfilesAsync(database, 1);
        var started = await JoinStartedAsync(store, "alice", Start);
        await store.SweepAsync(Start.AddHours(1), default);
        var assignment = Assert.Single(await AssignmentsAsync(database));
        await assignment.Reference.UpdateAsync("assignmentId", "another-game-assignment");

        await store.SweepAsync(Start.AddHours(1).AddMinutes(1), default);

        var bot = Assert.Single(await StoredPlayersAsync(database, started.MatchId),
            player => Field<bool>(player, "isSynthetic"));
        Assert.Equal(SolitairePlayerStatuses.Finished, Field<string>(bot, "status"));
        Assert.Equal("another-game-assignment", Field<string>(
            await assignment.Reference.GetSnapshotAsync(), "assignmentId"));
        Assert.Equal("playing", Field<string>(
            await database.Collection("solitaireMatches").Document(started.MatchId).GetSnapshotAsync(),
            "status"));
    }

    [Fact]
    public async Task FinishedPlayerCanStartAnotherMatch_AndClaimOlderNoContestWithoutLosingCurrentGame()
    {
        var (database, store) = CreateStore(allowManagedPlayers: false);
        await SeedBalanceAsync(database, "alice");
        var first = await JoinStartedAsync(store, "alice", Start);
        _ = await store.CommandAsync("alice", first.MatchId,
            Command(SolitaireCommandTypes.Submit, first.Version),
            "finish-first", Start.AddMinutes(1), default);
        var second = Assert.IsType<SolitaireMatchSessionResponse>((await JoinAsync(
            store, "alice", Start.AddMinutes(2), "join-second")).Session);
        Assert.NotEqual(first.MatchId, second.MatchId);

        await store.SweepAsync(Start.AddHours(4), default);
        Assert.Equal("settled", Field<string>(await database.Collection("solitaireMatches")
            .Document(first.MatchId).GetSnapshotAsync(), "status"));
        var current = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("alice", Start.AddHours(4), default)).Session);
        Assert.Equal(second.MatchId, current.MatchId);
        var history = await store.GetHistoryAsync("alice", 20, Start.AddHours(4), default);
        var older = Assert.Single(history, item => item.MatchId == first.MatchId);
        Assert.Equal("settled", older.Status);
        Assert.Equal(3, older.Players.Count(player => player.Status == SolitairePlayerStatuses.Open));
        Assert.True(older.CanClaim);
        Assert.Equal(5m, older.PayoutCredits);

        var claimed = await store.ClaimAsync("alice", first.MatchId, "claim-first",
            Start.AddHours(4), default);
        Assert.Equal(second.MatchId, Assert.IsType<SolitaireMatchSessionResponse>(claimed.Session).MatchId);
        Assert.Equal(9_500, await ReadBalanceAsync(database, "alice"));
        var revenue = Assert.Single((await database.Collection("solitaireMatchRevenue").GetSnapshotAsync()).Documents);
        Assert.Equal(0, Field<long>(revenue, "platformFeeCents"));
        Assert.Equal(500, Field<long>(revenue, "humanPayoutCents"));
    }

    [Fact]
    public async Task PauseBudgetStillExtendsOnlyThisPlayersGameplayClock()
    {
        var (database, store) = CreateStore(allowManagedPlayers: false);
        await SeedBalanceAsync(database, "alice");
        var started = await JoinStartedAsync(store, "alice", Start);
        var paused = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync(
            "alice", started.MatchId, Command(SolitaireCommandTypes.Pause, started.Version),
            "pause", Start.AddMinutes(1), default)).Session);
        Assert.True(paused.IsPaused);
        Assert.Equal(9 * 60_000, paused.RemainingMilliseconds);
        var resumed = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync(
            "alice", started.MatchId, Command(SolitaireCommandTypes.Resume, paused.Version),
            "resume", Start.AddMinutes(5), default)).Session);
        Assert.Equal(Start.AddMinutes(14), resumed.DeadlineAtUtc);
        Assert.Equal(9 * 60_000, resumed.RemainingMilliseconds);
        var current = Assert.IsType<SolitaireMatchSessionResponse>(
            (await store.GetSessionAsync("alice", Start.AddMinutes(10), default)).Session);
        Assert.Equal(4 * 60_000, current.RemainingMilliseconds);
        Assert.Equal(3, current.Players.Count(player => player.Status == SolitairePlayerStatuses.Open));
        Assert.Empty(await AssignmentsAsync(database));
    }

    [Theory]
    [InlineData(30, 460)]
    [InlineData(180, 410)]
    public async Task RankedSpeedBonusStillUsesPersonalGameplayClock(int finishAfterSeconds, int expectedScore)
    {
        var (database, store) = CreateStore(allowManagedPlayers: false);
        const string user = "speed-test-player";
        await SeedBalanceAsync(database, user);
        var started = await JoinStartedAsync(store, user, Start);
        var player = Assert.Single(await StoredPlayersAsync(database, started.MatchId));

        var suits = new[] { "clubs", "diamonds", "hearts", "spades" };
        var foundations = suits.Select(suit => (IReadOnlyList<SolitaireCard>)
            Enumerable.Range(1, suit == "spades" ? 11 : 13)
                .Select(rank => new SolitaireCard($"{suit}-{rank}", suit, rank, true)).ToArray()).ToArray();
        var tableau = Enumerable.Range(0, 7).Select(index => (IReadOnlyList<SolitaireCard>)(index switch
        {
            0 => [new SolitaireCard("spades-12", "spades", 12, true)],
            1 => [new SolitaireCard("spades-13", "spades", 13, true)],
            _ => Array.Empty<SolitaireCard>()
        })).ToArray();
        var nearWin = new SolitaireGameState([], [], foundations, tableau, 250, 20, 1001, "Your move")
        {
            DrawCount = 3
        };
        await player.Reference.UpdateAsync("gameStateJson", JsonSerializer.Serialize(nearWin,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        _ = await store.CommandAsync(user, started.MatchId,
            new SolitaireCommandRequest(SolitaireCommandTypes.Move, started.Version,
                new SolitairePileReference("tableau", 0), 0, new SolitairePileReference("foundation", 3), null),
            "queen", Start.AddSeconds(10), default);
        _ = await store.CommandAsync(user, started.MatchId,
            new SolitaireCommandRequest(SolitaireCommandTypes.Move, started.Version + 1,
                new SolitairePileReference("tableau", 1), 0, new SolitairePileReference("foundation", 3), null),
            "king", Start.AddSeconds(finishAfterSeconds), default);
        player = await player.Reference.GetSnapshotAsync();
        Assert.Equal(expectedScore, ReadGame(player).Score);
        Assert.Equal(finishAfterSeconds * 1_000L, Field<long>(player, "elapsedMilliseconds"));
        Assert.Equal(SolitairePlayerStatuses.Finished, Field<string>(player, "status"));
    }

    [Fact]
    public async Task LegacyPlayerWithoutPersonalTimingFieldsUsesMatchClock()
    {
        var (database, store) = CreateStore(allowManagedPlayers: false);
        await SeedBalanceAsync(database, "alice");
        var started = await JoinStartedAsync(store, "alice", Start);
        var player = Assert.Single(await StoredPlayersAsync(database, started.MatchId));
        await player.Reference.UpdateAsync(new Dictionary<string, object>
        {
            ["startedAt"] = FieldValue.Delete,
            ["deadlineAt"] = FieldValue.Delete
        });
        var paused = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync(
            "alice", started.MatchId, Command(SolitaireCommandTypes.Pause, started.Version),
            "legacy-pause", Start.AddMinutes(1), default)).Session);
        Assert.Equal(Start, paused.StartedAtUtc);
        Assert.Equal(Start.AddMinutes(10), paused.DeadlineAtUtc);
        player = await player.Reference.GetSnapshotAsync();
        Assert.Equal(Timestamp.FromDateTime(Start), Field<Timestamp>(player, "startedAt"));
        Assert.Equal(Timestamp.FromDateTime(Start.AddMinutes(10)), Field<Timestamp>(player, "deadlineAt"));
    }

    private static (FirestoreDb Database, FirestoreCompetitiveSolitaireStore Store) CreateStore(
        bool allowManagedPlayers = true)
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-solitaire-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly
        }.Build();
        return (database, new FirestoreCompetitiveSolitaireStore(database, new CompetitiveSolitaireOptions
        {
            AllowSingleHumanBotFill = allowManagedPlayers
        }));
    }

    private static Task<SolitaireStoreSession> JoinAsync(FirestoreCompetitiveSolitaireStore store,
        string user, DateTime now, string? key = null) => store.JoinAsync(user, user, 4, 500, 3,
            key ?? $"join-command-{user}", 1001, now, default);

    private static async Task<SolitaireMatchSessionResponse> JoinStartedAsync(
        FirestoreCompetitiveSolitaireStore store, string user, DateTime now) =>
        Assert.IsType<SolitaireMatchSessionResponse>((await JoinAsync(store, user, now)).Session);

    private static SolitaireCommandRequest Command(string type, int version) =>
        new(type, version, null, null, null, null);

    private static async Task<IReadOnlyList<ManagedPlayerProfile>> SeedProfilesAsync(
        FirestoreDb database, int count)
    {
        string[] names = ["CopperGarden", "SilverMeteor", "QuietMaple", "BlueLantern"];
        var profiles = new List<ManagedPlayerProfile>();
        var repository = new FirestoreManagedPlayerProfileRepository(database);
        for (var index = 0; profiles.Count < count; index++)
        {
            var id = $"managed-solitaire-test-{index}";
            if (new[] { 1, 2, 3 }.Any(hour =>
                ManagedPlayerAvailabilityPolicy.IsSleeping(id, Start.AddHours(hour)))) continue;
            var profile = new ManagedPlayerProfile(id, names[profiles.Count], 2 + profiles.Count % 3,
                new HashSet<string>([ManagedPlayerGames.Blackjack],
                    StringComparer.OrdinalIgnoreCase), Start);
            Assert.True(await repository.TryCreateAsync(profile, default));
            profiles.Add(profile);
        }
        return profiles;
    }

    private static Task SeedBalanceAsync(FirestoreDb database, string userId) =>
        database.Collection("userBalances").Document($"{userId}_slotsCredits").SetAsync(
            new Dictionary<string, object>
            {
                ["available"] = 100L,
                ["availableFractionalCents"] = 0L,
                ["version"] = 1L,
                ["updatedAt"] = Timestamp.FromDateTime(Start)
            });

    private static async Task<long> ReadBalanceAsync(FirestoreDb database, string userId)
    {
        var snapshot = await database.Collection("userBalances")
            .Document($"{userId}_slotsCredits").GetSnapshotAsync();
        return checked(Field<long>(snapshot, "available") * 100 +
            (snapshot.TryGetValue<long>("availableFractionalCents", out var cents) ? cents : 0));
    }

    private static Task<IReadOnlyList<DocumentSnapshot>> LedgerAsync(FirestoreDb database) =>
        ReadCollectionAsync(database, "balanceTransactions");

    private static Task<IReadOnlyList<DocumentSnapshot>> AssignmentsAsync(FirestoreDb database) =>
        ReadCollectionAsync(database, "managedPlayerAssignments");

    private static async Task<IReadOnlyList<DocumentSnapshot>> ReadCollectionAsync(
        FirestoreDb database, string name) =>
        (await database.Collection(name).GetSnapshotAsync()).Documents;

    private static async Task<IReadOnlyList<DocumentSnapshot>> StoredPlayersAsync(
        FirestoreDb database, string matchId) =>
        (await database.Collection("solitaireMatchPlayers")
            .WhereEqualTo("matchId", matchId).GetSnapshotAsync()).Documents;

    private static T Field<T>(DocumentSnapshot snapshot, string field) => snapshot.GetValue<T>(field);

    private static SolitaireGameState ReadGame(DocumentSnapshot snapshot) =>
        JsonSerializer.Deserialize<SolitaireGameState>(Field<string>(snapshot, "gameStateJson"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static async Task AssertBotBalancesUntouchedAsync(
        FirestoreDb database, IReadOnlyList<ManagedPlayerProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            var balance = await database.Collection("userBalances")
                .Document($"{profile.UserId}_slotsCredits").GetSnapshotAsync();
            Assert.True(balance.Exists);
            Assert.Equal(0, Field<long>(balance, "available"));
            Assert.Equal(0, Field<long>(balance, "reserved"));
        }
    }

    private static void AssertAutomationNeutral(SolitaireSessionResponse session)
    {
        var json = JsonSerializer.Serialize(session, session.GetType(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var privateField in new[] { "isBot", "skill", "difficulty", "synthetic", "managed-", "dealSeed" })
            Assert.DoesNotContain(privateField, json, StringComparison.OrdinalIgnoreCase);
    }
}
