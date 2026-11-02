using FortuneForge.Server.Admin.Management;
using Google.Cloud.Firestore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Server.Catalog;

public sealed record CatalogPopularityGame(string Id, string Route, long? PlayCount);
public sealed record CatalogPopularityResponse(string Metric, DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? TrackingStartedAtUtc, IReadOnlyList<CatalogPopularityGame> Games);

internal sealed record CatalogPopularitySnapshot(DateTimeOffset? StartedAtUtc, IReadOnlyDictionary<string, long> Counts);

internal interface ICatalogPopularityStore
{
    Task<CatalogPopularitySnapshot> ReadAsync(CancellationToken cancellationToken);
}

internal sealed class FirestoreCatalogPopularityStore(FirestoreDb database) : ICatalogPopularityStore
{
    public async Task<CatalogPopularitySnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var metadata = await database.Document("catalogGamePopularityMetadata/tracking").GetSnapshotAsync(cancellationToken);
        // Old deployments did not record every game consistently. Do not manufacture
        // an all-time ranking from the subset that happened to retain round history.
        if (!metadata.Exists) return new(null, new Dictionary<string, long>());
        if (!metadata.TryGetValue<long>("schemaVersion", out var version) || version != 1 ||
            !metadata.TryGetValue<Timestamp>("startedAt", out var startedAt))
            throw new InvalidDataException("Unsupported game popularity metadata.");

        var documents = await Task.WhenAll(AdminManagementCatalog.Games.Select(game =>
            database.Collection("catalogGamePopularity").Document(game.Id).GetSnapshotAsync(cancellationToken)));
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var document in documents.Where(document => document.Exists))
        {
            if (!document.TryGetValue<long>("playCount", out var count))
                throw new InvalidDataException("Invalid game popularity counter.");
            counts.Add(document.Id, count);
        }
        return new(startedAt.ToDateTimeOffset(), counts);
    }
}

internal sealed class CatalogPopularityService(ICatalogPopularityStore store, TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private CatalogPopularityResponse? cached;
    private DateTimeOffset expiresAt;
    private const long LargestSafeBrowserInteger = 9_007_199_254_740_991;

    public async Task<CatalogPopularityResponse> GetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached is not null && timeProvider.GetUtcNow() < expiresAt) return cached;
            var snapshot = await store.ReadAsync(cancellationToken);
            if (snapshot.Counts.Values.Any(count => count < 0 || count > LargestSafeBrowserInteger))
                throw new InvalidDataException("Invalid game popularity counter.");

            var now = timeProvider.GetUtcNow();
            if (snapshot.StartedAtUtc > now) throw new InvalidDataException("Invalid popularity tracking start.");
            var games = AdminManagementCatalog.Games.Select(game => new CatalogPopularityGame(
                game.Id, game.Route, snapshot.StartedAtUtc.HasValue ? snapshot.Counts.GetValueOrDefault(game.Id) : null)).ToArray();
            cached = new("recorded-plays", now, snapshot.StartedAtUtc, games);
            expiresAt = now.AddMinutes(1);
            return cached;
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}

public static class CatalogPopularityConfiguration
{
    public static IServiceCollection AddCataloguePopularity(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ICatalogPopularityStore, FirestoreCatalogPopularityStore>();
        services.AddSingleton<CatalogPopularityService>();
        return services;
    }
}
