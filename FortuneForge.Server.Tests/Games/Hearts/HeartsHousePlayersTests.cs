using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Admin.Management;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Hearts;
using FortuneForge.Server.Games.Hearts;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Hearts;

public sealed class HeartsHousePlayersTests
{
    [Theory]
    [InlineData(PlayerSeat.North, "House North")]
    [InlineData(PlayerSeat.East, "House East")]
    [InlineData(PlayerSeat.South, "House South")]
    [InlineData(PlayerSeat.West, "House West")]
    public void House_identity_is_explicit_stable_and_scoped_to_its_table(PlayerSeat seat, string name)
    {
        var first = HeartsHousePlayers.Create("first-table", seat);
        Assert.Equal(name, first.PlayerName);
        Assert.Equal(ManagedPlayerSkillLevels.Average, first.SkillLevel);
        Assert.Equal(first, HeartsHousePlayers.Create("first-table", seat));
        Assert.NotEqual(first.UserId, HeartsHousePlayers.Create("second-table", seat).UserId);
    }

    [Fact]
    public void House_factory_rejects_invalid_seats_skills_and_table_ids()
    {
        Assert.Throws<ArgumentException>(() => HeartsHousePlayers.Create(" ", PlayerSeat.East));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeartsHousePlayers.Create("table", (PlayerSeat)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeartsHousePlayers.Create("table", PlayerSeat.East, 0));
    }

    [Fact]
    public async Task Concurrent_free_tables_use_house_names_without_reserving_any_shared_profile()
    {
        var assignments = new PolicyOnlyAssignments();
        var roster = new HeartsManagedPlayerRoster(assignments);
        var games = new HeartsGameService(roster, new Clock());
        var tables = await Task.WhenAll(Enumerable.Range(0, 24).Select(index =>
            games.StartAsync($"human-{index}", new StartHeartsMatchRequest((uint)(1931 + index), 100))));

        Assert.Equal(24, tables.Select(table => table.MatchId).Distinct().Count());
        foreach (var table in tables)
            Assert.Equal(["House East", "House South", "House West"], table.Players
                .Where(player => player.Seat != table.HumanSeat).Select(player => player.DisplayName));
        Assert.Equal(24, assignments.PolicyChecks);
        Assert.Equal("hearts", assignments.LastGameId);
        var first = await roster.ReserveAsync("table-a", 3, [], DateTime.UtcNow, default);
        var second = await roster.ReserveAsync("table-b", 3, [], DateTime.UtcNow, default);
        Assert.Equal(6, first.Concat(second).Select(actor => actor.UserId).Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_free_and_trial_tables_still_honor_administrator_admission_controls(bool trial)
    {
        var assignments = new PolicyOnlyAssignments { Enabled = false };
        var games = new HeartsGameService(new HeartsManagedPlayerRoster(assignments), new Clock());
        await Assert.ThrowsAsync<ManagedPlayerSupplyUnavailableException>(() => trial
            ? games.StartTrialAsync("alice")
            : games.StartAsync("alice", new StartHeartsMatchRequest(1931, 100)));
        Assert.Equal(1, assignments.PolicyChecks);
        Assert.Equal(0, games.RetainedSessionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_house_tables_can_resume_advance_and_leave_after_admissions_are_disabled(bool trial)
    {
        var clock = new Clock();
        var assignments = new PolicyOnlyAssignments();
        var games = new HeartsGameService(new HeartsManagedPlayerRoster(assignments), clock);
        var table = trial ? await games.StartTrialAsync("alice")
            : await games.StartAsync("alice", new StartHeartsMatchRequest(1931, 100));
        assignments.Enabled = false;
        if (trial)
        {
            Assert.Equal(table.MatchId, (await games.StartTrialAsync("alice")).MatchId);
            Assert.Equal(["Trial bot 1", "Trial bot 2", "Trial bot 3"], table.Players
                .Where(player => player.Seat != table.HumanSeat).Select(player => player.DisplayName));
        }
        var passed = await games.PassAsync("alice", table.MatchId, table.Hand.Take(3).Select(card => card.Code).ToArray());
        Assert.Single(passed.SubmittedPasses);
        clock.Advance(TimeSpan.FromMinutes(5));
        await games.AdvanceDueManagedTurnsAsync();
        Assert.Equal(2, (await games.GetAsync("alice", table.MatchId)).SubmittedPasses.Count);
        Assert.Equal(1, assignments.PolicyChecks);
        if (trial) await games.CancelTrialAsync("alice", table.MatchId);
        else await games.LeaveAsync("alice", table.MatchId);
    }

    [Fact]
    public async Task Abandoned_house_and_trial_tables_are_purged_without_shared_profile_cleanup()
    {
        var clock = new Clock();
        var assignments = new PolicyOnlyAssignments();
        var games = new HeartsGameService(new HeartsManagedPlayerRoster(assignments), clock);
        await games.StartAsync("alice", new StartHeartsMatchRequest(1931, 100));
        await games.StartTrialAsync("bruno");
        clock.Advance(HeartsGameService.AbandonedSessionTimeout);
        await games.AdvanceDueManagedTurnsAsync();
        Assert.Equal(0, games.RetainedSessionCount);
        Assert.Equal(2, assignments.PolicyChecks);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    // The house adapter may read the admission switch, but must never use any
    // shared assignment write, lease, off-time, or profile availability operation.
    private sealed class PolicyOnlyAssignments : IManagedPlayerAssignmentStore, IManagedPlayerAdmissionPolicy
    {
        public bool Enabled { get; set; } = true;
        public int PolicyChecks { get; private set; }
        public string? LastGameId { get; private set; }
        public Task<bool> AllowsNewAssignmentsAsync(string gameId, CancellationToken cancellationToken)
        { PolicyChecks++; LastGameId = gameId; return Task.FromResult(Enabled); }
        public Task<bool> TryReserveAsync(string profileId, string gameId, string assignmentId,
            DateTime nowUtc, CancellationToken cancellationToken) => throw new InvalidOperationException("House seats do not reserve shared profiles.");
        public Task HeartbeatAsync(string profileId, string gameId, string assignmentId,
            DateTime nowUtc, CancellationToken cancellationToken) => throw new InvalidOperationException("House seats have no shared lease.");
        public Task<bool> TryRenewAsync(string gameId, string assignmentId,
            IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken) => throw new InvalidOperationException("House seats have no shared lease.");
        public Task<bool> ReleaseAsync(string profileId, string assignmentId,
            DateTime nowUtc, CancellationToken cancellationToken) => throw new InvalidOperationException("House seats have no shared lease.");
    }
}
