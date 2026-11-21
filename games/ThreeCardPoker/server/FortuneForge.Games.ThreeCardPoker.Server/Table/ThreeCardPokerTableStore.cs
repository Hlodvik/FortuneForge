using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Bots.ThreeCardPoker;

namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

internal interface IThreeCardPokerTableStore
{
    Task<ThreeCardPokerTableStoreResult> GetSessionAsync(string userId, DateTime nowUtc, CancellationToken cancellationToken);
    Task<ThreeCardPokerTableStoreResult> JoinAsync(
        string userId,
        string displayName,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<ThreeCardPokerTableStoreResult> CancelAsync(
        string userId,
        string ticketId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<ThreeCardPokerTableStoreResult> WagerAsync(
        string userId,
        string tableId,
        long wagerCents,
        long pairPlusCents,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<ThreeCardPokerTableStoreResult> SitOutAsync(
        string userId,
        string tableId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<ThreeCardPokerTableStoreResult> ActionAsync(
        string userId,
        string tableId,
        string action,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<ThreeCardPokerTableStoreResult> LeaveAsync(
        string userId,
        string tableId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task SweepAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<ThreeCardPokerTableHistoryItemResponse>> GetHistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken);
    Task<ThreeCardPokerTableHistoryItemResponse> MarkHistorySeenAsync(
        string userId,
        string resultId,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal static class ThreeCardPokerTableIds
{
    public static string Hash(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal static class ThreeCardPokerTableProjection
{
    public static ThreeCardPokerTableSessionResponse Session(
        ThreeCardPokerTableLobbyState state, string userId, DateTime nowUtc)
    {
        if (!state.Sessions.TryGetValue(userId, out var link) || link.Kind == ThreeCardPokerTableSessionKinds.Idle)
            return new ThreeCardPokerTableIdleSessionResponse();
        if (link.Kind == ThreeCardPokerTableSessionKinds.Queue && link.TicketId is { } ticketId)
        {
            var ticket = state.Tickets.SingleOrDefault(value => value.TicketId == ticketId && value.Status == "queued")
                ?? throw new ThreeCardPokerTableNotFoundException("This Three Card Poker queue ticket was not found.");
            var queued = state.Tickets.Where(value => value.Status == "queued").OrderBy(value => value.JoinedAtUtc)
                .ThenBy(value => value.TicketId, StringComparer.Ordinal).ToArray();
            var seats = queued.Select((value, index) => new ThreeCardPokerTableSeatResponse(
                value.PublicSeatId, value.DisplayName, index, "queued", 0, 0, 0, 0, 0, null, null,
                new([], null), value.UserId == userId, 0, 0)).ToArray();
            return new ThreeCardPokerTableQueueSessionResponse(ticket.TicketId,
                Array.FindIndex(queued, value => value.TicketId == ticketId) + 1,
                ticket.JoinedAtUtc, ticket.GraceEndsAtUtc, seats, ticket.Version);
        }
        if (link.Kind != ThreeCardPokerTableSessionKinds.Table || link.TableId is null ||
            !state.Tables.TryGetValue(link.TableId, out var table) || table.Players.All(player => player.ActorId != userId))
            throw new ThreeCardPokerTableNotFoundException("This Three Card Poker table was not found.");
        return new ThreeCardPokerTablePlaySessionResponse(Table(table, userId, nowUtc), table.Version);
    }

    public static ThreeCardPokerTableResponse Table(ThreeCardPokerTableState table, string userId, DateTime nowUtc)
    {
        var settled = table.Phase is ThreeCardPokerTablePhases.Settlement or ThreeCardPokerTablePhases.Settled;
        var reveal = settled || table.Phase == ThreeCardPokerTablePhases.Dealer && table.DealerVisibleCardCount == 3;
        var dealer = Hand(table.DealerCards, !reveal);
        var betting = table.Phase == ThreeCardPokerTablePhases.Betting;
        var actionDeadline = table.Players.Count == 1 && !ThreeCardPokerManagedSeat.IsManaged(table.Players[0])
            ? null
            : table.ActionDeadlineAtUtc;
        var seats = table.Players.OrderBy(player => player.Seat).Select(player => new ThreeCardPokerTableSeatResponse(
            player.PublicSeatId, player.DisplayName, player.Seat, player.Status,
            ThreeCardPokerMoney.ToRand(betting ? player.NextWagerCents : player.WagerCents),
            ThreeCardPokerMoney.ToRand(betting ? player.NextPairPlusCents : player.PairPlusCents),
            ThreeCardPokerMoney.ToRand(player.PlayWagerCents),
            ThreeCardPokerMoney.ToRand(betting ? checked(player.NextWagerCents + player.NextPairPlusCents) : player.TotalWagerCents),
            ThreeCardPokerMoney.ToRand(settled ? player.PayoutCents : 0), settled ? player.Outcome : null, player.LastAction,
            Hand(player.Cards, player.ActorId != userId && !reveal), player.ActorId == userId,
            ThreeCardPokerMoney.ToRand(settled ? player.AnteBonusPayoutCents : 0),
            ThreeCardPokerMoney.ToRand(settled ? player.PairPlusPayoutCents : 0))).ToArray();
        var viewer = table.Players.Single(player => player.ActorId == userId);
        return new ThreeCardPokerTableResponse(table.TableId, settled ? ThreeCardPokerTablePhases.Settled : table.Phase,
            table.RoundNumber, dealer, seats, table.ActiveSeat, ThreeCardPokerTableEngine.LegalActions(table, viewer),
            table.CreatedAtUtc, table.UpdatedAtUtc, actionDeadline, table.WagerDeadlineAtUtc,
            table.Transition, table.NextTransitionAtUtc,
            Remaining(actionDeadline, nowUtc), Remaining(table.WagerDeadlineAtUtc, nowUtc),
            Remaining(table.NextTransitionAtUtc, nowUtc));
    }

    private static long Remaining(DateTime? deadline, DateTime nowUtc) =>
        deadline is { } value ? Math.Max(0, (long)(value - nowUtc).TotalMilliseconds) : 0;
    private static ThreeCardPokerTableHandResponse Hand(IReadOnlyList<string> cards, bool hidden) => new(
        cards.Select(card => hidden ? new ThreeCardPokerTableCardResponse(null, null, true) : Card(card)).ToArray(),
        !hidden && cards.Count == 3 ? ThreeCardPokerRules.Evaluate(cards).Rank : null);
    private static ThreeCardPokerTableCardResponse Card(string code)
    {
        var value = ThreeCardPokerRules.ParseCard(code);
        return new(value.Rank, value.Suit, false);
    }
}
