using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Server.Bots;

internal sealed class ManagedPlayerQueuer(
    IManagedPlayerProfileGenerator generator,
    IManagedPlayerProfileRepository profiles,
    IManagedPlayerAssignmentStore assignments,
    ILogger<ManagedPlayerQueuer>? logger = null) : IManagedPlayerQueuer
{
    private const int CandidateLimit = 100;

    public async Task<IReadOnlyList<ManagedPlayerProfile>> ReserveAsync(
        string gameId,
        string assignmentId,
        int count,
        IReadOnlyCollection<string> excludedProfileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (count <= 0) return [];
        var excluded = excludedProfileIds.ToHashSet(StringComparer.Ordinal);
        var candidates = await profiles.ListSupportingAsync(
            gameId, CandidateLimit, cancellationToken);

        var selected = new List<ManagedPlayerProfile>(count);
        try
        {
            foreach (var candidate in candidates
                     .Where(candidate => !excluded.Contains(candidate.UserId))
                     .DistinctBy(candidate => candidate.UserId, StringComparer.Ordinal)
                     .OrderBy(candidate => StableRank(
                         gameId, assignmentId, candidate.UserId)))
            {
                if (!await assignments.TryReserveAsync(
                        candidate.UserId, gameId, assignmentId, nowUtc, cancellationToken))
                    continue;
                selected.Add(candidate);
                excluded.Add(candidate.UserId);
                if (selected.Count == count) return selected;
            }

            while (selected.Count < count)
            {
                var profile = await generator.GenerateAsync(gameId, nowUtc, cancellationToken);
                if (excluded.Contains(profile.UserId) ||
                    !await assignments.TryReserveAsync(
                        profile.UserId, gameId, assignmentId, nowUtc, cancellationToken))
                    continue;
                selected.Add(profile);
                excluded.Add(profile.UserId);
            }
            return selected;
        }
        catch
        {
            // No caller receives a partial supply, so none of these leases can be used.
            // A cancelled request must not prevent releasing profiles already reserved.
            foreach (var profile in selected)
            {
                try
                {
                    await ReleaseAsync(assignmentId, [profile.UserId], nowUtc, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    logger?.LogWarning(cleanupException,
                        "Unable to release an incomplete managed-player reservation for assignment {AssignmentId}.",
                        assignmentId);
                }
            }
            throw;
        }
    }

    public async Task HeartbeatAsync(
        string gameId,
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        foreach (var profileId in profileIds.Distinct(StringComparer.Ordinal))
        {
            await assignments.HeartbeatAsync(
                profileId, gameId, assignmentId, nowUtc, cancellationToken);
        }
    }

    public Task<bool> TryRenewAsync(
        string gameId,
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        assignments.TryRenewAsync(gameId, assignmentId, profileIds, nowUtc, cancellationToken);

    public async Task ReleaseAsync(
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        foreach (var profileId in profileIds.Distinct(StringComparer.Ordinal))
        {
            if (await assignments.ReleaseAsync(profileId, assignmentId, nowUtc, cancellationToken))
                await profiles.MarkLastActiveAsync(profileId, nowUtc, cancellationToken);
        }
    }

    private static string StableRank(string gameId, string assignmentId, string userId) =>
        Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{gameId}\n{assignmentId}\n{userId}")));
}
