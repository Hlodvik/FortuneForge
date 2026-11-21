using System.Collections.Concurrent;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Games.Hearts;

// These are Hearts lifecycle records. The allowed document kinds deliberately exclude platform wallets.
internal enum PaidHeartsDocumentKind { Match, Session, QueuePartition, Funding, FundingTransfer, Command, Availability, Revenue, History, Analytics }
internal sealed record PaidHeartsRead(string Path, bool Exists, string? Version);
internal sealed record PaidHeartsChange(PaidHeartsDocumentKind Kind, string Path, string? Fields, bool Create = false, bool Merge = false);
internal sealed record PaidHeartsMoney(string AccountId, long Cents, string JournalId, string Kind, string Source, DateTime AtUtc);
internal sealed record PaidHeartsObservation(string MatchJson, DateTime AtUtc);
internal sealed record PaidHeartsSettlementIntent(string MatchJson, bool Abandoned, DateTime AtUtc);
internal sealed record PaidHeartsReply(string GuardPath, string AccountId, string ResponseJson);
internal sealed record PaidHeartsPlan(string Id, string ActorId, DateTime PreparedAtUtc, PaidHeartsRead[] Reads,
    PaidHeartsChange[] Changes, PaidHeartsMoney[] Money, PaidHeartsObservation[] Observations,
    PaidHeartsSettlementIntent[] Settlements, PaidHeartsReply[] Replies, PaidHeartsFinancialSnapshot Evidence, GameRequestObservation? ObservationContext)
{
    internal string? FrozenSha256 { get; init; }
    internal GameWalletBatchRequest Request()
    {
        var balances = GameWalletJournal.Aggregate(Money.Select(value => (value.AccountId, value.Cents))
            .Concat(Replies.Select(value => (value.AccountId, 0L))));
        var effects = balances.Select(value => new GameWalletBatchEffect(value.AccountId,
            Math.Max(0, -value.NetCents), Math.Max(0, value.NetCents),
            value.MinimumBalanceBeforeCents > 0 ? value.MinimumBalanceBeforeCents : null)).ToArray();
        if (effects.Length > 4 || effects.Any(value => value.WagerCents > 100_000 || value.PayoutCents > 400_000 ||
            value.WagerCents > 0 && value.AccountId != ActorId) || effects.Sum(value => value.PayoutCents) > 400_000)
            throw new InvalidOperationException("The Hearts mutation exceeds its four-seat funding limits.");
        return new(Id, FrozenSha256 ?? PaidHeartsIds.Hash(JsonSerializer.Serialize(this, PaidHeartsMutation.Json)), effects);
    }
}

internal sealed class PaidHeartsMutation(Transaction transaction, FirestoreDb database)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal readonly ConcurrentDictionary<string, DocumentSnapshot> Reads = new(StringComparer.Ordinal);
    internal readonly List<PaidHeartsChange> Changes = [];
    internal readonly List<PaidHeartsMoney> Deltas = [];
    internal readonly List<PaidHeartsObservation> Observations = [];
    internal readonly List<PaidHeartsSettlementIntent> Settlements = [];
    internal PaidHeartsMatch? ResponseMatch { get; set; }
    internal string? Notice { get; set; }
    internal bool HasChanges => Changes.Count > 0;

    internal async Task<DocumentSnapshot> GetSnapshotAsync(DocumentReference reference, CancellationToken token)
    {
        var path = Relative(reference);
        if (database.Document(path).Path != reference.Path) throw new InvalidOperationException("The Hearts reference is outside its database.");
        if (Reads.TryGetValue(path, out var prior)) return prior;
        var snapshot = await transaction.GetSnapshotAsync(reference, token);
        Reads[path] = snapshot;
        return snapshot;
    }
    internal async Task<QuerySnapshot> GetSnapshotAsync(Query query, CancellationToken token)
    {
        var snapshot = await transaction.GetSnapshotAsync(query, token);
        foreach (var item in snapshot.Documents) Reads[Relative(item.Reference)] = item;
        return snapshot;
    }
    internal void Set(DocumentReference reference, Dictionary<string, object> fields, SetOptions? options = null) =>
        Changes.Add(new(Kind(reference), Relative(reference), FirestoreFieldCodec.Serialize(fields), Merge: options == SetOptions.MergeAll));
    internal void Update(DocumentReference reference, Dictionary<string, object> fields) => Set(reference, fields, SetOptions.MergeAll);
    internal void Create(DocumentReference reference, Dictionary<string, object> fields) =>
        Changes.Add(new(Kind(reference), Relative(reference), FirestoreFieldCodec.Serialize(fields), Create: true));
    internal void Delete(DocumentReference reference) => Changes.Add(new(Kind(reference), Relative(reference), null));
    internal void Observe(PaidHeartsMatch match, DateTime now) => Observations.Add(new(JsonSerializer.Serialize(match, Json), now));
    internal void Settlement(PaidHeartsMatch match, bool abandoned, DateTime now) => Settlements.Add(new(JsonSerializer.Serialize(match, Json), abandoned, now));
    internal void Money(PaidHeartsMoney delta)
    {
        if (Deltas.Any(value => value.JournalId == delta.JournalId)) throw new InvalidOperationException("Duplicate Hearts financial intent.");
        Deltas.Add(delta);
    }
    internal static string Version(DocumentSnapshot snapshot) => snapshot.UpdateTime?.ToString() ?? "";
    internal static string Relative(DocumentReference reference)
    {
        var marker = reference.Path.IndexOf("/documents/", StringComparison.Ordinal);
        if (marker < 0) throw new InvalidOperationException("The Hearts reference is outside its database.");
        return reference.Path[(marker + 11)..];
    }
    private static PaidHeartsDocumentKind Kind(DocumentReference reference) => Relative(reference).Split('/')[0] switch
    {
        "heartsPaidMatches" => PaidHeartsDocumentKind.Match,
        "heartsPaidSessions" => PaidHeartsDocumentKind.Session,
        "heartsPaidQueuePartitions" => PaidHeartsDocumentKind.QueuePartition,
        "heartsPaidFunding" => PaidHeartsDocumentKind.Funding,
        "heartsPaidFundingTransfers" => PaidHeartsDocumentKind.FundingTransfer,
        "heartsPaidCommands" => PaidHeartsDocumentKind.Command,
        "heartsPaidSeatAvailability" => PaidHeartsDocumentKind.Availability,
        "heartsPaidRevenue" => PaidHeartsDocumentKind.Revenue,
        "heartsPaidHistory" => PaidHeartsDocumentKind.History,
        GameAnalyticsOutbox.CollectionName => PaidHeartsDocumentKind.Analytics,
        _ => throw new InvalidOperationException("The Hearts lifecycle cannot mutate a platform collection.")
    };
}
