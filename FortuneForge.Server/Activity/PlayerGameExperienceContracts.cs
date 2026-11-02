using FortuneForge.Server.Admin.Management;

namespace FortuneForge.Server.Activity;

public sealed record PlayerGameExperience(
    string GameId, string State, bool? HasPlayed, IReadOnlyList<string> Provenance,
    string Coverage, DateTime? FirstPlayedAtUtc);

public sealed record PlayerGameExperienceResponse(
    int SchemaVersion, string UserId, DateTime GeneratedAtUtc, IReadOnlyList<PlayerGameExperience> Games);

internal interface IPlayerGameExperienceStore
{
    Task<PlayerGameExperienceResponse> ReadAsync(string userId, CancellationToken ct);
    Task RecordAcceptedAsync(string userId, AcceptedGameActivity activity, CancellationToken ct);
}

internal sealed record PlayerGameExperienceEvidence(string GameId, string Provenance);

internal static class PlayerGameExperienceProjection
{
    // Visibility controls discovery, not whether an accepted game has been played.
    internal static readonly IReadOnlySet<string> GameIds = AdminManagementCatalog.Games
        .Select(game => game.Id).ToHashSet(StringComparer.Ordinal);

    internal static PlayerGameExperienceResponse Create(string userId, DateTime now,
        IEnumerable<PlayerGameExperienceEvidence> evidence, bool anySourceAvailable,
        IReadOnlyList<GameExperienceCoverageProof>? complete = null)
    {
        var known = evidence.Where(item => GameIds.Contains(item.GameId))
            .GroupBy(item => item.GameId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => (IReadOnlyList<string>)group.Select(item => item.Provenance)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var covered = (complete ?? []).Where(proof => proof.AsOfUtc <= now && proof.ValidUntilUtc > now)
            .GroupBy(proof => proof.GameId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(proof => proof.AsOfUtc).First(), StringComparer.Ordinal);
        return new(1, userId, now, GameIds.Order(StringComparer.Ordinal).Select(game =>
            known.TryGetValue(game, out var sources)
                ? new PlayerGameExperience(game, "known", true, sources, "partial", null)
                : covered.TryGetValue(game, out var proof)
                    ? new PlayerGameExperience(game, "known", false,
                        proof.OriginId is { } origin
                            ? [proof.SourceVersion ?? AuthoritativeGameExperienceCoverage.SourceVersion, "coverage-activation:" + proof.ActivationId, "creation-origin:" + origin]
                            : [proof.SourceVersion ?? AuthoritativeGameExperienceCoverage.SourceVersion, "coverage-activation:" + proof.ActivationId], "complete", null)
                : new PlayerGameExperience(game, "unknown", null, [], anySourceAvailable ? "partial" : "unavailable", null))
            .ToArray());
    }
}
