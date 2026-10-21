using System.Text.Json;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Hearts;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.Matchmaking;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Hearts;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class PaidHeartsFirestoreTests
{
    private static readonly DateTime Start = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Reading_idle_session_never_creates_entry_or_opponents()
    {
        var (db, store, roster) = Create();
        await Seed(db, "alice");
        var session = await store.SessionAsync("alice", Start, default);
        Assert.Equal("idle", session.Kind);
        Assert.Equal(100m, session.BalanceCredits);
        Assert.Contains(1m, session.EntryOptions);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(0, roster.Reservations);
    }

    [Fact]
    public async Task Concurrent_joins_and_different_retry_keys_charge_once_and_resume_active_entry()
    {
        var (db, store, _) = Create();
        await Seed(db, "alice");
        var sessions = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => store.JoinAsync(
            "alice", "Alice", 1m, $"join-{index}", Start, default)));
        Assert.Single(sessions.Select(value => value.Queue!.MatchId).Distinct());
        Assert.Equal(99m, await Credits(db, "alice"));
        Assert.Single((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        var duplicate = await store.JoinAsync("alice", "Alice", 1m, "join-0", Start, default);
        Assert.Equal(sessions[0].Queue!.MatchId, duplicate.Queue!.MatchId);
        await Assert.ThrowsAsync<PaidHeartsConflictException>(() => store.JoinAsync("alice", "Alice", 2m, "join-0", Start, default));
    }

    [Fact]
    public async Task Insufficient_funds_creates_no_match_or_reservation()
    {
        var (db, store, roster) = Create();
        await Seed(db, "alice", 50);
        await Assert.ThrowsAsync<PaidHeartsInsufficientFundsException>(() => store.JoinAsync("alice", "Alice", 1m, "join", Start, default));
        Assert.Empty((await db.Collection("heartsPaidMatches").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(0, roster.Reservations);
        Assert.Equal(.5m, await Credits(db, "alice"));
    }

    [Fact]
    public async Task Four_humans_fill_same_table_with_distinct_private_hands_and_restart_resume()
    {
        var (db, store, roster) = Create();
        var users = new[] { "alice", "bruno", "cara", "drew" };
        await Task.WhenAll(users.Select(user => Seed(db, user)));
        foreach (var user in users) await store.JoinAsync(user, user, 1m, $"join-{user}", Start, default);
        var views = await Task.WhenAll(users.Select(user => store.SessionAsync(user, Start, default)));
        Assert.All(views, view => Assert.Equal("match", view.Kind));
        Assert.Single(views.Select(view => view.Table!.Match.MatchId).Distinct());
        Assert.Equal(4, views.Select(view => view.Table!.Match.HumanSeat).Distinct().Count());
        Assert.Equal(52, views.SelectMany(view => view.Table!.Match.Hand.Select(card => card.Code)).Distinct().Count());
        Assert.All(views, view => { Assert.Equal(13, view.Table!.Match.Hand.Count); Assert.Equal(4m, view.Table.PotCredits); });
        var payload = JsonSerializer.Serialize(views[0], Json);
        Assert.DoesNotContain("decisionSeed", payload);
        Assert.DoesNotContain("timingSeed", payload);
        Assert.DoesNotContain("\"seed\"", payload);
        Assert.DoesNotContain(views[1].Table!.Match.Hand[0].Code, payload);
        var restarted = new FirestorePaidHeartsStore(db, new MultiplayerMatchmaker(roster));
        var resumed = await restarted.SessionAsync(users[2], Start, default);
        Assert.Equal(views[2].Table!.Match.Hand, resumed.Table!.Match.Hand);
        Assert.Equal(99m, resumed.BalanceCredits);
        Assert.Equal(0, roster.Reservations);
        await Assert.ThrowsAsync<HeartsAccessException>(() => store.GetAsync("stranger", resumed.Table.Match.MatchId.ToString(), Start, default));
    }

    [Fact]
    public async Task Queue_refund_is_once_and_lobby_expires_without_trapping_entries()
    {
        var (db, store, _) = Create(bots: false);
        await Seed(db, "alice");
        var queued = await store.JoinAsync("alice", "Alice", 1m, "join", Start, default);
        var left = await store.CommandAsync("alice", queued.Queue!.MatchId, "leave", queued.Version, null, "leave", Start, default);
        Assert.Equal("idle", left.Kind);
        await store.CommandAsync("alice", queued.Queue.MatchId, "leave", queued.Version, null, "leave", Start, default);
        Assert.Equal(100m, await Credits(db, "alice"));
        await store.JoinAsync("alice", "Alice", 1m, "join-again", Start, default);
        var expired = await store.SessionAsync("alice", Start.AddMinutes(2), default);
        Assert.Equal("idle", expired.Kind);
        Assert.Equal(100m, await Credits(db, "alice"));
        Assert.Equal(4, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task Managed_arrivals_wait_for_humans_and_each_private_action_plays_real_hand()
    {
        var (db, store, roster) = Create();
        await Seed(db, "alice");
        var queued = await store.JoinAsync("alice", "Alice", 1m, "join", Start, default);
        await store.SessionAsync("alice", Start.AddSeconds(4), default);
        Assert.Equal(0, roster.Reservations);
        var arrivals = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.SessionAsync("alice", Start.AddSeconds(5), default)));
        var one = arrivals.MaxBy(view => view.Queue!.Players.Count)!;
        Assert.Equal(2, one.Queue!.Players.Count);
        Assert.Equal(1, roster.Reservations);
        var two = await store.SessionAsync("alice", Start.AddSeconds(9), default);
        Assert.Equal(3, two.Queue!.Players.Count);
        var active = await store.SessionAsync("alice", Start.AddSeconds(13), default);
        Assert.Equal("match", active.Kind);
        Assert.Equal(4m, active.Table!.PotCredits);
        Assert.Null(active.Table.ActionDeadlineAtUtc);
        Assert.Equal(3, roster.Reservations);
        var now = Start.AddSeconds(13);
        var id = active.Table.Match.MatchId.ToString("N");
        for (var guard = 0; guard < 100; guard++)
        {
            var match = await Stored(db, id);
            if (match.State!.Round.Phase == HeartsPhase.Complete) break;
            var view = await store.GetAsync("alice", id, now, default);
            var state = view.Table!.Match;
            if (state.Phase == "passing" && !state.SubmittedPasses.Contains(state.HumanSeat))
                await store.CommandAsync("alice", id, "pass", view.Version, state.Hand.Take(3).Select(card => card.Code).ToArray(), $"pass-{guard}", now, default);
            else if (state.YourTurn)
                await store.CommandAsync("alice", id, "card", view.Version, [state.LegalCards[0].Code], $"card-{guard}", now, default);
            else
            {
                var pending = await Stored(db, id);
                now = pending.NextActionAtUtc!.Value;
                await store.GetAsync("alice", id, now, default);
            }
        }
        var completed = await Stored(db, id);
        Assert.Equal(HeartsPhase.Complete, completed.State!.Round.Phase);
        Assert.Equal(13, completed.State.Round.CompletedTricks.Count);
        Assert.Equal(52, completed.State.Round.CompletedTricks.SelectMany(trick => trick.Plays).Count());
        Assert.All(completed.Players.Where(player => !player.IsAccountBacked), player =>
        {
            Assert.True(player.DecisionVersion >= 14);
            Assert.True(player.TimingVersion > 0);
            Assert.Equal(0, player.MissedTurns);
        });
        Assert.Equal(3, completed.Players.Where(player => !player.IsAccountBacked).Select(player => player.TimingSeed).Distinct().Count());
        Assert.Single((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Refunded_player_can_rejoin_the_same_waiting_table_with_a_fresh_entry()
    {
        var (db, store, _) = Create(bots: false);
        await Seed(db, "alice"); await Seed(db, "bruno");
        var alice = await store.JoinAsync("alice", "Alice", 1m, "a-joins", Start, default);
        await store.JoinAsync("bruno", "Bruno", 1m, "b-joins", Start, default);
        alice = await store.SessionAsync("alice", Start, default);
        await store.CommandAsync("alice", alice.Queue!.MatchId, "leave", alice.Version, null, "a-leaves", Start, default);
        var rejoined = await store.JoinAsync("alice", "Alice", 1m, "a-returns", Start, default);
        Assert.Equal(alice.Queue.MatchId, rejoined.Queue!.MatchId);
        Assert.Equal(99m, await Credits(db, "alice"));
        Assert.Equal(4, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task Paid_hearts_respects_real_cross_game_reservations_and_off_time()
    {
        var (db, _, _) = Create();
        var profiles = new FirestoreManagedPlayerProfileRepository(db);
        var assignments = new FirestoreManagedPlayerAssignmentStore(db);
        var ids = Enumerable.Range(0, 100).Select(index => $"hearts-available-{index}")
            .Where(id => !ManagedPlayerAvailabilityPolicy.IsSleeping(id, Start)).Take(6).ToArray();
        var names = new[] { "AmberFox", "BlueRaven", "QuietFern", "MossRiver", "CopperFinch", "NightWindow" };
        for (var index = 0; index < ids.Length; index++)
            Assert.True(await profiles.TryCreateAsync(new ManagedPlayerProfile(ids[index], names[index], 3,
                new HashSet<string> { ManagedPlayerGames.Hearts, ManagedPlayerGames.Blackjack }, Start), default));
        Assert.True(await assignments.TryReserveAsync(ids[0], ManagedPlayerGames.Blackjack, "occupied-blackjack", Start, default));
        await db.Collection("users").Document(ids[1]).UpdateAsync("managedNextAvailableAt", Timestamp.FromDateTime(Start.AddHours(1)));
        var queuer = new ManagedPlayerQueuer(new ManagedPlayerProfileGenerator(profiles), profiles, assignments);
        var store = new FirestorePaidHeartsStore(db, new MultiplayerMatchmaker(queuer));
        await Seed(db, "alice");
        await store.JoinAsync("alice", "Alice", 1m, "join", Start, default);
        foreach (var seconds in new[] { 5, 9, 13 }) await store.SessionAsync("alice", Start.AddSeconds(seconds), default);
        var current = await store.SessionAsync("alice", Start.AddSeconds(13), default);
        var match = await Stored(db, current.Table!.Match.MatchId.ToString("N"));
        Assert.DoesNotContain(match.Players, player => player.UserId == ids[0] || player.UserId == ids[1]);
        Assert.Equal(3, match.Players.Count(player => !player.IsAccountBacked));
        var held = await db.Collection("managedPlayerAssignments").Document(ids[0]).GetSnapshotAsync();
        Assert.Equal("occupied-blackjack", held.GetValue<string>("assignmentId"));
        Assert.Equal(3, match.Players.Where(player => !player.IsAccountBacked).Select(player => player.ManagedAssignmentId).Distinct().Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Funded_pool_pays_human_or_records_managed_win_without_bot_wallet_credits(bool humanWins)
    {
        var (db, store, roster) = Create();
        await Seed(db, "alice");
        await store.JoinAsync("alice", "Alice", 1m, "join", Start, default);
        foreach (var seconds in new[] { 5, 9, 13 }) await store.SessionAsync("alice", Start.AddSeconds(seconds), default);
        var current = await store.SessionAsync("alice", Start.AddSeconds(13), default);
        var id = current.Table!.Match.MatchId.ToString("N");
        var winnerId = humanWins ? "alice" : (await Stored(db, id)).Players.First(player => !player.IsAccountBacked).UserId;
        await PrepareFinalCard(db, id, player => player.UserId == winnerId ? 50 : 80);
        var result = await FinishFinalCard(db, store, "alice", id, Start.AddSeconds(20));
        Assert.Equal("result", result.Kind);
        Assert.Equal(humanWins ? 4m : 0m, result.Table!.PayoutCredits);
        Assert.Equal(humanWins ? 103m : 99m, await Credits(db, "alice"));
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => store.GetAsync("alice", id, Start.AddSeconds(22), default)));
        var revenue = Assert.Single((await db.Collection("heartsPaidRevenue").GetSnapshotAsync()).Documents);
        Assert.Equal(300L, revenue.GetValue<long>("houseFundedEntryCents"));
        Assert.Equal(humanWins ? -300L : 100L, revenue.GetValue<long>("houseNetCents"));
        Assert.Equal(humanWins ? 400L : 0L, revenue.GetValue<long>("humanPayoutCents"));
        Assert.Equal(humanWins ? 0L : 400L, revenue.GetValue<long>("botPrizeCents"));
        Assert.All((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents, entry => Assert.Equal("alice", entry.GetValue<string>("userId")));
        Assert.Single((await store.HistoryAsync("alice", default)).Items);
        Assert.Equal(3, roster.Released.Count);
        Assert.True((await new FirestoreHeartsOnboardingStore(db).GetAsync("alice", default)).ShouldAskPlayedBefore);
    }

    [Fact]
    public async Task Tied_humans_split_cents_exactly_and_action_retries_do_not_pay_again()
    {
        var (db, store, _) = Create();
        var users = new[] { "alice", "bruno", "cara", "drew" };
        foreach (var user in users) { await Seed(db, user); await store.JoinAsync(user, user, 1m, $"join-{user}", Start, default); }
        var current = await store.SessionAsync("alice", Start, default);
        var id = current.Table!.Match.MatchId.ToString("N");
        await PrepareFinalCard(db, id, _ => 50, tiedCount: 3);
        var result = await FinishFinalCard(db, store, "alice", id, Start.AddSeconds(1));
        Assert.Equal("result", result.Kind);
        Assert.Equal(3, result.Table!.WinnerSeats.Count);
        var model = await Stored(db, id);
        Assert.Equal(400L, model.Players.Sum(player => player.PayoutCents));
        Assert.Equal(new long[] { 133, 133, 134 }, model.Players.Where(player => player.PayoutCents > 0).Select(player => player.PayoutCents).Order());
        Assert.Equal(400m, (await Task.WhenAll(users.Select(user => Credits(db, user)))).Sum());
        Assert.Single((await db.Collection("heartsPaidRevenue").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Paid_server_shuffles_are_independent_and_moves_use_version_and_idempotency_guards()
    {
        var (db, store, _) = Create();
        var users = new[] { "a", "b", "c", "d", "e", "f", "g", "h" };
        foreach (var user in users) { await Seed(db, user); await store.JoinAsync(user, user, 1m, $"join-{user}", Start, default); }
        var first = await store.SessionAsync("a", Start, default);
        var second = await store.SessionAsync("e", Start, default);
        var firstModel = await Stored(db, first.Table!.Match.MatchId.ToString("N"));
        var secondModel = await Stored(db, second.Table!.Match.MatchId.ToString("N"));
        Assert.NotEqual(firstModel.State!.Round.Seed, secondModel.State!.Round.Seed);
        var cards = first.Table.Match.Hand.Take(3).Select(card => card.Code).ToArray();
        var calls = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.CommandAsync("a", firstModel.Id,
            "pass", first.Version, cards, "pass-once", Start, default)));
        Assert.All(calls, value => Assert.Single(value.Table!.Match.SubmittedPasses));
        await Assert.ThrowsAsync<PaidHeartsConflictException>(() => store.CommandAsync("b", firstModel.Id,
            "pass", first.Version, second.Table.Match.Hand.Take(3).Select(card => card.Code).ToArray(), "stale", Start, default));
        Assert.Equal(8, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task Inactivity_warns_once_then_forfeits_without_refund_or_indefinite_seat()
    {
        var (db, store, _) = Create();
        foreach (var user in new[] { "a", "b", "c", "d" }) { await Seed(db, user); await store.JoinAsync(user, user, 1m, $"join-{user}", Start, default); }
        var view = await store.SessionAsync("a", Start, default);
        var id = view.Table!.Match.MatchId.ToString("N");
        // Passing deadlines auto-play one legal pass per idle seat.
        var now = Start;
        for (var index = 0; index < 4; index++) { now = (await Stored(db, id)).NextActionAtUtc!.Value; await store.GetAsync("a", id, now, default); }
        var pending = await Stored(db, id);
        var timedOutUser = pending.Players.Single(player => player.Seat == pending.State!.Turn).UserId;
        Assert.Equal(1, pending.Players.Single(player => player.UserId == timedOutUser).MissedTurns);
        now = pending.NextActionAtUtc!.Value;
        await store.GetAsync("a", id, now, default);
        var forfeited = (await Stored(db, id)).Players.Single(player => player.UserId == timedOutUser);
        Assert.True(forfeited.HasLeft);
        Assert.Equal(99m, await Credits(db, timedOutUser));
        Assert.Equal(0L, forfeited.PayoutCents);
        var idle = await store.SessionAsync(timedOutUser, now, default);
        Assert.Equal("idle", idle.Kind);
        Assert.Contains("two missed turns", idle.Notice);
    }

    [Fact]
    public async Task Dealt_leave_exits_session_without_refund_and_an_explicit_new_entry_is_possible()
    {
        var (db, store, _) = Create();
        foreach (var user in new[] { "a", "b", "c", "d" }) { await Seed(db, user); await store.JoinAsync(user, user, 1m, $"join-{user}", Start, default); }
        var view = await store.SessionAsync("a", Start, default);
        var id = view.Table!.Match.MatchId.ToString("N");
        var left = await store.CommandAsync("a", id, "leave", view.Version, null, "a-leaves", Start, default);
        Assert.Equal("idle", left.Kind);
        Assert.Equal(99m, left.BalanceCredits);
        Assert.True((await Stored(db, id)).Players.Single(player => player.UserId == "a").HasLeft);
        Assert.Equal("idle", (await store.SessionAsync("a", Start, default)).Kind);
        var next = await store.JoinAsync("a", "a", 1m, "a-new-entry", Start, default);
        Assert.NotEqual(id, next.Queue!.MatchId);
        Assert.Equal(98m, next.BalanceCredits);
    }

    [Fact]
    public async Task Profile_release_failure_is_retried_by_worker_after_settlement_without_paying_twice()
    {
        var (db, store, roster) = Create();
        await Seed(db, "alice"); await store.JoinAsync("alice", "Alice", 1m, "join", Start, default);
        foreach (var seconds in new[] { 5, 9, 13 }) await store.SessionAsync("alice", Start.AddSeconds(seconds), default);
        var current = await store.SessionAsync("alice", Start.AddSeconds(13), default);
        var id = current.Table!.Match.MatchId.ToString("N");
        await PrepareFinalCard(db, id, player => player.UserId == "alice" ? 50 : 80);
        var model = await Stored(db, id);
        var actor = model.Players.Single(player => player.Seat == model.State!.Turn);
        roster.FailReleases = true;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (actor.IsAccountBacked)
                await store.CommandAsync(actor.UserId, id, "card", model.Version,
                    [HeartsEngine.LegalCards(model.State!.Round, actor.Seat)[0].Code], "final", Start.AddSeconds(20), default);
            else await store.GetAsync("alice", id, Start.AddSeconds(20), default);
        });
        Assert.Equal("result", (await Stored(db, id)).Status);
        Assert.True((await Stored(db, id)).ReleasePending);
        Assert.Equal(103m, await Credits(db, "alice"));
        roster.FailReleases = false;
        await store.SweepAsync(Start.AddSeconds(22), default);
        Assert.False((await Stored(db, id)).ReleasePending);
        Assert.Equal(3, roster.Released.Count);
        Assert.Equal(2, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.Single((await db.Collection("heartsPaidRevenue").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Final_card_timeout_cannot_trap_a_table_when_last_human_forfeits()
    {
        var (db, store, _) = Create();
        foreach (var user in new[] { "a", "b", "c", "d" }) { await Seed(db, user); await store.JoinAsync(user, user, 1m, $"join-{user}", Start, default); }
        var view = await store.SessionAsync("a", Start, default);
        var id = view.Table!.Match.MatchId.ToString("N");
        await PrepareFinalCard(db, id, _ => 50, tiedCount: 3);
        var model = await Stored(db, id);
        var finalActor = model.Players.Single(player => player.Seat == model.State!.Turn).UserId;
        foreach (var user in model.Players.Where(player => player.UserId != finalActor).Select(player => player.UserId))
        {
            var latest = await Stored(db, id);
            await store.CommandAsync(user, id, "leave", latest.Version, null, $"{user}-leave", Start.AddSeconds(15), default);
        }
        model = await Stored(db, id);
        model.Players.Single(player => player.UserId == finalActor).MissedTurns = 1;
        await db.Collection("heartsPaidMatches").Document(id).UpdateAsync("matchJson", JsonSerializer.Serialize(model, Json));
        await store.GetAsync(finalActor, id, model.NextActionAtUtc!.Value, default);
        var settled = await Stored(db, id);
        Assert.Equal("result", settled.Status);
        Assert.Empty(settled.Winners);
        Assert.All(settled.Players, player => Assert.True(player.HasLeft));
        var revenue = Assert.Single((await db.Collection("heartsPaidRevenue").GetSnapshotAsync()).Documents);
        Assert.Equal(400L, revenue.GetValue<long>("forfeitedPoolCents"));
        Assert.Equal(400L, revenue.GetValue<long>("houseNetCents"));
    }

    private static (FirestoreDb Db, FirestorePaidHeartsStore Store, RecordingQueuer Roster) Create(bool bots = true)
    {
        var db = new FirestoreDbBuilder { ProjectId = $"demo-paid-hearts-{Guid.NewGuid():N}", EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        var roster = new RecordingQueuer();
        return (db, new(db, new MultiplayerMatchmaker(roster), new PaidHeartsOptions { AllowManagedPlayers = bots }), roster);
    }

    private static Task Seed(FirestoreDb db, string userId, long cents = 10_000) => db.Collection("userBalances").Document($"{userId}_slotsCredits")
        .SetAsync(new Dictionary<string, object> { ["userId"] = userId, ["currencyId"] = "slotsCredits", ["available"] = cents / 100,
            ["availableFractionalCents"] = cents % 100, ["reserved"] = 0L, ["version"] = 1L });
    private static async Task<decimal> Credits(FirestoreDb db, string userId)
    {
        var value = await db.Collection("userBalances").Document($"{userId}_slotsCredits").GetSnapshotAsync();
        return value.GetValue<long>("available") + value.GetValue<long>("availableFractionalCents") / 100m;
    }
    private static async Task<PaidHeartsMatch> Stored(FirestoreDb db, string id) => JsonSerializer.Deserialize<PaidHeartsMatch>(
        (await db.Collection("heartsPaidMatches").Document(id).GetSnapshotAsync()).GetValue<string>("matchJson"), Json)!;

    private static async Task PrepareFinalCard(FirestoreDb db, string id, Func<PaidHeartsParticipant, int> desired, int tiedCount = 0)
    {
        var model = await Stored(db, id);
        var state = model.State!;
        var version = 0;
        foreach (var seat in Enum.GetValues<PlayerSeat>())
            if (state.Round.Phase == HeartsPhase.Passing)
                state = HeartsMatchEngine.Apply(state, new PassHeartsCards(seat,
                    HeartsManagedActionPolicy.ChoosePass(state.Round, seat, 3, 77, version++))).State;
        while (state.Round.CompletedTricks.Count != 12 || state.Round.CurrentTrick.Plays.Count != 3)
            state = HeartsMatchEngine.Apply(state, new PlayHeartsCard(state.Turn,
                HeartsManagedActionPolicy.ChooseCard(state.Round, state.Turn, 3, 77, version++))).State;
        var final = HeartsMatchEngine.Apply(state, new PlayHeartsCard(state.Turn, HeartsEngine.LegalCards(state.Round, state.Turn)[0])).State;
        var threshold = final.Round.Scores.OrderByDescending(score => score.Points).First().Seat;
        if (tiedCount == 0 && model.Players.Single(player => player.Seat == threshold) is { } thresholdPlayer && desired(thresholdPlayer) == 50)
            threshold = model.Players.First(player => player.UserId != thresholdPlayer.UserId && final.Round.Scores.Single(score => score.Seat == player.Seat).Points > 0).Seat;
        int Before(PlayerSeat seat)
        {
            var player = model.Players.Single(player => player.Seat == seat);
            var target = seat == threshold ? 100 : tiedCount > 0 ? 50 : desired(player);
            return target - final.Round.Scores.Single(score => score.Seat == seat).Points;
        }
        state = state with { Score = new(Before(PlayerSeat.North), Before(PlayerSeat.East), Before(PlayerSeat.South), Before(PlayerSeat.West)) };
        model.State = state;
        model.ScheduledSeat = state.Turn;
        model.NextActionAtUtc = Start.AddSeconds(20);
        await db.Collection("heartsPaidMatches").Document(id).UpdateAsync("matchJson", JsonSerializer.Serialize(model, Json));
    }

    private static async Task<PaidHeartsSessionResponse> FinishFinalCard(FirestoreDb db, FirestorePaidHeartsStore store, string viewer, string id, DateTime now)
    {
        var model = await Stored(db, id);
        var actor = model.Players.Single(player => player.Seat == model.State!.Turn);
        if (actor.IsAccountBacked)
        {
            var card = HeartsEngine.LegalCards(model.State!.Round, actor.Seat)[0].Code;
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.CommandAsync(actor.UserId, id, "card", model.Version, [card], "last-card", now, default)));
            await store.CommandAsync(actor.UserId, id, "card", model.Version, [card], "last-card", now, default);
        }
        else await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.GetAsync(viewer, id, Start.AddSeconds(20), default)));
        return await store.GetAsync(viewer, id, Start.AddSeconds(21), default);
    }

    private sealed class RecordingQueuer : IManagedPlayerQueuer
    {
        public int Reservations { get; private set; }
        public bool FailReleases { get; set; }
        public HashSet<string> Released { get; } = [];
        public Task<IReadOnlyList<ManagedPlayerProfile>> ReserveAsync(string gameId, string assignmentId, int count,
            IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken cancellationToken)
        {
            Reservations++;
            return Task.FromResult<IReadOnlyList<ManagedPlayerProfile>>([new($"managed-{Reservations}", $"River{Reservations}", 3,
                new HashSet<string> { ManagedPlayerGames.Hearts }, nowUtc)]);
        }
        public Task HeartbeatAsync(string gameId, string assignmentId, IReadOnlyCollection<string> ids, DateTime now, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> TryRenewAsync(string gameId, string assignmentId, IReadOnlyCollection<string> ids, DateTime now, CancellationToken ct) => Task.FromResult(true);
        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> ids, DateTime now, CancellationToken ct)
        {
            if (FailReleases) throw new InvalidOperationException("A simulated profile release failed.");
            foreach (var id in ids) Released.Add(id); return Task.CompletedTask;
        }
    }
}
