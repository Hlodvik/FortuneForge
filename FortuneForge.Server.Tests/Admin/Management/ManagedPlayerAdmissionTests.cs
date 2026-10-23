using FortuneForge.Server.Admin.Management;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Hearts;
using FortuneForge.Server.Bots.LiarsDice;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.Games.LiarsDice;
using Xunit;

namespace FortuneForge.Server.Tests.Admin.Management;

public sealed class ManagedPlayerAdmissionTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Disabled_admissions_do_not_query_or_generate_profiles_but_existing_lease_operations_continue()
    {
        var data = new Profiles();
        var assignments = new Assignments { Enabled = false };
        var queuer = new ManagedPlayerQueuer(data, data, assignments);
        Assert.Empty(await queuer.ReserveAsync("hearts", "test-table", 3, [], Now, default));
        Assert.Equal(0, data.Reads);
        Assert.Equal(0, data.Generated);
        Assert.Equal(0, assignments.Reservations);
        await queuer.HeartbeatAsync("hearts", "test-table", ["existing"], Now, default);
        Assert.True(await queuer.TryRenewAsync("hearts", "test-table", ["existing"], Now, default));
        await queuer.ReleaseAsync("test-table", ["existing"], Now, default);
        Assert.Equal(1, assignments.Heartbeats);
        Assert.Equal(1, assignments.Renewals);
        Assert.Equal(1, assignments.Releases);
    }

    [Fact]
    public async Task Compatibility_mode_stops_a_partial_batch_without_deleting_possibly_shared_legacy_leases()
    {
        var data = new Profiles();
        var assignments = new Assignments { DisableOnFirstReservation = true };
        var queuer = new ManagedPlayerQueuer(data, data, assignments);
        var reserved = await queuer.ReserveAsync("blackjack", "mid-request", 3, [], Now, default);
        Assert.Empty(reserved);
        Assert.Equal(1, assignments.Reservations);
        Assert.Equal(0, assignments.Releases);
        Assert.Equal(0, data.Generated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_Hearts_free_or_trial_table_reports_typed_unavailability_when_opponents_are_off(bool trial)
    {
        var roster = new Roster { Enabled = false };
        var service = new HeartsGameService(roster, TimeProvider.System);
        await Assert.ThrowsAsync<ManagedPlayerSupplyUnavailableException>(() => trial
            ? service.StartTrialAsync("alice")
            : service.StartAsync("alice", new(1931, 100)));
        Assert.Equal(1, roster.Releases);
    }

    [Fact]
    public async Task Existing_Hearts_trial_can_resume_and_play_after_new_admissions_are_disabled()
    {
        var roster = new Roster();
        var service = new HeartsGameService(roster, TimeProvider.System);
        var started = await service.StartTrialAsync("alice");
        roster.Enabled = false;
        var resumed = await service.StartTrialAsync("alice");
        Assert.Equal(started.MatchId, resumed.MatchId);
        var next = await service.PassAsync("alice", resumed.MatchId, resumed.Hand.Take(3).Select(card => card.Code).ToArray());
        Assert.Equal(started.MatchId, next.MatchId);
        Assert.Equal(1, roster.Reservations);
    }

    [Fact]
    public async Task Liars_Dice_requires_opponents_only_for_a_new_table_not_existing_play()
    {
        var roster = new Roster();
        var service = new LiarsDiceGameService(roster, TimeProvider.System);
        var started = await service.StartAsync("alice", new(1931, 5));
        roster.Enabled = false;
        var existing = await service.GetAsync("alice", started.MatchId);
        Assert.Equal(started.MatchId, existing.MatchId);
        await Assert.ThrowsAsync<ManagedPlayerSupplyUnavailableException>(() => service.StartAsync("bruno", new(1942, 5)));
    }

    private sealed class Roster : IHeartsManagedPlayerRoster, ILiarsDiceManagedPlayerRoster
    {
        public bool Enabled { get; set; } = true;
        public int Reservations { get; private set; }
        public int Releases { get; private set; }
        Task<IReadOnlyList<HeartsManagedPlayer>> IHeartsManagedPlayerRoster.ReserveAsync(string assignmentId, int count, IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken cancellationToken)
        { Reservations++; return Task.FromResult<IReadOnlyList<HeartsManagedPlayer>>(Enabled ? Enumerable.Range(0, count).Select(index => new HeartsManagedPlayer($"managed-{index}", $"Opponent{index}", 3)).ToArray() : []); }
        Task<IReadOnlyList<LiarsDiceManagedPlayer>> ILiarsDiceManagedPlayerRoster.ReserveAsync(string assignmentId, int count, IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken cancellationToken)
        { Reservations++; return Task.FromResult<IReadOnlyList<LiarsDiceManagedPlayer>>(Enabled ? Enumerable.Range(0, count).Select(index => new LiarsDiceManagedPlayer($"managed-{index}", $"Opponent{index}", 3)).ToArray() : []); }
        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken) { Releases++; return Task.CompletedTask; }
    }

    private sealed class Profiles : IManagedPlayerProfileGenerator, IManagedPlayerProfileRepository
    {
        public int Reads { get; private set; }
        public int Generated { get; private set; }
        public Task<ManagedPlayerProfile> GenerateAsync(string gameId, DateTime nowUtc, CancellationToken cancellationToken) { Generated++; return Task.FromResult(Profile("generated", gameId)); }
        public Task<bool> TryCreateAsync(ManagedPlayerProfile profile, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ManagedPlayerProfile>> ListSupportingAsync(string gameId, int limit, CancellationToken cancellationToken) { Reads++; return Task.FromResult<IReadOnlyList<ManagedPlayerProfile>>([Profile("first", gameId), Profile("second", gameId)]); }
        public Task MarkLastActiveAsync(string profileId, DateTime nowUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        private static ManagedPlayerProfile Profile(string id, string gameId) => new(id, id, 3, new HashSet<string>([gameId]), Now);
    }

    private sealed class Assignments : IManagedPlayerAssignmentStore, IManagedPlayerAdmissionPolicy
    {
        public bool Enabled { get; set; } = true;
        public bool DisableOnFirstReservation { get; set; }
        public int Reservations { get; private set; }
        public int Heartbeats { get; private set; }
        public int Renewals { get; private set; }
        public int Releases { get; private set; }
        public Task<bool> AllowsNewAssignmentsAsync(string gameId, CancellationToken cancellationToken) => Task.FromResult(Enabled);
        public Task<bool> TryReserveAsync(string profileId, string gameId, string assignmentId, DateTime nowUtc, CancellationToken cancellationToken) { Reservations++; if (DisableOnFirstReservation) Enabled = false; return Task.FromResult(true); }
        public Task HeartbeatAsync(string profileId, string gameId, string assignmentId, DateTime nowUtc, CancellationToken cancellationToken) { Heartbeats++; return Task.CompletedTask; }
        public Task<bool> TryRenewAsync(string gameId, string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken) { Renewals++; return Task.FromResult(true); }
        public Task<bool> ReleaseAsync(string profileId, string assignmentId, DateTime nowUtc, CancellationToken cancellationToken) { Releases++; return Task.FromResult(true); }
    }
}
