using Microsoft.Extensions.Caching.Memory;

namespace FortuneForge.Server.Activity;

// A rapid run may produce hundreds of accepted moves. Keep its first play and
// final result, and refresh ongoing activity at most twice a minute. Switching
// games/rounds bypasses the throttle so the Home ordering stays meaningful.
internal sealed class GameActivityRecorder(IGameActivityStore store, TimeProvider clock) : IDisposable
{
    private readonly MemoryCache tracked = new(new MemoryCacheOptions { SizeLimit = 10_000 });
    internal sealed record LastPlay(string GameId, string RoundKey, DateTime AtUtc, bool IsTerminal, string EventKey);

    public async Task RecordAsync(string userId, AcceptedGameActivity activity, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var key = AcceptedGameActivityReader.Hash(userId);
        if (tracked.TryGetValue<LastPlay>(key, out var prior) && prior is not null && prior.GameId == activity.GameId && prior.RoundKey == activity.RoundKey &&
            (prior.EventKey == activity.EventKey || (!activity.IsTerminal && now - prior.AtUtc < TimeSpan.FromSeconds(30)))) return;
        await store.RecordAsync(userId, activity, now, ct);
        // Cache only successful writes. A failed write will be retried on the
        // next genuine action, rather than suppressing activity for the window.
        tracked.Set(key, new LastPlay(activity.GameId, activity.RoundKey, now, activity.IsTerminal, activity.EventKey),
            new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromHours(1) });
    }

    public void Dispose() => tracked.Dispose();
}
