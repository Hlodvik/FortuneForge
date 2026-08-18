using FortuneForge.Server.Cards.Bots;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Admin.Operations;

internal sealed class AdminOperationsService(
    IAdminOperationsStore store,
    IOptions<AdminOperationsOptions> adminOptions,
    IOptions<CardBotPlatformOptions> botOptions)
{
    private readonly AdminOperationsOptions options = adminOptions.Value;
    private readonly CardBotPlatformOptions bots = botOptions.Value;

    public AdminOperationsRange ValidateRange(DateTimeOffset? from, DateTimeOffset? to, DateTime nowUtc)
    {
        nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var upper = to ?? new DateTimeOffset(nowUtc);
        var lower = from ?? upper.AddHours(-24);
        if (lower.Offset != TimeSpan.Zero || upper.Offset != TimeSpan.Zero)
            throw new AdminOperationsQueryException("from and to must use the UTC Z offset.");
        if (lower >= upper)
            throw new AdminOperationsQueryException("from must be earlier than to.");
        if (upper.UtcDateTime > nowUtc.AddMinutes(5))
            throw new AdminOperationsQueryException("to cannot be in the future.");
        if (upper - lower > TimeSpan.FromDays(Math.Clamp(options.MaximumRangeDays, 1, 31)))
            throw new AdminOperationsQueryException("The requested UTC range is too large.");
        return new(lower.UtcDateTime, upper.UtcDateTime);
    }

    public static int ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100)
            throw new AdminOperationsQueryException("limit must be from 1 through 100.");
        return limit;
    }

    public Task AuditAsync(
        string actorUserId,
        string operation,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        store.AppendAuthorizedAccessAuditAsync(actorUserId, operation, nowUtc, cancellationToken);

    public async Task<AdminOperationsOverviewResponse> OverviewAsync(
        AdminOperationsRange range,
        CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadAsync(range, cancellationToken);
        var slots = GameFinancials(snapshot.Financial, "slots");
        var blackjack = GameFinancials(snapshot.Financial, "blackjack");
        var solitaire = PoolFinancials(snapshot.Financial, "solitaire");
        var texasHoldem = PoolFinancials(snapshot.Financial, "texas-holdem");
        var purchases = snapshot.Funding.Where(static record => record.Category == "purchase").ToArray();
        var withdrawals = snapshot.Funding.Where(static record => record.Category == "withdrawal").ToArray();
        return new(
            range.FromUtc,
            range.ToUtc,
            slots,
            blackjack,
            solitaire,
            texasHoldem,
            slots.HouseNetCredits + blackjack.HouseNetCredits +
                solitaire.PlatformFeeCredits + texasHoldem.PlatformFeeCredits,
            new AdminFundingFlows(
                purchases.Sum(static record => record.Credits),
                purchases.Length,
                withdrawals.Sum(static record => record.Credits),
                withdrawals.Length),
            snapshot.Complete,
            snapshot.Limitations);
    }

    public async Task<AdminOperationsPage<AdminOperationsActivityItem>> ActivityAsync(
        AdminOperationsRange range,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadAsync(range, cancellationToken);
        var items = snapshot.Financial.Select(record => new AdminOperationsActivityItem(
                record.Id,
                record.Category,
                record.Game,
                record.Status,
                record.OccurredAtUtc,
                record.WageredCredits,
                record.PaidCredits,
                record.HouseNetCredits))
            .Concat(snapshot.Funding.Select(record => new AdminOperationsActivityItem(
                record.Id,
                "funding",
                record.Category,
                "completed",
                record.OccurredAtUtc,
                null,
                record.Credits,
                null)))
            .OrderByDescending(static item => item.OccurredAtUtc)
            .ThenBy(static item => item.EventId, StringComparer.Ordinal)
            .ToArray();
        return Page("activity", items, static item => item.OccurredAtUtc, static item => item.EventId, limit, cursor);
    }

    public async Task<AdminOperationsPage<AdminOperationsQueueItem>> QueuesAsync(
        AdminOperationsRange range,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadAsync(range, cancellationToken);
        return Page("queues", snapshot.Queues, static item => item.UpdatedAtUtc, static item => item.QueueId, limit, cursor);
    }

    public async Task<AdminOperationsPage<AdminOperationsMatchItem>> MatchesAsync(
        AdminOperationsRange range,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadAsync(range, cancellationToken);
        return Page("matches", snapshot.Matches, static item => item.StartedAtUtc, static item => item.MatchId, limit, cursor);
    }

    public async Task<AdminOperationsIntegrityResponse> IntegrityAsync(
        AdminOperationsRange range,
        CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadAsync(range, cancellationToken);
        var negativeFinancials = snapshot.Financial.Count(static record =>
            record.WageredCredits < 0 || record.PaidCredits < 0 || record.PlatformFeeCredits < 0);
        var solitaireFormulaFailures = snapshot.Financial.Count(static record =>
            record.Game == "solitaire" &&
            record.GrossPoolCredits - record.PaidCredits != record.PlatformFeeCredits);
        var unknownMatches = snapshot.Matches.Count(static match => match.Status == "unknown");
        var checks = new[]
        {
            Check("nonnegative-gaming-money", snapshot.Financial.Count, negativeFinancials,
                "Gaming monetary values must be non-negative."),
            Check("solitaire-platform-fee", snapshot.Financial.Count(static record => record.Game == "solitaire"),
                solitaireFormulaFailures, "Real-human pool Solitaire gross pool minus winner payout must equal the platform fee."),
            Check("known-match-status", snapshot.Matches.Count, unknownMatches,
                "Stored match status must be a recognized operational value."),
            new AdminOperationsIntegrityCheck(
                "bot-financial-isolation",
                "pass",
                "Bot practice is account-neutral and is excluded from every financial source and formula.",
                snapshot.BotLeases.Count,
                0)
        }.Concat(snapshot.SourceFindings.Select(static finding => new AdminOperationsIntegrityCheck(
            finding.Id,
            finding.Findings == 0 ? "pass" : "fail",
            finding.Summary,
            finding.RecordsChecked,
            finding.Findings))).ToArray();
        return new(range.FromUtc, range.ToUtc, checks, snapshot.Complete, snapshot.Limitations);
    }

    public async Task<AdminOperationsBotsResponse> BotsAsync(
        AdminOperationsRange range,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadAsync(range, cancellationToken);
        var games = new[]
        {
            BotTelemetry("blackjack", bots.Blackjack.Enabled, snapshot.BotLeases, nowUtc),
            BotTelemetry("solitaire", bots.Solitaire.Enabled, snapshot.BotLeases, nowUtc),
            BotTelemetry("texas-holdem", bots.TexasHoldem.Enabled, snapshot.BotLeases, nowUtc)
        };
        return new(
            range.FromUtc,
            range.ToUtc,
            games,
            "Synthetic bot play is nonfinancial and excluded from balances, ledgers, revenue, expense, liability, and house P&L.");
    }

    private AdminOperationsPage<T> Page<T>(
        string operation,
        IReadOnlyList<T> orderedItems,
        Func<T, DateTime> date,
        Func<T, string> id,
        int limit,
        string? cursor)
    {
        var cursorCodec = new AdminOperationsCursor(options.CursorSigningKey);
        IEnumerable<T> filtered = orderedItems;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var decoded = cursorCodec.Decode(operation, cursor);
            filtered = filtered.Where(item =>
                date(item) < decoded.OccurredAtUtc ||
                date(item) == decoded.OccurredAtUtc &&
                string.CompareOrdinal(id(item), decoded.Id) > 0);
        }
        var page = filtered.Take(limit + 1).ToArray();
        var hasNext = page.Length > limit;
        var items = page.Take(limit).ToArray();
        var next = hasNext && items.Length > 0
            ? cursorCodec.Encode(operation, date(items[^1]), id(items[^1]))
            : null;
        return new(items, next);
    }

    private static AdminGameFinancials GameFinancials(
        IReadOnlyList<AdminOperationsFinancialRecord> records,
        string game)
    {
        var selected = records.Where(record => record.Game == game).ToArray();
        return new(
            selected.Sum(static record => record.WageredCredits),
            selected.Sum(static record => record.PaidCredits),
            selected.Sum(static record => record.HouseNetCredits),
            selected.Length);
    }

    private static AdminPoolGameFinancials PoolFinancials(
        IReadOnlyList<AdminOperationsFinancialRecord> records,
        string game)
    {
        var selected = records.Where(record => record.Game == game).ToArray();
        return new(
            selected.Sum(static record => record.GrossPoolCredits),
            selected.Sum(static record => record.PaidCredits),
            selected.Sum(static record => record.PlatformFeeCredits),
            selected.Length);
    }

    private static AdminOperationsIntegrityCheck Check(
        string id,
        long records,
        long findings,
        string summary) => new(id, findings == 0 ? "pass" : "fail", summary, records, findings);

    private static AdminOperationsBotGameTelemetry BotTelemetry(
        string game,
        bool enabled,
        IReadOnlyList<AdminOperationsBotLeaseRecord> records,
        DateTime nowUtc)
    {
        var selected = records.Where(record => record.Game == game).ToArray();
        return new(
            game,
            enabled,
            selected.Length,
            selected.Count(static record => record.Completed),
            selected.Count(record => !record.Completed && record.ExpiresAtUtc > nowUtc));
    }
}
