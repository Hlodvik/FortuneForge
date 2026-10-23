using System.Text.Json;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Hearts;
using FortuneForge.Server.Matchmaking;
using FortuneForge.Server.Admin.Management;
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
                    return await ResponseAsync(match, userId, now, ct);
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
        return await ResponseAsync(Read(await Match(id).GetSnapshotAsync(ct)), userId, now, ct);
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
        string? joinNotice;
        for (var attempt = 0; ; attempt++)
        {
            if (attempt >= 20)
                throw new PaidHeartsConflictException("The available tables are changing quickly. Please try joining again.");
            var batch = new PaidHeartsJoinBatch(database);
            try
            {
                // Refresh the receipt first, including after a failed-condition
                // commit. Never re-use a stale balance or funding delta.
                var guard = await batch.ReadAsync(Guard(userId, key), ct);
                if (guard.Exists)
                {
                    Verify(guard, "join", "", detail);
                    joinNotice = Text(guard, "notice") is { Length: > 0 } savedNotice ? savedNotice : null;
                    break;
                }
                var session = await batch.ReadAsync(Session(userId), ct);
                var previousId = Text(session, "matchId");
                if (previousId.Length > 0)
                {
                    var previous = await batch.ReadAsync(Match(previousId), ct);
                    if (previous.Exists && Read(previous) is { Status: "queue" or "match" } active &&
                        active.Players.Any(player => player.UserId == userId && !player.HasLeft))
                    {
                        // Session CAS protects different request keys for the same
                        // account without charging or creating another seat.
                        batch.Set(Session(userId), new Dictionary<string, object> { ["matchId"] = previousId });
                        batch.Create(Guard(userId, key), GuardData("join", "", detail, now));
                        await batch.CommitAsync(ct);
                        joinNotice = null;
                        break;
                    }
                }

                // The index is advisory. All selected match, funding, account,
                // session and fallback-partition snapshots are conditional writes
                // in one atomic batch; no pessimistic read locks are held.
                var houseHint = await FindHouseTableHintAsync(cents, userId, now, ct);
                PaidHeartsMatch? activeMatch = null;
                DocumentSnapshot? activeSnapshot = null;
                if (houseHint is not null)
                {
                    var available = await batch.ReadAsync(Match(houseHint), ct);
                    if (!available.Exists || !CanTakeHouseSeat(Read(available), cents, userId, now))
                    {
                        await Task.Delay(20 + Random.Shared.Next(40), ct);
                        continue;
                    }
                    activeMatch = Read(available);
                    activeSnapshot = available;
                }
                PaidHeartsMatch? waitingMatch = null;
                if (activeMatch is null)
                {
                    var partition = await batch.ReadAsync(Partition(cents), ct);
                    var waitingId = Text(partition, "matchId");
                    PaidHeartsMatch? candidate = null;
                    if (waitingId.Length > 0)
                    {
                        var waiting = await batch.ReadAsync(Match(waitingId), ct);
                        if (waiting.Exists) candidate = Read(waiting);
                    }
                    waitingMatch = matchmaking.FindLiveTable(candidate is null ? [] : new[] { candidate },
                        value => value.Status == "queue" && value.EntryCents == cents && value.Players.Count < 4 &&
                            value.Players.All(player => player.IsAccountBacked) && now < value.CreatedAtUtc.AddMinutes(2),
                        value => value.CreatedAtUtc, value => value.Id);
                }
                var match = activeMatch ?? waitingMatch ?? new PaidHeartsMatch
                {
                    Id = newId, EntryCents = cents, CreatedAtUtc = now, UpdatedAtUtc = now,
                    NextAdmissionAtUtc = now.AddSeconds(options.HumanQueueSeconds)
                };
                var balance = ReadBalance(await batch.ReadAsync(Balance(userId), ct));
                if (balance < cents) throw new PaidHeartsInsufficientFundsException();
                var house = activeMatch?.Players.Where(player => IsVirtualHouse(activeMatch, player)).OrderBy(player => player.Seat).First();
                var funding = house is null ? null : await batch.ReadAsync(Funding(match.Id), ct);
                if (house is not null && !FundingReconciles(match, funding!))
                {
                    // Independent reads can straddle somebody else's atomic
                    // takeover. Refresh that legitimate race, but fail closed
                    // when the unchanged match's funding really is inconsistent.
                    var latest = await Match(match.Id).GetSnapshotAsync(ct);
                    if (!latest.Exists || latest.UpdateTime != activeSnapshot!.UpdateTime)
                    {
                        await Task.Delay(20 + Random.Shared.Next(40), ct);
                        continue;
                    }
                    throw new InvalidOperationException("The Hearts table funding does not reconcile.");
                }
                string? notice = null;
                if (house is not null)
                {
                    actor.Seat = house.Seat;
                    ReclassifyHouseStake(batch, match, house, actor, funding!, key, now);
                    match.Players[match.Players.IndexOf(house)] = actor;
                    if (match.ScheduledSeat == actor.Seat)
                        match.NextActionAtUtc = now.AddSeconds(options.ActionTimeoutSeconds);
                    notice = $"You joined in place of House {actor.Seat}, keeping that seat's existing cards and score. Your entry replaced its house-funded stake; the pot is unchanged.";
                    match.Message = $"{actor.DisplayName} joined in place of House {actor.Seat}.";
                }
                else
                {
                    actor.Seat = Enum.GetValues<PlayerSeat>().First(seat => match.Players.All(player => player.Seat != seat));
                    match.Players.Add(actor);
                }
                match.Version++;
                match.UpdatedAtUtc = now;
                var deal = house is null && match.Players.Count == 4;
                if (deal) Start(match, now, PaidHeartsIds.Seed());
                batch.Set(Balance(userId), BalanceData(balance - cents, now));
                Ledger(batch, $"{match.Id}-entry-{actor.EntryId}", userId, -cents, balance - cents, "hearts-entry", key, now);
                batch.Set(Session(userId), new Dictionary<string, object> { ["matchId"] = match.Id, ["updatedAt"] = Timestamp.FromDateTime(now) });
                if (house is null)
                    batch.Set(Partition(cents), new Dictionary<string, object> { ["matchId"] = match.Status == "queue" ? match.Id : "" });
                Write(batch, match);
                if (deal) WriteFunding(batch, match, now);
                var receipt = GuardData("join", "", detail, now);
                if (notice is not null) receipt["notice"] = notice;
                batch.Create(Guard(userId, key), receipt);
                await batch.CommitAsync(ct);
                joinNotice = notice;
                break;
            }
            catch (RpcException exception) when (exception.StatusCode is StatusCode.FailedPrecondition or StatusCode.AlreadyExists or StatusCode.Aborted)
            {
                // These statuses mean the atomic preconditions did not hold.
                // An uncertain DeadlineExceeded commit is not retried here.
                await Task.Delay(20 + Random.Shared.Next(60), ct);
            }
        }
        var response = await SessionAsync(userId, now, ct);
        return joinNotice is null || response.Notice is not null ? response : response with { Notice = joinNotice };
    }

    public async Task<PaidHeartsSessionResponse> PlayHouseAsync(
        string userId, string id, int expectedVersion, string key, DateTime now, CancellationToken ct)
    {
        id = Identifier(id); Key(key);
        if (expectedVersion < 0) throw new ArgumentException("ExpectedVersion cannot be negative.");
        var detail = expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var shuffleSeed = PaidHeartsIds.Seed();
        var observed = await Match(id).GetSnapshotAsync(ct);
        if (!observed.Exists || !Read(observed).Players.Any(player => player.UserId == userId && player.IsAccountBacked))
            throw new HeartsAccessException();
        await AdvanceAsync(id, now, ct);
        await TransactionAsync(async tx =>
        {
            var receipt = await tx.GetSnapshotAsync(Guard(userId, key), ct);
            if (receipt.Exists) { Verify(receipt, "play-house", id, detail); return false; }
            var snapshot = await tx.GetSnapshotAsync(Match(id), ct);
            if (!snapshot.Exists) throw new HeartsAccessException();
            var match = Read(snapshot);
            if (!match.Players.Any(player => player.UserId == userId && player.IsAccountBacked && !player.HasLeft))
                throw new HeartsAccessException();
            var partition = await tx.GetSnapshotAsync(Partition(match.EntryCents), ct);
            var policy = await tx.GetSnapshotAsync(database.Document(FirestoreAdminManagementStore.StatePath), ct);
            if (match.Version != expectedVersion)
                throw new PaidHeartsConflictException("Players have arrived or the table changed. Check the updated queue before choosing again.");
            if (match.Status != "queue" || match.Players.Count != 1 || !match.Players[0].IsAccountBacked ||
                Text(partition, "matchId") != id)
                throw new PaidHeartsConflictException("Other players are available. Continue with the human queue instead.");
            if (now < match.NextAdmissionAtUtc)
                throw new PaidHeartsConflictException("The human-player search is still running. Please wait a moment.");
            if (!options.AllowManagedPlayers || !FirestoreAdminManagementStore.ReadState(policy).AllowsBots(ManagedPlayerGames.Hearts))
                throw new PaidHeartsConflictException("House opponents are currently unavailable. You can wait for players or leave for a refund.");
            foreach (var seat in Enum.GetValues<PlayerSeat>().Where(seat => match.Players.All(player => player.Seat != seat)))
            {
                var house = HeartsHousePlayers.Create(match.Id, seat);
                match.Players.Add(new PaidHeartsParticipant
                {
                    UserId = house.UserId, DisplayName = house.PlayerName, Seat = seat,
                    SkillLevel = house.SkillLevel, JoinedAtUtc = now,
                    DecisionSeed = PaidHeartsIds.PrivateSeed(), TimingSeed = PaidHeartsIds.PrivateSeed()
                });
            }
            match.Version++;
            match.UpdatedAtUtc = now;
            Start(match, now, shuffleSeed);
            WriteFunding(tx, match, now);
            tx.Set(Partition(match.EntryCents), new Dictionary<string, object> { ["matchId"] = "" });
            Write(tx, match);
            tx.Create(Guard(userId, key), GuardData("play-house", id, detail, now));
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
        var deadlineAdvanced = await TransactionAsync(async tx =>
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
            if (ActionIsDue(match, now))
            {
                // Commit the authoritative timeout in this same transaction. Do
                // not throw after advancing: that would roll back the timeout
                // and let a late card/pass keep racing the worker indefinitely.
                AdvanceDueAction(tx, match, balances, now, nextSeed);
                if (operation != "leave" || match.Status != "match" || actor.HasLeft)
                {
                    tx.Create(Guard(userId, key), GuardData(operation, id, detail, now));
                    return true;
                }
                // A still-active player may leave after the overdue action.
                // The just-advanced actor/deadline must not be reset by it.
            }
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
            return false;
        }, ct);
        await ReleaseIfNeededAsync(id, now, ct);
        var response = await SessionAsync(userId, now, ct);
        return deadlineAdvanced ? response with
        {
            Notice = response.Notice ?? "The table timer expired before that action arrived. The automatic play stands; the table is now up to date."
        } : response;
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

    private async Task<PaidHeartsSessionResponse> ResponseAsync(PaidHeartsMatch match, string userId, DateTime now, CancellationToken ct)
    {
        var balance = ReadBalance(await Balance(userId).GetSnapshotAsync(ct));
        if (match.Status == "cancelled") return Idle(balance) with { Notice = match.Message };
        if (match.Status == "match" && match.Players.FirstOrDefault(player => player.UserId == userId && player.HasLeft) is { } departed)
            return Idle(balance) with { Notice = departed.MissedTurns > 0
                ? "Your seat was forfeited when the turn timer expired. Your entry remains in the pot; you can start another game."
                : "You left the table and forfeited your entry. You can start another game." };
        var canPlayHouse = match.Status == "queue" && match.Players.Count == 1 &&
            match.Players[0].IsAccountBacked && now >= match.NextAdmissionAtUtc && options.AllowManagedPlayers &&
            FirestoreAdminManagementStore.ReadState(await database.Document(FirestoreAdminManagementStore.StatePath)
                .GetSnapshotAsync(ct)).AllowsBots(ManagedPlayerGames.Hearts);
        return new(match.Status, match.Version, balance / 100m, Entries,
            match.Status == "queue" ? new PaidHeartsQueueResponse(match.Id, match.EntryCents / 100m, match.PotCents / 100m,
                match.Players.Single(player => player.UserId == userId).JoinedAtUtc,
                match.Players.OrderBy(player => player.Seat).Select(player => new PaidHeartsQueuePlayerResponse(
                    PaidHeartsProjection.Seat(player.Seat), player.DisplayName, player.UserId == userId)).ToArray(),
                canPlayHouse
                    ? "No other human players are waiting for this entry. Play against the house, or keep waiting. The queue refunds after two minutes."
                    : "Looking for human players. No house opponents will join without your permission. The queue refunds after two minutes.",
                canPlayHouse) : null,
            match.State is null ? null : PaidHeartsProjection.Table(match, userId));
    }

    private PaidHeartsSessionResponse Idle(long balance) => new("idle", 0, balance / 100m, Entries, null, null);
    private decimal[] Entries => options.EntryOptionsCents.Order().Select(cents => cents / 100m).ToArray();
    private DocumentReference Match(string id) => database.Collection("heartsPaidMatches").Document(id);
    private DocumentReference Session(string userId) => database.Collection("heartsPaidSessions").Document(PaidHeartsIds.Hash(userId));
    private DocumentReference Partition(long cents) => database.Collection("heartsPaidQueuePartitions").Document($"entry-{cents}-target-100-v1");
    private CollectionReference HouseTables(long cents) => database.Collection("heartsPaidSeatAvailability")
        .Document($"entry-{cents}-target-100-v1").Collection("tables");
    private DocumentReference HouseTable(PaidHeartsMatch match) =>
        HouseTables(match.EntryCents).Document($"{match.CreatedAtUtc.Ticks:D19}-{match.Id}");
    private DocumentReference Funding(string id) => database.Collection("heartsPaidFunding").Document(id);
    private DocumentReference Balance(string userId) => database.Collection("userBalances").Document($"{userId}_{Currency}");
    private DocumentReference Guard(string userId, string key) => database.Collection("heartsPaidCommands").Document(PaidHeartsIds.Hash($"{userId}\n{key}"));
    private static PaidHeartsMatch Read(DocumentSnapshot snapshot) => JsonSerializer.Deserialize<PaidHeartsMatch>(Text(snapshot, "matchJson"), Json)
        ?? throw new InvalidOperationException("The stored Hearts match is invalid.");
    private void Write(Transaction tx, PaidHeartsMatch match)
    {
        tx.Set(Match(match.Id), Data(match));
        // Availability is a transactionally maintained, entry-partitioned index.
        // Document IDs preserve creation order without a composite index or
        // scanning every active match. The match remains authoritative on join.
        if (HasJoinableHouseSeat(match))
            tx.Set(HouseTable(match), new Dictionary<string, object> { ["matchId"] = match.Id });
        else tx.Delete(HouseTable(match));
    }

    private static bool IsVirtualHouse(PaidHeartsMatch match, PaidHeartsParticipant player) =>
        !player.IsAccountBacked && !player.HasLeft && player.ManagedAssignmentId is null &&
        player.UserId == HeartsHousePlayers.Create(match.Id, player.Seat).UserId;

    private static bool HasJoinableHouseSeat(PaidHeartsMatch match) =>
        match.Status == "match" && match.State is { Winner: null } && match.Players.Count == 4 &&
        match.Players.Any(player => player.IsAccountBacked && !player.HasLeft) &&
        match.Players.Any(player => IsVirtualHouse(match, player));

    private static bool CanTakeHouseSeat(PaidHeartsMatch table, long cents, string userId, DateTime now) =>
        table.EntryCents == cents && HasJoinableHouseSeat(table) &&
        table.Players.All(player => player.UserId != userId) &&
        // Progression must process an overdue human forfeiture before somebody
        // enters that potentially abandoned table. House timing is replaced by
        // the incoming human's full turn allowance.
        !(ActionIsDue(table, now) && table.ScheduledSeat is { } seat &&
          table.Players.Any(player => player.Seat == seat && player.IsAccountBacked && !player.HasLeft));

    private async Task<string?> FindHouseTableHintAsync(long cents, string userId, DateTime now, CancellationToken ct)
    {
        Query query = HouseTables(cents).OrderBy(FieldPath.DocumentId).Limit(16);
        while (true)
        {
            var available = await query.GetSnapshotAsync(ct);
            foreach (var row in available.Documents)
            {
                var id = Text(row, "matchId");
                if (!Guid.TryParseExact(id, "N", out _)) continue;
                var snapshot = await Match(id).GetSnapshotAsync(ct);
                if (!snapshot.Exists) continue;
                var table = Read(snapshot);
                if (CanTakeHouseSeat(table, cents, userId, now)) return id;
            }
            if (available.Count < 16) return null;
            query = HouseTables(cents).OrderBy(FieldPath.DocumentId).StartAfter(available.Documents[^1]).Limit(16);
        }
    }

    private void ReclassifyHouseStake(PaidHeartsJoinBatch tx, PaidHeartsMatch match, PaidHeartsParticipant house,
        PaidHeartsParticipant human, DocumentSnapshot funding, string key, DateTime now)
    {
        var beforeHuman = checked(match.EntryCents * match.Players.Count(player => player.IsAccountBacked));
        var beforeHouse = checked(match.PotCents - beforeHuman);
        if (!FundingReconciles(match, funding))
            throw new InvalidOperationException("The Hearts table funding does not reconcile.");
        var initialHuman = funding.TryGetValue<long>("initialHumanEntryCents", out var value) ? value : beforeHuman;
        var afterHuman = checked(beforeHuman + match.EntryCents);
        var afterHouse = checked(beforeHouse - match.EntryCents);
        match.InitialHumanEntryCents = initialHuman;
        match.HouseTakeoverCount++;
        tx.Update(Funding(match.Id), new Dictionary<string, object>
        {
            ["initialHumanEntryCents"] = initialHuman,
            ["initialHouseFundedEntryCents"] = checked(match.PotCents - initialHuman),
            ["humanEntryCents"] = afterHuman, ["houseFundedEntryCents"] = afterHouse,
            ["takeoverCount"] = match.HouseTakeoverCount, ["updatedAt"] = Timestamp.FromDateTime(now)
        });
        var state = match.State!;
        tx.Create(database.Collection("heartsPaidFundingTransfers").Document($"{match.Id}-{human.EntryId}"),
            new Dictionary<string, object>
            {
                ["matchId"] = match.Id, ["userId"] = human.UserId, ["entryId"] = human.EntryId,
                ["seat"] = PaidHeartsProjection.Seat(house.Seat), ["replacedHouseId"] = house.UserId,
                ["entryCents"] = match.EntryCents, ["grossPoolCents"] = match.PotCents,
                ["humanEntryCentsBefore"] = beforeHuman, ["humanEntryCentsAfter"] = afterHuman,
                ["houseFundedEntryCentsBefore"] = beforeHouse, ["houseFundedEntryCentsAfter"] = afterHouse,
                ["inheritedRoundNumber"] = state.RoundNumber, ["inheritedMatchScore"] = state.Score.For(house.Seat),
                ["inheritedRoundPoints"] = state.Round.Scores.Single(score => score.Seat == house.Seat).Points,
                ["inheritedCompletedTricks"] = state.Round.CompletedTricks.Count,
                ["inheritedPhase"] = state.Round.Phase.ToString().ToLowerInvariant(),
                ["inheritedSubmittedPass"] = state.Round.SubmittedPasses.Any(pass => pass.Seat == house.Seat),
                ["idempotencyKey"] = key, ["occurredAt"] = Timestamp.FromDateTime(now), ["schemaVersion"] = 1L,
                ["financialClassification"] = "human-replaces-house-funded-hearts-entry-v1"
            });
    }

    private static bool FundingReconciles(PaidHeartsMatch match, DocumentSnapshot funding) =>
        funding.Exists && funding.GetValue<long>("entryCents") == match.EntryCents &&
        funding.GetValue<long>("grossPoolCents") == match.PotCents &&
        funding.GetValue<long>("humanEntryCents") == checked(match.EntryCents * match.Players.Count(player => player.IsAccountBacked)) &&
        funding.GetValue<long>("houseFundedEntryCents") == checked(match.EntryCents * match.Players.Count(player => !player.IsAccountBacked));
    private static Dictionary<string, object> Data(PaidHeartsMatch match)
    {
        var data = new Dictionary<string, object>
        {
            ["matchJson"] = JsonSerializer.Serialize(match, Json), ["status"] = match.Status,
            ["releasePending"] = match.ReleasePending,
            ["entryCents"] = match.EntryCents, ["updatedAt"] = Timestamp.FromDateTime(match.UpdatedAtUtc), ["schemaVersion"] = 1L
        };
        var due = match.Status == "queue" ? match.CreatedAtUtc.AddMinutes(2)
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
        tx.Create(database.Collection("balanceTransactions").Document(id), LedgerData(id, userId, amount, after, type, key, now));
    private static Dictionary<string, object> LedgerData(string id, string userId, long amount, long after,
        string type, string key, DateTime now) => new()
        {
            ["transactionId"] = id, ["userId"] = userId, ["currencyId"] = Currency,
            ["amountCents"] = amount, ["balanceAfterCents"] = after,
            ["amount"] = (double)(amount / 100m), ["balanceAfter"] = (double)(after / 100m),
            ["type"] = type, ["idempotencyKey"] = key, ["createdAt"] = Timestamp.FromDateTime(now)
        };
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
