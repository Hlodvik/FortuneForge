using System.Text.Json;
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
public sealed class PaidHeartsDeadlineFirestoreTests
{
    private static readonly DateTime Start = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task Passing_deadline_is_authoritative_at_and_after_expiry(int milliseconds, bool expired)
    {
        var (db, store, model) = await FourHumans();
        var actor = model.Players.Single(player => player.Seat == model.ScheduledSeat);
        var automatic = HeartsManagedActionPolicy.ChoosePass(model.State!.Round, actor.Seat,
            actor.SkillLevel, actor.DecisionSeed, actor.DecisionVersion).Select(card => card.Code).ToArray();
        var requested = model.State.Round.HandFor(actor.Seat).Where(card => !automatic.Contains(card.Code))
            .Take(3).Select(card => card.Code).ToArray();
        var now = model.NextActionAtUtc!.Value.AddMilliseconds(milliseconds);

        var result = await store.CommandAsync(actor.UserId, model.Id, "pass", model.Version, requested, "pass", now, default);
        var changed = await Read(db, model.Id);
        var submitted = Assert.Single(changed.State!.Round.SubmittedPasses);
        Assert.Equal(expired ? automatic : requested, submitted.Cards.Select(card => card.Code));
        Assert.Equal(expired ? 1 : 0, changed.Players.Single(player => player.UserId == actor.UserId).MissedTurns);
        Assert.Equal(expired, changed.Players.Single(player => player.UserId == actor.UserId).HasLeft);
        Assert.Equal(model.Version + 1, changed.Version);
        if (expired) { Assert.Equal("idle", result.Kind); Assert.Contains("timer expired", result.Notice); }
        else Assert.Null(result.Notice);

        var retry = await store.CommandAsync(actor.UserId, model.Id, "pass", model.Version, requested, "pass", now, default);
        Assert.Equal(result.Version, retry.Version);
        Assert.Single((await Read(db, model.Id)).State!.Round.SubmittedPasses);
        Assert.Equal(4, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task Late_card_uses_automatic_choice_once_and_returns_updated_table_not_requested_card()
    {
        var (db, store, model) = await FourHumans();
        await PreparePlaying(db, model, multipleLegalCards: true);
        var actor = model.Players.Single(player => player.Seat == model.ScheduledSeat);
        var automatic = HeartsManagedActionPolicy.ChooseCard(model.State!.Round, actor.Seat,
            actor.SkillLevel, actor.DecisionSeed, actor.DecisionVersion);
        var requested = HeartsEngine.LegalCards(model.State.Round, actor.Seat).First(card => card != automatic);
        var now = model.NextActionAtUtc!.Value.AddSeconds(1);

        var result = await store.CommandAsync(actor.UserId, model.Id, "card", model.Version, [requested.Code], "late-card", now, default);
        var changed = await Read(db, model.Id);
        Assert.Equal(model.Version + 1, changed.Version);
        Assert.Equal(1, changed.Players.Single(player => player.UserId == actor.UserId).MissedTurns);
        Assert.Equal(actor.DecisionVersion + 1, changed.Players.Single(player => player.UserId == actor.UserId).DecisionVersion);
        Assert.DoesNotContain(automatic, changed.State!.Round.HandFor(actor.Seat));
        Assert.Contains(requested, changed.State.Round.HandFor(actor.Seat));
        Assert.True(changed.Players.Single(player => player.UserId == actor.UserId).HasLeft);
        Assert.Equal("idle", result.Kind);
        Assert.Contains("timer expired", result.Notice);
    }

    [Fact]
    public async Task First_missed_pass_commits_forfeit_and_returns_idle_without_refunding_entry()
    {
        var (db, store, model) = await FourHumans();
        var actor = model.Players.Single(player => player.Seat == model.ScheduledSeat);
        var requested = model.State!.Round.HandFor(actor.Seat).Take(3).Select(card => card.Code).ToArray();

        var result = await store.CommandAsync(actor.UserId, model.Id, "pass", model.Version, requested,
            "first-miss", model.NextActionAtUtc!.Value, default);
        var changed = await Read(db, model.Id);
        Assert.True(changed.Players.Single(player => player.UserId == actor.UserId).HasLeft);
        Assert.Equal(1, changed.Players.Single(player => player.UserId == actor.UserId).MissedTurns);
        Assert.Single(changed.State!.Round.SubmittedPasses);
        Assert.Equal("idle", result.Kind);
        Assert.Contains("turn timer expired", result.Notice);
        Assert.Equal(99m, result.BalanceCredits);
        Assert.Equal(400L, changed.PotCents);
        Assert.Equal(4, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task Late_card_racing_worker_cannot_play_twice_or_escape_missed_turn()
    {
        var (db, store, model) = await FourHumans();
        await PreparePlaying(db, model, multipleLegalCards: true);
        var actor = model.Players.Single(player => player.Seat == model.ScheduledSeat);
        var requested = HeartsEngine.LegalCards(model.State!.Round, actor.Seat)[0].Code;
        var beforeCards = model.State.Round.HandFor(actor.Seat).Count;
        var now = model.NextActionAtUtc!.Value;
        var conflicts = 0;
        async Task LateCommand()
        {
            try { await store.CommandAsync(actor.UserId, model.Id, "card", model.Version, [requested], "race", now, default); }
            catch (PaidHeartsConflictException) { Interlocked.Increment(ref conflicts); }
        }

        await Task.WhenAll(LateCommand(), LateCommand(), store.SweepAsync(now, default), store.GetAsync(actor.UserId, model.Id, now, default));
        var changed = await Read(db, model.Id);
        Assert.Equal(model.Version + 1, changed.Version);
        Assert.Equal(beforeCards - 1, changed.State!.Round.HandFor(actor.Seat).Count);
        Assert.Equal(1, changed.Players.Single(player => player.UserId == actor.UserId).MissedTurns);
        Assert.True(changed.Players.Single(player => player.UserId == actor.UserId).HasLeft);
        Assert.Equal(actor.DecisionVersion + 1, changed.Players.Single(player => player.UserId == actor.UserId).DecisionVersion);
        Assert.InRange(conflicts, 0, 2); // Worker-wins replies conflict; command-wins retries use its receipt.
        Assert.Equal(4, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task Another_players_pass_or_leave_does_not_extend_unchanged_human_deadline()
    {
        var (db, store, model) = await FourHumans();
        var waiting = model.ScheduledSeat;
        var deadline = model.NextActionAtUtc;
        var other = model.Players.First(player => player.Seat != waiting);
        var cards = model.State!.Round.HandFor(other.Seat).Take(3).Select(card => card.Code).ToArray();
        await store.CommandAsync(other.UserId, model.Id, "pass", model.Version, cards, "other-pass", Start.AddSeconds(10), default);
        var passed = await Read(db, model.Id);
        Assert.Equal(waiting, passed.ScheduledSeat);
        Assert.Equal(deadline, passed.NextActionAtUtc);

        await store.CommandAsync(other.UserId, model.Id, "leave", passed.Version, null, "other-leaves", Start.AddSeconds(20), default);
        var left = await Read(db, model.Id);
        Assert.True(left.Players.Single(player => player.UserId == other.UserId).HasLeft);
        Assert.Equal(waiting, left.ScheduledSeat);
        Assert.Equal(deadline, left.NextActionAtUtc);
    }

    [Fact]
    public async Task Leave_at_deadline_commits_due_play_and_forfeit_once_without_restarting_next_human_timer()
    {
        var (db, store, model) = await FourHumans();
        await PreparePlaying(db, model, multipleLegalCards: true);
        var actor = model.Players.Single(player => player.Seat == model.ScheduledSeat);
        var now = model.NextActionAtUtc!.Value;
        var result = await store.CommandAsync(actor.UserId, model.Id, "leave", model.Version, null, "late-leave", now, default);
        var changed = await Read(db, model.Id);
        Assert.Equal("idle", result.Kind);
        Assert.True(changed.Players.Single(player => player.UserId == actor.UserId).HasLeft);
        Assert.Equal(1, changed.Players.Single(player => player.UserId == actor.UserId).MissedTurns);
        Assert.Equal(model.State!.Round.HandFor(actor.Seat).Count - 1, changed.State!.Round.HandFor(actor.Seat).Count);
        Assert.Equal(model.Version + 1, changed.Version);
        Assert.Equal(now.AddSeconds(45), changed.NextActionAtUtc);
        Assert.Equal(99m, result.BalanceCredits);
    }

    [Fact]
    public async Task Expired_last_card_settles_last_human_forfeit_without_rollback_or_duplicate_revenue()
    {
        var (db, store, model) = await FourHumans();
        await PreparePlaying(db, model, multipleLegalCards: false);
        while (model.State!.Round.CompletedTricks.Count != 12 || model.State.Round.CurrentTrick.Plays.Count != 3)
            model.State = HeartsMatchEngine.Apply(model.State, new PlayHeartsCard(model.State.Turn,
                HeartsEngine.LegalCards(model.State.Round, model.State.Turn)[0])).State;
        model.ScheduledSeat = model.State.Turn;
        var actor = model.Players.Single(player => player.Seat == model.ScheduledSeat);
        foreach (var player in model.Players) player.HasLeft = player.UserId != actor.UserId;
        await Save(db, model);
        var card = HeartsEngine.LegalCards(model.State.Round, actor.Seat)[0].Code;
        var now = model.NextActionAtUtc!.Value;

        var result = await store.CommandAsync(actor.UserId, model.Id, "card", model.Version, [card], "last-missed-card", now, default);
        Assert.Equal("result", result.Kind);
        var changed = await Read(db, model.Id);
        Assert.All(changed.Players, player => Assert.True(player.HasLeft));
        Assert.Equal(13, changed.State!.Round.CompletedTricks.Count);
        Assert.Empty(changed.Winners);
        await store.CommandAsync(actor.UserId, model.Id, "card", model.Version, [card], "last-missed-card", now, default);
        await store.SweepAsync(now.AddSeconds(1), default);
        var revenue = Assert.Single((await db.Collection("heartsPaidRevenue").GetSnapshotAsync()).Documents);
        Assert.Equal(400L, revenue.GetValue<long>("forfeitedPoolCents"));
        Assert.Equal(4, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task Expired_best_score_is_ineligible_before_automatic_final_card_and_live_players_receive_pot()
    {
        var (db, store, model) = await FourHumans();
        await PreparePlaying(db, model, multipleLegalCards: false);
        while (model.State!.Round.CompletedTricks.Count != 12 || model.State.Round.CurrentTrick.Plays.Count != 3)
            model.State = HeartsMatchEngine.Apply(model.State, new PlayHeartsCard(model.State.Turn,
                HeartsEngine.LegalCards(model.State.Round, model.State.Turn)[0])).State;
        var actor = model.Players.Single(player => player.Seat == model.State.Turn);
        var card = HeartsEngine.LegalCards(model.State.Round, actor.Seat)[0];
        var completed = HeartsMatchEngine.Apply(model.State, new PlayHeartsCard(actor.Seat, card)).State;
        var thresholdSeat = model.Players.First(player => player.Seat != actor.Seat).Seat;
        int PreviousScore(PlayerSeat seat) => (seat == actor.Seat ? 50 : seat == thresholdSeat ? 100 : 80)
            - completed.Round.Scores.Single(score => score.Seat == seat).Points;
        model.State = model.State with
        {
            Score = new(PreviousScore(PlayerSeat.North), PreviousScore(PlayerSeat.East),
                PreviousScore(PlayerSeat.South), PreviousScore(PlayerSeat.West))
        };
        model.ScheduledSeat = actor.Seat;
        await Save(db, model);

        var result = await store.CommandAsync(actor.UserId, model.Id, "card", model.Version, [card.Code], "best-score-expired",
            model.NextActionAtUtc!.Value, default);
        var changed = await Read(db, model.Id);
        Assert.Equal("result", result.Kind);
        Assert.Equal(50, changed.State!.Score.For(actor.Seat));
        Assert.True(changed.Players.Single(player => player.UserId == actor.UserId).HasLeft);
        Assert.DoesNotContain(actor.Seat, changed.Winners);
        Assert.Equal(0L, changed.Players.Single(player => player.UserId == actor.UserId).PayoutCents);
        Assert.Equal(400L, changed.Players.Where(player => player.UserId != actor.UserId).Sum(player => player.PayoutCents));
        Assert.All(changed.Players.Where(player => player.UserId != actor.UserId), player => Assert.False(player.HasLeft));
        Assert.Equal(99m, result.BalanceCredits);
    }

    private static async Task<(FirestoreDb Db, FirestorePaidHeartsStore Store, PaidHeartsMatch Match)> FourHumans()
    {
        var db = new FirestoreDbBuilder
        {
            ProjectId = $"demo-hearts-deadlines-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly
        }.Build();
        var store = new FirestorePaidHeartsStore(db, new MultiplayerMatchmaker(new NoReservations()));
        foreach (var user in new[] { "a", "b", "c", "d" })
        {
            await db.Collection("userBalances").Document($"{user}_slotsCredits").SetAsync(new Dictionary<string, object>
            {
                ["userId"] = user, ["currencyId"] = "slotsCredits", ["available"] = 100L,
                ["availableFractionalCents"] = 0L, ["version"] = 1L
            });
            await store.JoinAsync(user, user, 1m, $"join-{user}", Start, default);
        }
        var view = await store.SessionAsync("a", Start, default);
        var model = await Read(db, view.Table!.Match.MatchId.ToString("N"));
        return (db, store, model);
    }

    private static async Task PreparePlaying(FirestoreDb db, PaidHeartsMatch model, bool multipleLegalCards)
    {
        foreach (var seat in Enum.GetValues<PlayerSeat>())
            model.State = HeartsMatchEngine.Apply(model.State!, new PassHeartsCards(seat,
                model.State!.Round.HandFor(seat).Take(3).ToArray())).State;
        while (multipleLegalCards && (HeartsEngine.LegalCards(model.State!.Round, model.State.Turn).Count < 2 ||
                                     model.State.Round.CurrentTrick.Plays.Count == 3))
            model.State = HeartsMatchEngine.Apply(model.State, new PlayHeartsCard(model.State.Turn,
                HeartsEngine.LegalCards(model.State.Round, model.State.Turn)[0])).State;
        model.ScheduledSeat = model.State!.Turn;
        model.NextActionAtUtc = Start.AddSeconds(45);
        await Save(db, model);
    }

    private static Task Save(FirestoreDb db, PaidHeartsMatch model) => db.Collection("heartsPaidMatches").Document(model.Id)
        .UpdateAsync(new Dictionary<string, object>
        {
            ["matchJson"] = JsonSerializer.Serialize(model, Json),
            ["nextSweepAt"] = Timestamp.FromDateTime(model.NextActionAtUtc!.Value)
        });

    private static async Task<PaidHeartsMatch> Read(FirestoreDb db, string id) => JsonSerializer.Deserialize<PaidHeartsMatch>(
        (await db.Collection("heartsPaidMatches").Document(id).GetSnapshotAsync()).GetValue<string>("matchJson"), Json)!;

    private sealed class NoReservations : IManagedPlayerQueuer
    {
        public Task<IReadOnlyList<ManagedPlayerProfile>> ReserveAsync(string gameId, string assignmentId, int count,
            IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken ct) =>
            throw new InvalidOperationException("The four-human deadline tests must never reserve opponents.");
        public Task HeartbeatAsync(string gameId, string assignmentId, IReadOnlyCollection<string> profileIds,
            DateTime nowUtc, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> TryRenewAsync(string gameId, string assignmentId, IReadOnlyCollection<string> profileIds,
            DateTime nowUtc, CancellationToken ct) => Task.FromResult(true);
        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds,
            DateTime nowUtc, CancellationToken ct) => Task.CompletedTask;
    }
}
