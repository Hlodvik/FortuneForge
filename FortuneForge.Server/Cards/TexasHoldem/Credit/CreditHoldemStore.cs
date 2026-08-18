using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

internal interface ICreditHoldemStore
{
    Task<CreditHoldemStoreResult> GetSessionAsync(string userId, DateTime nowUtc, CancellationToken cancellationToken);
    Task<CreditHoldemStoreResult> JoinAsync(
        string userId,
        string displayName,
        int expectedVersion,
        string idempotencyKey,
        ulong seed,
        DateTime nowUtc,
        CancellationToken cancellationToken,
        string tableRuleId = CreditHoldemTableRules.StandardId);
    Task<CreditHoldemStoreResult> CancelAsync(
        string userId,
        string ticketId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<CreditHoldemStoreResult> ActionAsync(
        string userId,
        string matchId,
        CreditHoldemActionRequest request,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<CreditHoldemStoreResult> NextHandAsync(
        string userId,
        string matchId,
        int expectedVersion,
        string idempotencyKey,
        ulong seed,
        DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<CreditHoldemStoreResult> LeaveAsync(
        string userId,
        string matchId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<CreditHoldemHistoryResponse> HistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken);

    Task<CreditHoldemHistoryItemResponse> MarkHistorySeenAsync(
        string userId,
        string eventId,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal static class CreditHoldemIds
{
    public static string Hash(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal static class CreditHoldemProjection
{
    public static CreditHoldemHistoryItemResponse History(CreditHoldemHistoryRecord record) => new(
        record.EventId,
        record.MatchId,
        record.HandNumber,
        record.Status,
        record.Seen,
        record.StartedAtUtc,
        record.CompletedAtUtc,
        CreditHoldemMoney.ToCredits(record.CommittedCents),
        CreditHoldemMoney.ToCredits(record.PayoutCents));

    public static CreditHoldemSessionResponse Match(CreditHoldemMatch match, string userId, DateTime nowUtc) =>
        new CreditHoldemMatchSessionResponse(Table(match, userId, nowUtc), match.Version);

    public static CreditHoldemSessionResponse Result(CreditHoldemMatch match, string userId, DateTime nowUtc)
    {
        var standings = match.Players
            .OrderByDescending(player => player.Stack)
            .ThenByDescending(player => player.WonHandChips)
            .ThenBy(player => player.Seat)
            .Select((player, index) => new CreditHoldemStandingResponse(
                index + 1,
                player.PublicSeatId,
                player.DisplayName,
                player.Stack,
                player.Status,
                CreditHoldemMoney.ToCredits(player.AccountPayoutCents),
                player.ActorId == userId))
            .ToArray();
        return
        new CreditHoldemResultSessionResponse(
            match.MatchId,
            match.HandNumber,
            CreditHoldemMoney.ToCredits(match.HumanCommittedCents),
            CreditHoldemMoney.ToCredits(match.HumanPayoutCents),
            CreditHoldemMoney.ToCredits(match.HouseNetCents),
            match.StartedAtUtc,
            match.CompletedAtUtc ?? match.UpdatedAtUtc,
            standings,
            Table(match, userId, nowUtc),
            match.Version);
    }

    public static CreditHoldemTableResponse Table(CreditHoldemMatch match, string userId, DateTime nowUtc)
    {
        var viewer = match.Players.Single(player => player.ActorId == userId);
        var seats = match.Players.Select(player =>
        {
            var reveal = player.ActorId == userId || player.RevealAtShowdown;
            var cards = player.HoleCards.Select(card => reveal
                ? Card(card)
                : new CreditHoldemCardResponse(null, null, true)).ToArray();
            var hand = player.RevealAtShowdown && match.Community.Count == 5
                ? TexasHoldemRules.Evaluate(player.HoleCards.Concat(match.Community).ToArray()).Name
                : null;
            return new CreditHoldemSeatResponse(
                player.PublicSeatId,
                player.DisplayName,
                player.Seat,
                player.StartingStack,
                player.Stack,
                player.CommittedHand,
                player.CommittedRound,
                player.Status,
                player.LastAction,
                cards,
                hand,
                player.ActorId == userId);
        }).ToArray();
        var legalActions = match.ActiveSeat == viewer.Seat ? CreditHoldemEngine.LegalActions(match, viewer) : [];
        var minimumRaiseTo = checked(match.CurrentBet + match.MinimumRaise);
        var maximumRaiseTo = match.ActiveSeat == viewer.Seat && viewer.Status == "active"
            ? checked(viewer.CommittedRound + viewer.Stack)
            : 0;
        var shortAllInRaiseTo = legalActions.Contains(CreditHoldemActions.Raise) &&
            maximumRaiseTo > match.CurrentBet && maximumRaiseTo < minimumRaiseTo
                ? maximumRaiseTo
                : (int?)null;
        var winners = match.Players.Where(player => player.WonHandChips > 0).OrderBy(player => player.Seat).ToArray();
        var rule = CreditHoldemTableRules.Resolve(match.TableRuleId);
        return new CreditHoldemTableResponse(
            match.MatchId,
            match.Status,
            match.Street,
            match.HandNumber,
            match.DealerSeat,
            match.ActiveSeat,
            match.Players.Sum(player => player.CommittedHand),
            match.CurrentBet,
            minimumRaiseTo,
            maximumRaiseTo,
            shortAllInRaiseTo,
            match.Community.Select(Card).ToArray(),
            seats,
            legalActions,
            winners.Select(player => player.PublicSeatId).ToArray(),
            winners.Sum(player => player.WonHandChips),
            match.StartedAtUtc,
            match.MatchDeadlineAtUtc,
            match.ActionDeadlineAtUtc,
            match.ActionDeadlineAtUtc is { } deadline
                ? Math.Max(0, (long)(deadline - nowUtc).TotalMilliseconds)
                : 0,
            rule.Public);
    }

    private static CreditHoldemCardResponse Card(string card)
    {
        var separator = card.IndexOf('|');
        return new CreditHoldemCardResponse(card[..separator], card[(separator + 1)..], false);
    }
}
