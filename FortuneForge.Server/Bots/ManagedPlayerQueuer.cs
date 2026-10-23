using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Admin.Management;

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
        if (!await AdmissionsAllowed(gameId, cancellationToken)) return [];
        var excluded = excludedProfileIds.ToHashSet(StringComparer.Ordinal);
        var candidates = await profiles.ListSupportingAsync(
            gameId, CandidateLimit, cancellationToken);

        var selected = new List<ManagedPlayerProfile>(count);
        var batchId = Guid.NewGuid().ToString("N");
        var batches = assignments is IManagedPlayerReservationBatchStore { ProvisionalBatchesEnabled: true } enabled
            ? enabled : null;
        try
        {
            foreach (var candidate in candidates
                     .Where(candidate => !excluded.Contains(candidate.UserId))
                     .DistinctBy(candidate => candidate.UserId, StringComparer.Ordinal)
                     .OrderBy(candidate => StableRank(
                         gameId, assignmentId, candidate.UserId)))
            {
                if (!await AdmissionsAllowed(gameId, cancellationToken)) throw new AdmissionsStoppedException();
                if (!await ReserveProfile(candidate.UserId))
                    continue;
                selected.Add(candidate);
                excluded.Add(candidate.UserId);
                if (selected.Count == count) break;
            }

            while (selected.Count < count)
            {
                if (!await AdmissionsAllowed(gameId, cancellationToken)) throw new AdmissionsStoppedException();
                var profile = await generator.GenerateAsync(gameId, nowUtc, cancellationToken);
                if (excluded.Contains(profile.UserId) ||
                    !await ReserveProfile(profile.UserId))
                    continue;
                selected.Add(profile);
                excluded.Add(profile.UserId);
            }
            if (batches is not null && !await batches.CompleteBatchAsync(gameId, assignmentId, batchId,
                    selected.Select(profile => profile.UserId).ToArray(), nowUtc, cancellationToken))
                throw new AdmissionsStoppedException();
            return selected;
        }
        catch (Exception failure)
        {
            // Compatibility mode cannot distinguish ownership of same-table leases
            // shared with older binaries. Do not delete them on a policy stop;
            // unused partial reservations retain their bounded original lease.
            if (failure is AdmissionsStoppedException && batches is null) return [];
            // Only this call's still-provisional leases belong to its cleanup.
            // A concurrent same-table caller may already have committed them.
            // A cancelled request must not prevent releasing profiles already reserved.
            var cleanupFailed = false;
            foreach (var profile in selected)
            {
                try
                {
                    if (batches is null)
                        await ReleaseAsync(assignmentId, [profile.UserId], nowUtc, CancellationToken.None);
                    else if (await batches.ReleaseIncompleteBatchAsync(profile.UserId, assignmentId, batchId,
                                 nowUtc, CancellationToken.None))
                        await profiles.MarkLastActiveAsync(profile.UserId, nowUtc, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    cleanupFailed = true;
                    logger?.LogWarning(cleanupException,
                        "Unable to release an incomplete managed-player reservation for assignment {AssignmentId}.",
                        assignmentId);
                }
            }
            if (failure is AdmissionsStoppedException && !cleanupFailed) return [];
            throw;
        }

        Task<bool> ReserveProfile(string profileId) => batches is null
            ? assignments.TryReserveAsync(profileId, gameId, assignmentId, nowUtc, cancellationToken)
            : batches.TryReserveBatchAsync(profileId, gameId, assignmentId, batchId, nowUtc, cancellationToken);
    }

    private sealed class AdmissionsStoppedException : Exception;

    private Task<bool> AdmissionsAllowed(string gameId, CancellationToken cancellationToken) =>
        assignments is IManagedPlayerAdmissionPolicy policy
            ? policy.AllowsNewAssignmentsAsync(gameId, cancellationToken)
            : Task.FromResult(true);

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
