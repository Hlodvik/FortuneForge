using System.Collections.Immutable;
using FortuneForge.Games.Dice;

namespace FortuneForge.Games.LiarsDice;

public static class LiarsDiceEngine
{
    public static LiarsDiceRoundState StartRound(
        IEnumerable<string> turnOrder,
        IReadOnlyDictionary<string, IReadOnlyCollection<DieValue>> hands,
        LiarsDiceVariant variant = LiarsDiceVariant.ExactFace,
        string? startingPlayerId = null)
    {
        ArgumentNullException.ThrowIfNull(turnOrder);
        ArgumentNullException.ThrowIfNull(hands);
        if (!Enum.IsDefined(variant))
            throw new ArgumentOutOfRangeException(nameof(variant));

        var players = turnOrder.ToImmutableArray();
        if (players.Length < 2)
            throw new ArgumentException("Liar's Dice requires at least two players.", nameof(turnOrder));
        if (players.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Every player requires an ID.", nameof(turnOrder));
        if (players.Distinct(StringComparer.Ordinal).Count() != players.Length)
            throw new ArgumentException("Player IDs must be unique.", nameof(turnOrder));

        var copiedHands = players.ToImmutableDictionary(
            playerId => playerId,
            playerId => CopyHand(playerId, hands),
            StringComparer.Ordinal);
        if (hands.Keys.Any(playerId => !copiedHands.ContainsKey(playerId)))
            throw new ArgumentException("Hands cannot contain players outside the turn order.", nameof(hands));

        var startingIndex = startingPlayerId is null ? 0 : players.IndexOf(startingPlayerId);
        if (startingIndex < 0)
            throw new ArgumentException("The starting player must be in the turn order.", nameof(startingPlayerId));

        return new LiarsDiceRoundState(
            variant,
            players,
            copiedHands,
            startingIndex,
            null,
            null,
            LiarsDiceRoundPhase.Bidding,
            null);
    }

    public static LiarsDiceTransition Apply(LiarsDiceRoundState state, LiarsDiceCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        if (state.Phase != LiarsDiceRoundPhase.Bidding)
            throw new LiarsDiceRuleException("The round has already been resolved.");
        if (!string.Equals(command.PlayerId, state.CurrentPlayerId, StringComparison.Ordinal))
            throw new LiarsDiceRuleException("Only the current player may act.");

        return command switch
        {
            PlaceLiarsDiceBid placeBid => PlaceBid(state, placeBid),
            ChallengeLiarsDiceBid challenge => Challenge(state, challenge),
            _ => throw new ArgumentException("Unknown Liar's Dice command.", nameof(command)),
        };
    }

    private static LiarsDiceTransition PlaceBid(LiarsDiceRoundState state, PlaceLiarsDiceBid command)
    {
        command.Bid.Validate();
        var diceInPlay = state.Hands.Values.Sum(hand => hand.Length);
        if (command.Bid.Quantity > diceInPlay)
            throw new LiarsDiceRuleException("A bid cannot exceed the number of dice in play.");
        if (state.CurrentBid is not null && !command.Bid.IsHigherThan(state.CurrentBid))
            throw new LiarsDiceRuleException("A new bid must be higher than the current bid.");

        var next = state with
        {
            CurrentBid = command.Bid,
            CurrentBidderId = command.PlayerId,
            CurrentPlayerIndex = NextPlayerIndex(state),
        };
        return new LiarsDiceTransition(next, null);
    }

    private static LiarsDiceTransition Challenge(LiarsDiceRoundState state, ChallengeLiarsDiceBid command)
    {
        if (state.CurrentBid is null || state.CurrentBidderId is null)
            throw new LiarsDiceRuleException("A bid must be placed before it can be challenged.");

        var matchingDice = CountMatchingDice(state);
        var loserId = matchingDice >= state.CurrentBid.Quantity
            ? command.PlayerId
            : state.CurrentBidderId;
        var outcome = new LiarsDiceChallengeOutcome(
            command.PlayerId,
            state.CurrentBidderId,
            loserId,
            state.CurrentBid,
            matchingDice);
        var resolved = state with { Phase = LiarsDiceRoundPhase.Resolved, Outcome = outcome };
        return new LiarsDiceTransition(resolved, outcome);
    }

    private static ImmutableArray<DieValue> CopyHand(
        string playerId,
        IReadOnlyDictionary<string, IReadOnlyCollection<DieValue>> hands)
    {
        if (!hands.TryGetValue(playerId, out var hand) || hand is null || hand.Count == 0)
            throw new ArgumentException($"Player '{playerId}' requires at least one die.", nameof(hands));
        var copy = hand.ToImmutableArray();
        foreach (var die in copy)
            die.Validate();
        return copy;
    }

    private static int CountMatchingDice(LiarsDiceRoundState state) => state.Variant switch
    {
        LiarsDiceVariant.ExactFace => state.Hands.Values
            .SelectMany(hand => hand)
            .Count(die => die == state.CurrentBid!.Face),
        _ => throw new LiarsDiceRuleException("The Liar's Dice variant is not supported."),
    };

    private static int NextPlayerIndex(LiarsDiceRoundState state) =>
        (state.CurrentPlayerIndex + 1) % state.TurnOrder.Length;
}
