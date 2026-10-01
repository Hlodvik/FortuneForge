using FortuneForge.Server.Bots;
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
            generator, new ExistingProfileRepository(), assignments);

        var profile = Assert.Single(await queuer.ReserveAsync(
            ManagedPlayerGames.Blackjack, "table-1", 1, [], Now, default));

        Assert.Equal("managed-seed-player", profile.UserId);
        Assert.Equal(0, generator.GenerateCalls);
        Assert.Equal(["managed-seed-player"], assignments.ReservationAttempts);
    }

    [Fact]
    public async Task Queuer_calls_generator_only_after_existing_profiles_are_unavailable()
    {
        var generator = new RecordingGenerator();
        var assignments = new RecordingAssignmentStore("managed-seed-player");
        var queuer = new ManagedPlayerQueuer(
            generator, new ExistingProfileRepository(), assignments);

        var profile = Assert.Single(await queuer.ReserveAsync(
            ManagedPlayerGames.Blackjack, "table-2", 1, [], Now, default));

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

        var profile = await generator.GenerateAsync(ManagedPlayerGames.Blackjack, Now, default);

        Assert.Equal(2, repository.CreateCalls);
        Assert.StartsWith("managed-", profile.UserId, StringComparison.Ordinal);
        Assert.Contains(ManagedPlayerGames.Blackjack, profile.SupportedGames);
    }

    [Fact]
    public void IdentityFactory_creates_unique_profile_shaped_human_style_identities()
    {
        var profiles = Enumerable.Range(0, 64)
            .Select(_ => ManagedPlayerIdentityFactory.Create(ManagedPlayerGames.Blackjack, Now))
            .ToArray();

        Assert.Equal(64, profiles.Select(profile => profile.UserId)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.All(profiles, profile =>
        {
            Assert.StartsWith("managed-", profile.UserId, StringComparison.Ordinal);
            Assert.Matches("^[A-Za-z0-9 _-]{3,24}$", profile.PlayerName);
            Assert.Contains(ManagedPlayerGames.Blackjack, profile.SupportedGames);
            Assert.InRange(profile.SkillLevel, 2, 4);
            Assert.Equal(Now, profile.CreatedAtUtc);
        });
    }

    [Fact]
    public void IdentityFactory_supports_distinct_human_username_styles()
    {
        var names = Enumerable.Range(0, ManagedPlayerIdentityFactory.PlayerNameStyleCount)
            .Select(style => ManagedPlayerIdentityFactory.CreatePlayerName((ulong)style))
            .ToArray();

        Assert.Equal(ManagedPlayerIdentityFactory.PlayerNameStyleCount,
            names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(names, name => name.Contains(' '));
        Assert.Contains(names, name => name.Contains('_'));
        Assert.Contains(names, name => name.Contains('-'));
        Assert.Contains(names, name => char.IsDigit(name[0]));
        Assert.Contains(names, name => !name.Any(char.IsDigit));
        Assert.Contains(names, name => name.All(character =>
            char.IsLower(character) || char.IsDigit(character)));
        Assert.DoesNotContain(names, name =>
            System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z]+[0-9]{2}$"));
    }

    [Theory]
    [InlineData("velvetnoise", "6velvetnoise")]
    [InlineData("Velvet-Noise", "velvet_noise")]
    [InlineData("abcdefghij", "abcdefghik")]
    public void NamePolicyRejectsOrderSensitiveMatchesAtOrAboveNinetyPercent(
        string candidate,
        string existing)
    {
        Assert.False(ManagedPlayerNamePolicy.IsDistinct(candidate, [existing]));
    }

    [Fact]
    public void NamePolicyAllowsNamesWhoseCharactersAppearInAQuiteDifferentOrder()
    {
        Assert.True(ManagedPlayerNamePolicy.IsDistinct("edtaddletell", ["tedraddles"]));
    }

    [Fact]
    public void DecisionRandomnessIsDeterministicInsideTheServerOwnedAdapterBoundary()
    {
        var first = new DeterministicManagedPlayerRandom(42, "test-stream");
        var second = new DeterministicManagedPlayerRandom(42, "test-stream");

        Assert.Equal(
            Enumerable.Range(0, 8).Select(_ => first.Next(1_000)),
            Enumerable.Range(0, 8).Select(_ => second.Next(1_000)));
    }

    [Fact]
    public void AvailabilityPolicyStaggersRestAndEnforcesUsageLimits()
    {
        var sleeping = Enumerable.Range(0, 1_000)
            .Select(index => $"managed-sleep-{index}")
            .First(profileId => ManagedPlayerAvailabilityPolicy.IsSleeping(profileId, Now));
        var awake = Enumerable.Range(0, 1_000)
            .Select(index => $"managed-awake-{index}")
            .First(profileId => !ManagedPlayerAvailabilityPolicy.IsSleeping(profileId, Now));

        Assert.False(ManagedPlayerAvailabilityPolicy.IsAvailable(
            sleeping, Now, Now.AddHours(-1), 0, DateTime.UnixEpoch));
        Assert.True(ManagedPlayerAvailabilityPolicy.IsAvailable(
            awake, Now, Now.AddHours(-1), 0, DateTime.UnixEpoch));
        Assert.False(ManagedPlayerAvailabilityPolicy.IsAvailable(
            awake, Now, Now.AddHours(-1), 7_200, DateTime.UnixEpoch));
        Assert.False(ManagedPlayerAvailabilityPolicy.IsAvailable(
            awake, Now, Now.AddHours(-9), 0, Now.AddMinutes(1)));
        Assert.True(ManagedPlayerAvailabilityPolicy.IsAvailable(
            awake, Now, Now.AddHours(-9), 7_200, DateTime.UnixEpoch));
    }

    private sealed class RecordingGenerator : IManagedPlayerProfileGenerator
    {
        public int GenerateCalls { get; private set; }

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

    private sealed class ExistingProfileRepository : IManagedPlayerProfileRepository
    {
        public Task<bool> TryCreateAsync(
            ManagedPlayerProfile profile,
            CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ManagedPlayerProfile>> ListSupportingAsync(
            string gameId,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagedPlayerProfile>>([
                new(
                    "managed-seed-player",
                    "SeedPlayer",
                    3,
                    new HashSet<string>([ManagedPlayerGames.Blackjack], StringComparer.OrdinalIgnoreCase),
                    Now)
            ]);
        public Task MarkLastActiveAsync(
            string profileId,
            DateTime nowUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CollisionOnceProfileRepository : IManagedPlayerProfileRepository
    {
        public int CreateCalls { get; private set; }

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
            DateTime nowUtc,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
