using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Solitaire;
using FortuneForge.Server.Matchmaking;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed partial class FirestoreCompetitiveSolitaireStore
{
    private DateTime lastLegacySweepAtUtc = DateTime.MinValue;

    private MultiplayerQueuePlan<SolitaireTicket> PlanQueue(
        IEnumerable<SolitaireTicket> tickets,
        int playerCount,
        DateTime nowUtc) => matchmaking.PlanQueue(
            tickets,
            new MultiplayerQueueRules(options.AllowSingleHumanBotFill ? 1 : playerCount, playerCount),
            nowUtc,
            ticket => ticket.Status == QueueStatus,
            ticket => ticket.JoinedAtUtc,
            ticket => options.AllowSingleHumanBotFill
                ? ticket.JoinedAtUtc.Add(CompetitiveSolitaireOptions.HumanQueueGracePeriod)
                : ticket.JoinedAtUtc,
            ticket => ticket.TicketId);

    private async Task TryMatchPartitionAsync(
        string partitionKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var partitionReference = PartitionDocument(partitionKey);
        var preflight = await partitionReference.GetSnapshotAsync(cancellationToken);
        var playerCount = checked((int)ReadLong(preflight, "playerCount"));
        if (playerCount < 2) return;
        var queuedSeats = ReadQueuedManagedSeats(preflight);
        var ticketSnapshots = await Task.WhenAll(ReadStringArray(preflight, "ticketIds")
            .Select(id => TicketDocument(id).GetSnapshotAsync(cancellationToken)));
        var plan = PlanQueue(ticketSnapshots.Where(snapshot => snapshot.Exists).Select(ReadTicket), playerCount, nowUtc);
        if (!plan.IsReady && queuedSeats.Count == 0) return;

        var owner = Guid.NewGuid().ToString("N");
        if (!await ClaimQueuePartitionAsync(partitionReference, owner, nowUtc, cancellationToken)) return;
        IReadOnlyList<QueuedManagedSeat> originallyQueued = [];
        string originalAssignmentId = string.Empty;
        string matchId = string.Empty;
        string currentAssignmentId = string.Empty;
        ManagedPlayerProfile? proposedProfile = null;
        try
        {
            // The partition lease serializes bot arrivals across reads, sweeps and
            // server instances. Human joins are still allowed, then rechecked in
            // the transaction before a bot is shown or a match is created.
            var partition = await partitionReference.GetSnapshotAsync(cancellationToken);
            originallyQueued = ReadQueuedManagedSeats(partition);
            originalAssignmentId = ReadString(partition, "queuedManagedAssignmentId");
            var currentSnapshots = await Task.WhenAll(ReadStringArray(partition, "ticketIds")
                .Select(id => TicketDocument(id).GetSnapshotAsync(cancellationToken)));
            var currentPlan = PlanQueue(
                currentSnapshots.Where(snapshot => snapshot.Exists).Select(ReadTicket), playerCount, nowUtc);
            var firstTicketId = currentPlan.HumanTickets.FirstOrDefault()?.TicketId ?? string.Empty;
            var staleQueue = originallyQueued.Count > 0 &&
                (!currentPlan.IsReady ||
                 ReadString(partition, "queuedManagedFirstTicketId") != firstTicketId ||
                 originalAssignmentId != QueueAssignmentId(partitionKey, firstTicketId));
            if (staleQueue)
            {
                await RunTransactionAsync(async transaction =>
                {
                    var latest = await transaction.GetSnapshotAsync(partitionReference, cancellationToken);
                    if (ReadString(latest, "matchingOwner") != owner) return false;
                    var snapshots = await Task.WhenAll(ReadStringArray(latest, "ticketIds")
                        .Select(id => transaction.GetSnapshotAsync(TicketDocument(id), cancellationToken)));
                    var latestPlan = PlanQueue(
                        snapshots.Where(snapshot => snapshot.Exists).Select(ReadTicket), playerCount, nowUtc);
                    if (latestPlan.IsReady)
                    {
                        var latestFirst = latestPlan.HumanTickets[0].TicketId;
                        if (ReadString(latest, "queuedManagedFirstTicketId") == latestFirst &&
                            ReadString(latest, "queuedManagedAssignmentId") ==
                            QueueAssignmentId(partitionKey, latestFirst)) return false;
                    }
                    var reset = ManagedQueueFields([], string.Empty, string.Empty, null);
                    reset["hasQueuedPlayers"] = ReadStringArray(latest, "ticketIds").Count > 0;
                    reset["updatedAt"] = Timestamp.FromDateTime(nowUtc);
                    transaction.Set(partitionReference, reset, SetOptions.MergeAll);
                    return true;
                }, cancellationToken);
                return;
            }
            if (!currentPlan.IsReady) return;

            if (originallyQueued.Count > 0 &&
                !await matchmaking.TryRenewManagedPlayersAsync(
                    ManagedPlayerGames.Solitaire, originalAssignmentId,
                    originallyQueued.Select(seat => seat.UserId).ToArray(),
                    nowUtc, cancellationToken))
            {
                // Persisted queue seats can outlive their managed-profile leases
                // if a worker stalls. Never move a profile into a match after
                // another assignment has claimed it.
                await RunTransactionAsync(async transaction =>
                {
                    var latest = await transaction.GetSnapshotAsync(partitionReference, cancellationToken);
                    if (ReadString(latest, "matchingOwner") != owner ||
                        ReadString(latest, "queuedManagedAssignmentId") != originalAssignmentId)
                        return false;
                    var reset = ManagedQueueFields([], string.Empty, string.Empty, null);
                    reset["hasQueuedPlayers"] = ReadStringArray(latest, "ticketIds").Count > 0;
                    reset["updatedAt"] = Timestamp.FromDateTime(nowUtc);
                    transaction.Set(partitionReference, reset, SetOptions.MergeAll);
                    return true;
                }, cancellationToken);
                return;
            }

            var selectedIds = currentPlan.HumanTickets.Select(ticket => ticket.TicketId).ToArray();
            matchId = CreateLookupKey($"{partitionKey}\n{string.Join("\n", selectedIds)}");
            currentAssignmentId = QueueAssignmentId(partitionKey, firstTicketId);
            var requiredBots = currentPlan.ManagedSeatsRequired(playerCount);
            var nextAt = ReadOptionalTimestamp(partition, "queuedManagedNextAt")
                ?? currentPlan.HumanTickets[0].JoinedAtUtc.Add(CompetitiveSolitaireOptions.HumanQueueGracePeriod);
            if (originallyQueued.Count < requiredBots && nowUtc >= nextAt)
            {
                var excluded = currentPlan.HumanTickets.Select(ticket => ticket.UserId)
                    .Concat(originallyQueued.Select(seat => seat.UserId)).ToArray();
                proposedProfile = (await matchmaking.ReserveManagedPlayersAsync(
                    ManagedPlayerGames.Solitaire, currentAssignmentId, 1,
                    excluded, nowUtc, cancellationToken)).SingleOrDefault();
            }

            await RunTransactionAsync(async transaction =>
            {
                var latest = await transaction.GetSnapshotAsync(partitionReference, cancellationToken);
                if (ReadString(latest, "matchingOwner") != owner) return false;
                var snapshots = await Task.WhenAll(ReadStringArray(latest, "ticketIds")
                    .Select(id => transaction.GetSnapshotAsync(TicketDocument(id), cancellationToken)));
                var tickets = snapshots.Where(snapshot => snapshot.Exists).Select(ReadTicket)
                    .Where(ticket => ticket.Status == QueueStatus).ToArray();
                var latestPlan = PlanQueue(tickets, playerCount, nowUtc);
                // A human may have joined while profiles were being reserved. Take
                // a new snapshot on the next sweep so that human gets the seat.
                if (!latestPlan.IsReady || !latestPlan.HumanTickets.Select(ticket => ticket.TicketId)
                        .SequenceEqual(selectedIds, StringComparer.Ordinal)) return false;
                var staged = ReadQueuedManagedSeats(latest);
                if (staged.Count > 0 &&
                    ReadString(latest, "queuedManagedAssignmentId") != currentAssignmentId) return false;
                var humans = latestPlan.HumanTickets;
                var needed = latestPlan.ManagedSeatsRequired(playerCount);
                var usable = staged.Take(needed).ToArray();
                var dueAt = ReadOptionalTimestamp(latest, "queuedManagedNextAt")
                    ?? humans[0].JoinedAtUtc.Add(CompetitiveSolitaireOptions.HumanQueueGracePeriod);
                if (needed > 0 && usable.Length == needed && nowUtc < dueAt)
                {
                    if (staged.Count > needed)
                        transaction.Set(partitionReference,
                            ManagedQueueFields(usable, currentAssignmentId, firstTicketId, dueAt),
                            SetOptions.MergeAll);
                    return false;
                }
                if (needed > 0 && usable.Length < needed)
                {
                    if (proposedProfile is null || nowUtc < dueAt)
                    {
                        if (staged.Count > needed)
                            transaction.Set(partitionReference,
                                ManagedQueueFields(usable, currentAssignmentId, firstTicketId, dueAt),
                                SetOptions.MergeAll);
                        return false;
                    }
                    var arrived = usable.Append(new QueuedManagedSeat(
                        proposedProfile.UserId, proposedProfile.PlayerName,
                        proposedProfile.SkillLevel, nowUtc)).ToArray();
                    var nextArrivalAt = nowUtc.Add(arrived.Length == needed
                        ? ManagedQueueArrivalPolicy.FinalPause(ManagedPlayerGames.Solitaire, firstTicketId)
                        : ManagedQueueArrivalPolicy.NextGap(
                            ManagedPlayerGames.Solitaire, firstTicketId, arrived.Length));
                    transaction.Set(partitionReference,
                        ManagedQueueFields(arrived, currentAssignmentId, firstTicketId, nextArrivalAt),
                        SetOptions.MergeAll);
                    return true;
                }

                var matchSnapshot = await transaction.GetSnapshotAsync(MatchDocument(matchId), cancellationToken);
                if (matchSnapshot.Exists) return false;
                var first = humans[0];
                var managedProfiles = usable.Select(seat => seat.ToProfile()).ToArray();
                var humanEntry = checked(humans.Count * first.BuyInCents);
                var houseFundedEntry = checked(managedProfiles.Length * first.BuyInCents);
                var pool = checked(humanEntry + houseFundedEntry);
                var payout = checked(pool * 90 / 100);
                var seedSnapshot = snapshots.First(snapshot => snapshot.Id == first.TicketId);
                var seed = checked((uint)ReadLong(seedSnapshot, "dealSeed"));
                var match = new SolitaireMatch(
                    matchId, playerCount, first.BuyInCents, pool, payout, checked(pool - payout), seed,
                    nowUtc, nowUtc.Add(SolitaireCompetitionRules.MatchDuration), PlayingMatchStatus,
                    humans.Select(ticket => ticket.UserId).Concat(managedProfiles.Select(profile => profile.UserId)).ToArray(),
                    humans.Select(ticket => ticket.DisplayName).Concat(managedProfiles.Select(profile => profile.PlayerName)).ToArray(),
                    humans.Select(ticket => ticket.TicketId).Concat(managedProfiles.Select(_ => string.Empty)).ToArray(),
                    humans.Select(ticket => ticket.JoinedAtUtc).Concat(usable.Select(seat => seat.JoinedAtUtc)).ToArray(),
                    null, null)
                {
                    PartitionKey = partitionKey, SeatsFilled = true, DrawCount = first.DrawCount,
                    FinancialModel = HouseFundedPoolModel,
                    HumanEntryCents = humanEntry, HouseFundedEntryCents = houseFundedEntry
                };
                transaction.Create(MatchDocument(matchId), MatchData(match));
                transaction.Create(HouseFundingDocument(matchId), new Dictionary<string, object>
                {
                    ["matchId"] = matchId,
                    ["currencyId"] = SlotsCreditsCurrencyId,
                    ["financialClassification"] = HouseFundedPoolModel,
                    ["humanEntryCents"] = humanEntry,
                    ["houseFundedEntryCents"] = houseFundedEntry,
                    ["humanPlayerCount"] = (long)humans.Count,
                    ["managedPlayerCount"] = (long)managedProfiles.Length,
                    ["fundedAt"] = Timestamp.FromDateTime(nowUtc),
                    ["schemaVersion"] = 1L
                });
                var game = SolitaireEngine.CreateGame(seed, match.DrawCount);
                var securityPlayers = new List<SolitairePlayerState>();
                for (var index = 0; index < humans.Count; index++)
                {
                    var ticket = humans[index];
                    transaction.Update(TicketDocument(ticket.TicketId), new Dictionary<string, object>
                    {
                        ["status"] = MatchedStatus, ["matchId"] = matchId
                    });
                    var humanPlayer = new SolitairePlayerState(
                        matchId, ticket.UserId, ticket.DisplayName, index + 1, SolitairePlayerStatuses.Playing,
                        game, 1, null, null, 0, false)
                    {
                        StartedAtUtc = nowUtc, DeadlineAtUtc = match.DeadlineAtUtc
                    };
                    securityPlayers.Add(humanPlayer);
                    transaction.Create(PlayerDocument(matchId, ticket.UserId), PlayerData(humanPlayer));
                    transaction.Set(SessionDocument(ticket.UserId),
                        SessionData(ticket.UserId, SolitaireSessionKinds.Match, null, matchId, nowUtc),
                        SetOptions.MergeAll);
                }
                for (var index = 0; index < managedProfiles.Length; index++)
                {
                    var player = SolitaireManagedPlayerProgression.Initialize(match,
                        SolitaireManagedPlayerPolicy.Start(match, humans.Count + index + 1, managedProfiles[index]));
                    player.HostMetadata[ManagedAssignmentMetadataKey] = currentAssignmentId;
                    securityPlayers.Add(player);
                    transaction.Create(PlayerDocument(matchId, player.UserId), PlayerData(player));
                }
                WriteSecurityState(transaction, match, securityPlayers, nowUtc, "managed-match-start");
                var selected = selectedIds.ToHashSet(StringComparer.Ordinal);
                var remainingIds = tickets.Where(ticket => !selected.Contains(ticket.TicketId))
                    .Select(ticket => ticket.TicketId).ToArray();
                var partitionChanges = ManagedQueueFields([], string.Empty, string.Empty, null);
                partitionChanges["ticketIds"] = remainingIds;
                partitionChanges["hasQueuedPlayers"] = remainingIds.Length > 0;
                partitionChanges["updatedAt"] = Timestamp.FromDateTime(nowUtc);
                transaction.Set(partitionReference, partitionChanges, SetOptions.MergeAll);
                return true;
            }, cancellationToken);
        }
        finally
        {
            try
            {
                if (originallyQueued.Count > 0 || proposedProfile is not null)
                {
                    // Unknown transaction outcomes are resolved from durable queue
                    // and match state before any lease is released.
                    var durableQueue = await partitionReference.GetSnapshotAsync(CancellationToken.None);
                    var retained = ReadQueuedManagedSeats(durableQueue)
                        .Select(seat => seat.UserId).ToHashSet(StringComparer.Ordinal);
                    if (matchId.Length > 0)
                    {
                        var committed = await MatchDocument(matchId).GetSnapshotAsync(CancellationToken.None);
                        if (ReadString(committed, "status") == PlayingMatchStatus)
                            retained.UnionWith(ReadStringArray(committed, "playerIds"));
                    }
                    if (originallyQueued.Count > 0 && originalAssignmentId.Length > 0)
                        await matchmaking.ReleaseManagedPlayersAsync(originalAssignmentId,
                            originallyQueued.Select(seat => seat.UserId)
                                .Where(id => !retained.Contains(id)).ToArray(), nowUtc, CancellationToken.None);
                    if (proposedProfile is not null && !retained.Contains(proposedProfile.UserId))
                        await matchmaking.ReleaseManagedPlayersAsync(currentAssignmentId,
                            [proposedProfile.UserId], nowUtc, CancellationToken.None);
                }
            }
            finally
            {
                await ReleaseQueuePartitionAsync(partitionReference, owner, CancellationToken.None);
            }
        }
    }

    private async Task<bool> ClaimQueuePartitionAsync(
        DocumentReference reference,
        string owner,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // A joining request can wait for another instance to finish the same
        // short supply operation, then reconnect directly to its committed game.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var outcome = await RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
                if (ReadStringArray(snapshot, "ticketIds").Count == 0 &&
                    ReadQueuedManagedSeats(snapshot).Count == 0) return 0;
                if (ReadOptionalTimestamp(snapshot, "matchingLeaseUntil") > nowUtc) return 1;
                transaction.Update(reference, new Dictionary<string, object>
                {
                    ["matchingOwner"] = owner,
                    ["matchingLeaseUntil"] = Timestamp.FromDateTime(nowUtc.AddMinutes(1))
                });
                return 2;
            }, cancellationToken);
            if (outcome == 2) return true;
            if (outcome == 0) return false;
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
        return false;
    }

    private Task<bool> ReleaseQueuePartitionAsync(
        DocumentReference reference,
        string owner,
        CancellationToken cancellationToken) => RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            if (ReadString(snapshot, "matchingOwner") != owner) return false;
            transaction.Update(reference, new Dictionary<string, object>
            {
                ["matchingOwner"] = FieldValue.Delete,
                ["matchingLeaseUntil"] = FieldValue.Delete
            });
            return true;
        }, cancellationToken);

    public async Task SweepAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var partitions = await database.Collection("solitaireQueuePartitions")
            .WhereEqualTo("hasQueuedPlayers", true).GetSnapshotAsync(cancellationToken);
        foreach (var partition in partitions.Documents)
        {
            if (ReadStringArray(partition, "ticketIds").Count == 0 &&
                ReadQueuedManagedSeats(partition).Count == 0) continue;
            try
            {
                await TryMatchPartitionAsync(partition.Id, nowUtc, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                logger?.LogError(exception,
                    "Unable to advance Solitaire queue partition {PartitionId}.", partition.Id);
            }
        }

        // New matches carry a persisted due time, so hours-long admission
        // windows do not require a match graph transaction every five seconds.
        // The occasional legacy scan migrates matches created before this field
        // existed, or during a rolling deployment with an older server.
        var dueMatches = await database.Collection("solitaireMatches")
            .WhereLessThanOrEqualTo("nextSweepAt", Timestamp.FromDateTime(nowUtc))
            .GetSnapshotAsync(cancellationToken);
        // An older instance in a rolling deployment can settle a newly
        // scheduled match without clearing this field. Prune those entries so
        // they do not pollute every future due-time query.
        foreach (var inactive in dueMatches.Documents.Where(match =>
            ReadString(match, "status") != PlayingMatchStatus))
        {
            try
            {
                await inactive.Reference.UpdateAsync("nextSweepAt", FieldValue.Delete,
                    cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                logger?.LogWarning(exception,
                    "Unable to clear obsolete Solitaire sweep schedule for match {MatchId}.",
                    inactive.Id);
            }
        }
        var scheduled = dueMatches.Documents
            .Where(match => ReadString(match, "status") == PlayingMatchStatus)
            .ToDictionary(match => match.Id, StringComparer.Ordinal);
        if (lastLegacySweepAtUtc == DateTime.MinValue ||
            nowUtc < lastLegacySweepAtUtc ||
            nowUtc - lastLegacySweepAtUtc >= TimeSpan.FromMinutes(1))
        {
            try
            {
                var legacyMatches = await database.Collection("solitaireMatches")
                    .WhereEqualTo("status", PlayingMatchStatus).GetSnapshotAsync(cancellationToken);
                foreach (var match in legacyMatches.Documents.Where(match =>
                    ReadOptionalTimestamp(match, "nextSweepAt") is null))
                    scheduled.TryAdd(match.Id, match);
                lastLegacySweepAtUtc = nowUtc;
            }
            catch (Exception exception) when (exception is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                logger?.LogError(exception,
                    "Unable to inspect legacy Solitaire matches; scheduled matches will still advance.");
            }
        }
        foreach (var match in scheduled.Values.OrderBy(match => match.Id, StringComparer.Ordinal))
        {
            try
            {
                await TryAdmitHourlyManagedPlayerAsync(match, nowUtc, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                logger?.LogError(exception,
                    "Unable to admit a Solitaire competitor to match {MatchId}.", match.Id);
            }
            try
            {
                await AdvanceMatchAsync(match.Id, nowUtc, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                logger?.LogError(exception,
                    "Unable to advance Solitaire match {MatchId}.", match.Id);
            }
        }
        var pendingReleases = await database.Collection("solitaireMatches")
            .WhereEqualTo("managedPlayersReleasePending", true).GetSnapshotAsync(cancellationToken);
        foreach (var match in pendingReleases.Documents)
        {
            try
            {
                await AdvanceMatchAsync(match.Id, nowUtc, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                logger?.LogError(exception,
                    "Unable to release managed players for Solitaire match {MatchId}.", match.Id);
            }
        }
    }

    private static string ManagedAssignmentId(string matchId) => $"solitaire:{matchId}";
    private const string ManagedAssignmentMetadataKey = "managed-assignment";

    private static readonly TimeSpan ManagedMatchArrivalInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// A live match starts with its human entrant. Vacant seats are filled one
    /// at a time, no more often than hourly, using the shared managed-profile
    /// reservation service. This path does not stage bots in a human queue.
    /// </summary>
    private async Task TryAdmitHourlyManagedPlayerAsync(
        DocumentSnapshot preflight,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var candidate = ReadMatch(preflight);
        if (!options.AllowSingleHumanBotFill || !CanAdmitHourlyManagedPlayer(candidate, nowUtc)) return;

        var assignmentId = ManagedAssignmentId(candidate.MatchId);
        var proposed = (await matchmaking.ReserveManagedPlayersAsync(
            ManagedPlayerGames.Solitaire,
            assignmentId,
            1,
            candidate.PlayerIds.ToArray(),
            nowUtc,
            cancellationToken)).SingleOrDefault();
        if (proposed is null) return;
        try
        {
            await RunTransactionAsync(async transaction =>
            {
                var matchReference = MatchDocument(candidate.MatchId);
                var latestSnapshot = await transaction.GetSnapshotAsync(
                    matchReference, cancellationToken);
                var latest = ReadMatch(latestSnapshot);
                if (!CanAdmitHourlyManagedPlayer(latest, nowUtc) ||
                    latest.PlayerIds.Contains(proposed.UserId, StringComparer.Ordinal))
                    return false;

                var fundingReference = HouseFundingDocument(latest.MatchId);
                var funding = await transaction.GetSnapshotAsync(
                    fundingReference, cancellationToken);
                var humanCount = latest.TicketIds.Count(id => !string.IsNullOrEmpty(id));
                var managedCount = latest.PlayerIds.Count - humanCount;
                var priorPlayerSnapshots = await Task.WhenAll(latest.PlayerIds.Select(id =>
                    transaction.GetSnapshotAsync(PlayerDocument(latest.MatchId, id), cancellationToken)));
                var priorPlayers = priorPlayerSnapshots.Select(snapshot => ReadPlayer(snapshot, latest)).ToArray();
                if (!funding.Exists ||
                    latest.HumanEntryCents != checked(humanCount * latest.BuyInCents) ||
                    latest.HouseFundedEntryCents != checked(managedCount * latest.BuyInCents) ||
                    ReadLong(funding, "humanEntryCents") != latest.HumanEntryCents ||
                    ReadLong(funding, "houseFundedEntryCents") != latest.HouseFundedEntryCents)
                    throw new InvalidOperationException(
                        "The open Solitaire match funding does not reconcile before bot admission.");

                var houseFundedEntry = checked(latest.HouseFundedEntryCents + latest.BuyInCents);
                var pool = checked(latest.HumanEntryCents + houseFundedEntry);
                var payout = checked(pool * 90 / 100);
                var filled = latest.PlayerIds.Count + 1 >= latest.PlayerCount;
                var updated = latest with
                {
                    PlayerIds = latest.PlayerIds.Append(proposed.UserId).ToArray(),
                    DisplayNames = latest.DisplayNames.Append(proposed.PlayerName).ToArray(),
                    TicketIds = latest.TicketIds.Append(string.Empty).ToArray(),
                    JoinedAtUtc = latest.JoinedAtUtc.Append(nowUtc).ToArray(),
                    HouseFundedEntryCents = houseFundedEntry,
                    PrizePoolCents = pool,
                    WinnerPayoutCents = payout,
                    PlatformFeeCents = checked(pool - payout),
                    SeatsFilled = filled,
                    SeatFillEligibleAtUtc = filled ? null : nowUtc.Add(ManagedMatchArrivalInterval)
                };

                // Each late entrant gets the identical seeded deal, but a fresh
                // personal ten-minute game clock and independent move schedule.
                var playerClock = latest with
                {
                    StartedAtUtc = nowUtc,
                    DeadlineAtUtc = nowUtc.Add(SolitaireCompetitionRules.MatchDuration)
                };
                var player = SolitaireManagedPlayerProgression.Initialize(playerClock,
                    SolitaireManagedPlayerPolicy.Start(playerClock, latest.PlayerIds.Count + 1, proposed));
                player.HostMetadata[ManagedAssignmentMetadataKey] = assignmentId;

                WriteSecurityState(transaction, updated, priorPlayers.Append(player).ToArray(), nowUtc, "managed-seat-admitted");
                transaction.Create(PlayerDocument(latest.MatchId, proposed.UserId), PlayerData(player));
                var matchData = MatchData(updated);
                var existingSweepAt = ReadOptionalTimestamp(latestSnapshot, "nextSweepAt");
                var botActionAt = SolitaireManagedPlayerProgression.NextActionAt(player);
                matchData["nextSweepAt"] = Timestamp.FromDateTime(
                    existingSweepAt is { } dueAt && dueAt < botActionAt ? dueAt : botActionAt);
                transaction.Update(matchReference, matchData);
                transaction.Set(fundingReference, new Dictionary<string, object>
                {
                    ["humanEntryCents"] = latest.HumanEntryCents,
                    ["houseFundedEntryCents"] = houseFundedEntry,
                    ["humanPlayerCount"] = (long)humanCount,
                    ["managedPlayerCount"] = (long)(managedCount + 1),
                    ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
                }, SetOptions.MergeAll);
                return true;
            }, cancellationToken);
        }
        finally
        {
            // Transaction outcomes can be unknown after a transport failure.
            // Resolve durable membership before releasing the reserved profile.
            var durable = await MatchDocument(candidate.MatchId)
                .GetSnapshotAsync(CancellationToken.None);
            if (!ReadStringArray(durable, "playerIds")
                    .Contains(proposed.UserId, StringComparer.Ordinal))
                await matchmaking.ReleaseManagedPlayersAsync(
                    assignmentId, [proposed.UserId], nowUtc, CancellationToken.None);
        }
    }

    private static bool CanAdmitHourlyManagedPlayer(SolitaireMatch match, DateTime nowUtc) =>
        match.Status == PlayingMatchStatus &&
        match.FinancialModel == HouseFundedPoolModel &&
        match.AdmissionClosesAtUtc is { } closesAt && nowUtc < closesAt &&
        !match.SeatsFilled &&
        match.PlayerIds.Count < match.PlayerCount &&
        match.TicketIds.Any(id => !string.IsNullOrEmpty(id)) &&
        nowUtc >= (match.SeatFillEligibleAtUtc ??
            match.StartedAtUtc.Add(ManagedMatchArrivalInterval));
}
