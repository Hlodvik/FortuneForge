using System.Security.Cryptography;

namespace FortuneForge.Server.Bots;

internal sealed record ManagedPlayerProfile(
    string UserId,
    string PlayerName,
    int SkillLevel,
    IReadOnlySet<string> SupportedGames,
    DateTime CreatedAtUtc);

internal interface IManagedPlayerProfileGenerator
{
    Task<bool> EnsurePersistedAsync(
        ManagedPlayerProfile profile,
        CancellationToken cancellationToken);

    Task<ManagedPlayerProfile> GenerateAsync(
        string gameId,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal interface IManagedPlayerProfileRepository
{
    Task<bool> EnsurePersistedAsync(
        ManagedPlayerProfile profile,
        CancellationToken cancellationToken);

    Task<bool> TryCreateAsync(
        ManagedPlayerProfile profile,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ManagedPlayerProfile>> ListSupportingAsync(
        string gameId,
        int limit,
        CancellationToken cancellationToken);

    Task MarkLastActiveAsync(
        string profileId,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal interface IManagedPlayerAssignmentStore
{
    Task<bool> TryReserveAsync(
        string profileId,
        string gameId,
        string assignmentId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task HeartbeatAsync(
        string profileId,
        string gameId,
        string assignmentId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<bool> ReleaseAsync(
        string profileId,
        string assignmentId,
        CancellationToken cancellationToken);
}

internal interface IManagedPlayerQueuer
{
    Task<IReadOnlyList<ManagedPlayerProfile>> ReserveAsync(
        string gameId,
        string assignmentId,
        int count,
        IReadOnlyCollection<string> excludedProfileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task HeartbeatAsync(
        string gameId,
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal static class ManagedPlayerIdentityFactory
{
    private static readonly string[] First =
    [
        "Maya", "Noah", "Avery", "Milo", "Lena", "Theo", "Nina", "Eli",
        "Sasha", "Remy", "Zara", "Ivy", "Kai", "Nico", "Mara", "Jules"
    ];
    private static readonly string[] Last =
    [
        "River", "Stone", "Vale", "Brooks", "Lane", "Reed", "Hart", "Gray",
        "Quinn", "Blake", "Wells", "Fox", "Parker", "Rowan", "Lake", "Sage"
    ];

    public static ManagedPlayerProfile Create(string gameId, DateTime nowUtc)
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        var seed = BitConverter.ToUInt64(bytes);
        var first = First[(int)(seed % (ulong)First.Length)];
        var last = Last[(int)((seed >> 8) % (ulong)Last.Length)];
        var suffix = 10 + (int)((seed >> 16) % 90);
        return new(
            $"managed-{Guid.NewGuid():N}",
            $"{first}{last}{suffix}",
            2 + (int)((seed >> 24) % 3),
            new HashSet<string>([gameId], StringComparer.OrdinalIgnoreCase),
            nowUtc);
    }
}
