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
            var humanPlayers = cohort.Select((value, index) => new SolitairePlayerResponse(
                value.UserId,
                value.DisplayName,
                index + 1,
                value.JoinedAtUtc,
                SolitairePlayerStatuses.Queued,
                value.UserId == userId)).ToArray();
            var queuedBots = position <= ticket.PlayerCount &&
                ReadString(partitionSnapshot, "queuedManagedFirstTicketId") == cohort[0].TicketId
                ? ReadQueuedManagedSeats(partitionSnapshot).Take(ticket.PlayerCount - cohort.Length).ToArray()
                : [];
            var players = humanPlayers.Concat(queuedBots.Select((bot, index) =>
                new SolitairePlayerResponse(
                    $"competitor-{CreateLookupKey(bot.UserId)[..12]}",
                    bot.PlayerName,
                    humanPlayers.Length + index + 1,
                    bot.JoinedAtUtc,
                    SolitairePlayerStatuses.Queued,
                    false))).ToArray();
            // Every eventual seat contributes an equal entry. Any unfilled
            // seats in a bot-enabled lobby are funded by the house at deal.
            var poolCents = checked(ticket.PlayerCount * ticket.BuyInCents);
            var payoutCents = checked(poolCents * 90 / 100);
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
        CancellationToken cancellationToken) =>
        (await GetHistoryPageAsync(userId, limit, null, nowUtc, cancellationToken)).Items;

    public async Task<SolitaireHistoryPageResponse> GetHistoryPageAsync(
        string userId,
        int limit,
        string? cursor,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (cursor is null)
            _ = await GetSessionAsync(userId, nowUtc, cancellationToken);
        await EnsureLegacyHistoryTimestampsAsync(userId, nowUtc, cancellationToken);

        var query = database.Collection("solitaireMatchPlayers")
            .WhereEqualTo("userId", userId)
            .OrderByDescending("startedAt");
        if (cursor is not null)
        {
            if (cursor.Length != 64 || cursor.Any(character => !char.IsAsciiHexDigit(character)))
                throw new ArgumentException("The Solitaire history cursor is invalid.", nameof(cursor));
            var cursorSnapshot = await database.Collection("solitaireMatchPlayers")
                .Document(cursor).GetSnapshotAsync(cancellationToken);
            if (!cursorSnapshot.Exists || ReadString(cursorSnapshot, "userId") != userId ||
                !cursorSnapshot.TryGetValue<Timestamp>("startedAt", out _))
                throw new ArgumentException("The Solitaire history cursor is invalid.", nameof(cursor));
            query = query.StartAfter(cursorSnapshot);
        }
        var snapshots = (await query.Limit(limit + 1).GetSnapshotAsync(cancellationToken)).Documents;
        var selected = snapshots.Take(limit).ToArray();
        using var hydrateLimit = new SemaphoreSlim(8);
        var items = await Task.WhenAll(selected.Select(async snapshot =>
        {
            await hydrateLimit.WaitAsync(cancellationToken);
            try
            {
                return await ReadHistoryItemAsync(snapshot, userId, cancellationToken);
            }
            finally
            {
                hydrateLimit.Release();
            }
        }));
        return new SolitaireHistoryPageResponse(
            items.Where(item => item is not null).Select(item => item!).ToArray(),
            snapshots.Count > limit ? selected[^1].Id : null);
    }

    private async Task EnsureLegacyHistoryTimestampsAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var marker = database.Collection("solitaireHistoryIndexMigrations")
            .Document(CreateLookupKey(userId));
        if ((await marker.GetSnapshotAsync(cancellationToken)).Exists) return;
        await foreach (var snapshot in database.Collection("solitaireMatchPlayers")
            .WhereEqualTo("userId", userId).StreamAsync(cancellationToken))
        {
            if (snapshot.TryGetValue<Timestamp>("startedAt", out var startedAt) &&
                startedAt.ToDateTime() != DateTime.UnixEpoch) continue;
            var matchId = ReadString(snapshot, "matchId");
            var match = await MatchDocument(matchId).GetSnapshotAsync(cancellationToken);
            if (!match.Exists) continue;
            await snapshot.Reference.UpdateAsync(new Dictionary<string, object>
            {
                ["startedAt"] = match.GetValue<Timestamp>("startedAt")
            }, cancellationToken: cancellationToken);
        }
        await marker.SetAsync(new Dictionary<string, object>
        {
            ["completedAt"] = Timestamp.FromDateTime(nowUtc)
        }, cancellationToken: cancellationToken);
    }

    private async Task<SolitaireHistoryItemResponse?> ReadHistoryItemAsync(
        DocumentSnapshot playerSnapshot,
        string userId,
        CancellationToken cancellationToken)
    {
        var matchId = ReadString(playerSnapshot, "matchId");
        var graph = await ReadMatchGraphForReadAsync(matchId, cancellationToken);
        if (graph.Match.Status is not (PlayingMatchStatus or SettledMatchStatus)) return null;
        var resultSnapshot = graph.Match.Status == SettledMatchStatus
            ? await CardGameResultDocument(matchId, userId).GetSnapshotAsync(cancellationToken)
            : null;
        var standings = SolitaireCompetitionRules.Rank(graph.Players);
        var placement = graph.Match.Status == SettledMatchStatus
            ? standings.Select((player, index) => new { player.UserId, Placement = index + 1 })
                .First(value => value.UserId == userId).Placement
            : 0;
        var current = graph.Players.First(player => player.UserId == userId);
        return new SolitaireHistoryItemResponse(
            matchId,
            graph.Match.PlayerCount,
            SolitaireMoney.ToCredits(graph.Match.BuyInCents),
            SolitaireMoney.ToCredits(graph.Match.PrizePoolCents),
            placement,
            current.Game.Score,
            checked((int)Math.Ceiling((current.ElapsedMilliseconds ?? 0) / 1000d)),
            SolitaireMoney.ToCredits(current.PayoutCents),
            graph.Match.Status == SettledMatchStatus
                ? SolitaireMoney.ToCredits(current.PayoutCents - graph.Match.BuyInCents)
                : 0m,
            graph.Match.CompletedAtUtc,
            graph.Match.DisplayNames
                .Where((_, index) => graph.Match.PlayerIds[index] != userId)
                .ToArray())
        {
            Status = graph.Match.Status,
            StartedAtUtc = current.StartedAtUtc == DateTime.UnixEpoch
                ? graph.Match.StartedAtUtc : current.StartedAtUtc,
            Players = PlayerResponses(graph.Match, graph.Players, userId),
            ClaimStatus = resultSnapshot is null
                ? null : EmptyToNull(ReadString(resultSnapshot, "claimStatus")),
            CanClaim = resultSnapshot is not null &&
                ReadString(resultSnapshot, "claimStatus") == SolitaireClaimStatuses.Unclaimed &&
                ReadString(resultSnapshot, "settlementStatus") == "claimable"
        };
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
                    "Waiting for players...",
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
