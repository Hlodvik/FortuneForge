using System.Text.Json;
using FortuneForge.Games.ThreeCardPoker;
using FortuneForge.Server.Cards.ThreeCardPoker.Table;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.ThreeCardPoker;
using FortuneForge.Server.Matchmaking;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.ThreeCardPoker;

// Reuse the existing card-table emulator harness; every test has an isolated project.
[Collection("Blackjack table Firestore emulator")]
public sealed class ThreeCardPokerTableFirestoreEmulatorTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly DateTime Start = new(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DuplicateJoinAndAntePairPlusWagerAcrossInstancesAreAtomic()
    {
        var (database, first, second) = CreateStores();
        await SeedPlayers(database);
        var joins = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => (index % 2 == 0 ? first : second).JoinAsync(
            "alice", "Player A", 0, Key("join-alice"), Start, default)));
        Assert.All(joins, joined => Assert.IsType<ThreeCardPokerTablePlaySessionResponse>(joined.Session));
        Assert.Single(joins.Select(joined => ((ThreeCardPokerTablePlaySessionResponse)joined.Session).Table.TableId).Distinct());
        var bob = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await first.JoinAsync(
            "bob", "Player B", 0, Key("join-bob"), Start, default)).Session);
        Assert.NotEqual(((ThreeCardPokerTablePlaySessionResponse)joins[0].Session).Table.TableId, bob.Table.TableId);
        var now = Start;
        var session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await first.GetSessionAsync("alice", now, default)).Session);
        var table = await ReadTable(database, session.Table.TableId);
        Assert.Single(table.Players);
        var actor = table.Players.Single(player => player.Seat == table.ActiveSeat).ActorId;
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(index => (index % 2 == 0 ? first : second).WagerAsync(
            actor, table.TableId, 100, 100, session.Version, Key("ante-pair-once"), now, default)));
        Assert.Equal(9_800, await ReadBalance(database, actor));
        var debit = Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(-2.0, debit.GetValue<double>("amount"));
        await Assert.ThrowsAsync<ThreeCardPokerTableConflictException>(() => second.WagerAsync(
            actor, table.TableId, 100, 200, session.Version, Key("ante-pair-once"), now, default));
        Assert.Equal(9_800, await ReadBalance(database, actor));
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        var reconnected = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await second.GetSessionAsync(actor, now, default)).Session);
        Assert.Equal(table.TableId, reconnected.Table.TableId);
    }

    [Theory]
    [InlineData("play", 10_200, 300, 500)]
    [InlineData("fold", 10_000, 200, 200)]
    public async Task ConcurrentDecisionAndPayoutCommitOneWalletLedgerHistoryAndRevenue(string action, long expectedBalance, long expectedWager, long expectedPayout)
    {
        var (database, first, second) = CreateStores();
        await SeedPlayers(database);
        await first.JoinAsync("alice", "Player", 0, Key("join-alice"), Start, default);
        var now = Start;
        var session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await first.GetSessionAsync("alice", now, default)).Session);
        var table = await ReadTable(database, session.Table.TableId);
        var actor = table.Players.OrderBy(player => player.Seat).First().ActorId;
        for (var step = 0; step < 100; step++)
        {
            session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await first.GetSessionAsync(actor, now, default)).Session);
            if (session.Table.Phase == ThreeCardPokerTablePhases.Active && session.Table.Transition is null) break;
            if (session.Table.Phase == ThreeCardPokerTablePhases.Betting && session.Table.Transition == "human-wager")
            {
                table = await ReadTable(database, session.Table.TableId);
                var wagerActor = table.Players.Single(player => player.Seat == table.ActiveSeat).ActorId;
                await first.WagerAsync(wagerActor, table.TableId, 100, wagerActor == actor ? 100 : 0, session.Version,
                    Key($"ante-{wagerActor}"), now, default);
            }
            now = now.AddMilliseconds(200);
        }
        Assert.Equal(ThreeCardPokerTablePhases.Active, session.Table.Phase);
        Assert.Contains(ThreeCardPokerActions.Play, session.Table.LegalActions);
        Assert.All(session.Table.Dealer.Cards, card => Assert.True(card.Hidden));
        Assert.Single(session.Table.Seats);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(index => (index % 2 == 0 ? first : second).ActionAsync(
            actor, session.Table.TableId, action, session.Version, Key("decide-once"), now, default)));
        Assert.Equal(action == "play" ? 9_700 : 9_800, await ReadBalance(database, actor));
        await Assert.ThrowsAsync<ThreeCardPokerTableConflictException>(() => second.ActionAsync(
            actor, session.Table.TableId, action, session.Version, Key("stale-decision"), now, default));

        for (var step = 0; step < 100; step++)
        {
            now = now.AddSeconds(1);
            session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await first.GetSessionAsync(actor, now, default)).Session);
            if (session.Table.Phase == ThreeCardPokerTablePhases.Dealer) break;
            if (session.Table.Phase == ThreeCardPokerTablePhases.Active && session.Table.Transition is null)
            {
                table = await ReadTable(database, session.Table.TableId);
                var nextActor = table.Players.Single(player => player.Seat == table.ActiveSeat).ActorId;
                await first.ActionAsync(nextActor, table.TableId, ThreeCardPokerActions.Play, session.Version,
                    Key($"decide-{nextActor}"), now, default);
            }
        }
        Assert.Equal(ThreeCardPokerTablePhases.Dealer, session.Table.Phase);
        now = now.Add(ThreeCardPokerTableEngine.DealerCardDuration);
        var settled = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => (index % 2 == 0 ? first : second).GetSessionAsync(actor, now, default)));
        Assert.All(settled, result => Assert.Equal(ThreeCardPokerTablePhases.Settled,
            Assert.IsType<ThreeCardPokerTablePlaySessionResponse>(result.Session).Table.Phase));
        Assert.Equal(expectedBalance, await ReadBalance(database, actor));
        var revenue = Assert.Single((await database.Collection("threeCardPokerTableRoundRevenue").GetSnapshotAsync()).Documents);
        Assert.Equal(0, revenue.GetValue<long>("botFinancialContributionCents"));
        Assert.Equal(revenue.GetValue<long>("humanWagerCents") - revenue.GetValue<long>("humanPayoutCents"), revenue.GetValue<long>("houseNetCents"));
        var history = Assert.Single(await first.GetHistoryAsync(actor, 10, default));
        Assert.Equal(expectedWager / 100m, history.WagerCredits);
        Assert.Equal(expectedPayout / 100m, history.PayoutCredits);
        var allLedger = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(allLedger.Count, allLedger.Select(item => item.Id).Distinct().Count());
        var actorLedger = allLedger.Where(item => item.GetValue<string>("userId") == actor).ToArray();
        Assert.Equal(action == "play" ? 3 : 2, actorLedger.Length);
        Assert.Single(actorLedger, item => item.GetValue<string>("type") == "three-card-poker-table-payout");
        await second.GetSessionAsync(actor, now, default);
        Assert.Equal(allLedger.Count, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.Single(await second.GetHistoryAsync(actor, 10, default));
    }

    [Fact]
    public async Task SoloTableNeverReservesManagedPlayersOrChargesTheirWallets()
    {
        var (database, _, _) = CreateStores();
        var profiles = new FirestoreManagedPlayerProfileRepository(database);
        var queuer = new ManagedPlayerQueuer(new ManagedPlayerProfileGenerator(profiles), profiles,
            new FirestoreManagedPlayerAssignmentStore(database));
        var matchmaker = new MultiplayerMatchmaker(queuer);
        var first = new FirestoreThreeCardPokerTableStore(database, PairDeck, () => 3UL, matchmaker);
        var second = new FirestoreThreeCardPokerTableStore(database, PairDeck, () => 3UL, matchmaker);
        await SeedPlayers(database);
        await first.JoinAsync("alice", "Player A", 0, Key("solo-table-join"), Start, default);
        var now = Start;
        var starts = await Task.WhenAll(first.GetSessionAsync("alice", now, default), second.GetSessionAsync("alice", now, default));
        var session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>(starts[0].Session);
        Assert.All(starts, value => Assert.Equal(session.Table.TableId,
            Assert.IsType<ThreeCardPokerTablePlaySessionResponse>(value.Session).Table.TableId));
        var table = await ReadTable(database, session.Table.TableId);
        Assert.Equal("alice", Assert.Single(table.Players).ActorId);
        var assignments = (await database.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents;
        Assert.Empty(assignments);
        Assert.Equal(10_000, await ReadBalance(database, "alice"));
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        await first.WagerAsync("alice", table.TableId, 100, 100, session.Version, Key("managed-table-ante"), now, default);
        var folded = false;
        for (var step = 0; step < 100; step++)
        {
            now = now.AddSeconds(1);
            session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await second.GetSessionAsync("alice", now, default)).Session);
            if (session.Table.Phase == ThreeCardPokerTablePhases.Settled) break;
            if (session.Table.LegalActions.Contains(ThreeCardPokerActions.Fold))
            {
                await first.ActionAsync("alice", table.TableId, ThreeCardPokerActions.Fold, session.Version,
                    Key("managed-table-fold"), now, default);
                folded = true;
            }
        }
        Assert.True(folded);
        Assert.Equal(ThreeCardPokerTablePhases.Settled, session.Table.Phase);
        var humanHistory = Assert.Single(await first.GetHistoryAsync("alice", 10, default));
        var revenue = Assert.Single((await database.Collection("threeCardPokerTableRoundRevenue").GetSnapshotAsync()).Documents);
        Assert.Equal(1, revenue.GetValue<long>("humanPlayerCount"));
        Assert.Equal(200, revenue.GetValue<long>("humanWagerCents"));
        Assert.Equal((long)(humanHistory.PayoutCredits * 100m), revenue.GetValue<long>("humanPayoutCents"));
        Assert.Equal(0, revenue.GetValue<long>("botFinancialContributionCents"));
        Assert.Equal(10_000 - 200 + (long)(humanHistory.PayoutCredits * 100m), await ReadBalance(database, "alice"));
        Assert.All((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents,
            entry => Assert.Equal("alice", entry.GetValue<string>("userId")));
        Assert.Empty((await database.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);
    }
    private (FirestoreDb Database, FirestoreThreeCardPokerTableStore First, FirestoreThreeCardPokerTableStore Second) CreateStores()
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-fortuneforge-three-card-poker-{Guid.NewGuid():N}", Endpoint = fixture.Endpoint,
            ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
        }.Build();
        return (database,
            new FirestoreThreeCardPokerTableStore(database, PairDeck, () => 3UL),
            new FirestoreThreeCardPokerTableStore(database, PairDeck, () => 3UL));
    }

    private static Task SeedPlayers(FirestoreDb database) => Task.WhenAll(new[] { "alice", "bob", "charlie" }.Select(user =>
        database.Collection("userBalances").Document($"{user}_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["available"] = 100L, ["availableFractionalCents"] = 0L, ["version"] = 1L, ["updatedAt"] = Timestamp.FromDateTime(Start)
        })));
    private static async Task<long> ReadBalance(FirestoreDb database, string user)
    {
        var value = await database.Collection("userBalances").Document($"{user}_slotsCredits").GetSnapshotAsync();
        return value.GetValue<long>("available") * 100 + value.GetValue<long>("availableFractionalCents");
    }
    private static async Task<ThreeCardPokerTableState> ReadTable(FirestoreDb database, string tableId)
    {
        var value = await database.Collection("threeCardPokerTables").Document(tableId).GetSnapshotAsync();
        return JsonSerializer.Deserialize<ThreeCardPokerTableState>(value.GetValue<string>("tableJson"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private static string Key(string value) => value.PadRight(16, 'x');
    private static IReadOnlyList<string> PairDeck()
    {
        var prefix = new[]
        {
            "Q|spades", "2|clubs", "3|clubs", "J|hearts",
            "Q|diamonds", "5|clubs", "7|clubs", "9|spades",
            "4|spades", "8|clubs", "K|clubs", "6|diamonds"
        };
        var deck = new[] { "clubs", "diamonds", "hearts", "spades" }.SelectMany(suit =>
            new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" }.Select(rank => $"{rank}|{suit}"));
        return prefix.Concat(deck).Distinct(StringComparer.Ordinal).ToArray();
    }
}
