namespace FortuneForge.Games.Solitaire;

internal static class CompetitiveSolitaireBotSimulation
{
    private static readonly string[] Names =
    [
        "Avery", "Blake", "Casey", "Drew", "Emery", "Finley", "Gray"
    ];

    public static string DisplayName(int seat) => Names[(seat - 1) % Names.Length];

    public static int Skill(int seat) => 2 + ((seat - 1) % 3);

    public static SolitaireGameState Play(uint dealSeed, int drawCount, int seat, int skill)
    {
        var game = SolitaireEngine.CreateGame(dealSeed, drawCount);
        var random = new StableRandom(((ulong)dealSeed << 32) | (uint)seat);
        var maximumCommands = skill switch
        {
            2 => 72,
            3 => 120,
            _ => 168
        };

        for (var version = 1; version <= maximumCommands && !SolitaireEngine.IsWon(game); version++)
        {
            var legal = SolitaireBotAgent.LegalCommands(game, version);
            if (legal.Count == 0) break;
            SolitaireCandidate selected;
            if (skill == 2 || (skill == 3 && version % 4 == 0))
            {
                selected = legal[random.Next(legal.Count)];
            }
            else
            {
                selected = legal
                    .OrderByDescending(candidate => candidate.Result.Score)
                    .ThenByDescending(candidate => candidate.Result.Foundations.Sum(pile => pile.Count))
                    .ThenBy(candidate => candidate.Result.Tableau.Sum(pile => pile.Count(card => !card.FaceUp)))
                    .ThenBy(candidate => Key(candidate.Command), StringComparer.Ordinal)
                    .First();
            }
            game = selected.Result;
        }

        return game with { Message = SolitaireEngine.IsWon(game) ? "Game complete" : "Time expired" };
    }

    public static long ElapsedMilliseconds(uint dealSeed, int seat, int skill)
    {
        var spread = (long)((dealSeed + (uint)(seat * 7_919)) % 90_000);
        return Math.Min(599_000, 420_000 - (skill * 30_000L) + spread);
    }

    private static string Key(SolitaireCommandRequest value) =>
        $"{value.Type}:{value.From?.Zone}:{value.From?.Index}:{value.StartIndex}:{value.To?.Zone}:{value.To?.Index}:{value.Column}";

    private struct StableRandom
    {
        private ulong state;

        public StableRandom(ulong seed)
        {
            state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        public int Next(int maximum)
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return (int)((state * 2685821657736338717UL) % (uint)maximum);
        }
    }
}
