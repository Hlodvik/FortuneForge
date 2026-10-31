using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;
using FortuneForge.Server.Cards.Baccarat;

namespace FortuneForge.Server.RoundAudit;

internal static class BaccaratOutcomeAudit
{
    internal const string Version = "baccarat-outcome-v1";
    internal static bool Verify(BaccaratStoreRound stored, long actualWagerCents, long actualReturnCents)
    {
        try
        {
            var dealt = stored.Round.ConsumedCards;
            if (dealt.Length is < 4 or > 6 || dealt.Any(card => !Enum.IsDefined(card.Rank) || !Enum.IsDefined(card.Suit))) return false;
            var player = new List<PlayingCard> { dealt[0], dealt[2] };
            var banker = new List<PlayingCard> { dealt[1], dealt[3] };
            var p = player.Sum(Points) % 10; var b = banker.Sum(Points) % 10; var natural = p >= 8 || b >= 8; var next = 4;
            if (!natural && p <= 5)
            {
                if (next >= dealt.Length) return false;
                player.Add(dealt[next++]);
            }
            var third = player.Count == 3 ? Points(player[2]) : -1;
            // An independent tableau expressed as allowed third-card sets.
            var bankerDraw = third < 0 ? b <= 5 : b <= 2 || b == 3 && third != 8 ||
                b == 4 && "234567".Contains((char)('0' + third)) ||
                b == 5 && "4567".Contains((char)('0' + third)) || b == 6 && third is 6 or 7;
            if (!natural && bankerDraw)
            {
                if (next >= dealt.Length) return false;
                banker.Add(dealt[next++]);
            }
            if (next != dealt.Length || natural != stored.Round.EndedOnNatural ||
                !player.SequenceEqual(stored.Round.PlayerHand.Cards) || !banker.SequenceEqual(stored.Round.BankerHand.Cards)) return false;
            p = player.Sum(Points) % 10; b = banker.Sum(Points) % 10;
            var outcome = p == b ? BaccaratRoundOutcome.Tie : p > b ? BaccaratRoundOutcome.Player : BaccaratRoundOutcome.Banker;
            if (stored.Round.Outcome != outcome || stored.Settlement.RoundOutcome != outcome || stored.Settlement.BetSide != stored.BetSide || !Enum.IsDefined(stored.BetSide)) return false;
            var stake = stored.Settlement.Stake;
            if (stake <= 0 || stored.PlayerPairSettlement.Stake < 0 || stored.BankerPairSettlement.Stake < 0) return false;
            var win = stored.BetSide.ToString() == outcome.ToString();
            var main = win ? stored.BetSide switch
            {
                BaccaratBetSide.Banker => decimal.Round(stake * 195 / 100, 2, MidpointRounding.AwayFromZero),
                BaccaratBetSide.Tie => stake * 9, _ => stake * 2
            } : outcome == BaccaratRoundOutcome.Tie && stored.BetSide != BaccaratBetSide.Tie ? stake : 0;
            var pairPlayer = player[0].Rank == player[1].Rank ? stored.PlayerPairSettlement.Stake * 12 : 0;
            var pairBanker = banker[0].Rank == banker[1].Rank ? stored.BankerPairSettlement.Stake * 12 : 0;
            var disposition = win ? BaccaratBetDisposition.Win : outcome == BaccaratRoundOutcome.Tie && stored.BetSide != BaccaratBetSide.Tie
                ? BaccaratBetDisposition.Push : BaccaratBetDisposition.Loss;
            return stored.Settlement.TotalReturn == main && stored.Settlement.Profit == main - stake && stored.Settlement.Disposition == disposition &&
                PairMatches(stored.PlayerPairSettlement, pairPlayer) && PairMatches(stored.BankerPairSettlement, pairBanker) && stored.TotalStake * 100 == actualWagerCents &&
                (main + pairPlayer + pairBanker) * 100 == actualReturnCents;
        }
        catch (ArithmeticException) { return false; }
    }
    private static int Points(PlayingCard card) => (int)card.Rank < 10 ? (int)card.Rank : 0;
    private static bool PairMatches(BaccaratPairSettlement pair, decimal expected) => pair.TotalReturn == expected && pair.Profit == expected - pair.Stake &&
        pair.Disposition == (pair.Stake == 0 ? BaccaratBetDisposition.NotPlaced : expected > 0 ? BaccaratBetDisposition.Win : BaccaratBetDisposition.Loss);
}
