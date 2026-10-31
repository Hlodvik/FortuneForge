namespace FortuneForge.Games.AndarBahar.Server;

public static class AndarBaharOutcomeAudit
{
    public const string Version = "andar-bahar-outcome-v1";
    public static bool Verify(AndarBaharStoredRound stored, long actualReturnCents)
    {
        try
        {
            var round = stored.Round;
            var cards = round.Deals.Select(deal => deal.Card).Prepend(round.Joker).ToArray();
            if (round.Deals.Count is < 1 or > 49 || cards.Any(card => !Enum.IsDefined(card.Rank) || !Enum.IsDefined(card.Suit)) ||
                cards.Distinct().Count() != cards.Length || stored.StakeCents <= 0 || stored.StakeCents % 10 != 0 || !Enum.IsDefined(stored.Side)) return false;
            for (var index = 0; index < round.Deals.Count; index++)
            {
                var deal = round.Deals[index];
                if (deal.Number != index + 1 || deal.Side != (index % 2 == 0 ? AndarBaharSide.Andar : AndarBaharSide.Bahar) ||
                    (deal.Card.Rank == round.Joker.Rank) != (index == round.Deals.Count - 1)) return false;
            }
            var winner = round.Deals.Count % 2 == 1 ? AndarBaharSide.Andar : AndarBaharSide.Bahar;
            var expected = stored.Side != winner ? 0 : winner == AndarBaharSide.Andar
                ? checked(stored.StakeCents / 10 * 19) : checked(stored.StakeCents * 2);
            return actualReturnCents == expected && stored.BalanceAfterWagerCents >= 0 &&
                stored.BalanceAfterSettlementCents == checked(stored.BalanceAfterWagerCents + expected);
        }
        catch (ArithmeticException) { return false; }
    }
}
