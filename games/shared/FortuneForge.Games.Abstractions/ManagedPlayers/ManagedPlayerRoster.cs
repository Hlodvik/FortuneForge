namespace FortuneForge.Games.Abstractions.ManagedPlayers;

public sealed record ManagedPlayerRosterProfile(string UserId, string PlayerName, int SkillLevel);

/// <summary>Reserves and maintains the platform's managed player leases for one game.</summary>
public interface IManagedPlayerRosterClient
{
    Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveAsync(string assignmentId, int count,
        IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken cancellationToken);

    Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds,
        DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Returns false only when the platform confirms the assignment no longer owns every requested lease.</summary>
    Task<bool> TryRenewAsync(string assignmentId, IReadOnlyCollection<string> profileIds,
        DateTime nowUtc, CancellationToken cancellationToken) => Task.FromException<bool>(new ManagedPlayerRosterUnavailableException());

    Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds,
        DateTime nowUtc, CancellationToken cancellationToken);
}

public sealed class ManagedPlayerRosterUnavailableException(Exception? innerException = null)
    : InvalidOperationException("The game's managed player roster is temporarily unavailable.", innerException);
