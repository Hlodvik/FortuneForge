using FortuneForge.Games.Cards;
using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Games.Hearts;

public sealed record HeartsBotObservation(
    PlayerSeat Seat,
    IReadOnlyList<PlayingCard> Hand,
    IReadOnlyList<PlayingCard> LegalCards,
    HeartsPhase Phase,
    HeartsPassDirection PassDirection,
    TrickState CurrentTrick,
    bool HeartsBroken,
    IReadOnlyList<HeartsScore> Scores);

public sealed record HeartsBotPassDecision(IReadOnlyList<PlayingCard> Cards);

public sealed class HeartsBotAgent
{
    public HeartsBotPassDecision ChoosePass(
        HeartsBotObservation observation,
        int skillLevel,
        ulong seed,
        int version,
        CardBotGameOptions options)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(options);
        CardBotSkillLevels.Validate(skillLevel);
        if (observation.Hand.Count < 3)
            throw new InvalidOperationException("A Hearts bot cannot pass from an incomplete hand.");

        var random = new DeterministicBotRandom(seed, $"hearts-pass:{version}:{skillLevel}");
        var dangerous = observation.Hand
            .OrderByDescending(PassPriority)
            .ThenBy(_ => random.Next(10_000))
            .Take(3)
            .ToArray();
        return new HeartsBotPassDecision(dangerous);
    }

    public PlayingCard ChooseCard(
        HeartsBotObservation observation,
        int skillLevel,
        ulong seed,
        int version,
        CardBotGameOptions options)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(options);
        CardBotSkillLevels.Validate(skillLevel);
        if (observation.LegalCards.Count == 0)
            throw new InvalidOperationException("A Hearts bot cannot act without a legal card.");

        var random = new DeterministicBotRandom(seed, $"hearts-play:{version}:{skillLevel}");
        if (skillLevel == CardBotSkillLevels.Poor && random.NextDouble() < 0.25)
            return random.Choose(observation.LegalCards);

        return observation.LegalCards
            .OrderByDescending(card => HeartsRules.PointValue(card))
            .ThenByDescending(RankValue)
            .First();
    }

    private static int PassPriority(PlayingCard card) => card switch
    {
        { Suit: CardSuit.Spades, Rank: CardRank.Queen } => 1_000,
        { Suit: CardSuit.Hearts } => 700 + RankValue(card),
        _ => RankValue(card),
    };

    private static int RankValue(PlayingCard card) => card.Rank == CardRank.Ace ? 14 : (int)card.Rank;
}
