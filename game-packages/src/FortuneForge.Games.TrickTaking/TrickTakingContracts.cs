using FortuneForge.Games.Cards;

namespace FortuneForge.Games.TrickTaking;

public enum PlayerSeat
{
    North,
    East,
    South,
    West,
}

public sealed record PlayerHand(PlayerSeat Seat, IReadOnlyList<PlayingCard> Cards);

public sealed record TrickPlay(PlayerSeat Seat, PlayingCard Card);

public sealed record TrickState(PlayerSeat Leader, IReadOnlyList<TrickPlay> Plays)
{
    public bool IsComplete => Plays.Count == TrickTakingRules.PlayerCount;

    public PlayerSeat NextPlayer => Leader.Advance(Plays.Count);

    public CardSuit? LeadSuit => Plays.Count == 0 ? null : Plays[0].Card.Suit;
}

public sealed record CompletedTrick(
    int Number,
    PlayerSeat Leader,
    PlayerSeat Winner,
    IReadOnlyList<TrickPlay> Plays);

public sealed record TrickTakingDeal(uint Seed, IReadOnlyList<PlayerHand> Hands)
{
    public IReadOnlyList<PlayingCard> HandFor(PlayerSeat seat) =>
        Hands.Single(hand => hand.Seat == seat).Cards;
}

public sealed class TrickTakingRuleException(string message) : InvalidOperationException(message);
