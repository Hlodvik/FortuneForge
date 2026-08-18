using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Admin.Operations;

internal sealed class FirestoreAdminOperationsStore(
    FirestoreDb database,
    IOptions<AdminOperationsOptions> options) : IAdminOperationsStore
{
    private const int CentsPerCredit = 100;
    private readonly int maximumDocuments = Math.Clamp(
        options.Value.MaximumDocumentsPerCollection,
        100,
        25_000);

    public async Task<AdminOperationsSnapshot> ReadAsync(
        AdminOperationsRange range,
        CancellationToken cancellationToken)
    {
        var reads = await Task.WhenAll(
            ReadRangeAsync("slotSpinResults", "createdAt", range, cancellationToken),
            ReadRangeAsync("blackjackGames", "updatedAt", range, cancellationToken),
            ReadRangeAsync("solitaireMatchRevenue", "recognizedAt", range, cancellationToken),
            ReadRangeAsync("slotCreditPurchases", "statusUpdatedAt", range, cancellationToken),
            ReadRangeAsync("slotCreditWithdrawals", "statusUpdatedAt", range, cancellationToken),
            ReadRangeAsync("solitaireQueuePartitions", "updatedAt", range, cancellationToken),
            ReadRangeAsync("solitaireMatches", "startedAt", range, cancellationToken),
            ReadRangeAsync("cardBotTurnLeases", "updatedAt", range, cancellationToken),
            ReadRangeAsync("creditHoldemMatchRevenue", "recognizedAt", range, cancellationToken),
            ReadRangeAsync("blackjackTableRoundRevenue", "recognizedAt", range, cancellationToken));

        var solitaireRecords = reads[2].Documents
            .Select(TryMapSolitaireRevenue)
            .ToArray();
        var rejectedSolitaireRecords = solitaireRecords.Count(static record => record is null);

        var creditHoldemRecords = reads[8].Documents
            .Select(TryMapCreditHoldemRevenue)
            .ToArray();
        var rejectedCreditHoldemRecords = creditHoldemRecords.Count(static record => record is null);

        var blackjackTableRecords = reads[9].Documents
            .Select(TryMapBlackjackTableRevenue)
            .ToArray();
        var rejectedBlackjackTableRecords = blackjackTableRecords.Count(static record => record is null);

        var limitations = reads
            .Where(static read => read.Truncated)
            .Select(static read => $"{read.Collection} exceeded the safe document cap; totals are incomplete.")
            .Concat(rejectedCreditHoldemRecords == 0
                ? []
                : [$"{rejectedCreditHoldemRecords} credit Hold'em revenue record(s) failed the real-human pool financial contract and were excluded."])
            .Concat(rejectedSolitaireRecords == 0
                ? []
                : [$"{rejectedSolitaireRecords} Solitaire revenue record(s) failed the real-human pool financial contract and were excluded."])
            .Concat(rejectedBlackjackTableRecords == 0
                ? []
                : [$"{rejectedBlackjackTableRecords} Blackjack table revenue record(s) failed the real-human dealer-counterparty financial contract and were excluded."])
            .ToArray();

        var financial = reads[0].Documents.Select(MapSlot)
            .Concat(reads[1].Documents
                .Where(document => String(document, "status") == "completed")
                .Select(MapBlackjack))
            .Concat(solitaireRecords.OfType<AdminOperationsFinancialRecord>())
            .Concat(creditHoldemRecords.OfType<AdminOperationsFinancialRecord>())
            .Concat(blackjackTableRecords
                .OfType<ValidatedBlackjackTableRevenue>()
                .Select(static record => record.Financial))
            .OrderByDescending(static record => record.OccurredAtUtc)
            .ThenBy(static record => record.Id, StringComparer.Ordinal)
            .ToArray();

        var funding = reads[3].Documents
            .Where(document => String(document, "status") == "completed")
            .Select(document => new AdminOperationsFundingRecord(
                OpaqueId("purchase", document.Id),
                "purchase",
                TimestampUtc(document, "statusUpdatedAt"),
                Decimal(document, "credits")))
            .Concat(reads[4].Documents
                .Where(document => String(document, "status") == "completed")
                .Select(document => new AdminOperationsFundingRecord(
                    OpaqueId("withdrawal", document.Id),
                    "withdrawal",
                    TimestampUtc(document, "statusUpdatedAt"),
                    Decimal(document, "creditsDebited"))))
            .OrderByDescending(static record => record.OccurredAtUtc)
            .ThenBy(static record => record.Id, StringComparer.Ordinal)
            .ToArray();

        var queues = reads[5].Documents.Select(MapQueue)
            .OrderByDescending(static item => item.UpdatedAtUtc)
            .ThenBy(static item => item.QueueId, StringComparer.Ordinal)
            .ToArray();

        var matches = reads[1].Documents.Select(MapBlackjackMatch)
            .Concat(reads[6].Documents.Select(MapSolitaireMatch))
            .Concat(blackjackTableRecords
                .OfType<ValidatedBlackjackTableRevenue>()
                .Select(static record => record.Match))
            .OrderByDescending(static item => item.StartedAtUtc)
            .ThenBy(static item => item.MatchId, StringComparer.Ordinal)
            .ToArray();

        var leases = reads[7].Documents.Select(document => new AdminOperationsBotLeaseRecord(
                OpaqueId("bot-lease", document.Id),
                SafeGame(String(document, "game")),
                TimestampUtc(document, "updatedAt"),
                TimestampUtc(document, "expiresAt"),
                document.ContainsField("completedAt")))
            .ToArray();

        var sourceFindings = new[]
        {
            new AdminOperationsSourceFinding(
                "solitaire-revenue-contract",
                "Solitaire revenue must be slotsCredits, classified as real-human-pool-v1, contain zero bot contribution, and satisfy pool minus payout equals platform fee.",
                reads[2].Documents.Count,
                rejectedSolitaireRecords),
            new AdminOperationsSourceFinding(
                "credit-holdem-revenue-contract",
                "Credit Hold'em revenue must be slotsCredits, classified as real-human-pool-v1, contain zero bot contribution, and satisfy pool minus payout equals platform fee.",
                reads[8].Documents.Count,
                rejectedCreditHoldemRecords),
            new AdminOperationsSourceFinding(
                "blackjack-table-revenue-contract",
                "Blackjack table revenue must be slotsCredits, classified as real-human-dealer-counterparty-v1, contain zero bot contribution, and satisfy signed house net equals wager minus payout.",
                reads[9].Documents.Count,
                rejectedBlackjackTableRecords)
        };

        return new AdminOperationsSnapshot(
            financial,
            funding,
            queues,
            matches,
            leases,
            sourceFindings,
            limitations.Length == 0,
            limitations);
    }

    public Task AppendAuthorizedAccessAuditAsync(
        string actorUserId,
        string operation,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var record = new Dictionary<string, object>
        {
            ["actorHash"] = OpaqueId("admin-actor", actorUserId),
            ["operation"] = operation,
            ["occurredAt"] = Timestamp.FromDateTime(DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc)),
            ["schemaVersion"] = 1L
        };
        return database.Collection("adminOperationsAudit")
            .Document(id)
            .CreateAsync(record, cancellationToken);
    }

    private async Task<CollectionRead> ReadRangeAsync(
        string collection,
        string timestampField,
        AdminOperationsRange range,
        CancellationToken cancellationToken)
    {
        var query = database.Collection(collection)
            .WhereGreaterThanOrEqualTo(timestampField, Timestamp.FromDateTime(range.FromUtc))
            .WhereLessThan(timestampField, Timestamp.FromDateTime(range.ToUtc))
            .OrderByDescending(timestampField)
            .Limit(maximumDocuments + 1);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        var documents = snapshot.Documents.Take(maximumDocuments).ToArray();
        return new CollectionRead(collection, documents, snapshot.Count > maximumDocuments);
    }

    private static AdminOperationsFinancialRecord MapSlot(DocumentSnapshot document)
    {
        var wagered = Decimal(document, "wageredSlotsCredits");
        var paid = Decimal(document, "wonSlotsCredits");
        return new(
            OpaqueId("slot", document.Id),
            "gaming",
            "slots",
            "completed",
            TimestampUtc(document, "createdAt"),
            wagered,
            paid,
            wagered - paid);
    }

    private static AdminOperationsFinancialRecord MapBlackjack(DocumentSnapshot document)
    {
        var wagered = CreditsFromCents(Long(document, "totalWagerCents"));
        var paid = CreditsFromCents(Long(document, "payoutCents"));
        return new(
            OpaqueId("blackjack", document.Id),
            "gaming",
            "blackjack",
            "completed",
            TimestampUtc(document, "updatedAt"),
            wagered,
            paid,
            wagered - paid);
    }

    private static AdminOperationsFinancialRecord? TryMapSolitaireRevenue(DocumentSnapshot document)
    {
        if (String(document, "currencyId") != "slotsCredits" ||
            String(document, "financialClassification") != "real-human-pool-v1" ||
            !document.TryGetValue<long>("botFinancialContributionCents", out var botContribution) ||
            botContribution != 0 ||
            !document.TryGetValue<long>("grossPoolCents", out var gross) ||
            !document.TryGetValue<long>("winnerPayoutCents", out var payout) ||
            !document.TryGetValue<long>("platformFeeCents", out var fee) ||
            !document.TryGetValue<long>("humanPlayerCount", out var humanPlayers) ||
            gross < 0 || payout < 0 || fee < 0 || humanPlayers < 2 ||
            gross < payout || gross - payout != fee)
        {
            return null;
        }

        return new(
            OpaqueId("solitaire", document.Id),
            "gaming",
            "solitaire",
            "settled",
            TimestampUtc(document, "recognizedAt"),
            CreditsFromCents(gross),
            CreditsFromCents(payout),
            CreditsFromCents(fee),
            CreditsFromCents(gross),
            CreditsFromCents(fee));
    }

    private static AdminOperationsFinancialRecord? TryMapCreditHoldemRevenue(DocumentSnapshot document)
    {
        if (String(document, "currencyId") != "slotsCredits" ||
            String(document, "financialClassification") != "real-human-pool-v1" ||
            !document.TryGetValue<long>("botFinancialContributionCents", out var botContribution) ||
            botContribution != 0 ||
            !document.TryGetValue<long>("humanPrizePoolCents", out var gross) ||
            !document.TryGetValue<long>("humanPayoutCents", out var payout) ||
            !document.TryGetValue<long>("platformFeeCents", out var fee) ||
            !document.TryGetValue<long>("humanPlayerCount", out var humanPlayers) ||
            gross < 0 || payout < 0 || fee < 0 || humanPlayers < 2 ||
            gross < payout || gross - payout != fee)
        {
            return null;
        }

        return new(
            OpaqueId("credit-holdem", document.Id),
            "gaming",
            "texas-holdem",
            "settled",
            TimestampUtc(document, "recognizedAt"),
            CreditsFromCents(gross),
            CreditsFromCents(payout),
            CreditsFromCents(fee),
            CreditsFromCents(gross),
            CreditsFromCents(fee));
    }

    private static ValidatedBlackjackTableRevenue? TryMapBlackjackTableRevenue(DocumentSnapshot document)
    {
        if (String(document, "currencyId") != "slotsCredits" ||
            String(document, "financialClassification") != "real-human-dealer-counterparty-v1" ||
            !document.TryGetValue<long>("botFinancialContributionCents", out var botContribution) ||
            botContribution != 0 ||
            !document.TryGetValue<long>("humanWagerCents", out var wager) ||
            !document.TryGetValue<long>("humanPayoutCents", out var payout) ||
            !document.TryGetValue<long>("houseNetCents", out var houseNet) ||
            !document.TryGetValue<long>("humanPlayerCount", out var humanPlayers) ||
            wager < 0 || payout < 0 || humanPlayers is < 1 or > int.MaxValue ||
            (decimal)wager - payout != houseNet)
        {
            return null;
        }

        var recognizedAt = TimestampUtc(document, "recognizedAt");
        var financial = new AdminOperationsFinancialRecord(
            OpaqueId("blackjack-table", document.Id),
            "gaming",
            "blackjack",
            "settled",
            recognizedAt,
            CreditsFromCents(wager),
            CreditsFromCents(payout),
            CreditsFromCents(houseNet));
        var match = new AdminOperationsMatchItem(
            OpaqueId("blackjack-table-match", document.Id),
            "blackjack",
            "settled",
            checked((int)humanPlayers),
            recognizedAt,
            recognizedAt,
            CreditsFromCents(wager),
            CreditsFromCents(payout),
            CreditsFromCents(houseNet));
        return new(financial, match);
    }

    private static AdminOperationsQueueItem MapQueue(DocumentSnapshot document)
    {
        var tickets = ArrayCount(document, "ticketIds");
        return new(
            OpaqueId("solitaire-queue", document.Id),
            "solitaire",
            tickets == 0 ? "idle" : "waiting",
            checked((int)Long(document, "playerCount")),
            tickets,
            CreditsFromCents(Long(document, "buyInCents")),
            TimestampUtc(document, "updatedAt"));
    }

    private static AdminOperationsMatchItem MapBlackjackMatch(DocumentSnapshot document)
    {
        var wagered = CreditsFromCents(Long(document, "totalWagerCents"));
        var paid = CreditsFromCents(Long(document, "payoutCents"));
        return new(
            OpaqueId("blackjack-match", document.Id),
            "blackjack",
            SafeStatus(String(document, "status")),
            1,
            TimestampUtc(document, "createdAt"),
            String(document, "status") == "completed" ? TimestampUtc(document, "updatedAt") : null,
            wagered,
            paid,
            wagered - paid);
    }

    private static AdminOperationsMatchItem MapSolitaireMatch(DocumentSnapshot document)
    {
        var gross = CreditsFromCents(Long(document, "prizePoolCents"));
        var paid = CreditsFromCents(Long(document, "winnerPayoutCents"));
        return new(
            OpaqueId("solitaire-match", document.Id),
            "solitaire",
            SafeStatus(String(document, "status")),
            checked((int)Long(document, "playerCount")),
            TimestampUtc(document, "startedAt"),
            OptionalTimestampUtc(document, "completedAt"),
            gross,
            paid,
            CreditsFromCents(Long(document, "platformFeeCents")));
    }

    private static string SafeGame(string value) => value switch
    {
        "blackjack" => value,
        "solitaire" => value,
        "texas-holdem" => value,
        _ => "unknown"
    };

    private static string SafeStatus(string value) => value switch
    {
        "active" or "playing" or "completed" or "settled" or "queued" or "cancelled" => value,
        _ => "unknown"
    };

    private static string String(DocumentSnapshot document, string field) =>
        document.TryGetValue<string>(field, out var value) ? value : string.Empty;

    private static long Long(DocumentSnapshot document, string field) =>
        document.TryGetValue<long>(field, out var value) ? value : 0;

    private static decimal Decimal(DocumentSnapshot document, string field)
    {
        if (document.TryGetValue<double>(field, out var doubleValue))
        {
            return Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
        }
        if (document.TryGetValue<long>(field, out var longValue)) return longValue;
        return 0;
    }

    private static DateTime TimestampUtc(DocumentSnapshot document, string field) =>
        document.TryGetValue<Timestamp>(field, out var value)
            ? value.ToDateTime()
            : DateTime.UnixEpoch;

    private static DateTime? OptionalTimestampUtc(DocumentSnapshot document, string field) =>
        document.TryGetValue<Timestamp>(field, out var value) ? value.ToDateTime() : null;

    private static int ArrayCount(DocumentSnapshot document, string field)
    {
        var data = document.ToDictionary();
        return data.TryGetValue(field, out var value) && value is IEnumerable<object> items
            ? items.Count()
            : 0;
    }

    private static decimal CreditsFromCents(long cents) => cents / (decimal)CentsPerCredit;

    internal static string OpaqueId(string scope, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}\n{value}"));
        return Convert.ToHexStringLower(hash.AsSpan(0, 12));
    }

    private sealed record CollectionRead(
        string Collection,
        IReadOnlyList<DocumentSnapshot> Documents,
        bool Truncated);

    private sealed record ValidatedBlackjackTableRevenue(
        AdminOperationsFinancialRecord Financial,
        AdminOperationsMatchItem Match);
}
