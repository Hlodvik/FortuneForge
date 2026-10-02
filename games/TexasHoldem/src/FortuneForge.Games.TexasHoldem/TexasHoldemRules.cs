using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.TexasHoldem;

public static class TexasHoldemRules
{
    private static readonly string[] Suits = ["clubs", "diamonds", "hearts", "spades"];
    private static readonly string[] Ranks = ["2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A"];

    public static IReadOnlyList<string> CreateDeck(ulong seed)
    {
        var deck = Suits.SelectMany(suit => Ranks.Select(rank => $"{rank}|{suit}")).ToArray();
        var random = new DeterministicGameRandom(seed, "holdem-deck-v1");
        for (var index = deck.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (deck[index], deck[swap]) = (deck[swap], deck[index]);
        }
        return deck;
    }

    public static HoldemHandValue Evaluate(IReadOnlyList<string> cards) => TexasHoldemHandEvaluator.Evaluate(cards);

    public static (int Rank, string Suit) Parse(string card)
    {
        var parsed = CardCode.Parse(card);
        var rank = parsed.Rank == CardRank.Ace ? 14 : (int)parsed.Rank;
        return (rank, card[(card.IndexOf('|') + 1)..]);
    }

    private sealed class DeterministicGameRandom(ulong seed, string stream)
    {
        private ulong counter;

        public int Next(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            return (int)(NextUInt64() % (uint)exclusiveMaximum);
        }

        private ulong NextUInt64()
        {
            Span<byte> input = stackalloc byte[16];
            BinaryPrimitives.WriteUInt64LittleEndian(input, seed);
            BinaryPrimitives.WriteUInt64LittleEndian(input[8..], counter++);
            var streamBytes = Encoding.UTF8.GetBytes(stream);
            var payload = new byte[input.Length + streamBytes.Length];
            input.CopyTo(payload);
            streamBytes.CopyTo(payload.AsSpan(input.Length));
            return BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(payload));
        }
    }
}
