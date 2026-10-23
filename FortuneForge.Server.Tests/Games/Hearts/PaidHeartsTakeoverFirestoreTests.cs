using System.Text.Json;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Bots.Hearts;
using FortuneForge.Server.Games.Hearts;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Hearts;

public sealed partial class PaidHeartsFirestoreTests
{
    [Fact]
    public async Task Active_house_takeover_inherits_hand_score_passes_and_tricks_with_full_current_turn_and_constant_pot()
    {
        var (db, store, _) = Create();
        var table = await StartHouse(db, store);
        await Seed(db, "bruno");
        var state = table.State!;
        foreach (var seat in Enum.GetValues<PlayerSeat>())
            state = HeartsMatchEngine.Apply(state, new PassHeartsCards(seat,
                HeartsManagedActionPolicy.ChoosePass(state.Round, seat, 3, 99, 0))).State;
        var version = 0;
        while (state.Round.CompletedTricks.Count < 2 || state.Turn != PlayerSeat.East)
            state = HeartsMatchEngine.Apply(state, new PlayHeartsCard(state.Turn,
                HeartsManagedActionPolicy.ChooseCard(state.Round, state.Turn, 3, 99, version++))).State;
        table.State = state with { Score = new HeartsMatchScore(35, 19, 67, 42) };
        table.ScheduledSeat = PlayerSeat.East;
        table.NextActionAtUtc = Start.AddSeconds(7);
        await SaveTakeoverTable(db, table);
        var before = JsonSerializer.Serialize(table.State, Json);
        var hand = table.State.Round.HandFor(PlayerSeat.East).Select(card => card.Code).ToArray();

        var joined = await store.JoinAsync("bruno", "Bruno", 1m, "take-house", Start.AddSeconds(6), default);
        var changed = await Stored(db, table.Id);
        Assert.Equal("match", joined.Kind);
        Assert.Equal("east", joined.Table!.Match.HumanSeat);
        Assert.Equal(hand, joined.Table.Match.Hand.Select(card => card.Code));
        Assert.Equal(before, JsonSerializer.Serialize(changed.State, Json));
        Assert.Equal(19, joined.Table.Match.Score.East);
        Assert.Equal(Start.AddSeconds(51), changed.NextActionAtUtc);
        Assert.Equal(4m, joined.Table.PotCredits);
        Assert.Contains("existing cards and score", joined.Notice);
        Assert.Contains("pot is unchanged", joined.Notice);
        Assert.Equal(99m, await Credits(db, "bruno"));
        Assert.Equal(4, changed.Players.Count);
        Assert.Equal(2, changed.Players.Count(player => player.IsAccountBacked));
        Assert.Equal(2, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        var funding = await db.Collection("heartsPaidFunding").Document(table.Id).GetSnapshotAsync();
        Assert.Equal(100L, funding.GetValue<long>("initialHumanEntryCents"));
        Assert.Equal(300L, funding.GetValue<long>("initialHouseFundedEntryCents"));
        Assert.Equal(200L, funding.GetValue<long>("humanEntryCents"));
        Assert.Equal(200L, funding.GetValue<long>("houseFundedEntryCents"));
        Assert.Equal(400L, funding.GetValue<long>("grossPoolCents"));
        var transfer = Assert.Single((await db.Collection("heartsPaidFundingTransfers").GetSnapshotAsync()).Documents);
        Assert.Equal(19L, transfer.GetValue<long>("inheritedMatchScore"));
        Assert.Equal(state.Round.CompletedTricks.Count, transfer.GetValue<long>("inheritedCompletedTricks"));
        Assert.DoesNotContain("cards", JsonSerializer.Serialize(transfer.ToDictionary(), Json), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("seed", JsonSerializer.Serialize(joined, Json), StringComparison.OrdinalIgnoreCase);
        var retries = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => store.JoinAsync(
            "bruno", "Bruno", 1m, "take-house", Start.AddSeconds(6), default)));
        Assert.All(retries, retry => Assert.Equal(table.Id, retry.Table!.Match.MatchId.ToString("N")));
        Assert.Equal(99m, await Credits(db, "bruno"));
        Assert.Single((await db.Collection("heartsPaidFundingTransfers").GetSnapshotAsync()).Documents);
        await store.SweepAsync(Start.AddSeconds(7), default);
        var oldDue = await Stored(db, table.Id);
        Assert.Equal(changed.Version, oldDue.Version);
        Assert.Equal(before, JsonSerializer.Serialize(oldDue.State, Json));
        Assert.False(oldDue.Players.Single(player => player.UserId == "bruno").HasLeft);
        await store.SweepAsync(Start.AddSeconds(51), default);
        var expired = await Stored(db, table.Id);
        Assert.True(expired.Players.Single(player => player.UserId == "bruno").HasLeft);
        Assert.Equal(1, expired.Players.Single(player => player.UserId == "bruno").MissedTurns);
        Assert.Equal(99m, await Credits(db, "bruno"));
    }

    [Fact]
    public async Task Taking_over_unscheduled_house_keeps_another_players_running_timer_and_submitted_pass()
    {
        var (db, store, _) = Create();
        var table = await StartHouse(db, store);
        await Seed(db, "bruno");
        table.State = HeartsMatchEngine.Apply(table.State!, new PassHeartsCards(PlayerSeat.East,
            table.State!.Round.HandFor(PlayerSeat.East).Take(3).ToArray())).State;
        table.ScheduledSeat = PlayerSeat.North;
        table.NextActionAtUtc = Start.AddSeconds(30);
        await SaveTakeoverTable(db, table);
        var before = JsonSerializer.Serialize(table.State, Json);
        await store.JoinAsync("bruno", "Bruno", 1m, "take-house", Start.AddSeconds(6), default);
        var changed = await Stored(db, table.Id);
        Assert.Equal(Start.AddSeconds(30), changed.NextActionAtUtc);
        Assert.Equal(PlayerSeat.North, changed.ScheduledSeat);
        Assert.Equal(before, JsonSerializer.Serialize(changed.State, Json));
        Assert.Contains(changed.State!.Round.SubmittedPasses, pass => pass.Seat == PlayerSeat.East);
    }

    [Fact]
    public async Task Concurrent_human_takeovers_fill_only_three_house_seats_and_charge_each_account_once()
    {
        var (db, store, _) = Create();
        var table = await StartHouse(db, store);
        var users = new[] { "bruno", "cara", "drew", "emma", "frank", "gwen", "hugo" };
        foreach (var user in users) await Seed(db, user);
        await Task.WhenAll(users.Select(user => store.JoinAsync(user, user, 1m, $"join-{user}", Start.AddSeconds(6), default)));
        var changed = await Stored(db, table.Id);
        Assert.Equal(4, changed.Players.Count);
        Assert.All(changed.Players, player => Assert.True(player.IsAccountBacked));
        Assert.Equal(3, (await db.Collection("heartsPaidFundingTransfers").GetSnapshotAsync()).Count);
        Assert.Equal(8, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.All(await Task.WhenAll(users.Select(user => Credits(db, user))), amount => Assert.Equal(99m, amount));
        var funding = await db.Collection("heartsPaidFunding").Document(table.Id).GetSnapshotAsync();
        Assert.Equal(400L, funding.GetValue<long>("humanEntryCents"));
        Assert.Equal(0L, funding.GetValue<long>("houseFundedEntryCents"));
        Assert.Equal(400L, funding.GetValue<long>("grossPoolCents"));
        Assert.Empty((await Availability(db, 100).GetSnapshotAsync()).Documents);
        var second = Assert.Single((await db.Collection("heartsPaidMatches").GetSnapshotAsync()).Documents,
            snapshot => snapshot.Id != table.Id);
        Assert.Equal(4, (await Stored(db, second.Id)).Players.Count);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Parallel_takeover_capacity_small_batch(int count)
    {
        var (db, store, _) = Create();
        var table = await StartHouse(db, store);
        var users = Enumerable.Range(0, count).Select(index => $"incoming-{index}").ToArray();
        foreach (var user in users) await Seed(db, user);
        await Task.WhenAll(users.Select(user => store.JoinAsync(user, user, 1m, $"join-{user}", Start.AddSeconds(6), default)));
        var changed = await Stored(db, table.Id);
        Assert.Equal(4, changed.Players.Count);
        Assert.Equal(4, changed.Players.Count(player => player.IsAccountBacked));
        Assert.Equal(3, (await db.Collection("heartsPaidFundingTransfers").GetSnapshotAsync()).Count);
        Assert.Equal(count + 1, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.All(await Task.WhenAll(users.Select(user => Credits(db, user))), amount => Assert.Equal(99m, amount));
    }

    [Fact]
    public async Task Four_concurrent_humans_fill_the_same_new_queue_without_house_admission()
    {
        var (db, store, _) = Create();
        var users = new[] { "north-user", "east-user", "south-user", "west-user" };
        foreach (var user in users) await Seed(db, user);
        await Task.WhenAll(users.Select(user => store.JoinAsync(user, user, 1m, $"join-{user}", Start, default)));
        var matches = await db.Collection("heartsPaidMatches").GetSnapshotAsync();
        var table = await Stored(db, Assert.Single(matches.Documents).Id);
        Assert.Equal("match", table.Status);
        Assert.Equal(4, table.Players.Count);
        Assert.All(table.Players, player => Assert.True(player.IsAccountBacked));
        Assert.Equal(4, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task Humans_replace_house_seats_one_by_one_until_full_then_search_for_a_new_table()
    {
        var (db, store, _) = Create();
        var table = await StartHouse(db, store);
        var users = new[] { "bruno", "cara", "drew" };
        for (var index = 0; index < users.Length; index++)
        {
            var user = users[index];
            await Seed(db, user);
            var joined = await store.JoinAsync(user, user, 1m, $"join-{user}", Start.AddSeconds(6), default);
            Assert.Equal(table.Id, joined.Table!.Match.MatchId.ToString("N"));
            var current = await Stored(db, table.Id);
            Assert.Equal(index + 2, current.Players.Count(player => player.IsAccountBacked));
            Assert.Equal(2 - index, current.Players.Count(player => !player.IsAccountBacked));
            Assert.Equal(4, current.Players.Count);
            Assert.Equal(4m, joined.Table.PotCredits);
        }
        await Seed(db, "emma");
        var next = await store.JoinAsync("emma", "Emma", 1m, "join-emma", Start.AddSeconds(6), default);
        Assert.Equal("queue", next.Kind);
        Assert.NotEqual(table.Id, next.Queue!.MatchId);
        Assert.Single(next.Queue.Players);
    }

    [Fact]
    public async Task Available_active_house_seat_is_preferred_over_an_existing_human_waiting_queue()
    {
        var (db, store, _) = Create();
        var table = await StartHouse(db, store);
        foreach (var user in new[] { "bruno", "cara", "drew" }) await Seed(db, user);
        await store.JoinAsync("bruno", "Bruno", 1m, "join-bruno", Start.AddSeconds(6), default);
        table = await Stored(db, table.Id);
        table.ScheduledSeat = PlayerSeat.North;
        table.NextActionAtUtc = Start.AddSeconds(7);
        await SaveTakeoverTable(db, table);
        // While the existing human's timeout is overdue, this table cannot take
        // a new player until its authoritative forfeiture is processed.
        var waiting = await store.JoinAsync("cara", "Cara", 1m, "join-cara", Start.AddSeconds(8), default);
        Assert.Equal("queue", waiting.Kind);
        await store.GetAsync("bruno", table.Id, Start.AddSeconds(8), default);
        var joined = await store.JoinAsync("drew", "Drew", 1m, "join-drew", Start.AddSeconds(8), default);
        Assert.Equal("match", joined.Kind);
        Assert.Equal(table.Id, joined.Table!.Match.MatchId.ToString("N"));
        var queue = await Stored(db, waiting.Queue!.MatchId);
        Assert.Equal("queue", queue.Status);
        Assert.Equal("cara", Assert.Single(queue.Players).UserId);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("no-active-human")]
    [InlineData("legacy-profiles")]
    [InlineData("wrong-entry")]
    public async Task Active_join_ignores_ineligible_or_stale_availability_rows(string kind)
    {
        var (db, store, _) = Create();
        var table = await StartHouse(db, store);
        await Seed(db, "bruno");
        if (kind == "completed") table.Status = "result";
        if (kind == "no-active-human") table.Players.Single(player => player.IsAccountBacked).HasLeft = true;
        if (kind == "legacy-profiles")
            foreach (var player in table.Players.Where(player => !player.IsAccountBacked)) player.ManagedAssignmentId = "legacy";
        await SaveTakeoverTable(db, table);
        var joined = await store.JoinAsync("bruno", "Bruno", kind == "wrong-entry" ? 2m : 1m,
            "join-ineligible", Start.AddSeconds(6), default);
        Assert.Equal("queue", joined.Kind);
        Assert.NotEqual(table.Id, joined.Queue!.MatchId);
        Assert.DoesNotContain((await Stored(db, table.Id)).Players, player => player.UserId == "bruno");
        Assert.Empty((await db.Collection("heartsPaidFundingTransfers").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task A_forfeited_account_never_buys_back_into_its_previous_match()
    {
        var (db, store, _) = Create();
        var table = await StartHouse(db, store);
        await Seed(db, "bruno");
        await store.JoinAsync("bruno", "Bruno", 1m, "join-bruno", Start.AddSeconds(6), default);
        var current = await store.SessionAsync("alice", Start.AddSeconds(6), default);
        await store.CommandAsync("alice", table.Id, "leave", current.Version, null, "leave-alice", Start.AddSeconds(6), default);
        var rejoined = await store.JoinAsync("alice", "Alice", 1m, "new-entry", Start.AddSeconds(6), default);
        Assert.Equal("queue", rejoined.Kind);
        Assert.NotEqual(table.Id, rejoined.Queue!.MatchId);
        var previous = await Stored(db, table.Id);
        Assert.True(previous.Players.Single(player => player.UserId == "alice").HasLeft);
        Assert.Equal(4, previous.Players.Count);
        Assert.Equal(98m, await Credits(db, "alice"));
    }

    [Fact]
    public async Task Taken_over_human_can_win_inherited_seat_and_funding_reconciles_at_settlement()
    {
        var (db, store, _) = Create();
        var table = await StartHouse(db, store);
        await Seed(db, "bruno");
        await store.JoinAsync("bruno", "Bruno", 1m, "join-bruno", Start.AddSeconds(6), default);
        await PrepareFinalCard(db, table.Id, player => player.UserId == "bruno" ? 50 : 80);
        await FinishFinalCard(db, store, "bruno", table.Id, Start.AddSeconds(20));
        var revenue = await db.Collection("heartsPaidRevenue").Document(table.Id).GetSnapshotAsync();
        Assert.Equal(200L, revenue.GetValue<long>("humanEntryCents"));
        Assert.Equal(200L, revenue.GetValue<long>("houseFundedEntryCents"));
        Assert.Equal(100L, revenue.GetValue<long>("initialHumanEntryCents"));
        Assert.Equal(100L, revenue.GetValue<long>("houseStakeReclassifiedCents"));
        Assert.Equal(400L, revenue.GetValue<long>("humanPayoutCents"));
        Assert.Equal(-200L, revenue.GetValue<long>("houseNetCents"));
        Assert.Equal(103m, await Credits(db, "bruno"));
        Assert.Single((await store.HistoryAsync("bruno", default)).Items);
        Assert.Empty((await Availability(db, 100).GetSnapshotAsync()).Documents);
    }

    private static async Task<PaidHeartsMatch> StartHouse(FirestoreDb db, FirestorePaidHeartsStore store)
    {
        await Seed(db, "alice");
        var queued = await store.JoinAsync("alice", "Alice", 1m, "join-alice", Start, default);
        var started = await store.PlayHouseAsync("alice", queued.Queue!.MatchId, queued.Version, "play-house", Start.AddSeconds(5), default);
        return await Stored(db, started.Table!.Match.MatchId.ToString("N"));
    }

    private static Task SaveTakeoverTable(FirestoreDb db, PaidHeartsMatch table) =>
        db.Collection("heartsPaidMatches").Document(table.Id).UpdateAsync("matchJson", JsonSerializer.Serialize(table, Json));

    private static CollectionReference Availability(FirestoreDb db, long entry) =>
        db.Collection("heartsPaidSeatAvailability").Document($"entry-{entry}-target-100-v1").Collection("tables");
}
