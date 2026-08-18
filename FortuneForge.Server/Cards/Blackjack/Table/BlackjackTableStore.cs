using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Server.Cards.Blackjack.Table;

internal interface IBlackjackTableStore
{
    Task<BlackjackTableStoreResult> GetSessionAsync(string userId, DateTime nowUtc, CancellationToken cancellationToken);
    Task<BlackjackTableStoreResult> JoinAsync(
        string userId,
        string displayName,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<BlackjackTableStoreResult> CancelAsync(
        string userId,
        string ticketId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<BlackjackTableStoreResult> WagerAsync(
        string userId,
        string tableId,
        long wagerCents,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<BlackjackTableStoreResult> ActionAsync(
        string userId,
        string tableId,
        string action,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<BlackjackTableStoreResult> LeaveAsync(
        string userId,
        string tableId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task SweepAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<BlackjackTableHistoryItemResponse>> GetHistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken);
    Task<BlackjackTableHistoryItemResponse> MarkHistorySeenAsync(
        string userId,
        string resultId,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal static class BlackjackTableIds
{
    public static string Hash(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal static class BlackjackTableProjection
{
    public static BlackjackTableSessionResponse Session(
        BlackjackTableLobbyState state,
        string userId,
        DateTime nowUtc)
    {
        if (!state.Sessions.TryGetValue(userId, out var link) || link.Kind == BlackjackTableSessionKinds.Idle)
            return new BlackjackTableIdleSessionResponse();
        if (link.Kind == BlackjackTableSessionKinds.Queue && link.TicketId is { } ticketId)
        {
            var ticket = state.Tickets.SingleOrDefault(value => value.TicketId == ticketId && value.Status == "queued")
                ?? throw new BlackjackTableNotFoundException("The Blackjack table queue ticket was not found.");
            var queued = state.Tickets.Where(value => value.Status == "queued")
                .OrderBy(value => value.JoinedAtUtc)
                .ThenBy(value => value.TicketId, StringComparer.Ordinal)
                .ToArray();
            var seats = queued.Select((value, index) => new BlackjackTableSeatResponse(
                value.PublicSeatId,
                value.DisplayName,
                index,
                "queued",
                0,
                0,
                0,
                null,
                null,
                EmptyHand(),
                value.UserId == userId,
                [],
                0,
                0)).ToArray();
            return new BlackjackTableQueueSessionResponse(
                ticket.TicketId,
                Array.FindIndex(queued, value => value.TicketId == ticketId) + 1,
                ticket.JoinedAtUtc,
                ticket.GraceEndsAtUtc,
                seats,
                ticket.Version);
        }
        if (link.Kind != BlackjackTableSessionKinds.Table || link.TableId is null ||
            !state.Tables.TryGetValue(link.TableId, out var table) ||
            table.Players.All(player => player.ActorId != userId))
            throw new BlackjackTableNotFoundException("The Blackjack table was not found.");
        return new BlackjackTablePlaySessionResponse(Table(table, userId, nowUtc), table.Version);
    }

    public static BlackjackTableResponse Table(BlackjackTableState table, string userId, DateTime nowUtc)
    {
        var visibleDealerCards = Math.Clamp(table.DealerVisibleCardCount, 0, table.DealerCards.Count);
        var dealerCards = table.DealerCards.Select((card, index) => index >= visibleDealerCards
            ? new BlackjackTableCardResponse(null, null, true)
            : Card(card)).ToArray();
        var dealerValue = visibleDealerCards == table.DealerCards.Count && table.DealerCards.Count > 0
            ? BlackjackRules.Score(table.DealerCards)
            : null;
        var hideDealerResolution = table.Phase == BlackjackTablePhases.Dealer &&
                                   visibleDealerCards < table.DealerCards.Count;
        var dealer = new BlackjackTableHandResponse(
            dealerCards,
            dealerValue?.Score,
            dealerValue?.Soft ?? false,
            dealerValue?.Blackjack ?? false,
            dealerValue?.Bust ?? false);
        var seats = table.Players.OrderBy(player => player.Seat).Select(player =>
        {
            var hand = PlayerHand(player.Cards);
            var primaryStatus = hideDealerResolution && player.Status == "completed" ? "waiting" : player.Status;
            var displayedWager = table.Phase == BlackjackTablePhases.Betting
                ? player.NextWagerCents
                : player.WagerCents;
            var hands = new List<BlackjackTablePlayerHandResponse>
            {
                new(
                    1,
                    hand,
                    BlackjackMoney.ToRand(player.WagerCents),
                    BlackjackMoney.ToRand(player.TotalWagerCents),
                    BlackjackMoney.ToRand(hideDealerResolution ? 0 : BlackjackTableEngine.PrimaryPayoutFor(player)),
                    primaryStatus,
                    hideDealerResolution ? null : player.Outcome,
                    player.LastAction,
                    table.Phase == BlackjackTablePhases.Active &&
                    table.ActiveSeat == player.Seat && player.ActiveHandIndex == 0)
            };
            if (player.SecondaryHand is { } secondary)
            {
                hands.Add(new(
                    2,
                    PlayerHand(secondary.Cards),
                    BlackjackMoney.ToRand(secondary.WagerCents),
                    BlackjackMoney.ToRand(secondary.TotalWagerCents),
                    BlackjackMoney.ToRand(hideDealerResolution ? 0 : BlackjackTableEngine.SecondaryPayoutFor(player)),
                    hideDealerResolution && secondary.Status == "completed" ? "waiting" : secondary.Status,
                    hideDealerResolution ? null : secondary.Outcome,
                    secondary.LastAction,
                    table.Phase == BlackjackTablePhases.Active &&
                    table.ActiveSeat == player.Seat && player.ActiveHandIndex == 1));
            }
            return new BlackjackTableSeatResponse(
                player.PublicSeatId,
                player.DisplayName,
                player.Seat,
                primaryStatus,
                BlackjackMoney.ToRand(displayedWager),
                BlackjackMoney.ToRand(BlackjackTableEngine.TotalCommitted(player)),
                BlackjackMoney.ToRand(hideDealerResolution ? 0 : player.PayoutCents),
                hideDealerResolution ? null : player.Outcome,
                player.LastAction,
                hand,
                player.ActorId == userId,
                hands,
                BlackjackMoney.ToRand(player.InsuranceWagerCents),
                BlackjackMoney.ToRand(hideDealerResolution ? 0 : player.InsurancePayoutCents));
        }).ToArray();
        var viewer = table.Players.Single(player => player.ActorId == userId);
        var legal = BlackjackTableEngine.LegalActions(table, viewer);
        return new BlackjackTableResponse(
            table.TableId,
            table.Phase,
            table.RoundNumber,
            dealer,
            seats,
            table.ActiveSeat,
            legal,
            table.CreatedAtUtc,
            table.UpdatedAtUtc,
            table.ActionDeadlineAtUtc,
            table.WagerDeadlineAtUtc,
            table.Transition,
            table.NextTransitionAtUtc,
            table.ActionDeadlineAtUtc is { } actionDeadline
                ? Math.Max(0, (long)(actionDeadline - nowUtc).TotalMilliseconds)
                : 0,
            table.WagerDeadlineAtUtc is { } wagerDeadline
                ? Math.Max(0, (long)(wagerDeadline - nowUtc).TotalMilliseconds)
                : 0,
            table.NextTransitionAtUtc is { } transitionDeadline
                ? Math.Max(0, (long)(transitionDeadline - nowUtc).TotalMilliseconds)
                : 0);
    }

    private static BlackjackTableHandResponse EmptyHand() => new([], null, false, false, false);

    private static BlackjackTableHandResponse PlayerHand(IReadOnlyList<string> cards)
    {
        var value = cards.Count > 0 ? BlackjackRules.Score(cards) : null;
        return new(
            cards.Select(Card).ToArray(),
            value?.Score,
            value?.Soft ?? false,
            value?.Blackjack ?? false,
            value?.Bust ?? false);
    }

    private static BlackjackTableCardResponse Card(string code)
    {
        var value = BlackjackRules.ParseCard(code);
        return new BlackjackTableCardResponse(value.Rank, value.Suit, false);
    }
}
