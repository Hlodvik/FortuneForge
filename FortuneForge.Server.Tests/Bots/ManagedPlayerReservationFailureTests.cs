using FortuneForge.Server.Bots;
using Xunit;

namespace FortuneForge.Server.Tests.Bots;

public sealed class ManagedPlayerReservationFailureTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GenerationFailureReleasesProfilesAlreadyReservedFromTheSharedPool()
    {
        var failure = new InvalidOperationException("Generation failed");
        var repository = new Profiles(1);
        var assignments = new Assignments();
        var queuer = new ManagedPlayerQueuer(new FailingGenerator(failure), repository, assignments);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queuer.ReserveAsync(ManagedPlayerGames.Solitaire, "solitaire:match", 2, [], Now, default));

        Assert.Same(failure, actual);
        Assert.Equal(assignments.Reserved, assignments.Released);
        Assert.Equal(assignments.Reserved, repository.LastActive);
        Assert.All(assignments.CleanupTokens, token => Assert.False(token.CanBeCanceled));
    }

    [Fact]
    public async Task CancellationWhileReservingTheNextProfileDoesNotCancelLeaseCleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new Profiles(2);
        var assignments = new Assignments(onReserved: cancellation.Cancel);
        var queuer = new ManagedPlayerQueuer(new FailingGenerator(new InvalidOperationException()), repository, assignments);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            queuer.ReserveAsync(ManagedPlayerGames.Solitaire, "solitaire:cancelled", 2, [], Now, cancellation.Token));

        Assert.Single(assignments.Reserved);
        Assert.Equal(assignments.Reserved, assignments.Released);
        Assert.Equal(assignments.Reserved, repository.LastActive);
        Assert.All(assignments.CleanupTokens, token => Assert.False(token.CanBeCanceled));
    }

    [Fact]
    public async Task AFailedReleaseDoesNotMaskTheOriginalFailureOrPreventOtherCleanup()
    {
        var failure = new InvalidOperationException("Generation failed after reserving three profiles");
        var repository = new Profiles(3);
        var assignments = new Assignments(failFirstRelease: true);
        var queuer = new ManagedPlayerQueuer(new FailingGenerator(failure), repository, assignments);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queuer.ReserveAsync(ManagedPlayerGames.Solitaire, "solitaire:failed-cleanup", 4, [], Now, default));

        Assert.Same(failure, actual);
        Assert.Equal(3, assignments.Reserved.Count);
        Assert.Equal(assignments.Reserved, assignments.Released);
        Assert.Equal(assignments.Reserved.Skip(1), repository.LastActive);
    }

    private sealed class FailingGenerator(Exception failure) : IManagedPlayerProfileGenerator
    {
        public Task<ManagedPlayerProfile> GenerateAsync(string gameId, DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromException<ManagedPlayerProfile>(failure);
    }

    private sealed class Profiles(int count) : IManagedPlayerProfileRepository
    {
        public List<string> LastActive { get; } = [];
        public Task<bool> TryCreateAsync(ManagedPlayerProfile profile, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ManagedPlayerProfile>> ListSupportingAsync(string gameId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagedPlayerProfile>>(Enumerable.Range(0, count).Select(index => new ManagedPlayerProfile(
                $"managed-existing-{index}", $"Player {index}", 3,
                new HashSet<string>([gameId], StringComparer.OrdinalIgnoreCase), Now)).ToArray());
        public Task MarkLastActiveAsync(string profileId, DateTime nowUtc, CancellationToken cancellationToken)
        {
            Assert.False(cancellationToken.CanBeCanceled);
            LastActive.Add(profileId);
            return Task.CompletedTask;
        }
    }

    private sealed class Assignments(Action? onReserved = null, bool failFirstRelease = false) : IManagedPlayerAssignmentStore
    {
        public List<string> Reserved { get; } = [];
        public List<string> Released { get; } = [];
        public List<CancellationToken> CleanupTokens { get; } = [];
        public Task<bool> TryReserveAsync(string profileId, string gameId, string assignmentId, DateTime nowUtc, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reserved.Add(profileId);
            onReserved?.Invoke();
            return Task.FromResult(true);
        }
        public Task HeartbeatAsync(string profileId, string gameId, string assignmentId, DateTime nowUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> TryRenewAsync(string gameId, string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> ReleaseAsync(string profileId, string assignmentId, DateTime nowUtc, CancellationToken cancellationToken)
        {
            Released.Add(profileId);
            CleanupTokens.Add(cancellationToken);
            return failFirstRelease && Released.Count == 1
                ? Task.FromException<bool>(new InvalidOperationException("Release failed"))
                : Task.FromResult(true);
        }
    }
}
