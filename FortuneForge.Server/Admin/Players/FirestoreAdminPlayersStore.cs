using System.Text.Json;
using FortuneForge.Server.Admin.Operations;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Admin.Players;

internal sealed class FirestoreAdminPlayersStore(FirestoreDb database, IOptions<AdminOperationsOptions> options) : IAdminPlayersStore
{
    private readonly AdminOperationsCursor cursorCodec = new(options.Value.CursorSigningKey);
    private readonly int scanLimit = Math.Clamp(options.Value.MaximumDocumentsPerCollection, 100, 1_000);
    private const string StorageOrder = "History is paginated by immutable storage identifier, not chronological order; timestamps are displayed when recorded.";
    private const string CashFlowCaution = "Gaming net is recorded wallet cash flow, including open stakes and refunds; it is not settled player loss or house profit. Deposits and withdrawals are excluded.";
    private const string MatchCoverage = "Match history covers cardGameResults and Hearts paid history. Other games and legacy records may have wallet events only; free-play and in-memory matches are not complete.";

    public async Task<AdminPlayerPage<AdminPlayerItem>> ListAsync(string? query, int limit, string? cursor, CancellationToken ct)
    {
        AdminOperationsService.ValidateLimit(limit);
        var text = (query ?? "").Trim();
        if (text.Length > 254 || text.Any(char.IsControl)) throw new AdminOperationsQueryException("Search is limited to 254 characters.");
        var scope = "players:list:" + text.ToUpperInvariant();
        Query source = database.Collection("users").OrderBy(FieldPath.DocumentId);
        if (text.Length > 0)
        {
            if (!text.Contains('/') && cursor is null)
            {
                var exact = await database.Collection("users").Document(text).GetSnapshotAsync(ct);
                if (exact.Exists) return new([Player(exact)], null, true, []);
            }
            source = text.Contains('@')
                ? database.Collection("users").WhereEqualTo("email", text.ToLowerInvariant()).OrderBy(FieldPath.DocumentId)
                : database.Collection("users").WhereGreaterThanOrEqualTo("normalizedPlayerName", text.ToUpperInvariant())
                    .WhereLessThanOrEqualTo("normalizedPlayerName", text.ToUpperInvariant() + "\uf8ff").OrderBy("normalizedPlayerName");
        }
        source = await After(source, scope, cursor, "users", ct);
        var documents = (await source.Limit(limit + 1).GetSnapshotAsync(ct)).Documents;
        var page = documents.Take(limit).ToArray();
        return new(page.Select(Player).ToArray(), documents.Count > limit ? Encode(scope, page[^1].Id) : null,
            text.Length == 0 || text.Contains('@'), text.Length == 0 ? [] : ["Search accepts exact user ID, exact email, or a player-name prefix. Legacy profiles without a normalized name are not returned by prefix search."]);
    }

    public async Task<AdminPlayerDetail?> DetailAsync(string userId, CancellationToken ct)
    {
        AdminPlayersController.ValidateUserId(userId);
        var user = await database.Collection("users").Document(userId).GetSnapshotAsync(ct);
        if (!user.Exists) return null;
        var balanceReads = await Task.WhenAll(new[] { "slotsCredits", "freeGames", "specialPoints", "energy" }
            .Select(currency => database.Collection("userBalances").Document($"{userId}_{currency}").GetSnapshotAsync(ct)));
        var ledgerTask = database.Collection("balanceTransactions").WhereEqualTo("userId", userId)
            .OrderBy(FieldPath.DocumentId).Limit(scanLimit + 1).GetSnapshotAsync(ct);
        var purchaseTask = database.Collection("slotCreditPurchases").WhereEqualTo("userId", userId)
            .OrderBy(FieldPath.DocumentId).Limit(scanLimit + 1).GetSnapshotAsync(ct);
        await Task.WhenAll(ledgerTask, purchaseTask);
        var ledger = ledgerTask.Result.Documents.Take(scanLimit).ToArray();
        var purchases = purchaseTask.Result.Documents.Take(scanLimit).ToArray();
        var cash = ledger.Select(Gaming).OfType<AdminPlayerGamingEvent>().ToArray();
        var limitations = new List<string> { CashFlowCaution };
        if (ledgerTask.Result.Count > scanLimit) limitations.Add($"Wallet summary is capped at {scanLimit} stored records and is not a lifetime total.");
        if (purchaseTask.Result.Count > scanLimit) limitations.Add($"Purchase summary is capped at {scanLimit} stored records and is not a lifetime total.");
        var unknown = cash.Count(item => item.NetCredits is null);
        if (unknown > 0) limitations.Add($"{unknown} wallet event(s) had unsupported types or invalid money; they are shown in history but excluded from gaming totals.");
        var completed = purchases.Where(document => Text(document, "status") == "completed").Select(Purchase).ToArray();
        if (completed.Any(item => item.Credits is null)) limitations.Add("A completed purchase had invalid credits and was excluded from the purchase total.");
        if (completed.Any(item => !item.MockClassificationKnown)) limitations.Add("A completed purchase lacked real/mock classification and was excluded from both funding totals.");
        var summary = new AdminPlayerSummary(completed.Where(item => item.MockClassificationKnown && !item.IsMock).Sum(item => item.Credits ?? 0),
            cash.Sum(item => item.WageredCredits ?? 0), cash.Sum(item => item.PaidCredits ?? 0), cash.Sum(item => item.NetCredits ?? 0),
            ledger.Length + purchases.Length, "bounded-records", limitations.Count == 1, limitations,
            cash.Sum(item => item.RefundedCredits),
            completed.Where(item => item.MockClassificationKnown && item.IsMock).Sum(item => item.Credits ?? 0));
        return new(Player(user), new(WalletCredits(balanceReads[0]), Integer(balanceReads[1], "available"),
                Integer(balanceReads[2], "available"), Integer(balanceReads[3], "available")), summary,
            new(false, ["users", "userBalances", "slotCreditPurchases", "balanceTransactions", "cardGameResults", "heartsPaidHistory"],
                [MatchCoverage, "This view is read-only and does not refresh payment providers or change financial state.", "Missing balance fields remain unknown rather than being reported as zero."]));
    }

    public async Task<AdminPlayerPage<AdminPlayerPurchase>> PurchasesAsync(string userId, int limit, string? cursor, CancellationToken ct)
    {
        var page = await UserPage("slotCreditPurchases", userId, "purchases", limit, cursor, ct);
        var purchases = page.Items.Select(Purchase).ToArray();
        return new(purchases, page.NextCursor, purchases.All(item => item.Credits is not null && item.MockClassificationKnown),
            [StorageOrder, "Statuses are locally recorded; opening this page does not reconcile with the payment provider. Mock purchases are labelled and are not real-money revenue. Missing real/mock classification remains unknown, not real."]);
    }

    public async Task<AdminPlayerPage<AdminPlayerGamingEvent>> GamingAsync(string userId, int limit, string? cursor, CancellationToken ct)
    {
        var page = await UserPage("balanceTransactions", userId, "gaming", limit, cursor, ct);
        var items = page.Items.Select(Gaming).OfType<AdminPlayerGamingEvent>().ToArray();
        return new(items, page.NextCursor, items.All(item => item.NetCredits is not null),
            [StorageOrder, CashFlowCaution, "Non-Rand currencies, purchase credits and withdrawal events are excluded. A page can be empty while more source records remain.", "Legacy wallet events do not always record a match ID; no match link is fabricated."]);
    }

    public async Task<AdminPlayerPage<AdminPlayerMatch>> MatchesAsync(string userId, int limit, string? cursor, CancellationToken ct)
    {
        AdminOperationsService.ValidateLimit(limit); AdminPlayersController.ValidateUserId(userId);
        var scope = $"players:matches:{userId}";
        var position = cursor is null ? "0|" : cursorCodec.Decode(scope, cursor).Id;
        var parts = position.Split('|', 2);
        if (parts.Length != 2 || parts[0] is not ("0" or "1")) throw new AdminOperationsQueryException("The cursor is invalid.");
        if (parts[1].Contains('/') || parts[1].Length > 512) throw new AdminOperationsQueryException("The cursor is invalid.");
        var sourceIndex = int.Parse(parts[0]);
        var collections = new[] { "cardGameResults", "heartsPaidHistory" };
        var result = new List<AdminPlayerMatch>();
        for (var index = sourceIndex; index < collections.Length; index++)
        {
            if (result.Count == limit)
                return new(result, Encode(scope, $"{index}|"), false, [StorageOrder, MatchCoverage]);
            Query query = database.Collection(collections[index]).WhereEqualTo("userId", userId).OrderBy(FieldPath.DocumentId);
            if (index == sourceIndex && parts[1].Length > 0) query = query.StartAfter(parts[1]);
            var remaining = limit - result.Count;
            var documents = (await query.Limit(remaining + 1).GetSnapshotAsync(ct)).Documents;
            var consumed = documents.Take(remaining).ToArray();
            foreach (var document in consumed)
            {
                var item = index == 0 ? CardMatch(document) : HeartsMatch(document, userId);
                if (item is not null) result.Add(item);
            }
            if (documents.Count > remaining)
                return new(result, Encode(scope, $"{index}|{consumed[^1].Id}"), false, [StorageOrder, MatchCoverage]);
        }
        return new(result, null, false, [StorageOrder, MatchCoverage]);
    }

    private async Task<(IReadOnlyList<DocumentSnapshot> Items, string? NextCursor)> UserPage(string collection, string userId,
        string operation, int limit, string? cursor, CancellationToken ct)
    {
        AdminOperationsService.ValidateLimit(limit); AdminPlayersController.ValidateUserId(userId);
        var scope = $"players:{operation}:{userId}";
        Query query = database.Collection(collection).WhereEqualTo("userId", userId).OrderBy(FieldPath.DocumentId);
        if (cursor is not null) query = query.StartAfter(Decode(scope, cursor));
        var documents = (await query.Limit(limit + 1).GetSnapshotAsync(ct)).Documents;
        var page = documents.Take(limit).ToArray();
        return (page, documents.Count > limit ? Encode(scope, page[^1].Id) : null);
    }

    private async Task<Query> After(Query query, string scope, string? cursor, string collection, CancellationToken ct)
    {
        if (cursor is null) return query;
        var snapshot = await database.Collection(collection).Document(Decode(scope, cursor)).GetSnapshotAsync(ct);
        if (!snapshot.Exists) throw new AdminOperationsQueryException("The search position no longer exists. Restart the search.");
        return query.StartAfter(snapshot);
    }
    private string Encode(string scope, string id) => cursorCodec.Encode(scope, DateTime.UnixEpoch, id);
    private string Decode(string scope, string value)
    {
        var id = cursorCodec.Decode(scope, value).Id;
        if (id.Contains('/') || id.Length > 512) throw new AdminOperationsQueryException("The cursor is invalid.");
        return id;
    }

    private static AdminPlayerItem Player(DocumentSnapshot document)
    {
        var tags = document.TryGetValue<object[]>("profileTags", out var values) ? values : [];
        return new(document.Id, Text(document, "playerName", document.Id), OptionalText(document, "email"),
            Text(document, "role", "player"), Text(document, "status", "unknown"), Boolean(document, "deactivated"),
            tags.Any(value => value?.ToString() == "bot"), Time(document, "createdAt"), Time(document, "lastActiveAt"));
    }

    private static AdminPlayerPurchase Purchase(DocumentSnapshot document) => new(document.Id, Text(document, "userId"),
        OptionalText(document, "invoiceId"), OptionalText(document, "providerId"), Boolean(document, "isMock"),
        Text(document, "status", "unknown"), Text(document, "currency", "unknown"), Integer(document, "amountMinor"),
        Money(document, "credits") is >= 0m and var credits ? credits : null, Time(document, "createdAt"), Time(document, "statusUpdatedAt"),
        document.TryGetValue<object>("isMock", out var mock) && mock is bool);

    private static readonly (string Prefix, string Game)[] GamePrefixes =
    [ ("three-card-poker-", "three-card-poker"), ("house-holdem-", "house-holdem"), ("texas-holdem-", "texas-holdem"),
      ("dragon-tiger-", "dragon-tiger"), ("andar-bahar-", "andar-bahar"), ("hi-lo-", "hi-lo"), ("mines-", "mines"),
      ("casino-war-", "casino-war"), ("video-poker-", "video-poker"), ("blackjack-", "blackjack"),
      ("solitaire-", "solitaire"), ("baccarat-", "baccarat"), ("sic-bo-", "sic-bo"), ("sicbo-", "sic-bo"),
      ("roulette-", "roulette"), ("craps-", "craps"), ("liars-dice-", "liars-dice"), ("hearts-", "hearts"),
      ("keno-", "keno"), ("slot-", "slots"), ("arcade-competition-", "arcade-competition") ];

    private static readonly IReadOnlySet<string> ChargeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "slot-wager", "blackjack-wager", "blackjack-double", "blackjack-table-wager", "blackjack-table-double",
        "blackjack-table-split", "blackjack-table-insurance", "three-card-poker-table-wager", "three-card-poker-table-play",
        "house-holdem-wager", "house-holdem-play-wager", "video-poker-wager", "baccarat-wager", "keno-wager", "sic-bo-wager",
        "casino-war-opening-wager", "casino-war-war-wager", "solitaire-buyin", "hearts-entry", "arcade-competition-entry",
        "hi-lo-wager", "mines-wager", "dragon-tiger-wager", "andar-bahar-wager", "texas-holdem-blind"
    };
    private static readonly IReadOnlySet<string> PayoutTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "slot-payout", "blackjack-payout", "blackjack-table-payout", "three-card-poker-table-payout", "house-holdem-payout",
        "video-poker-payout", "baccarat-payout", "keno-payout", "sic-bo-payout", "casino-war-tie-payout", "casino-war-primary-payout",
        "solitaire-winner-payout-claim", "hearts-match-payout", "arcade-competition-payout", "texas-holdem-hand-payout",
        "hi-lo-payout", "mines-payout", "dragon-tiger-payout", "andar-bahar-payout"
    };
    private static readonly IReadOnlySet<string> RefundTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "blackjack-table-wager-release", "three-card-poker-table-wager-release", "solitaire-refund",
        "solitaire-no-contest-refund-claim", "solitaire-test-refund-claim", "hearts-queue-refund"
    };

    internal static AdminPlayerGamingEvent? Gaming(DocumentSnapshot document)
    {
        if (Text(document, "currencyId") != "slotsCredits") return null;
        var type = Text(document, "type", "unknown");
        if (type == "credit-purchase" || type.StartsWith("withdrawal-", StringComparison.Ordinal)) return null;
        var prefix = GamePrefixes.FirstOrDefault(pair => type.StartsWith(pair.Prefix, StringComparison.Ordinal));
        var game = OptionalText(document, "gameId") ?? prefix.Game ?? "unclassified";
        var amount = Money(document, "amount", "amountCents");
        var isCharge = ChargeTypes.Contains(type) || type.StartsWith("texas-holdem-action-v", StringComparison.Ordinal) &&
            int.TryParse(type.AsSpan("texas-holdem-action-v".Length), out var version) && version >= 0;
        var isPayout = PayoutTypes.Contains(type);
        var isRefund = RefundTypes.Contains(type);
        var isAdjustment = type is "blackjack-table-wager-adjustment" or "three-card-poker-table-wager-adjustment";
        var known = amount is not null && (isCharge && amount <= 0 || isPayout && amount >= 0 || isRefund && amount >= 0 || isAdjustment);
        decimal? wager = known ? amount < 0 ? -amount : 0 : null;
        decimal? paid = known ? isPayout ? amount : 0 : null;
        var refund = known && amount > 0 && (isRefund || isAdjustment) ? amount.Value : 0;
        return new(document.Id, Text(document, "userId"), game, OptionalText(document, "matchId") ?? OptionalText(document, "roundId"),
            type, "posted", Time(document, "createdAt"), wager, paid, known ? amount : null, amount,
            Money(document, "balanceAfter", "balanceAfterCents"), "balanceTransactions", refund);
    }

    private static AdminPlayerMatch? CardMatch(DocumentSnapshot document)
    {
        var game = Text(document, "game");
        if (game is not ("blackjack" or "texas-holdem" or "three-card-poker" or "house-holdem" or "solitaire" or "hi-lo" or "andar-bahar")) return null;
        var wager = Cents(document, "wagerCents") ?? Cents(document, "buyInCents");
        var paid = Cents(document, "payoutCents");
        var complete = Time(document, "completedAt") ?? Time(document, "settledAt") ?? Time(document, "recognizedAt");
        return new(Text(document, "matchId", Text(document, "tableId", document.Id)), game,
            Text(document, "settlementStatus", Text(document, "claimStatus", "recorded")), Time(document, "startedAt") ?? complete,
            complete, wager, paid, Cents(document, "netCents") ?? (paid - wager));
    }

    private static AdminPlayerMatch? HeartsMatch(DocumentSnapshot document, string userId)
    {
        try
        {
            using var json = JsonDocument.Parse(Text(document, "matchJson"));
            var root = json.RootElement;
            var players = root.GetProperty("players").EnumerateArray().ToArray();
            var owner = players.FirstOrDefault(player => player.GetProperty("userId").GetString() == userId);
            if (owner.ValueKind == JsonValueKind.Undefined) return null;
            var wager = root.GetProperty("entryCents").GetInt64() / 100m;
            var paid = owner.GetProperty("payoutCents").GetInt64() / 100m;
            return new(root.GetProperty("id").GetString()!, "hearts", Text(document, "status", "unknown"),
                root.TryGetProperty("createdAtUtc", out var created) && created.TryGetDateTime(out var start) ? start.ToUniversalTime() : null,
                root.TryGetProperty("completedAtUtc", out var completed) && completed.ValueKind == JsonValueKind.String && completed.TryGetDateTime(out var end) ? end.ToUniversalTime() : null,
                wager, paid, paid - wager, players.Select(player => new AdminPlayerMatchParticipant(
                    player.GetProperty("userId").GetString(), player.GetProperty("displayName").GetString(),
                    !player.GetProperty("isAccountBacked").GetBoolean())).ToArray());
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return null; }
    }

    internal static decimal? Money(DocumentSnapshot document, string field, string? centsField = null)
    {
        if (centsField is not null && document.ContainsField(centsField)) return Cents(document, centsField);
        if (!document.Exists || !document.TryGetValue<object>(field, out var raw)) return null;
        try
        {
            decimal value = raw switch { long integer => integer, int integer => integer,
                double number when double.IsFinite(number) => Convert.ToDecimal(number), _ => throw new FormatException() };
            return value * 100m == decimal.Truncate(value * 100m) ? value : null;
        }
        catch (Exception exception) when (exception is OverflowException or FormatException) { return null; }
    }
    private static decimal? WalletCredits(DocumentSnapshot document)
    {
        var whole = Integer(document, "available");
        var fraction = document.ContainsField("availableFractionalCents") ? Integer(document, "availableFractionalCents") : 0;
        if (whole is null || whole < 0 || fraction is null or < 0 or > 99) return null;
        try { return checked(whole.Value + fraction.Value / 100m); } catch (OverflowException) { return null; }
    }
    private static decimal? Cents(DocumentSnapshot document, string field) => Integer(document, field) / 100m;
    private static long? Integer(DocumentSnapshot document, string field) => document.Exists && document.TryGetValue<object>(field, out var raw)
        ? raw switch { long value => value, int value => value, _ => null } : null;
    private static string Text(DocumentSnapshot document, string field, string fallback = "") => OptionalText(document, field) ?? fallback;
    private static string? OptionalText(DocumentSnapshot document, string field) => document.Exists && document.TryGetValue<string>(field, out var value) && value.Length > 0 ? value : null;
    private static DateTime? Time(DocumentSnapshot document, string field) => document.Exists && document.TryGetValue<Timestamp>(field, out var value) ? value.ToDateTime() : null;
    private static bool Boolean(DocumentSnapshot document, string field) => document.Exists && document.TryGetValue<bool>(field, out var value) && value;
}
