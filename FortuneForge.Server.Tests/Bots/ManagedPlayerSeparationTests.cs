using FortuneForge.Server.Bots;
using FortuneForge.Server.Cards.Bots;
using Xunit;

namespace FortuneForge.Server.Tests.Bots;

public sealed class ManagedPlayerSeparationTests
{
    private static readonly DateTime Now =
        new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Queuer_reuses_an_available_profile_without_generating_one()
    {
        var generator = new RecordingGenerator();
        var assignments = new RecordingAssignmentStore();
        var queuer = new ManagedPlayerQueuer(
            new SingleProfileDirectory(), generator, new EmptyProfileRepository(), assignments);

        var profile = Assert.Single(await queuer.ReserveAsync(
            CardBotGames.Blackjack, "table-1", 1, [], Now, default));

        Assert.Equal("managed-seed-player", profile.UserId);
        Assert.Equal(1, generator.EnsureCalls);
        Assert.Equal(0, generator.GenerateCalls);
        Assert.Equal(["managed-seed-player"], assignments.ReservationAttempts);
    }

    [Fact]
    public async Task Queuer_calls_generator_only_after_existing_profiles_are_unavailable()
    {
        var generator = new RecordingGenerator();
        var assignments = new RecordingAssignmentStore("managed-seed-player");
        var queuer = new ManagedPlayerQueuer(
            new SingleProfileDirectory(), generator, new EmptyProfileRepository(), assignments);

        var profile = Assert.Single(await queuer.ReserveAsync(
            CardBotGames.Blackjack, "table-2", 1, [], Now, default));

        Assert.Equal("managed-generated-player", profile.UserId);
        Assert.Equal(1, generator.GenerateCalls);
        Assert.Equal(
            ["managed-seed-player", "managed-generated-player"],
            assignments.ReservationAttempts);
    }

    [Fact]
    public async Task Generator_retries_identity_collisions_through_profile_repository()
    {
        var repository = new CollisionOnceProfileRepository();
        var generator = new ManagedPlayerProfileGenerator(repository);

        var profile = await generator.GenerateAsync(CardBotGames.Blackjack, Now, default);

        Assert.Equal(2, repository.CreateCalls);
        Assert.StartsWith("managed-", profile.UserId, StringComparison.Ordinal);
        Assert.Contains(CardBotGames.Blackjack, profile.SupportedGames);
    }

    private sealed class SingleProfileDirectory : IBotDirectory
    {
        public IReadOnlyList<BotProfile> Profiles { get; } =
        [
            new(
                "seed-player",
                "SeedPlayer",
                3,
                new HashSet<string>([CardBotGames.Blackjack], StringComparer.OrdinalIgnoreCase))
        ];

        public IReadOnlyList<BotProfile> Select(
            string gameId,
            ulong selectionSeed,
            int count,
            int? preferredSkillLevel = null) => Profiles.Take(count).ToArray();
    }

    private sealed class RecordingGenerator : IManagedPlayerProfileGenerator
    {
        public int EnsureCalls { get; private set; }
        public int GenerateCalls { get; private set; }

        public Task<bool> EnsurePersistedAsync(
            ManagedPlayerProfile profile,
            CancellationToken cancellationToken)
        {
            EnsureCalls++;
            return Task.FromResult(true);
        }

        public Task<ManagedPlayerProfile> GenerateAsync(
            string gameId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            GenerateCalls++;
            return Task.FromResult(new ManagedPlayerProfile(
                "managed-generated-player",
                "GeneratedPlayer",
                3,
                new HashSet<string>([gameId], StringComparer.OrdinalIgnoreCase),
                nowUtc));
        }
    }

    private sealed class EmptyProfileRepository : IManagedPlayerProfileRepository
    {
        public Task<bool> EnsurePersistedAsync(
            ManagedPlayerProfile profile,
            CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> TryCreateAsync(
            ManagedPlayerProfile profile,
            CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ManagedPlayerProfile>> ListSupportingAsync(
            string gameId,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagedPlayerProfile>>([]);
        public Task MarkLastActiveAsync(
            string profileId,
            DateTime nowUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CollisionOnceProfileRepository : IManagedPlayerProfileRepository
    {
        public int CreateCalls { get; private set; }

        public Task<bool> EnsurePersistedAsync(
            ManagedPlayerProfile profile,
            CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> TryCreateAsync(
            ManagedPlayerProfile profile,
            CancellationToken cancellationToken) => Task.FromResult(++CreateCalls > 1);
        public Task<IReadOnlyList<ManagedPlayerProfile>> ListSupportingAsync(
            string gameId,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagedPlayerProfile>>([]);
        public Task MarkLastActiveAsync(
            string profileId,
            DateTime nowUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingAssignmentStore(params string[] unavailableProfileIds)
        : IManagedPlayerAssignmentStore
    {
        private readonly HashSet<string> unavailable =
            new(unavailableProfileIds, StringComparer.Ordinal);
        public List<string> ReservationAttempts { get; } = [];

        public Task<bool> TryReserveAsync(
            string profileId,
            string gameId,
            string assignmentId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            ReservationAttempts.Add(profileId);
            return Task.FromResult(!unavailable.Contains(profileId));
        }

        public Task HeartbeatAsync(
            string profileId,
            string gameId,
            string assignmentId,
            DateTime nowUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> ReleaseAsync(
            string profileId,
            string assignmentId,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
