using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Cards.Blackjack.Table;
using Google.Cloud.Firestore;
using System.Text.Json;
using Xunit;

namespace FortuneForge.Server.Tests.Blackjack.Table;

public sealed class BlackjackTableFirestoreEmulatorTests : IClassFixture<BlackjackTableFirestoreEmulatorFixture>
{
    private static readonly DateTime Start = new(2026, 8, 15, 14, 0, 0, DateTimeKind.Utc);
    private readonly BlackjackTableFirestoreEmulatorFixture fixture;

    public BlackjackTableFirestoreEmulatorTests(BlackjackTableFirestoreEmulatorFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task DuplicateFreeJoinAcrossInstancesReconnectsToTheSameDurableTable()
    {
        var (database, first, suffix) = CreateStore();
        var second = new FirestoreBlackjackTableStore(database);
        var user = $"join-user-{suffix}";
        await SeedBalanceAsync(database, user, 5_000);
        const string key = "same-join-command";

        var attempts = await Task.WhenAll(
            first.JoinAsync(user, "CalmOtter", 0, Key(key), Start, default),
            second.JoinAsync(user, "CalmOtter", 0, Key(key), Start, default));
        Assert.All(attempts, result => Assert.IsType<BlackjackTableQueueSessionResponse>(result.Session));
        var started = await first.GetSessionAsync(user, Start.AddSeconds(6), default);
        var reconnected = await second.GetSessionAsync(user, Start.AddSeconds(6), default);

        var firstTable = Assert.IsType<BlackjackTablePlaySessionResponse>(started.Session);
        var secondTable = Assert.IsType<BlackjackTablePlaySessionResponse>(reconnected.Session);
        Assert.Equal(firstTable.Table.TableId, secondTable.Table.TableId);
        var ledger = await LedgerAsync(database, user);
        Assert.Empty(ledger);
        Assert.Equal(5_000, await ReadBalanceAsync(database, user));
        Assert.Equal(3, firstTable.Table.Seats.Count);
    }

    [Fact]
    public async Task JoinRacingRoundTransitionIsAtomicAndNeverSwapsASeatMidHand()
    {
        var (database, first, suffix) = CreateStore(DeckWithPrefix(
            "2|clubs", "3|clubs", "4|clubs", "5|clubs",
            "6|clubs", "7|clubs", "8|clubs", "9|clubs"));
        var second = new FirestoreBlackjackTableStore(database);
        var host = $"host-{suffix}";
        var late = $"late-{suffix}";
        await SeedBalanceAsync(database, host, 5_000);
        await SeedBalanceAsync(database, late, 5_000);
        await first.JoinAsync(host, "AmberFinch", 0, Key("host-join"), Start, default);
        var started = Assert.IsType<BlackjackTablePlaySessionResponse>(
            (await first.GetSessionAsync(host, Start.AddSeconds(6), default)).Session);
        await ReadyUntilActive(
            first, database, started.Table.TableId, [host], Start.AddSeconds(6), "race-ready");
        var raceTime = Start.AddSeconds(7);
        for (var attempt = 0; attempt < 10; attempt++, raceTime = raceTime.AddSeconds(2))
        {
            started = Assert.IsType<BlackjackTablePlaySessionResponse>(
                (await first.GetSessionAsync(host, raceTime, default)).Session);
            if (started.Table.LegalActions.Contains(BlackjackActions.Stand)) break;
        }
        Assert.Contains(BlackjackActions.Stand, started.Table.LegalActions);
        var before = started.Table.Seats.Select(seat => seat.SeatId).ToArray();

        await Task.WhenAll(
            second.JoinAsync(late, "SunnyWillow", 0, Key("late-join"), raceTime, default),
            first.ActionAsync(
                host,
                started.Table.TableId,
                BlackjackActions.Stand,
                started.Version,
                Key("host-stand"),
                raceTime,
                default));

        var hostState = Assert.IsType<BlackjackTablePlaySessionResponse>(
            (await first.GetSessionAsync(host, raceTime, default)).Session);
        var lateState = await second.GetSessionAsync(late, raceTime, default);
        if (hostState.Table.Phase == BlackjackTablePhases.Active)
            Assert.Equal(before, hostState.Table.Seats.Take(before.Length).Select(seat => seat.SeatId));
        Assert.True(lateState.Session is BlackjackTableQueueSessionResponse or BlackjackTablePlaySessionResponse);
        Assert.InRange(hostState.Table.Seats.Count, 3, 4);
        Assert.Equal(5_000, await ReadBalanceAsync(database, late));
    }

    [Fact]
    public async Task DurableWorkerLeaseAndRoundSettlementRemainSingleAcrossInstances()
    {
        var (database, first, suffix) = CreateStore();
        var second = new FirestoreBlackjackTableStore(database);
        var user = $"lease-user-{suffix}";
        await SeedBalanceAsync(database, user, 5_000);
        await first.JoinAsync(user, "CopperRobin", 0, Key("lease-join"), Start, default);
        await Task.WhenAll(
            first.SweepAsync(Start.AddSeconds(6), default),
            second.SweepAsync(Start.AddSeconds(6), default));

        var lease = await database.Collection("blackjackTableLeases").Document("deadline-worker").GetSnapshotAsync();
        Assert.True(lease.Exists);
        Assert.StartsWith("blackjack-table-worker-", Field<string>(lease, "owner"), StringComparison.Ordinal);
        var session = await second.GetSessionAsync(user, Start.AddSeconds(6), default);
        Assert.IsType<BlackjackTablePlaySessionResponse>(session.Session);
        var ledger = await LedgerAsync(database, user);
        Assert.Empty(ledger);
        Assert.Equal(5_000, await ReadBalanceAsync(database, user));
        var revenue = await database.Collection("blackjackTableRoundRevenue").GetSnapshotAsync();
        Assert.Empty(revenue.Documents);
    }

    [Fact]
    public async Task DuplicateDoubleAndPayoutAreSingleAndHouseRevenueExcludesBots()
    {
        var (database, first, suffix) = CreateStore(DoubleDeck());
        var second = new FirestoreBlackjackTableStore(database);
        var user = $"double-user-{suffix}";
        await SeedBalanceAsync(database, user, 10_000);
        await first.JoinAsync(user, "MellowBadger", 0, Key("double-join"), Start, default);
        var play = Assert.IsType<BlackjackTablePlaySessionResponse>(
            (await first.GetSessionAsync(user, Start.AddSeconds(6), default)).Session);

        await ReadyUntilActive(first, database, play.Table.TableId, [user], Start.AddSeconds(7), "double-ready");
        for (var attempt = 0; attempt < 10; attempt++)
        {
            play = Assert.IsType<BlackjackTablePlaySessionResponse>((await first.GetSessionAsync(
                user, Start.AddSeconds(9 + attempt * 2), default)).Session);
            if (play.Table.LegalActions.Contains(BlackjackActions.Double)) break;
        }
        Assert.Equal(BlackjackTablePhases.Active, play.Table.Phase);
        Assert.Contains(BlackjackActions.Double, play.Table.LegalActions);
        const string actionKey = "duplicate-double-command";
        await first.ActionAsync(
            user,
            play.Table.TableId,
            BlackjackActions.Double,
            play.Version,
            Key(actionKey),
            Start.AddSeconds(15),
            default);
        await second.ActionAsync(
            user,
            play.Table.TableId,
            BlackjackActions.Double,
            play.Version,
            Key(actionKey),
            Start.AddSeconds(16),
            default);
        await FinishRound(first, database, play.Table.TableId, Start.AddSeconds(17), "double-finish");

        var ledger = await LedgerAsync(database, user);
        Assert.Single(ledger, document => Field<string>(document, "type") == "blackjack-table-double");
        var roundId = $"{play.Table.TableId}-round-{play.Table.Round}";
        var roundPayouts = ledger.Where(document =>
            Field<string>(document, "type") == "blackjack-table-payout" &&
            Field<string>(document, "idempotencyKey") == roundId).ToArray();
        Assert.InRange(roundPayouts.Length, 0, 1);
        Assert.Equal(
            10_000 + ledger.Sum(document => checked((long)(Field<double>(document, "amount") * 100))),
            await ReadBalanceAsync(database, user));

        var revenues = await database.Collection("blackjackTableRoundRevenue")
            .WhereEqualTo("roundId", roundId)
            .GetSnapshotAsync();
        var revenue = Assert.Single(revenues.Documents);
        Assert.Equal(200, Field<long>(revenue, "humanWagerCents"));
        Assert.Equal(1, Field<long>(revenue, "humanPlayerCount"));
        Assert.Equal(0, Field<long>(revenue, "botFinancialContributionCents"));
        Assert.Equal(
            Field<long>(revenue, "humanWagerCents") - Field<long>(revenue, "humanPayoutCents"),
            Field<long>(revenue, "houseNetCents"));
        Assert.DoesNotContain(
            (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents,
            document => Field<string>(document, "userId").StartsWith("bot:", StringComparison.Ordinal));

        var history = Assert.Single(await first.GetHistoryAsync(user, 20, default));
        Assert.Equal(("blackjack", "credit-table", "completed", "paid"),
            (history.Game, history.Mode, history.ClaimStatus, history.SettlementStatus));
        Assert.Equal(history.PayoutCredits - history.WagerCredits, history.NetCredits);
        var storedResult = await database.Collection("cardGameResults").Document(history.ResultId).GetSnapshotAsync();
        Assert.Equal(user, Field<string>(storedResult, "userId"));
        Assert.Equal(play.Table.TableId, Field<string>(storedResult, "matchId"));
        Assert.Equal(checked(Field<long>(storedResult, "payoutCents") - Field<long>(storedResult, "wagerCents")),
            Field<long>(storedResult, "netCents"));
        Assert.Equal("paid", Field<string>(storedResult, "settlementStatus"));
        Assert.False(history.Seen);
        var seen = await first.MarkHistorySeenAsync(user, history.ResultId, Start.AddMinutes(2), default);
        var seenReplay = await second.MarkHistorySeenAsync(user, history.ResultId, Start.AddMinutes(3), default);
        Assert.True(seen.Seen);
        Assert.Equal(seen.SeenAtUtc, seenReplay.SeenAtUtc);
    }

    [Fact]
    public async Task DurableBoundariesFillEmptySeatsBeforeReplacingABotAtFullCapacity()
    {
        var (database, store, suffix) = CreateStore();
        var users = new[]
        {
            $"host-{suffix}", $"empty-four-{suffix}", $"empty-five-{suffix}", $"replace-{suffix}"
        };
        foreach (var user in users) await SeedBalanceAsync(database, user, 20_000);
        await store.JoinAsync(users[0], "HostPlayer", 0, Key("boundary-host"), Start, default);
        var host = Assert.IsType<BlackjackTablePlaySessionResponse>(
            (await store.GetSessionAsync(users[0], Start.AddSeconds(6), default)).Session);
        var tableId = host.Table.TableId;
        await ReadyUntilActive(store, database, tableId, users, Start.AddSeconds(7), "initial");

        await store.JoinAsync(users[1], "FourthSeat", 0, Key("boundary-four"), Start.AddSeconds(8), default);
        await FinishRound(store, database, tableId, Start.AddSeconds(9), "round-one");
        var table = await ReadTableAsync(database, tableId);
        Assert.Equal(4, table.Players.Count);
        Assert.Equal(3, table.Players.Single(player => player.ActorId == users[1]).Seat);
        Assert.Equal(2, table.Players.Count(player => player.IsBot));

        await ReadyUntilActive(store, database, tableId, users, Start.AddSeconds(10), "second");
        await store.JoinAsync(users[2], "FifthSeat", 0, Key("boundary-five"), Start.AddSeconds(11), default);
        await FinishRound(store, database, tableId, Start.AddSeconds(12), "round-two");
        table = await ReadTableAsync(database, tableId);
        Assert.Equal(5, table.Players.Count);
        Assert.Equal(4, table.Players.Single(player => player.ActorId == users[2]).Seat);
        Assert.Equal(2, table.Players.Count(player => player.IsBot));

        await ReadyUntilActive(store, database, tableId, users, Start.AddSeconds(13), "third");
        await store.JoinAsync(users[3], "Replacement", 0, Key("boundary-replace"), Start.AddSeconds(14), default);
        await FinishRound(store, database, tableId, Start.AddSeconds(15), "round-three");
        table = await ReadTableAsync(database, tableId);
        Assert.Equal(5, table.Players.Count);
        Assert.Equal(4, table.Players.Count(player => !player.IsBot));
        Assert.Single(table.Players, player => player.IsBot);
        Assert.Contains(table.Players, player => player.ActorId == users[3]);
    }

    [Theory]
    [InlineData(1, 3, 2)]
    [InlineData(2, 3, 1)]
    [InlineData(3, 3, 0)]
    [InlineData(4, 4, 0)]
    [InlineData(5, 5, 0)]
    public async Task DurableGraceStartUsesThreeAsMinimumAndFiveAsCapacity(
        int humanCount,
        int occupiedCount,
        int botCount)
    {
        var (database, store, suffix) = CreateStore();
        for (var index = 0; index < humanCount; index++)
        {
            var user = $"seat-{index}-{suffix}";
            await SeedBalanceAsync(database, user, 5_000);
            await store.JoinAsync(
                user, $"Player{index}", 0, Key($"seat-join-{index}"), Start, default);
        }
        var session = Assert.IsType<BlackjackTablePlaySessionResponse>(
            (await store.GetSessionAsync($"seat-0-{suffix}", Start.AddSeconds(6), default)).Session);
        var table = await ReadTableAsync(database, session.Table.TableId);
        Assert.Equal(occupiedCount, table.Players.Count);
        Assert.Equal(humanCount, table.Players.Count(player => !player.IsBot));
        Assert.Equal(botCount, table.Players.Count(player => player.IsBot));
    }

    [Fact]
    public async Task AccumulatedCancelledLifecyclesPruneShardTicketsSessionsAndState()
    {
        var (database, store, suffix) = CreateStore();
        const int lifecycleCount = 32;

        for (var index = 0; index < lifecycleCount; index++)
        {
            var user = $"lifecycle-{index}-{suffix}";
            await SeedBalanceAsync(database, user, 5_000);
            var joined = Assert.IsType<BlackjackTableQueueSessionResponse>((await store.JoinAsync(
                user,
                $"Lifecycle{index}",
                0,
                Key($"lifecycle-join-{index}"),
                Start.AddMilliseconds(index),
                default)).Session);
            var cancelKey = Key($"lifecycle-cancel-{index}");
            var cancelled = await store.CancelAsync(
                user,
                joined.TicketId,
                joined.Version,
                cancelKey,
                Start.AddSeconds(1).AddMilliseconds(index),
                default);
            Assert.IsType<BlackjackTableIdleSessionResponse>(cancelled.Session);
            var replay = await store.CancelAsync(
                user,
                joined.TicketId,
                joined.Version,
                cancelKey,
                Start.AddSeconds(2).AddMilliseconds(index),
                default);
            Assert.IsType<BlackjackTableIdleSessionResponse>(replay.Session);
            Assert.Equal(5_000, await ReadBalanceAsync(database, user));
        }

        Assert.Empty((await database.Collection("blackjackTableState").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("blackjackTableTickets").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("blackjackTableSessions").GetSnapshotAsync()).Documents);
        var guards = (await database.Collection("blackjackTableCommandGuards").GetSnapshotAsync()).Documents;
        Assert.Equal(lifecycleCount * 2, guards.Count);
        Assert.All(guards, guard => Assert.True(guard.ContainsField("expiresAt")));
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task ConcurrentFiveHumanPollAndJoinReplaysStayInOneBoundedShard()
    {
        var (database, store, suffix) = CreateStore();
        var stores = Enumerable.Range(0, 8)
            .Select(_ => new FirestoreBlackjackTableStore(database))
            .ToArray();
        var users = Enumerable.Range(0, 5).Select(index => $"poll-{index}-{suffix}").ToArray();
        foreach (var user in users) await SeedBalanceAsync(database, user, 5_000);

        var joins = await Task.WhenAll(users.Select((user, index) => stores[index].JoinAsync(
            user,
            $"PollPlayer{index}",
            0,
            Key($"poll-join-{index}"),
            Start,
            default)));
        Assert.All(joins, result => Assert.IsType<BlackjackTableQueueSessionResponse>(result.Session));

        var polls = await Task.WhenAll(Enumerable.Range(0, 10).Select(index =>
            stores[index % stores.Length].GetSessionAsync(
                users[index % users.Length],
                Start.AddSeconds(6),
                default)));
        var sessions = polls.Select(result => Assert.IsType<BlackjackTablePlaySessionResponse>(result.Session)).ToArray();
        var tableId = Assert.Single(sessions.Select(session => session.Table.TableId).Distinct(StringComparer.Ordinal));
        Assert.All(sessions, session => Assert.Equal(5, session.Table.Seats.Count));

        var replays = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            stores[index % stores.Length].JoinAsync(
                users[0],
                "PollPlayer0",
                0,
                Key("poll-join-0"),
                Start.AddSeconds(7),
                default)));
        Assert.All(replays, result =>
            Assert.Equal(tableId, Assert.IsType<BlackjackTablePlaySessionResponse>(result.Session).Table.TableId));

        var shards = (await database.Collection("blackjackTableState").GetSnapshotAsync()).Documents;
        var shard = Assert.Single(shards);
        Assert.Equal(5, Field<long>(shard, "humanCount"));
        Assert.Equal(1, Field<long>(shard, "tableCount"));
        Assert.Equal(0, Field<long>(shard, "queuedTicketCount"));
        Assert.InRange(Field<string>(shard, "stateJson").Length, 1, 64_000);
        Assert.False(shard.ContainsField("guards"));
        foreach (var user in users)
        {
            Assert.Equal(5_000, await ReadBalanceAsync(database, user));
            Assert.Empty(await LedgerAsync(database, user));
        }
    }

    private (FirestoreDb Database, FirestoreBlackjackTableStore Store, string Suffix) CreateStore(
        IReadOnlyList<string>? deck = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-fortuneforge-blackjack-{suffix}",
            Endpoint = fixture.Endpoint,
            ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
            EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
        }.Build();
        return (database, deck is null
            ? new FirestoreBlackjackTableStore(database)
            : new FirestoreBlackjackTableStore(database, () => deck.ToArray(), () => 123UL), suffix);
    }

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
        var document = await database.Collection("userBalances").Document($"{userId}_slotsCredits").GetSnapshotAsync();
        return checked(Field<long>(document, "available") * 100 + Field<long>(document, "availableFractionalCents"));
    }

    private static async Task<IReadOnlyList<DocumentSnapshot>> LedgerAsync(FirestoreDb database, string userId) =>
        (await database.Collection("balanceTransactions").WhereEqualTo("userId", userId).GetSnapshotAsync()).Documents;

    private static async Task<BlackjackTableState> ReadTableAsync(FirestoreDb database, string tableId)
    {
        var snapshot = await database.Collection("blackjackTables").Document(tableId).GetSnapshotAsync();
        return JsonSerializer.Deserialize<BlackjackTableState>(
            Field<string>(snapshot, "tableJson"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The persisted Blackjack table could not be read.");
    }

    private static async Task ReadyUntilActive(
        FirestoreBlackjackTableStore store,
        FirestoreDb database,
        string tableId,
        IReadOnlyList<string> knownUsers,
        DateTime nowUtc,
        string keyPrefix)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var table = await ReadTableAsync(database, tableId);
            if (table.Phase == BlackjackTablePhases.Active) return;
            if (table.Phase == BlackjackTablePhases.Dealer)
            {
                var observer = table.Players.First(player => !player.IsBot).ActorId;
                await store.GetSessionAsync(observer, nowUtc.AddSeconds(attempt * 2 + 1), default);
                continue;
            }
            if (table.Phase == BlackjackTablePhases.Insurance)
            {
                var active = table.Players.Single(player => player.Seat == table.ActiveSeat);
                if (active.IsBot)
                {
                    var observer = table.Players.First(player => !player.IsBot).ActorId;
                    await store.GetSessionAsync(observer, nowUtc.AddSeconds(attempt * 2 + 1), default);
                }
                else
                {
                    Assert.Contains(active.ActorId, knownUsers);
                    await store.ActionAsync(
                        active.ActorId,
                        tableId,
                        BlackjackActions.DeclineInsurance,
                        table.Version,
                        Key($"{keyPrefix}-insurance-{attempt}"),
                        nowUtc.AddSeconds(attempt * 2 + 1),
                        default);
                }
                continue;
            }
            Assert.Equal(BlackjackTablePhases.Betting, table.Phase);
            foreach (var player in table.Players.Where(player => !player.IsBot && player.NextWagerCents == 0).ToArray())
            {
                Assert.Contains(player.ActorId, knownUsers);
                table = await ReadTableAsync(database, tableId);
                if (table.Phase != BlackjackTablePhases.Betting) break;
                var current = table.Players.Single(value => value.ActorId == player.ActorId);
                if (current.NextWagerCents != 0) continue;
                await store.WagerAsync(
                    player.ActorId,
                    tableId,
                    100,
                    table.Version,
                    Key($"{keyPrefix}-{attempt}-{Array.IndexOf(knownUsers.ToArray(), player.ActorId)}"),
                    nowUtc.AddSeconds(attempt * 2),
                    default);
            }
            table = await ReadTableAsync(database, tableId);
            if (table.Phase == BlackjackTablePhases.Betting &&
                table.Players.Where(player => !player.IsBot).All(player => player.NextWagerCents > 0))
            {
                var observer = table.Players.First(player => !player.IsBot).ActorId;
                await store.GetSessionAsync(observer, nowUtc.AddSeconds(attempt * 2 + 1), default);
            }
        }
        Assert.Equal(BlackjackTablePhases.Active, (await ReadTableAsync(database, tableId)).Phase);
    }

    private static async Task FinishRound(
        FirestoreBlackjackTableStore store,
        FirestoreDb database,
        string tableId,
        DateTime nowUtc,
        string keyPrefix)
    {
        for (var action = 0; action < 40; action++)
        {
            var table = await ReadTableAsync(database, tableId);
            if (table.Phase == BlackjackTablePhases.Betting) return;
            var stepTime = nowUtc.AddSeconds(action * 2);
            var active = table.ActiveSeat is { } seat
                ? table.Players.Single(value => value.Seat == seat)
                : null;
            if (table.Phase == BlackjackTablePhases.Active && active is { IsBot: false } && table.Transition is null)
            {
                await store.ActionAsync(
                    active.ActorId,
                    tableId,
                    BlackjackActions.Stand,
                    table.Version,
                    Key($"{keyPrefix}-{action}"),
                    stepTime,
                    default);
            }
            else
            {
                var observer = table.Players.First(player => !player.IsBot).ActorId;
                await store.GetSessionAsync(observer, stepTime, default);
            }
        }
        Assert.Equal(BlackjackTablePhases.Betting, (await ReadTableAsync(database, tableId)).Phase);
    }

    private static T Field<T>(DocumentSnapshot document, string name) =>
        document.TryGetValue<T>(name, out var value)
            ? value
            : throw new InvalidOperationException($"Firestore field {name} is missing.");

    private static string Key(string value) => value.PadRight(16, 'x');

    private static IReadOnlyList<string> DoubleDeck() => DeckWithPrefix(
        "5|spades", "2|clubs", "3|clubs", "6|hearts",
        "6|diamonds", "5|clubs", "7|clubs", "10|spades", "K|hearts");

    private static IReadOnlyList<string> DeckWithPrefix(params string[] prefix)
    {
        var suits = new[] { "clubs", "diamonds", "hearts", "spades" };
        var ranks = new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
        return prefix.Concat(suits.SelectMany(suit => ranks.Select(rank => $"{rank}|{suit}")))
            .Distinct(StringComparer.Ordinal)
            .Take(52)
            .ToArray();
    }
}
