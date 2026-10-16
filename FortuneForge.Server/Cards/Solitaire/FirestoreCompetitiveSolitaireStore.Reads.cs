using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed partial class FirestoreCompetitiveSolitaireStore
{
    public async Task<SolitaireStoreSession> GetSessionAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var sessionReference = SessionDocument(userId);
        var sessionSnapshot = await sessionReference.GetSnapshotAsync(cancellationToken);
        if (ReadString(sessionSnapshot, "kind") == SolitaireSessionKinds.Queued)
        {
            var queuedTicket = await TicketDocument(ReadString(sessionSnapshot, "ticketId"))
                .GetSnapshotAsync(cancellationToken);
            if (queuedTicket.Exists && ReadString(queuedTicket, "status") == QueueStatus)
                await TryMatchPartitionAsync(ReadString(queuedTicket, "partitionKey"), nowUtc, cancellationToken);
            sessionSnapshot = await sessionReference.GetSnapshotAsync(cancellationToken);
        }
        var matchId = EmptyToNull(ReadString(sessionSnapshot, "matchId"));
        if (matchId is not null)
        {
            await AdvanceMatchAsync(matchId, nowUtc, cancellationToken);
            sessionSnapshot = await sessionReference.GetSnapshotAsync(cancellationToken);
        }
        var balanceSnapshot = await BalanceDocument(userId).GetSnapshotAsync(cancellationToken);
        var balanceCents = ReadBalanceCents(balanceSnapshot);
        if (!sessionSnapshot.Exists ||
            ReadString(sessionSnapshot, "kind") is "" or SolitaireSessionKinds.Idle)
        {
            return new SolitaireStoreSession(new SolitaireIdleSessionResponse(), balanceCents);
        }

        var kind = ReadString(sessionSnapshot, "kind");
        if (kind == SolitaireSessionKinds.Queued)
        {
            var ticketId = ReadString(sessionSnapshot, "ticketId");
            var ticketSnapshot = await TicketDocument(ticketId).GetSnapshotAsync(cancellationToken);
            if (!ticketSnapshot.Exists)
            {
                throw new InvalidOperationException("A Solitaire queue session is missing its ticket.");
            }
            var ticket = ReadTicket(ticketSnapshot);
            if (ticket.Status != QueueStatus)
            {
                var transitioned = await ReadQueueTransitionAsync(userId, ticketId, nowUtc, cancellationToken);
                if (transitioned is not null) return transitioned;
                throw new SolitaireConflictException(
                    "The queue changed while reconnecting. Retry the session request.");
            }
            var partitionSnapshot = await PartitionDocument(ticket.PartitionKey)
                .GetSnapshotAsync(cancellationToken);
            var ticketIds = ReadStringArray(partitionSnapshot, "ticketIds");
            var queueSnapshots = await Task.WhenAll(ticketIds.Select(id =>
                TicketDocument(id).GetSnapshotAsync(cancellationToken)));
            var queue = queueSnapshots
                .Where(snapshot => snapshot.Exists)
                .Select(ReadTicket)
                .Where(value => value.Status == QueueStatus)
                .OrderBy(value => value.JoinedAtUtc)
                .ThenBy(value => value.TicketId, StringComparer.Ordinal)
                .ToArray();
            var position = Array.FindIndex(queue, value => value.TicketId == ticketId) + 1;
            if (position <= 0)
            {
                var transitioned = await ReadQueueTransitionAsync(userId, ticketId, nowUtc, cancellationToken);
                if (transitioned is not null) return transitioned;
                throw new InvalidOperationException("A Solitaire ticket is missing from its queue partition.");
            }
            var cohort = queue.Skip((position - 1) / ticket.PlayerCount * ticket.PlayerCount)
                .Take(ticket.PlayerCount).ToArray();
            var players = cohort.Select((value, index) => new SolitairePlayerResponse(
                value.UserId,
                value.DisplayName,
                index + 1,
                value.JoinedAtUtc,
                SolitairePlayerStatuses.Queued,
                value.UserId == userId)).ToArray();
            // Managed seats have no buy-in. Only this cohort's human entrants
            // contribute to the prospective pool, just as in the dealt match.
            var humanCount = options.AllowSingleHumanBotFill ? cohort.Length : ticket.PlayerCount;
            var poolCents = checked(humanCount * ticket.BuyInCents);
            var payoutCents = humanCount == 1 ? ticket.BuyInCents : checked(poolCents * 90 / 100);
            return new SolitaireStoreSession(
                new SolitaireQueueSessionResponse(
                    ticket.TicketId,
                    ticket.PlayerCount,
                    SolitaireMoney.ToCredits(ticket.BuyInCents),
                    SolitaireMoney.ToCredits(poolCents),
                    SolitaireMoney.ToCredits(payoutCents),
                    position,
                    ticket.JoinedAtUtc,
                    players),
                balanceCents);
        }

        matchId = ReadString(sessionSnapshot, "matchId");
        var graph = await ReadMatchGraphForReadAsync(matchId, cancellationToken);
        if (graph.Match.Status == SettledMatchStatus || kind == SolitaireSessionKinds.Result)
        {
            var resultSnapshot = await CardGameResultDocument(matchId, userId)
                .GetSnapshotAsync(cancellationToken);
            var claimStatus = ReadString(resultSnapshot, "claimStatus");
            return new SolitaireStoreSession(
                BuildResult(
                    graph.Match,
                    graph.Players,
                    userId,
                    claimStatus is "" ? SolitaireClaimStatuses.Unclaimed : claimStatus,
                    ReadString(resultSnapshot, "settlementStatus") == "claimable"),
                balanceCents);
        }

        var current = graph.Players.FirstOrDefault(player => player.UserId == userId)
            ?? throw new SolitaireNotFoundException("The Solitaire match was not found.");
        var remaining = current.Status == SolitairePlayerStatuses.Playing
            ? PlayRemainingMilliseconds(graph.Match, current, nowUtc)
            : 0;
        return new SolitaireStoreSession(
            new SolitaireMatchSessionResponse(
                graph.Match.MatchId,
                graph.Match.PlayerCount,
                SolitaireMoney.ToCredits(graph.Match.BuyInCents),
                SolitaireMoney.ToCredits(graph.Match.PrizePoolCents),
                SolitaireMoney.ToCredits(graph.Match.WinnerPayoutCents),
                current.StartedAtUtc == DateTime.UnixEpoch
                    ? graph.Match.StartedAtUtc
                    : current.StartedAtUtc,
                PlayerDeadline(graph.Match, current),
                current.Version,
                current.Game.Score,
                current.Game.Moves,
                remaining,
                SolitaireEngine.ToResponse(current.Game),
                PlayerResponses(graph.Match, graph.Players, userId))
            {
                IsPaused = current.Status == SolitairePlayerStatuses.Playing &&
                    current.PausedAtUtc is not null,
                PauseRemainingMilliseconds = current.Status == SolitairePlayerStatuses.Playing
                    ? PauseRemainingMilliseconds(current, nowUtc)
                    : 0,
                CanUndo = current.Status == SolitairePlayerStatuses.Playing
                    && current.UndoHistory.Count > 0,
                IntegrityWarning = current.IntegrityWarnings.LastOrDefault() is { } warning
                    ? new SolitaireIntegrityWarningResponse(
                        warning.WarningId,
                        warning.Reason,
                        warning.Purpose,
                        warning.OccurredAtUtc,
                        warning.AcknowledgedAtUtc is not null)
                    : null
            },
            balanceCents);
    }

    private async Task<SolitaireStoreSession?> ReadQueueTransitionAsync(
        string userId,
        string previousTicketId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var refreshed = await SessionDocument(userId).GetSnapshotAsync(cancellationToken);
        return ReadString(refreshed, "kind") != SolitaireSessionKinds.Queued ||
            ReadString(refreshed, "ticketId") != previousTicketId
            ? await GetSessionAsync(userId, nowUtc, cancellationToken)
            : null;
    }

    public async Task<IReadOnlyList<SolitaireHistoryItemResponse>> GetHistoryAsync(
        string userId,
        int limit,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        _ = await GetSessionAsync(userId, nowUtc, cancellationToken);
        var snapshots = new List<DocumentSnapshot>();
        await foreach (var snapshot in database.Collection("solitaireMatchPlayers")
            .WhereEqualTo("userId", userId)
            .Limit(500)
            .StreamAsync(cancellationToken))
        {
            snapshots.Add(snapshot);
        }

        var matchIds = snapshots.Select(snapshot => ReadString(snapshot, "matchId"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var histories = new List<SolitaireHistoryItemResponse>();
        foreach (var matchId in matchIds)
        {
            var resultSnapshot = await CardGameResultDocument(matchId, userId)
                .GetSnapshotAsync(cancellationToken);
            if (ReadString(resultSnapshot, "claimStatus") != SolitaireClaimStatuses.Completed)
            {
                continue;
            }
            var graph = await ReadMatchGraphForReadAsync(matchId, cancellationToken);
            if (graph.Match.Status != SettledMatchStatus || graph.Match.CompletedAtUtc is null)
            {
                continue;
            }
            var standings = SolitaireCompetitionRules.Rank(graph.Players);
            var placement = standings
                .Select((player, index) => new { player.UserId, Placement = index + 1 })
                .First(value => value.UserId == userId)
                .Placement;
            var current = graph.Players.First(player => player.UserId == userId);
            histories.Add(new SolitaireHistoryItemResponse(
                matchId,
                graph.Match.PlayerCount,
                SolitaireMoney.ToCredits(graph.Match.BuyInCents),
                SolitaireMoney.ToCredits(graph.Match.PrizePoolCents),
                placement,
                current.Game.Score,
                checked((int)Math.Ceiling((current.ElapsedMilliseconds ?? 0) / 1000d)),
                SolitaireMoney.ToCredits(current.PayoutCents),
                SolitaireMoney.ToCredits(current.PayoutCents - graph.Match.BuyInCents),
                graph.Match.CompletedAtUtc.Value,
                graph.Match.DisplayNames
                    .Where((_, index) => graph.Match.PlayerIds[index] != userId)
                    .ToArray()));
        }
        return histories
            .OrderByDescending(history => history.CompletedAtUtc)
            .ThenBy(history => history.MatchId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private async Task<ReadGraph> ReadMatchGraphForReadAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        var matchSnapshot = await MatchDocument(matchId).GetSnapshotAsync(cancellationToken);
        if (!matchSnapshot.Exists)
        {
            throw new SolitaireNotFoundException("The Solitaire match was not found.");
        }
        var match = ReadMatch(matchSnapshot);
        var playerSnapshots = await Task.WhenAll(match.PlayerIds.Select(userId =>
            PlayerDocument(matchId, userId).GetSnapshotAsync(cancellationToken)));
        if (playerSnapshots.Any(snapshot => !snapshot.Exists))
        {
            throw new InvalidOperationException("A Solitaire match is missing a player state.");
        }
        return new ReadGraph(
            match,
            playerSnapshots.Select(snapshot => ReadPlayer(snapshot, match)).ToArray());
    }

    private static SolitaireResultSessionResponse BuildResult(
        SolitaireMatch match,
        IReadOnlyList<SolitairePlayerState> players,
        string currentUserId,
        string claimStatus,
        bool isClaimable)
    {
        if (match.CompletedAtUtc is null)
        {
            throw new InvalidOperationException("A settled Solitaire match has no completion time.");
        }
        var ranked = SolitaireCompetitionRules.Rank(players);
        var standings = ranked.Select((player, index) => new SolitaireStandingResponse(
            index + 1,
            PublicPlayerId(match, player),
            player.DisplayName,
            player.Game.Score,
            player.Game.Moves,
            checked((int)Math.Ceiling((player.ElapsedMilliseconds ?? 0) / 1000d)),
            player.Status,
            SolitaireMoney.ToCredits(player.PayoutCents),
            player.UserId == currentUserId)).ToArray();
        return new SolitaireResultSessionResponse(
            match.MatchId,
            match.PlayerCount,
            SolitaireMoney.ToCredits(match.BuyInCents),
            SolitaireMoney.ToCredits(match.PrizePoolCents),
            SolitaireMoney.ToCredits(match.WinnerPayoutCents),
            SolitaireMoney.ToCredits(match.PlatformFeeCents),
            match.StartedAtUtc,
            match.CompletedAtUtc.Value,
            standings)
        {
            ClaimStatus = claimStatus,
            CanClaim = claimStatus == SolitaireClaimStatuses.Unclaimed && isClaimable
        };
    }

    private static IReadOnlyList<SolitairePlayerResponse> PlayerResponses(
        SolitaireMatch match,
        IReadOnlyList<SolitairePlayerState> players,
        string currentUserId)
    {
        var occupied = players.ToDictionary(player => player.Seat);
        return Enumerable.Range(1, match.PlayerCount)
            .Select(seat => occupied.TryGetValue(seat, out var player)
                ? new SolitairePlayerResponse(
                    PublicPlayerId(match, player),
                    player.DisplayName,
                    seat,
                    JoinedAt(match, player),
                    player.Status,
                    player.UserId == currentUserId,
                    IsTerminal(player) ? player.Game.Score : null,
                    IsTerminal(player) ? player.Game.Moves : null,
                    IsTerminal(player) && player.ElapsedMilliseconds is not null
                        ? checked((int)Math.Ceiling(player.ElapsedMilliseconds.Value / 1000d))
                        : null)
                : new SolitairePlayerResponse(
                    $"open-seat-{seat}",
                    "Open seat",
                    seat,
                    match.StartedAtUtc,
                    SolitairePlayerStatuses.Open,
                    false))
            .ToArray();
    }

    private static string PublicPlayerId(SolitaireMatch match, SolitairePlayerState player) =>
        player.IsAccountBacked ? player.UserId : $"competitor-{player.Seat}";

    private static DateTime JoinedAt(SolitaireMatch match, SolitairePlayerState player)
    {
        var index = PlayerIndex(match.PlayerIds, player.UserId);
        return index >= 0 && index < match.JoinedAtUtc.Count
            ? match.JoinedAtUtc[index]
            : match.StartedAtUtc;
    }

    private sealed record ReadGraph(
        SolitaireMatch Match,
        IReadOnlyList<SolitairePlayerState> Players);
}
