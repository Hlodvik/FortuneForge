using FortuneForge.Games.Cards;

namespace FortuneForge.Games.TrickTaking;

public static class TrickTakingDealer
{
    public static TrickTakingDeal Deal(uint seed, PlayerSeat firstSeat = PlayerSeat.North)
    {
        var deck = StandardDeck.Create().ToArray();
        var random = new Mulberry32(seed);
        for (var index = deck.Length - 1; index > 0; index--)
        {
            var swapIndex = random.NextIndex(index + 1);
            (deck[index], deck[swapIndex]) = (deck[swapIndex], deck[index]);
        }

        var hands = Enumerable.Range(0, TrickTakingRules.PlayerCount)
            .Select(_ => new List<PlayingCard>(TrickTakingRules.CardsPerPlayer))
            .ToArray();
        for (var index = 0; index < deck.Length; index++)
            hands[index % hands.Length].Add(deck[index]);

        var frozen = hands
            .Select((hand, offset) => new PlayerHand(firstSeat.Advance(offset), hand.ToArray()))
            .ToArray();
        TrickTakingRules.ValidateDeal(frozen);
        return new TrickTakingDeal(seed, frozen);
    }

    private sealed class Mulberry32(uint seed)
    {
        private uint value = seed;

        public int NextIndex(int exclusiveMaximum) =>
            (int)(((ulong)NextUInt() * (uint)exclusiveMaximum) >> 32);

        private uint NextUInt()
        {
            unchecked
            {
                value += 0x6d2b79f5u;
                var result = value;
                result = (result ^ (result >> 15)) * (result | 1u);
                result ^= result + (result ^ (result >> 7)) * (result | 61u);
                return result ^ (result >> 14);
            }
        }
    }
}
