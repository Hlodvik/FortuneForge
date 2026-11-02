using FortuneForge.Server.Admin.Management;
using FortuneForge.Server.Catalog;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Catalog;

public sealed class CatalogPopularityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UninitializedTrackingReturnsUnknownInsteadOfInventedHistoricalCounts()
    {
        using var service = new CatalogPopularityService(new Store(new(null, new Dictionary<string, long>())), new Clock());
        var result = await service.GetAsync(default);
        Assert.Null(result.TrackingStartedAtUtc);
        Assert.Equal(CatalogContract.Entries,
            result.Games.Select(game => (game.Id, game.Route)).OrderBy(game => game.Id, StringComparer.Ordinal).ToArray());
        Assert.All(result.Games, game => Assert.Null(game.PlayCount));
        Assert.Equal("recorded-plays", result.Metric);
    }

    [Fact]
    public async Task CountersUseCanonicalCatalogueRoutesAndOnlyDefaultToZeroAfterTrackingStarts()
    {
        var store = new Store(new(Now.AddHours(-1), new Dictionary<string, long>
        {
            ["wukong-journey-to-the-west"] = 85, ["hearts"] = 12, ["unregistered"] = 999,
            ["free-solitaire"] = 7, ["solitaire"] = 3, ["mines"] = 5, ["liars-dice"] = 2, ["asteroids"] = 1,
        }));
        using var service = new CatalogPopularityService(store, new Clock());
        var result = await service.GetAsync(default);
        Assert.Equal(CatalogContract.Entries,
            result.Games.Select(game => (game.Id, game.Route)).OrderBy(game => game.Id, StringComparer.Ordinal).ToArray());
        Assert.Equal(CatalogContract.Entries.Count, result.Games.Select(game => game.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(CatalogContract.Entries.Count, result.Games.Select(game => game.Route).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(CatalogContract.DefaultVisibleIds,
            AdminManagementCatalog.Games.Where(game => game.DefaultVisible).Select(game => game.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.Equal(85, Assert.Single(result.Games, game => game.Route == "/slots/wukong").PlayCount);
        Assert.Equal(12, Assert.Single(result.Games, game => game.Id == "hearts").PlayCount);
        Assert.Equal(7, Assert.Single(result.Games, game => game.Route == "/cards/free-solitaire").PlayCount);
        Assert.Equal(3, Assert.Single(result.Games, game => game.Route == "/cards/solitaire").PlayCount);
        Assert.Equal(5, Assert.Single(result.Games, game => game.Route == "/games/mines").PlayCount);
        // Popularity describes all managed routes; visibility filtering belongs to the catalogue consumer.
        Assert.Equal(2, Assert.Single(result.Games, game => game.Id == "liars-dice").PlayCount);
        Assert.Equal(1, Assert.Single(result.Games, game => game.Id == "asteroids").PlayCount);
        Assert.Equal(0, Assert.Single(result.Games, game => game.Id == "craps").PlayCount);
        Assert.DoesNotContain(result.Games, game => game.Id == "unregistered");
    }

    [Fact]
    public async Task ConcurrentReadersShareOneCachedReadThenRefreshAfterOneMinute()
    {
        var clock = new Clock();
        var store = new Store(new(Now.AddHours(-1), new Dictionary<string, long> { ["hearts"] = 2 }));
        using var service = new CatalogPopularityService(store, clock);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.GetAsync(default)));
        Assert.Equal(1, store.Reads);
        clock.Now = Now.AddSeconds(61);
        store.Snapshot = new(Now.AddHours(-1), new Dictionary<string, long> { ["hearts"] = 3 });
        var refreshed = await service.GetAsync(default);
        Assert.Equal(2, store.Reads);
        Assert.Equal(3, Assert.Single(refreshed.Games, game => game.Id == "hearts").PlayCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9_007_199_254_740_992)]
    public async Task CorruptCountsFailRatherThanBeingPresentedAsPopularity(long count)
    {
        using var service = new CatalogPopularityService(new Store(new(Now, new Dictionary<string, long> { ["hearts"] = count })), new Clock());
        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetAsync(default));
    }

    [Fact]
    public async Task StoreFailureReturnsUnavailableWithoutExposingDetailsOrFakeZeroCounts()
    {
        var store = new Store(new(null, new Dictionary<string, long>())) { Failure = new IOException("private database diagnostic") };
        using var provider = new ServiceCollection()
            .AddSingleton<ICatalogPopularityStore>(store)
            .AddSingleton<TimeProvider>(new Clock())
            .AddSingleton<CatalogPopularityService>().BuildServiceProvider();
        var controller = new CatalogPopularityController(provider, NullLogger<CatalogPopularityController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = Assert.IsType<ObjectResult>(await controller.Get(default));
        Assert.Equal(503, result.StatusCode);
        Assert.DoesNotContain("private database", System.Text.Json.JsonSerializer.Serialize(result.Value));
        Assert.DoesNotContain("playCount", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = CatalogPopularityTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Store(CatalogPopularitySnapshot snapshot) : ICatalogPopularityStore
    {
        public CatalogPopularitySnapshot Snapshot { get; set; } = snapshot;
        public int Reads { get; private set; }
        public Exception? Failure { get; init; }
        public Task<CatalogPopularitySnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Failure is null ? Task.FromResult(Snapshot) : Task.FromException<CatalogPopularitySnapshot>(Failure);
        }
    }
}
