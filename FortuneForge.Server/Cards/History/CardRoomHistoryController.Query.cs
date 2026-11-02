using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.History;

public sealed partial class CardRoomHistoryController
{
    internal const int HistoryDocumentBudget = 1_000;
    private const int HistoryPageSize = 64;
    private static readonly string[] HistoryGames =
    [
        "blackjack", "texas-holdem", "three-card-poker", "house-holdem",
        "solitaire", "hi-lo", "andar-bahar", "mines"
    ];
    private static readonly string[] HistoryTimestampFields =
        ["completedAt", "settledAt", "recognizedAt", "startedAt"];

    internal sealed record HistoryQueryPage(
        IReadOnlyList<CardRoomHistoryItemResponse> Items, int DocumentsRead, bool IsComplete);

    internal static Task<HistoryQueryPage> ReadLatestHistoryAsync(
        FirestoreDb database, string userId, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);
        return database.RunTransactionAsync(async transaction =>
        {
            // Every page sees one consistent snapshot. State belongs to this callback;
            // one attempt also bounds the total returned documents per HTTP request.
            var documentsRead = 0;
            var items = new Dictionary<string, CardRoomHistoryItemResponse>(StringComparer.Ordinal);
            var owned = database.Collection("cardGameResults")
                .WhereEqualTo("userId", userId).WhereIn("game", HistoryGames);
            var minimumTimestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
            var streams = HistoryTimestampFields.Select(field => new HistoryTimestampStream(
                field, owned.WhereGreaterThanOrEqualTo(field, minimumTimestamp)
                    .OrderByDescending(field).OrderBy(FieldPath.DocumentId))).ToArray();

            HistoryQueryPage Result(bool complete) => new(
                complete ? Ranked().Take(limit).ToArray() : [], documentsRead, complete);

            IOrderedEnumerable<CardRoomHistoryItemResponse> Ranked() => items.Values
                .OrderByDescending(item => item.CompletedAtUtc ?? item.StartedAtUtc)
                .ThenBy(item => item.ResultId, StringComparer.Ordinal);

            void Add(IEnumerable<DocumentSnapshot> documents)
            {
                foreach (var document in documents)
                    if (ToPublicItem(document) is { } item) items[document.Id] = item;
            }

            async Task<bool> ReadPage(HistoryTimestampStream stream)
            {
                var size = Math.Min(HistoryPageSize, HistoryDocumentBudget - documentsRead);
                if (size == 0) return false;
                var query = stream.Cursor is null ? stream.Query : stream.Query.StartAfter(stream.Cursor);
                var page = await transaction.GetSnapshotAsync(query.Limit(size), cancellationToken);
                documentsRead += page.Count;
                Add(page.Documents);
                stream.Exhausted = page.Count < size;
                if (page.Count > 0)
                {
                    stream.Cursor = page.Documents[^1];
                    stream.FrontierUtc = TimestampUtc(stream.Cursor, stream.Field);
                }
                return true;
            }

            foreach (var stream in streams)
                if (!await ReadPage(stream)) return Result(false);

            while (true)
            {
                var ranked = Ranked().Take(limit).ToArray();
                var cutoff = ranked.Length == limit
                    ? ranked[^1].CompletedAtUtc ?? ranked[^1].StartedAtUtc : (DateTime?)null;
                // An unseen result's chosen timestamp equals one stream's raw field.
                // A fixed top-N union is insufficient when earlier fields override
                // later ones. Drain every frontier that could still reach the cutoff.
                // Drain ties too: Firestore name order need not equal .NET Ordinal.
                var pending = streams.Where(stream => !stream.Exhausted &&
                    (cutoff is null || stream.FrontierUtc is null || stream.FrontierUtc.Value >= cutoff.Value)).ToArray();
                if (pending.Length == 0)
                {
                    if (cutoff is { } timestamp && timestamp > DateTime.UnixEpoch) return Result(true);
                    break;
                }
                // Recompute the cutoff after each page: new candidates can make
                // another stream's old frontier irrelevant without another read.
                if (!await ReadPage(pending[0])) return Result(false);
            }

            // Ordering by a timestamp omits missing fields. If undated UnixEpoch
            // results can enter this page, prove their coverage with an owner-scoped
            // scan, rather than silently dropping legacy records. Equality indexes
            // already supply the default ascending document-name order.
            var byId = owned.OrderBy(FieldPath.DocumentId);
            DocumentSnapshot? cursor = null;
            while (true)
            {
                var size = Math.Min(HistoryPageSize, HistoryDocumentBudget - documentsRead);
                if (size == 0) return Result(false);
                var query = cursor is null ? byId : byId.StartAfter(cursor);
                var page = await transaction.GetSnapshotAsync(query.Limit(size), cancellationToken);
                documentsRead += page.Count;
                Add(page.Documents);
                if (page.Count < size) return Result(true);
                cursor = page.Documents[^1];
            }
        }, TransactionOptions.ForMaxAttempts(1), cancellationToken);
    }

    private sealed class HistoryTimestampStream(string field, Query query)
    {
        internal string Field { get; } = field;
        internal Query Query { get; } = query;
        internal DocumentSnapshot? Cursor { get; set; }
        internal DateTime? FrontierUtc { get; set; }
        internal bool Exhausted { get; set; }
    }
}
