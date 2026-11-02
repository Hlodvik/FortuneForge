using FortuneForge.Games.Abstractions.Analytics;
using Google.Cloud.Firestore;
using Grpc.Core;
using System.Globalization;

namespace FortuneForge.Server.Activity;

internal sealed class FirestoreGameAnalyticsStore(FirestoreDb database, TimeProvider clock) : IGameAnalyticsReader
{
    private sealed record PresenceSnapshot(DateTime AsOfUtc, IReadOnlyDictionary<string, long> Games);
    private readonly SemaphoreSlim presenceGate = new(1, 1);
    private PresenceSnapshot? presenceSnapshot;
    internal const string Buckets = "gameAnalyticsBuckets";
    internal const string Presence = "gamePlayerPresence";
    private DocumentReference Metadata => database.Collection("gameAnalyticsMetadata").Document("tracking");
    private DocumentReference Bucket(string? userId, string periodKey) => database.Collection(Buckets)
        .Document((userId is null ? "global" : GameAnalyticsOutbox.Hash(userId)) + ":" + periodKey);

    internal Task InitializeAsync(CancellationToken ct) => Retry(async () => await database.RunTransactionAsync(async tx =>
    {
        var metadata = await tx.GetSnapshotAsync(Metadata, ct);
        if (!metadata.Exists) tx.Create(Metadata, new Dictionary<string, object>
        { ["schemaVersion"] = 1L, ["startedAt"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime), ["timeZone"] = "UTC" });
    }, cancellationToken: ct), ct);

    internal Task WriteOutboxAsync(GameAnalyticsSettlement value, CancellationToken ct)
    {
        var document = GameAnalyticsOutbox.Create(value);
        return document is null ? Task.CompletedTask : database.Collection(GameAnalyticsOutbox.CollectionName).Document(document.Id).SetAsync(document.Fields, cancellationToken: ct);
    }

    internal async Task<int> ProcessPendingAsync(int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var pending = await database.Collection(GameAnalyticsOutbox.CollectionName).WhereEqualTo("pending", true).Limit(limit).GetSnapshotAsync(ct);
        foreach (var item in pending.Documents) await ProjectAsync(item.Reference, ct);
        return pending.Count;
    }

    internal Task ProjectAsync(DocumentReference reference, CancellationToken ct) => Retry(async () =>
        await database.RunTransactionAsync(async tx =>
        {
            var document = await tx.GetSnapshotAsync(reference, ct);
            if (!document.Exists || !document.TryGetValue<bool>("pending", out var pending) || !pending) return;
            GameAnalyticsSettlement settlement;
            GameAnalyticsOutboxDocument? canonical;
            try { settlement = ReadSettlement(document); canonical = GameAnalyticsOutbox.Create(settlement); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                tx.Update(reference, new Dictionary<string, object> { ["pending"] = false, ["projectionError"] = "invalid-source-contract", ["reviewRequired"] = true });
                return;
            }
            if (canonical is null)
            {
                tx.Update(reference, new Dictionary<string, object> { ["pending"] = false, ["ignoredReason"] = "non-human" });
                return;
            }
            if (canonical.Id != reference.Id)
            {
                tx.Update(reference, new Dictionary<string, object> { ["pending"] = false, ["projectionError"] = "identity-mismatch", ["reviewRequired"] = true });
                return;
            }
            var receipt = database.Collection("gameAnalyticsParticipations").Document(canonical.Id);
            var round = database.Collection("gameAnalyticsRounds").Document((string)canonical.Fields["roundKey"]);
            var prior = await tx.GetSnapshotAsync(receipt, ct);
            if (prior.Exists)
            {
                tx.Update(reference, new Dictionary<string, object> { ["pending"] = false });
                return;
            }
            var priorRound = await tx.GetSnapshotAsync(round, ct);
            var metadata = await tx.GetSnapshotAsync(Metadata, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            tx.Create(receipt, new Dictionary<string, object>
            {
                ["userId"] = settlement.UserId, ["gameId"] = settlement.GameId, ["roundKey"] = round.Id,
                ["completedAt"] = Timestamp.FromDateTime(settlement.CompletedAtUtc), ["projectedAt"] = Timestamp.FromDateTime(now),
                ["sourceCollection"] = settlement.SourceCollection, ["sourceDocumentId"] = settlement.SourceDocumentId,
            });
            if (!priorRound.Exists)
                tx.Create(round, new Dictionary<string, object> { ["gameId"] = settlement.GameId, ["mode"] = settlement.Mode, ["completedAt"] = Timestamp.FromDateTime(settlement.CompletedAtUtc) });
            foreach (var period in GameAnalyticsPeriods.At(settlement.CompletedAtUtc))
            {
                Increment(tx, null, period, settlement.GameId, SettlementIncrements(settlement, !priorRound.Exists), now);
                Increment(tx, settlement.UserId, period, settlement.GameId, SettlementIncrements(settlement, true), now);
            }
            if (!metadata.Exists)
                tx.Create(Metadata, new Dictionary<string, object> { ["schemaVersion"] = 1L, ["startedAt"] = Timestamp.FromDateTime(now), ["timeZone"] = "UTC" });
            tx.Update(reference, new Dictionary<string, object> { ["pending"] = false, ["projectedAt"] = Timestamp.FromDateTime(now) });
        }, cancellationToken: ct), ct);

    internal static GameAnalyticsSettlement ReadSettlement(DocumentSnapshot item)
    {
        if (item.GetValue<long>("schemaVersion") != 1) throw new ArgumentException("Unknown analytics source version.");
        return new(item.GetValue<string>("gameId"), item.GetValue<string>("roundId"), item.GetValue<string>("userId"),
            item.GetValue<string>("mode"), item.GetValue<bool>("isHuman"), item.GetValue<Timestamp>("completedAt").ToDateTime(),
            item.TryGetValue<Timestamp?>("startedAt", out var start) ? start?.ToDateTime() : null,
            item.TryGetValue<long?>("wagerCents", out var wager) ? wager : null,
            item.TryGetValue<long?>("payoutCents", out var payout) ? payout : null,
            item.GetValue<string>("sourceCollection"), item.GetValue<string>("sourceDocumentId"));
    }

    private static Dictionary<string, object> SettlementIncrements(GameAnalyticsSettlement value, bool firstRound)
    {
        var paid = value.Mode == "paid";
        var known = paid && value.WagerCents is not null && value.PayoutCents is not null;
        var net = known ? value.PayoutCents!.Value - value.WagerCents!.Value : 0;
        return new()
        {
            ["humanRounds"] = FieldValue.Increment(1), ["uniqueRounds"] = FieldValue.Increment(firstRound ? 1 : 0),
            ["paidRounds"] = FieldValue.Increment(paid ? 1 : 0), ["practiceRounds"] = FieldValue.Increment(value.Mode == "practice" ? 1 : 0),
            ["freeRounds"] = FieldValue.Increment(value.Mode == "free" ? 1 : 0),
            ["knownMoneyRounds"] = FieldValue.Increment(known ? 1 : 0), ["unknownMoneyRounds"] = FieldValue.Increment(paid && !known ? 1 : 0),
            ["wagerCents"] = FieldValue.Increment(known ? value.WagerCents!.Value : 0), ["payoutCents"] = FieldValue.Increment(known ? value.PayoutCents!.Value : 0),
            ["netWinCents"] = FieldValue.Increment(Math.Max(0, net)), ["netLossCents"] = FieldValue.Increment(Math.Max(0, -net)),
        };
    }

    private void Increment(Transaction tx, string? userId, GameAnalyticsPeriod period, string gameId, Dictionary<string, object> metrics, DateTime now) =>
        tx.Set(Bucket(userId, period.Key), new Dictionary<string, object>
        {
            ["schemaVersion"] = 1L, ["updatedAt"] = Timestamp.FromDateTime(now), ["timeZone"] = "UTC",
            ["totals"] = metrics, ["games"] = new Dictionary<string, object> { [gameId] = metrics },
        }, SetOptions.MergeAll);

    public async Task<GameAnalyticsReport> ReadAsync(string period, string? date, string? userId, CancellationToken ct)
    {
        if (userId is not null && (string.IsNullOrWhiteSpace(userId) || userId.Length > 512 || userId.Any(char.IsControl))) throw new GameAnalyticsQueryException("Invalid user identifier.");
        var now = clock.GetUtcNow().UtcDateTime;
        var selection = GameAnalyticsPeriods.Resolve(period, date, now);
        var dataTask = Bucket(userId, selection.Key).GetSnapshotAsync(ct);
        var metadataTask = Metadata.GetSnapshotAsync(ct);
        var countTask = ReadPresenceAsync(userId, now, ct);
        await Task.WhenAll(dataTask, metadataTask, countTask);
        var data = dataTask.Result;
        var totals = data.Exists && data.TryGetValue<Dictionary<string, object>>("totals", out var saved) ? Metrics(saved) : new();
        var games = data.Exists && data.TryGetValue<Dictionary<string, object>>("games", out var savedGames) ? savedGames : [];
        return new(selection, now, metadataTask.Result.Exists ? metadataTask.Result.GetValue<Timestamp>("startedAt").ToDateTime() : null,
            totals, GameAnalyticsOutbox.GameIds.Order(StringComparer.Ordinal).Select(id => new GameAnalyticsGame(id,
                games.TryGetValue(id, out var value) && value is Dictionary<string, object> map ? Metrics(map) : new(),
                countTask.Result.Games.TryGetValue(id, out var active) ? active : 0)).ToArray(), countTask.Result.Games.Values.Sum(),
            ["UTC periods: weeks start Monday. Totals cover tracked completed rounds; legacy history is not silently backfilled.",
             "Human rounds count each human's participation; unique rounds count each physical game round once. Practice/free rounds never contribute real-money totals.",
             "Wager and payout are gross settled amounts in Rand cents. Net wins and net losses are summed separately; unknown paid money remains separately counted.",
             "Active players are distinct authenticated human accounts with a visible game heartbeat within 90 seconds. Time is observed foreground time between heartbeats, not full match duration.",
             "Paid settlements use a durable transactional outbox. In-memory free/practice games use a bounded hosted queue and cannot survive a process crash before persistence.",
             "Global live-player counts are cached for at most ten seconds; PresenceAsOfUtc identifies the snapshot time. Completed-round aggregates may lag by thirty seconds plus queued processing. Observed time is durably batched for up to five minutes plus polling delay, and flushed when leaving or switching games."], countTask.Result.AsOfUtc);
    }

    private async Task<PresenceSnapshot> ReadPresenceAsync(string? userId, DateTime now, CancellationToken ct)
    {
        if (userId is not null)
        {
            var item = await database.Collection(Presence).Document(GameAnalyticsOutbox.Hash(userId)).GetSnapshotAsync(ct);
            return new(now, item.Exists && item.TryGetValue<Timestamp>("expiresAt", out var expires) && expires.ToDateTime() > now
                ? new Dictionary<string, long> { [item.GetValue<string>("gameId")] = 1 } : new Dictionary<string, long>());
        }
        await presenceGate.WaitAsync(ct);
        try
        {
            if (presenceSnapshot is { } cached && now >= cached.AsOfUtc && now - cached.AsOfUtc < TimeSpan.FromSeconds(10)) return cached;
            var counts = await Task.WhenAll(GameAnalyticsOutbox.GameIds.Select(async game => new KeyValuePair<string, long>(game,
                (await database.Collection(Presence).WhereEqualTo("gameId", game).WhereGreaterThan("expiresAt", Timestamp.FromDateTime(now)).Count().GetSnapshotAsync(ct)).Count ?? 0)));
            presenceSnapshot = new(now, counts.ToDictionary(item => item.Key, item => item.Value));
            return presenceSnapshot;
        }
        finally { presenceGate.Release(); }
    }

    private static GameAnalyticsMetrics Metrics(Dictionary<string, object> values)
    {
        long Number(string key) => values.TryGetValue(key, out var value) && value is long number ? number : 0;
        return new(Number("humanRounds"), Number("uniqueRounds"), Number("paidRounds"), Number("practiceRounds"), Number("freeRounds"),
            Number("knownMoneyRounds"), Number("unknownMoneyRounds"), Number("wagerCents"), Number("payoutCents"), Number("netWinCents"), Number("netLossCents"), Number("observedActiveMilliseconds"));
    }

    internal Task TouchPresenceAsync(string userId, string gameId, string sessionId, bool visible, CancellationToken ct) => Retry(async () =>
        await database.RunTransactionAsync(async tx =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var reference = database.Collection(Presence).Document(GameAnalyticsOutbox.Hash(userId));
            var user = await tx.GetSnapshotAsync(database.Collection("users").Document(userId), ct);
            var previous = await tx.GetSnapshotAsync(reference, ct);
            var tags = user.Exists && user.TryGetValue<object[]>("profileTags", out var rawTags) ? rawTags : [];
            if (!user.Exists || tags.Any(tag => tag?.ToString() == "bot"))
            {
                if (previous.Exists) tx.Delete(reference);
                return;
            }
            var oldSession = previous.Exists && previous.TryGetValue<string>("sessionId", out var session) ? session : null;
            if (!visible && oldSession != sessionId) return;
            var pending = previous.Exists && previous.TryGetValue<Dictionary<string, long>>("pendingTime", out var priorTime) ? priorTime : [];
            var lastFlush = previous.Exists && previous.TryGetValue<Timestamp>("lastTimeFlushedAt", out var flushedAt) ? flushedAt.ToDateTime() : now;
            var oldGame = previous.Exists ? previous.GetValue<string>("gameId") : null;
            if (previous.Exists && previous.GetValue<string>("gameId") == gameId && previous.GetValue<Timestamp>("expiresAt").ToDateTime() > now)
            {
                var last = previous.GetValue<Timestamp>("lastSeenAt").ToDateTime();
                if (now > last && now - last <= TimeSpan.FromSeconds(45))
                    AccumulatePresenceTime(pending, gameId, last, now);
            }
            if (!visible || oldGame is not null && oldGame != gameId || now - lastFlush >= TimeSpan.FromMinutes(5))
            {
                FlushPresenceTime(tx, userId, pending, now);
                pending.Clear(); lastFlush = now;
            }
            var data = new Dictionary<string, object>
            {
                ["userId"] = userId, ["gameId"] = gameId, ["sessionId"] = sessionId, ["lastSeenAt"] = Timestamp.FromDateTime(now),
                ["expiresAt"] = Timestamp.FromDateTime(visible ? now.AddSeconds(90) : now), ["schemaVersion"] = 1L,
                ["pendingTime"] = pending, ["lastTimeFlushedAt"] = Timestamp.FromDateTime(lastFlush),
            };
            if (pending.Count > 0) data["timeFlushDueAt"] = Timestamp.FromDateTime(lastFlush.AddMinutes(5));
            tx.Set(reference, data);
        }, cancellationToken: ct), ct);

    internal async Task<int> FlushPendingPresenceAsync(int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var now = clock.GetUtcNow().UtcDateTime;
        // Missing due-at fields are excluded; idle accounts are never scanned.
        var due = await database.Collection(Presence).WhereLessThanOrEqualTo("timeFlushDueAt", Timestamp.FromDateTime(now)).Limit(limit).GetSnapshotAsync(ct);
        foreach (var item in due.Documents)
            await Retry(async () => await database.RunTransactionAsync(async tx =>
            {
                var current = await tx.GetSnapshotAsync(item.Reference, ct);
                if (!current.Exists || !current.TryGetValue<Timestamp>("timeFlushDueAt", out var dueAt) || dueAt.ToDateTime() > now) return;
                var pending = current.TryGetValue<Dictionary<string, long>>("pendingTime", out var time) ? time : [];
                FlushPresenceTime(tx, current.GetValue<string>("userId"), pending, now);
                tx.Update(item.Reference, new Dictionary<string, object>
                { ["pendingTime"] = new Dictionary<string, long>(), ["timeFlushDueAt"] = FieldValue.Delete, ["lastTimeFlushedAt"] = Timestamp.FromDateTime(now) });
            }, cancellationToken: ct), ct);
        return due.Count;
    }

    private static void AccumulatePresenceTime(Dictionary<string, long> pending, string gameId, DateTime start, DateTime end)
    {
        // Split the rare heartbeat crossing UTC midnight into its actual day;
        // corresponding week/month boundaries then follow automatically.
        while (start < end)
        {
            var stop = end < start.Date.AddDays(1) ? end : start.Date.AddDays(1);
            var key = start.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "|" + gameId;
            pending[key] = pending.GetValueOrDefault(key) + (long)(stop - start).TotalMilliseconds;
            start = stop;
        }
    }

    private void FlushPresenceTime(Transaction tx, string userId, Dictionary<string, long> pending, DateTime now)
    {
        foreach (var (key, milliseconds) in pending)
        {
            var separator = key.IndexOf('|');
            if (separator != 8 || milliseconds <= 0) continue;
            var gameId = key[(separator + 1)..];
            if (!GameAnalyticsOutbox.GameIds.Contains(gameId) || !DateTime.TryParseExact(key[..separator], "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day)) continue;
            var increments = new Dictionary<string, object> { ["observedActiveMilliseconds"] = FieldValue.Increment(milliseconds) };
            foreach (var period in GameAnalyticsPeriods.At(day))
            {
                Increment(tx, null, period, gameId, increments, now);
                Increment(tx, userId, period, gameId, increments, now);
            }
        }
    }

    private static async Task Retry(Func<Task> action, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { await action(); return; }
            catch (RpcException error) when (error.StatusCode == StatusCode.Aborted && attempt < 4)
            { await Task.Delay(Random.Shared.Next(30, 120) * (attempt + 1), ct); }
        }
    }
}
