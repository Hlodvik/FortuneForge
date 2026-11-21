using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

internal sealed record CreditHoldemReadVersion(string Path, bool Exists, string? UpdatedAt);
internal sealed record CreditHoldemDocumentChange(string Path, string Fields, bool Create, bool Merge);
internal sealed record CreditHoldemWalletDelta(string AccountId, long Cents, string JournalId, string Reason, string SourceKey, DateTime OccurredAt);
internal sealed record CreditHoldemObservation(string MatchJson, DateTime OccurredAt, string Kind);
internal sealed record CreditHoldemAuditIntent(string MatchJson, DateTime SettledAt);
internal sealed record CreditHoldemReplyIntent(string GuardPath, string AccountId, string Kind, string SessionJson);
internal sealed record CreditHoldemRosterRelease(string AssignmentId, string[] ProfileIds);
internal sealed record CreditHoldemFrozenReply(string Kind, string SessionJson, long BalanceCents);
internal sealed record CreditHoldemMutationPlan(string Id, string ActorAccountId, DateTime PreparedAt,
    CreditHoldemReadVersion[] Reads, CreditHoldemDocumentChange[] Changes, CreditHoldemWalletDelta[] Wallet,
    CreditHoldemObservation[] Observations, CreditHoldemAuditIntent[] Audits, CreditHoldemReplyIntent[] Replies,
    GameRequestObservation? Request, CreditHoldemRosterRelease[]? RosterReleases = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CreditHoldemFinancialSnapshot? FinancialEvidence = null)
{
    internal string? FrozenSha256 { get; init; }
    internal GameWalletBatchRequest WalletRequest()
    {
        var balances = GameWalletJournal.Aggregate(Wallet.Select(value => (value.AccountId, value.Cents))
            .Concat(Replies.Select(value => (value.AccountId, 0L))));
        return new(Id, FrozenSha256 ?? CreditHoldemIds.Hash(JsonSerializer.Serialize(this, CreditHoldemMutation.Json)),
            balances.Select(value => new GameWalletBatchEffect(value.AccountId, Math.Max(0, -value.NetCents),
                Math.Max(0, value.NetCents), value.MinimumBalanceBeforeCents)).ToArray());
    }
}

/// <summary>A Texas table mutation: queue/session/table changes, financial journal deltas, and game-authored audit intents.</summary>
internal sealed class CreditHoldemMutation(Transaction transaction, FirestoreDb database, IGameWalletAccountReader accounts)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, DocumentSnapshot> reads = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<long>> balances = new(StringComparer.Ordinal);
    private readonly List<CreditHoldemDocumentChange> changes = [];
    private readonly List<CreditHoldemWalletDelta> wallet = [];
    private readonly List<CreditHoldemObservation> observations = [];
    private readonly List<CreditHoldemAuditIntent> audits = [];
    internal IReadOnlyList<CreditHoldemDocumentChange> Changes => changes;
    internal bool HasChanges => changes.Count > 0 || observations.Count > 0 || audits.Count > 0 || wallet.Count > 0;

    public async Task<DocumentSnapshot> GetSnapshotAsync(DocumentReference document, CancellationToken token)
    {
        var snapshot = await transaction.GetSnapshotAsync(document, token);
        reads[RelativePath(document)] = snapshot;
        return snapshot;
    }

    public async Task<QuerySnapshot> GetSnapshotAsync(Query query, CancellationToken token)
    {
        var snapshot = await transaction.GetSnapshotAsync(query, token);
        foreach (var document in snapshot.Documents) reads[RelativePath(document.Reference)] = document;
        return snapshot;
    }

    public void Set(DocumentReference document, Dictionary<string, object> fields, SetOptions? options = null) =>
        Change(document, fields, false, options == SetOptions.MergeAll);
    public void Create(DocumentReference document, Dictionary<string, object> fields) => Change(document, fields, true, false);

    private void Change(DocumentReference document, Dictionary<string, object> fields, bool create, bool merge)
    {
        var path = RelativePath(document);
        var collection = path.Split('/')[0];
        if (collection != GameAnalyticsOutbox.CollectionName && collection is not (CreditHoldemBackend.Sessions or CreditHoldemBackend.Tickets or CreditHoldemBackend.QueuePartitions or
            CreditHoldemBackend.Matches or CreditHoldemBackend.CommandGuards or CreditHoldemBackend.MatchRevenue or
            CreditHoldemBackend.PlayerHistory or "cardGameResults"))
            throw new InvalidOperationException($"The Hold'em lifecycle cannot write platform collection {collection}.");
        changes.Add(new(path, FirestoreFieldCodec.Serialize(fields), create, merge));
    }

    internal Task<long> BalanceAsync(string account, CancellationToken token) => balances.GetOrAdd(account, async key =>
    {
        // A remote balance may already include a frozen wallet debit whose game guard/session has not published yet.
        // Read the game locks first; reading an absent lock also fences a competing prepare until this transaction ends.
        var locks = await transaction.GetAllSnapshotsAsync(reads.Keys.Where(path => IsAuthoritativeRead(path, changes))
            .Order(StringComparer.Ordinal).Select(path => database.Collection(CreditHoldemBackend.Locks).Document(CreditHoldemIds.Hash(path))), token);
        foreach (var held in locks.Where(value => value.Exists))
            throw new CreditHoldemPendingMutationException(held.GetValue<string>("mutationId"));
        return (await accounts.GetStateAsync(key, token)).SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException();
    });

    internal void WalletDelta(string account, long cents, string journalId, string reason, string source, DateTime now)
    {
        if (wallet.Any(value => value.JournalId == journalId))
            throw new InvalidOperationException("The Hold'em mutation repeats a financial journal entry.");
        wallet.Add(new(account, cents, journalId, reason, source, now));
    }

    internal void Observe(CreditHoldemMatch match, DateTime now, string kind = "engine-state") =>
        observations.Add(new(JsonSerializer.Serialize(match, Json), now, kind));
    internal void Audit(CreditHoldemMatch match, DateTime now) => audits.Add(new(JsonSerializer.Serialize(match, Json), now));

    internal async Task<Dictionary<string, object>> AfterAsync(DocumentReference document, CancellationToken token)
    {
        var path = RelativePath(document);
        if (!reads.TryGetValue(path, out var snapshot)) snapshot = await GetSnapshotAsync(document, token);
        var fields = snapshot.Exists ? snapshot.ToDictionary() : new Dictionary<string, object>();
        foreach (var change in changes.Where(value => value.Path == path))
        {
            if (!change.Merge) fields.Clear();
            foreach (var (key, value) in FirestoreFieldCodec.Deserialize(change.Fields)) fields[key] = value;
        }
        return fields;
    }

    internal async Task<CreditHoldemMutationPlan> FreezeAsync(string id, CancellationToken token)
    {
        // Version every write target, including creates not explicitly read by the original lifecycle.
        foreach (var path in changes.Select(value => value.Path).Distinct(StringComparer.Ordinal).ToArray())
            if (!reads.ContainsKey(path)) await GetSnapshotAsync(database.Document(path), token);
        foreach (var change in changes.Where(value => value.Create))
            if (reads[change.Path].Exists || changes.Count(value => value.Create && value.Path == change.Path) != 1)
                throw new CreditHoldemConflictException("A Hold'em mutation would recreate an existing game receipt.");
        foreach (var delta in wallet)
        {
            var current = await GetSnapshotAsync(database.Collection(CreditHoldemBackend.WalletJournal).Document(delta.JournalId), token);
            var legacy = await GetSnapshotAsync(database.Collection("balanceTransactions").Document(delta.JournalId), token);
            if (current.Exists || legacy.Exists)
                throw new CreditHoldemConflictException("The Hold'em financial journal already contains this accepted operation.");
        }
        var replies = new List<CreditHoldemReplyIntent>();
        foreach (var guard in changes.Where(value => value.Path.StartsWith(CreditHoldemBackend.CommandGuards + "/", StringComparison.Ordinal)))
        {
            var fields = FirestoreFieldCodec.Deserialize(guard.Fields);
            if (fields.GetValueOrDefault("userId") is not string account) continue;
            var now = fields.GetValueOrDefault("createdAt") is Timestamp timestamp ? timestamp.ToDateTime() : DateTime.UtcNow;
            var session = await CreditHoldemSessionProjection.FromAfterAsync(this, database, account, now, token);
            replies.Add(new(guard.Path, account, session.Kind, JsonSerializer.Serialize(session, Json)));
        }
        var actor = GameSecurityContext.Current?.UserId ?? replies.FirstOrDefault()?.AccountId ?? wallet.FirstOrDefault()?.AccountId ?? "texas-holdem-table-worker";
        var releases = new List<CreditHoldemRosterRelease>();
        foreach (var path in changes.Select(value => value.Path).Where(path => path.StartsWith(CreditHoldemBackend.Matches + "/", StringComparison.Ordinal)).Distinct())
        {
            var after = await AfterAsync(database.Document(path), token);
            if (!reads.TryGetValue(path, out var before) || !before.Exists || !before.TryGetValue<string>("matchJson", out var beforeJson) ||
                after.GetValueOrDefault("matchJson") is not string afterJson) continue;
            var prior = CreditHoldemMatchPersistence.Deserialize(beforeJson);
            var next = CreditHoldemMatchPersistence.Deserialize(afterJson);
            var currentIds = after.ContainsKey("managedPlayersReleasedAt") ? new HashSet<string>() :
                next.Players.Where(value => !value.IsAccountBacked).Select(value => value.ActorId).ToHashSet(StringComparer.Ordinal);
            var removed = prior.Players.Where(value => !value.IsAccountBacked && !currentIds.Contains(value.ActorId))
                .Select(value => value.ActorId).Order(StringComparer.Ordinal).ToArray();
            if (removed.Length > 0) releases.Add(new($"{CreditHoldemBackend.GameId}:{prior.MatchId}", removed));
        }
        var financialEvidence = audits.Count == 0 ? null : await CreditHoldemFinancialProof.ReadPriorAsync(database,
            document => GetSnapshotAsync(document, token), query => GetSnapshotAsync(query, token), audits, wallet);
        return new(id, actor, DateTime.UtcNow, reads.Values.Where(value => IsAuthoritativeRead(RelativePath(value.Reference), changes))
                .OrderBy(value => RelativePath(value.Reference), StringComparer.Ordinal)
                .Select(value => new CreditHoldemReadVersion(RelativePath(value.Reference), value.Exists, Version(value))).ToArray(),
            changes.ToArray(), wallet.ToArray(), observations.ToArray(), audits.ToArray(), replies.ToArray(), GameSecurityContext.Current, releases.ToArray(), financialEvidence);
    }

    internal static bool IsAuthoritativeRead(string path, IReadOnlyCollection<CreditHoldemDocumentChange> changes) =>
        changes.Any(value => value.Path == path) || path.Split('/')[0] is
            CreditHoldemBackend.Matches or CreditHoldemBackend.Sessions or CreditHoldemBackend.Tickets or
            CreditHoldemBackend.QueuePartitions or CreditHoldemBackend.CommandGuards or CreditHoldemBackend.MatchRevenue or
            CreditHoldemBackend.PlayerHistory;

    internal static string? Version(DocumentSnapshot snapshot) => snapshot.UpdateTime?.ToString();

    private string RelativePath(DocumentReference document)
    {
        const string marker = "/documents/";
        var separator = document.Path.IndexOf(marker, StringComparison.Ordinal);
        var path = separator < 0 ? document.Path : document.Path[(separator + marker.Length)..];
        if (database.Document(path).Path != document.Path)
            throw new InvalidOperationException("The Hold'em mutation references a different Firestore database.");
        return path;
    }

    internal static CreditHoldemStoreResult RestoreReply(CreditHoldemFrozenReply reply)
    {
        using var document = JsonDocument.Parse(reply.SessionJson);
        var root = document.RootElement;
        T Read<T>(string name) => root.GetProperty(name).Deserialize<T>(Json)
            ?? throw new InvalidOperationException("A stored Hold'em response field is empty.");
        var version = Read<int>("version");
        var rule = root.TryGetProperty("tableRule", out var savedRule) ? savedRule.Deserialize<CreditHoldemTableRuleResponse>(Json) : null;
        // StateVersion is deliberately JsonIgnore on the public derived contracts. Reconstruct through
        // their constructors so the inherited Version retains the exact accepted optimistic version.
        CreditHoldemSessionResponse session = reply.Kind switch
        {
            CreditHoldemSessionKinds.Idle => new CreditHoldemIdleSessionResponse(),
            CreditHoldemSessionKinds.Queue => new CreditHoldemQueueSessionResponse(Read<string>("ticketId"), Read<int>("position"),
                Read<DateTime>("joinedAtUtc"), Read<DateTime>("humanGraceEndsAtUtc"), Read<CreditHoldemSeatResponse[]>("players"), version, rule),
            CreditHoldemSessionKinds.Match => new CreditHoldemMatchSessionResponse(Read<CreditHoldemTableResponse>("table"), version),
            CreditHoldemSessionKinds.Result => new CreditHoldemResultSessionResponse(Read<string>("matchId"), Read<int>("handNumber"),
                Read<decimal>("humanCommittedCredits"), Read<decimal>("humanPayoutCredits"), Read<decimal>("houseNetCredits"),
                Read<DateTime>("startedAtUtc"), Read<DateTime>("completedAtUtc"), Read<CreditHoldemStandingResponse[]>("standings"),
                Read<CreditHoldemTableResponse>("finalTable"), version),
            _ => throw new InvalidOperationException("The saved Hold'em response kind is unknown.")
        };
        if (session.Kind != Read<string>("kind") || session.Version != version || reply.BalanceCents < 0)
            throw new InvalidOperationException("The saved Hold'em response identity is invalid.");
        return new(session, reply.BalanceCents);
    }
}
