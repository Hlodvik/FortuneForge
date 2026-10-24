using System.Globalization;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

internal static class CreditHoldemEscrow
{
    internal static readonly TimeSpan QueueLifetime = TimeSpan.FromMinutes(5);

    public static string Partition(string ruleId) => $"open-table-v4-buyin-{ruleId}";

    public static int? RequestedCents(decimal? credits, CreditHoldemTableRule rule)
    {
        if (credits is null) return null;
        if (credits < CreditHoldemMoney.ToCredits(rule.BigBlindCents) || credits > CreditHoldemMoney.ToCredits(rule.MaximumStackCents))
            throw new ArgumentOutOfRangeException(nameof(credits), "The buy-in is outside this table's limits.");
        var cents = credits.Value * CreditHoldemMoney.CentsPerCredit;
        if (cents != decimal.Truncate(cents) || cents < rule.BigBlindCents || cents > rule.MaximumStackCents)
            throw new ArgumentOutOfRangeException(nameof(credits), "Choose a whole-cent buy-in between the big blind and this table's stack limit.");
        return checked((int)cents);
    }

    public static int ReserveAmount(long balance, CreditHoldemTableRule rule, int? requestedCents)
    {
        var amount = requestedCents ?? checked((int)Math.Min(balance, rule.MaximumStackCents));
        if (amount < rule.BigBlindCents || amount > rule.MaximumStackCents)
        {
            if (requestedCents is null && balance < rule.BigBlindCents)
                throw new CreditHoldemInsufficientCreditsException(balance, rule.BigBlindCents);
            throw new ArgumentOutOfRangeException(nameof(requestedCents), "The buy-in must be between the big blind and this table's stack limit.");
        }
        if (balance < amount) throw new CreditHoldemInsufficientCreditsException(balance, amount);
        return amount;
    }

    public static string JoinDetail(int version, string ruleId, int? requestedCents) =>
        $"{version}:{ruleId}:buy-in:{requestedCents?.ToString(CultureInfo.InvariantCulture) ?? "auto"}";

    public static bool Expired(CreditHoldemTicket ticket, DateTime nowUtc) =>
        ticket.BuyInCents > 0 && ticket.Status is "queued" or "pending-next-hand" &&
        nowUtc >= ticket.JoinedAtUtc.Add(QueueLifetime);

    public static bool CanAcceptTakeover(CreditHoldemMatch match) =>
        match.UsesBuyInEscrow && match.Status is "active" or "completed" &&
        match.Players.Any(player => !match.LeavingActorIds.Contains(player.ActorId)) &&
        match.Players.Count(player => player.IsAccountBacked && !match.LeavingActorIds.Contains(player.ActorId)) +
        match.PendingTakeovers.Count < CreditHoldemMoney.MaximumSeats;
}
