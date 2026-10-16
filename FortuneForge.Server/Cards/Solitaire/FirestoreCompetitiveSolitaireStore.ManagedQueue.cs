using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Solitaire;
using FortuneForge.Server.Matchmaking;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed partial class FirestoreCompetitiveSolitaireStore
{
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
        var ticketSnapshots = await Task.WhenAll(ReadStringArray(preflight, "ticketIds")
            .Select(id => TicketDocument(id).GetSnapshotAsync(cancellationToken)));
        var plan = PlanQueue(ticketSnapshots.Where(snapshot => snapshot.Exists).Select(ReadTicket), playerCount, nowUtc);
        if (!plan.IsReady) return;

        var selectedIds = plan.HumanTickets.Select(ticket => ticket.TicketId).ToArray();
        var matchId = CreateLookupKey($"{partitionKey}\n{string.Join("\n", selectedIds)}");
        var owner = Guid.NewGuid().ToString("N");
        if (!await ClaimQueuePartitionAsync(partitionReference, owner, nowUtc, cancellationToken)) return;
        // Each supply attempt owns different leases. If a slow attempt outlives
        // the partition lease, its cleanup cannot release its successor's seats.
        var assignmentId = $"{ManagedAssignmentId(matchId)}:{owner}";
        IReadOnlyList<ManagedPlayerProfile> managedProfiles = [];
        try
        {
            managedProfiles = await matchmaking.ReserveManagedPlayersAsync(
                ManagedPlayerGames.Solitaire, assignmentId, plan.ManagedSeatsRequired(playerCount),
                plan.HumanTickets.Select(ticket => ticket.UserId).ToArray(), nowUtc, cancellationToken);
            await RunTransactionAsync(async transaction =>
            {
                var partition = await transaction.GetSnapshotAsync(partitionReference, cancellationToken);
                if (ReadString(partition, "matchingOwner") != owner) return false;
                var snapshots = await Task.WhenAll(ReadStringArray(partition, "ticketIds")
                    .Select(id => transaction.GetSnapshotAsync(TicketDocument(id), cancellationToken)));
                var tickets = snapshots.Where(snapshot => snapshot.Exists).Select(ReadTicket)
                    .Where(ticket => ticket.Status == QueueStatus).ToArray();
                var currentPlan = PlanQueue(tickets, playerCount, nowUtc);
                // A human may have joined while profiles were being reserved. Take
                // a new snapshot on the next sweep so that human gets the seat.
                if (!currentPlan.IsReady || !currentPlan.HumanTickets.Select(ticket => ticket.TicketId)
                        .SequenceEqual(selectedIds, StringComparer.Ordinal)) return false;
                var matchSnapshot = await transaction.GetSnapshotAsync(MatchDocument(matchId), cancellationToken);
                if (matchSnapshot.Exists) return false;

                var humans = currentPlan.HumanTickets;
                var first = humans[0];
                if (managedProfiles.Count != currentPlan.ManagedSeatsRequired(playerCount))
                    throw new InvalidOperationException("The managed-player queuer did not supply enough Solitaire profiles.");
                var pool = checked(humans.Count * first.BuyInCents);
                var payout = humans.Count == 1 ? first.BuyInCents : checked(pool * 90 / 100);
                var seedSnapshot = snapshots.First(snapshot => snapshot.Id == first.TicketId);
                var seed = checked((uint)ReadLong(seedSnapshot, "dealSeed"));
                var match = new SolitaireMatch(
                    matchId, playerCount, first.BuyInCents, pool, payout, checked(pool - payout), seed,
                    nowUtc, nowUtc.Add(SolitaireCompetitionRules.MatchDuration), PlayingMatchStatus,
                    humans.Select(ticket => ticket.UserId).Concat(managedProfiles.Select(profile => profile.UserId)).ToArray(),
                    humans.Select(ticket => ticket.DisplayName).Concat(managedProfiles.Select(profile => profile.PlayerName)).ToArray(),
                    humans.Select(ticket => ticket.TicketId).Concat(managedProfiles.Select(_ => string.Empty)).ToArray(),
                    humans.Select(ticket => ticket.JoinedAtUtc).Concat(managedProfiles.Select(_ => nowUtc)).ToArray(),
                    null, null)
                {
                    PartitionKey = partitionKey, SeatsFilled = true, DrawCount = first.DrawCount
                };
                transaction.Create(MatchDocument(matchId), MatchData(match));
                var game = SolitaireEngine.CreateGame(seed, match.DrawCount);
                for (var index = 0; index < humans.Count; index++)
                {
                    var ticket = humans[index];
                    transaction.Update(TicketDocument(ticket.TicketId), new Dictionary<string, object>
                    {
                        ["status"] = MatchedStatus, ["matchId"] = matchId
                    });
                    transaction.Create(PlayerDocument(matchId, ticket.UserId), PlayerData(new SolitairePlayerState(
                        matchId, ticket.UserId, ticket.DisplayName, index + 1, SolitairePlayerStatuses.Playing,
                        game, 1, null, null, 0, false)
                    {
                        StartedAtUtc = nowUtc, DeadlineAtUtc = match.DeadlineAtUtc
                    }));
                    transaction.Set(SessionDocument(ticket.UserId),
                        SessionData(ticket.UserId, SolitaireSessionKinds.Match, null, matchId, nowUtc),
                        SetOptions.MergeAll);
                }
                for (var index = 0; index < managedProfiles.Count; index++)
                {
                    var player = SolitaireManagedPlayerPolicy.Start(match, humans.Count + index + 1, managedProfiles[index]);
                    player.HostMetadata[ManagedAssignmentMetadataKey] = assignmentId;
                    transaction.Create(PlayerDocument(matchId, player.UserId), PlayerData(player));
                }
                var selected = selectedIds.ToHashSet(StringComparer.Ordinal);
                var remainingIds = tickets.Where(ticket => !selected.Contains(ticket.TicketId))
                    .Select(ticket => ticket.TicketId).ToArray();
                transaction.Set(partitionReference, new Dictionary<string, object>
                {
                    ["ticketIds"] = remainingIds,
                    ["hasQueuedPlayers"] = remainingIds.Length > 0,
                    ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
                }, SetOptions.MergeAll);
                return true;
            }, cancellationToken);
        }
        finally
        {
            try
            {
                if (managedProfiles.Count > 0)
                {
                    // An unknown commit outcome still retains the profiles
                    // recorded in the durable match; unused supply is released.
                    var committed = await MatchDocument(matchId).GetSnapshotAsync(CancellationToken.None);
                    var retained = ReadString(committed, "status") == PlayingMatchStatus
                        ? ReadStringArray(committed, "playerIds").ToHashSet(StringComparer.Ordinal)
                        : new HashSet<string>(StringComparer.Ordinal);
                    await matchmaking.ReleaseManagedPlayersAsync(assignmentId,
                        managedProfiles.Select(profile => profile.UserId).Where(id => !retained.Contains(id)).ToArray(),
                        nowUtc, CancellationToken.None);
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
                if (ReadStringArray(snapshot, "ticketIds").Count == 0) return 0;
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
            if (ReadStringArray(partition, "ticketIds").Count > 0)
                await TryMatchPartitionAsync(partition.Id, nowUtc, cancellationToken);
        }
        var matches = await database.Collection("solitaireMatches")
            .WhereEqualTo("status", PlayingMatchStatus).GetSnapshotAsync(cancellationToken);
        foreach (var match in matches.Documents)
            await AdvanceMatchAsync(match.Id, nowUtc, cancellationToken);
        var pendingReleases = await database.Collection("solitaireMatches")
            .WhereEqualTo("managedPlayersReleasePending", true).GetSnapshotAsync(cancellationToken);
        foreach (var match in pendingReleases.Documents)
            await AdvanceMatchAsync(match.Id, nowUtc, cancellationToken);
    }

    private static string ManagedAssignmentId(string matchId) => $"solitaire:{matchId}";
    private const string ManagedAssignmentMetadataKey = "managed-assignment";
}
