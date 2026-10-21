using System.Text.Json;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Hearts;
using FortuneForge.Server.Matchmaking;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Server.Games.Hearts;

internal sealed partial class FirestorePaidHeartsStore
{
    private const string Currency = "slotsCredits";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly FirestoreDb database;
    private readonly IMultiplayerMatchmaker matchmaking;
    private readonly PaidHeartsOptions options;
    private readonly ILogger<FirestorePaidHeartsStore>? logger;

    public FirestorePaidHeartsStore(FirestoreDb database, IMultiplayerMatchmaker matchmaking, PaidHeartsOptions? options = null,
        ILogger<FirestorePaidHeartsStore>? logger = null)
    {
        this.database = database;
        this.matchmaking = matchmaking;
        this.options = options ?? new();
        this.logger = logger;
        this.options.Validate();
    }

    public async Task<PaidHeartsSessionResponse> SessionAsync(string userId, DateTime now, CancellationToken ct)
    {
        var session = await Session(userId).GetSnapshotAsync(ct);
        var id = Text(session, "matchId");
        if (id.Length > 0)
        {
            await AdvanceAsync(id, now, ct);
            var snapshot = await Match(id).GetSnapshotAsync(ct);
            if (snapshot.Exists)
            {
                var match = Read(snapshot);
                if (match.Players.Any(player => player.IsAccountBacked && player.UserId == userId))
                    return await ResponseAsync(match, userId, ct);
            }
        }
        return Idle(ReadBalance(await Balance(userId).GetSnapshotAsync(ct)));
    }

    public async Task<PaidHeartsSessionResponse> GetAsync(string userId, string id, DateTime now, CancellationToken ct)
    {
        id = Identifier(id);
        var snapshot = await Match(id).GetSnapshotAsync(ct);
        if (!snapshot.Exists || !Read(snapshot).Players.Any(player => player.IsAccountBacked && player.UserId == userId))
            throw new HeartsAccessException();
        await AdvanceAsync(id, now, ct);
        return await ResponseAsync(Read(await Match(id).GetSnapshotAsync(ct)), userId, ct);
    }

    public async Task<PaidHeartsSessionResponse> JoinAsync(
        string userId, string name, decimal entryCredits, string key, DateTime now, CancellationToken ct)
    {
        Key(key);
        if (entryCredits is < 1 or > 1_000) throw new ArgumentException("Choose one of the available Hearts entries.");
        var centsDecimal = entryCredits * 100m;
        if (centsDecimal != decimal.Truncate(centsDecimal) || centsDecimal > long.MaxValue || centsDecimal < 0)
            throw new ArgumentException("Choose one of the available Hearts entries.");
        var cents = (long)centsDecimal;
        if (!options.EntryOptionsCents.Contains(cents)) throw new ArgumentException("Choose one of the available Hearts entries.");
        var detail = cents.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var newId = Guid.NewGuid().ToString("N");
        var actor = Human(userId, name, now);
        await TransactionAsync(async tx =>
        {
            var guard = await tx.GetSnapshotAsync(Guard(userId, key), ct);
            if (guard.Exists) { Verify(guard, "join", "", detail); return false; }
            var session = await tx.GetSnapshotAsync(Session(userId), ct);
            var previousId = Text(session, "matchId");
            if (previousId.Length > 0)
            {
                var previous = await tx.GetSnapshotAsync(Match(previousId), ct);
                if (previous.Exists && Read(previous) is { Status: "queue" or "match" } active &&
                    active.Players.Any(player => player.UserId == userId && !player.HasLeft))
                {
                    tx.Create(Guard(userId, key), GuardData("join", "", detail, now));
                    return false;
                }
            }
            var partition = await tx.GetSnapshotAsync(Partition(cents), ct);
            var waitingId = Text(partition, "matchId");
            PaidHeartsMatch? candidate = null;
            if (waitingId.Length > 0)
            {
                var waiting = await tx.GetSnapshotAsync(Match(waitingId), ct);
                if (waiting.Exists) candidate = Read(waiting);
            }
            var match = matchmaking.FindLiveTable(candidate is null ? [] : new[] { candidate },
                value => value.Status == "queue" && value.EntryCents == cents && value.Players.Count < 4 &&
                    now < value.CreatedAtUtc.AddMinutes(2), value => value.CreatedAtUtc, value => value.Id)
                ?? new PaidHeartsMatch
                {
                    Id = newId, EntryCents = cents, CreatedAtUtc = now, UpdatedAtUtc = now,
                    NextAdmissionAtUtc = options.AllowManagedPlayers ? now.AddSeconds(options.HumanQueueSeconds) : now.AddMinutes(2)
                };
            var balance = ReadBalance(await tx.GetSnapshotAsync(Balance(userId), ct));
            if (balance < cents) throw new PaidHeartsInsufficientFundsException();
            actor.Seat = Enum.GetValues<PlayerSeat>().First(seat => match.Players.All(player => player.Seat != seat));
            match.Players.Add(actor);
            match.Version++;
            match.UpdatedAtUtc = now;
            if (match.Players.Count == 4) Start(match, now, PaidHeartsIds.Seed());
            tx.Set(Balance(userId), BalanceData(balance - cents, now), SetOptions.MergeAll);
            Ledger(tx, $"{match.Id}-entry-{actor.EntryId}", userId, -cents, balance - cents, "hearts-entry", key, now);
            tx.Set(Session(userId), new Dictionary<string, object> { ["matchId"] = match.Id, ["updatedAt"] = Timestamp.FromDateTime(now) });
            tx.Set(Partition(cents), new Dictionary<string, object> { ["matchId"] = match.Status == "queue" ? match.Id : "" });
            Write(tx, match);
            if (match.Status == "match") WriteFunding(tx, match, now);
            tx.Create(Guard(userId, key), GuardData("join", "", detail, now));
            return true;
        }, ct);
        return await SessionAsync(userId, now, ct);
    }

    public async Task<PaidHeartsSessionResponse> CommandAsync(
        string userId, string id, string operation, int expectedVersion, IReadOnlyList<string>? cards,
        string key, DateTime now, CancellationToken ct)
    {
        id = Identifier(id); Key(key);
        if (expectedVersion < 0) throw new ArgumentException("ExpectedVersion cannot be negative.");
        var detail = JsonSerializer.Serialize(new { expectedVersion, cards }, Json);
        var nextSeed = PaidHeartsIds.Seed();
        await TransactionAsync(async tx =>
        {
            var guard = await tx.GetSnapshotAsync(Guard(userId, key), ct);
            if (guard.Exists) { Verify(guard, operation, id, detail); return false; }
            var snapshot = await tx.GetSnapshotAsync(Match(id), ct);
            if (!snapshot.Exists) throw new HeartsAccessException();
            var match = Read(snapshot);
            var actor = match.Players.SingleOrDefault(player => player.UserId == userId && player.IsAccountBacked)
                ?? throw new HeartsAccessException();
            var balances = await BalancesAsync(tx, match, ct);
            var session = await tx.GetSnapshotAsync(Session(userId), ct);
            if (match.Version != expectedVersion) throw new PaidHeartsConflictException("The table changed. Refresh it before trying again.");
            if (match.Status == "result") throw new PaidHeartsConflictException("This match has finished.");
            if (operation == "leave")
            {
                if (match.Status == "queue")
                {
                    match.Players.Remove(actor);
                    var after = checked(balances[userId] + match.EntryCents);
                    tx.Set(Balance(userId), BalanceData(after, now), SetOptions.MergeAll);
                    Ledger(tx, $"{id}-queue-refund-{actor.EntryId}", userId, match.EntryCents, after, "hearts-queue-refund", key, now);
                    if (Text(session, "matchId") == id) tx.Set(Session(userId), new Dictionary<string, object> { ["matchId"] = "" });
                    if (!match.Players.Any(player => player.IsAccountBacked)) CancelEmptyQueue(match, now);
                }
                else
                {
                    Forfeit(actor);
                    match.Message = $"{actor.DisplayName} left and forfeited their entry. Their remaining cards will be played automatically.";
                    if (match.Players.All(player => !player.IsAccountBacked || player.HasLeft))
                        Settle(tx, match, balances, now, abandoned: true);
                    else Schedule(match, now);
                    if (Text(session, "matchId") == id)
                        tx.Set(Session(userId), new Dictionary<string, object> { ["matchId"] = "" });
                }
            }
            else
            {
                if (match.Status != "match" || actor.HasLeft) throw new PaidHeartsConflictException("You cannot act at this table.");
                if (operation == "next-round")
                {
                    match.State = HeartsMatchEngine.StartNextRound(match.State!, nextSeed);
                    match.RecentTrick = null;
                    match.Message = "A new round has been dealt.";
                }
                else
                {
                    HeartsCommand command = operation switch
                    {
                        "pass" => new PassHeartsCards(actor.Seat, (cards ?? []).Select(CardCode.Parse).ToArray()),
                        "card" when cards?.Count == 1 => new PlayHeartsCard(actor.Seat, CardCode.Parse(cards[0])),
                        _ => throw new ArgumentException("This Hearts action is not supported.")
                    };
                    Apply(match, command);
                }
                if (match.State!.Winner is not null) Settle(tx, match, balances, now);
                else Schedule(match, now);
            }
            match.Version++;
            match.UpdatedAtUtc = now;
            Write(tx, match);
            tx.Create(Guard(userId, key), GuardData(operation, id, detail, now));
            return true;
        }, ct);
        await ReleaseIfNeededAsync(id, now, ct);
        return await SessionAsync(userId, now, ct);
    }

    public async Task<PaidHeartsHistoryResponse> HistoryAsync(string userId, CancellationToken ct)
    {
        var snapshots = await database.Collection("heartsPaidHistory").WhereEqualTo("userId", userId).GetSnapshotAsync(ct);
        var items = snapshots.Documents.Select(snapshot => Read(snapshot))
            .OrderByDescending(match => match.CompletedAtUtc).Take(30)
            .Select(match => PaidHeartsProjection.Table(match, userId)).ToArray();
        return new(items);
    }

    private static PaidHeartsParticipant Human(string userId, string name, DateTime now) => new()
    {
        UserId = userId, EntryId = Guid.NewGuid().ToString("N"), DisplayName = name, IsAccountBacked = true, JoinedAtUtc = now,
        DecisionSeed = PaidHeartsIds.PrivateSeed(), TimingSeed = PaidHeartsIds.PrivateSeed()
    };

    private async Task<PaidHeartsSessionResponse> ResponseAsync(PaidHeartsMatch match, string userId, CancellationToken ct)
    {
        var balance = ReadBalance(await Balance(userId).GetSnapshotAsync(ct));
        if (match.Status == "cancelled") return Idle(balance);
        if (match.Status == "match" && match.Players.FirstOrDefault(player => player.UserId == userId && player.HasLeft) is { } departed)
            return Idle(balance) with { Notice = departed.MissedTurns >= 2
                ? "Your seat was forfeited after two missed turns. Your entry remains in the pot; you can start another game."
                : "You left the table and forfeited your entry. You can start another game." };
        return new(match.Status, match.Version, balance / 100m, Entries,
            match.Status == "queue" ? new PaidHeartsQueueResponse(match.Id, match.EntryCents / 100m, match.PotCents / 100m,
                match.Players.Single(player => player.UserId == userId).JoinedAtUtc,
                match.Players.OrderBy(player => player.Seat).Select(player => new PaidHeartsQueuePlayerResponse(
                    PaidHeartsProjection.Seat(player.Seat), player.DisplayName, player.UserId == userId)).ToArray(),
                "Looking for players before filling the open seats.") : null,
            match.State is null ? null : PaidHeartsProjection.Table(match, userId));
    }

    private PaidHeartsSessionResponse Idle(long balance) => new("idle", 0, balance / 100m, Entries, null, null);
    private decimal[] Entries => options.EntryOptionsCents.Order().Select(cents => cents / 100m).ToArray();
    private DocumentReference Match(string id) => database.Collection("heartsPaidMatches").Document(id);
    private DocumentReference Session(string userId) => database.Collection("heartsPaidSessions").Document(PaidHeartsIds.Hash(userId));
    private DocumentReference Partition(long cents) => database.Collection("heartsPaidQueuePartitions").Document($"entry-{cents}-target-100-v1");
    private DocumentReference Balance(string userId) => database.Collection("userBalances").Document($"{userId}_{Currency}");
    private DocumentReference Guard(string userId, string key) => database.Collection("heartsPaidCommands").Document(PaidHeartsIds.Hash($"{userId}\n{key}"));
    private static string Assignment(string id) => $"hearts-paid:{id}";
    private static PaidHeartsMatch Read(DocumentSnapshot snapshot) => JsonSerializer.Deserialize<PaidHeartsMatch>(Text(snapshot, "matchJson"), Json)
        ?? throw new InvalidOperationException("The stored Hearts match is invalid.");
    private void Write(Transaction tx, PaidHeartsMatch match) => tx.Set(Match(match.Id), Data(match));
    private static Dictionary<string, object> Data(PaidHeartsMatch match)
    {
        var data = new Dictionary<string, object>
        {
            ["matchJson"] = JsonSerializer.Serialize(match, Json), ["status"] = match.Status,
            ["releasePending"] = match.ReleasePending,
            ["entryCents"] = match.EntryCents, ["updatedAt"] = Timestamp.FromDateTime(match.UpdatedAtUtc), ["schemaVersion"] = 1L
        };
        var due = match.Status == "queue" ? match.AdmissionClaimExpiresAtUtc ?? match.NextAdmissionAtUtc
            : match.Status == "match" ? new[] { match.NextActionAtUtc, match.LeaseRenewalAtUtc }
                .Where(value => value is not null).Min() : null;
        if (due is { } next) data["nextSweepAt"] = Timestamp.FromDateTime(next);
        return data;
    }
    private static Dictionary<string, object> GuardData(string operation, string target, string detail, DateTime now) => new()
    {
        ["operation"] = operation, ["target"] = target, ["detail"] = detail, ["createdAt"] = Timestamp.FromDateTime(now)
    };
    private static void Verify(DocumentSnapshot snapshot, string operation, string target, string detail)
    {
        if (Text(snapshot, "operation") != operation || Text(snapshot, "target") != target || Text(snapshot, "detail") != detail)
            throw new PaidHeartsConflictException("This Idempotency-Key was already used for another Hearts action.");
    }
    private static string Text(DocumentSnapshot snapshot, string key) => snapshot.Exists && snapshot.TryGetValue<string>(key, out var value) ? value : "";
    private static long ReadBalance(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists) return 0;
        if (!snapshot.TryGetValue<long>("available", out var whole) || whole < 0)
            throw new InvalidOperationException("The account balance is invalid.");
        var fraction = snapshot.TryGetValue<long>("availableFractionalCents", out var value) ? value : 0;
        if (fraction is < 0 or > 99) throw new InvalidOperationException("The account balance is invalid.");
        return checked(whole * 100 + fraction);
    }
    private static Dictionary<string, object> BalanceData(long cents, DateTime now) => new()
    {
        ["available"] = cents / 100, ["availableFractionalCents"] = cents % 100,
        ["version"] = FieldValue.Increment(1), ["updatedAt"] = Timestamp.FromDateTime(now)
    };
    private void Ledger(Transaction tx, string id, string userId, long amount, long after, string type, string key, DateTime now) =>
        tx.Create(database.Collection("balanceTransactions").Document(id), new Dictionary<string, object>
        {
            ["transactionId"] = id, ["userId"] = userId, ["currencyId"] = Currency,
            ["amountCents"] = amount, ["balanceAfterCents"] = after,
            ["amount"] = (double)(amount / 100m), ["balanceAfter"] = (double)(after / 100m),
            ["type"] = type, ["idempotencyKey"] = key, ["createdAt"] = Timestamp.FromDateTime(now)
        });
    private async Task<Dictionary<string, long>> BalancesAsync(Transaction tx, PaidHeartsMatch match, CancellationToken ct)
    {
        var ids = match.Players.Where(player => player.IsAccountBacked).Select(player => player.UserId).ToArray();
        var snapshots = await Task.WhenAll(ids.Select(id => tx.GetSnapshotAsync(Balance(id), ct)));
        return ids.Select((id, index) => (id, balance: ReadBalance(snapshots[index])))
            .ToDictionary(value => value.id, value => value.balance, StringComparer.Ordinal);
    }
    private async Task<T> TransactionAsync<T>(Func<Transaction, Task<T>> action, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await database.RunTransactionAsync(action, cancellationToken: ct); }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.Aborted && attempt < 10)
            { await Task.Delay(20 * (attempt + 1) + Random.Shared.Next(10, 60), ct); }
        }
    }
    private static void Key(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new ArgumentException("A valid Idempotency-Key is required.");
    }
    private static string Identifier(string id)
    {
        if (!Guid.TryParse(id, out var value)) throw new ArgumentException("The Hearts match identifier is invalid.");
        return value.ToString("N");
    }
}
