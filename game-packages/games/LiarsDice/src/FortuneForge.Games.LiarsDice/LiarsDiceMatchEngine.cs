using System.Collections.Immutable;
using FortuneForge.Games.Dice;

namespace FortuneForge.Games.LiarsDice;

public static class LiarsDiceMatchEngine
{
    public const int DefaultDicePerPlayer = 5;

    public static LiarsDiceMatchState Start(
        uint seed,
        IReadOnlyList<string>? playerIds = null,
        int dicePerPlayer = DefaultDicePerPlayer,
        LiarsDiceVariant variant = LiarsDiceVariant.ExactFace)
    {
        if (playerIds is null)
            throw new ArgumentNullException(nameof(playerIds), "The host must supply the table participants.");
        var players = playerIds.ToImmutableArray();
        ValidatePlayers(players, dicePerPlayer);
        var counts = players.ToImmutableDictionary(player => player, _ => dicePerPlayer, StringComparer.Ordinal);
        return new LiarsDiceMatchState(
            dicePerPlayer,
            players,
            counts,
            1,
            StartRound(players, counts, seed, variant),
            null,
            null);
    }

    public static LiarsDiceMatchTransition Apply(LiarsDiceMatchState state, LiarsDiceCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        if (state.Winner is not null)
            throw new LiarsDiceRuleException("The Liar's Dice match is already complete.");

        var transition = LiarsDiceEngine.Apply(state.Round, command);
        if (transition.Outcome is not { } outcome)
            return new LiarsDiceMatchTransition(state with { Round = transition.State }, null, $"{command.PlayerId} placed a bid.");

        var nextCounts = state.DiceCounts.SetItem(outcome.LoserId, state.DiceCounts[outcome.LoserId] - 1);
        var remaining = state.AllPlayerIds.Where(player => nextCounts[player] > 0).ToArray();
        var winner = remaining.Length == 1 ? remaining[0] : null;
        var next = state with
        {
            Round = transition.State,
            DiceCounts = nextCounts,
            LastOutcome = outcome,
            Winner = winner,
        };
        var message = winner is null
            ? $"{outcome.LoserId} loses a die. Start the next round."
            : $"{winner} wins the Liar's Dice match!";
        return new LiarsDiceMatchTransition(next, outcome, message);
    }

    public static LiarsDiceMatchState StartNextRound(LiarsDiceMatchState state, uint seed)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Winner is not null)
            throw new LiarsDiceRuleException("The Liar's Dice match is already complete.");
        if (state.Round.Phase != LiarsDiceRoundPhase.Resolved || state.LastOutcome is null)
            throw new LiarsDiceRuleException("The current Liar's Dice round must resolve before starting another.");

        var active = state.AllPlayerIds.Where(player => state.DiceCounts[player] > 0).ToImmutableArray();
        if (active.Length < 2)
            throw new LiarsDiceRuleException("At least two players are required for another round.");
        var starter = NextActivePlayer(state.Round.TurnOrder, state.LastOutcome.LoserId, active);
        return state with
        {
            RoundNumber = checked(state.RoundNumber + 1),
            Round = StartRound(active, state.DiceCounts, seed, state.Round.Variant, starter),
            LastOutcome = null,
        };
    }

    private static LiarsDiceRoundState StartRound(
        ImmutableArray<string> players,
        IReadOnlyDictionary<string, int> diceCounts,
        uint seed,
        LiarsDiceVariant variant,
        string? starter = null)
    {
        var hands = RollHands(players, diceCounts, seed);
        return LiarsDiceEngine.StartRound(players, hands, variant, starter);
    }

    private static IReadOnlyDictionary<string, IReadOnlyCollection<DieValue>> RollHands(
        ImmutableArray<string> players,
        IReadOnlyDictionary<string, int> diceCounts,
        uint seed)
    {
        var random = new DeterministicDiceRandom(seed);
        return players.ToDictionary(
            player => player,
            player => (IReadOnlyCollection<DieValue>)Enumerable.Range(0, diceCounts[player]).Select(_ => new DieValue(random.NextFace())).ToImmutableArray(),
            StringComparer.Ordinal);
    }

    private static string NextActivePlayer(
        ImmutableArray<string> priorOrder,
        string loserId,
        ImmutableArray<string> active)
    {
        var loserIndex = priorOrder.IndexOf(loserId);
        for (var offset = 1; offset <= priorOrder.Length; offset++)
        {
            var candidate = priorOrder[(loserIndex + offset) % priorOrder.Length];
            if (active.Contains(candidate))
                return candidate;
        }
        return active[0];
    }

    private static void ValidatePlayers(ImmutableArray<string> players, int dicePerPlayer)
    {
        if (players.Length < 2)
            throw new ArgumentException("Liar's Dice requires at least two players.", nameof(players));
        if (players.Any(string.IsNullOrWhiteSpace) || players.Distinct(StringComparer.Ordinal).Count() != players.Length)
            throw new ArgumentException("Liar's Dice player IDs must be unique and non-empty.", nameof(players));
        if (dicePerPlayer <= 0)
            throw new ArgumentOutOfRangeException(nameof(dicePerPlayer), "Each Liar's Dice player needs at least one die.");
    }

    private sealed class DeterministicDiceRandom(uint seed)
    {
        private uint value = seed;

        public int NextFace()
        {
            unchecked
            {
                value += 0x6d2b79f5u;
                var result = value;
                result = (result ^ (result >> 15)) * (result | 1u);
                result ^= result + (result ^ (result >> 7)) * (result | 61u);
                return (int)((result ^ (result >> 14)) % 6) + 1;
            }
        }
    }
}
