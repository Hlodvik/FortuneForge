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
        if (skillLevel == CardBotSkillLevels.Poor && random.NextDouble() < 0.35)
            return new HeartsBotPassDecision(observation.Hand.OrderBy(_ => random.Next(10_000)).Take(3).ToArray());
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
        var personalityVariance = observation.Seat switch
        {
            PlayerSeat.East => 0.03,
            PlayerSeat.West => 0.10,
            _ => 0,
        };
        var errorRate = skillLevel switch
        {
            CardBotSkillLevels.Poor => 0.42,
            CardBotSkillLevels.Average => 0.10,
            _ => 0.01,
        } + personalityVariance;
        if (random.NextDouble() < errorRate)
            return random.Choose(observation.LegalCards);

        if (observation.CurrentTrick.Plays.Count == 0)
        {
            var safeLeads = observation.LegalCards.Where(card => HeartsRules.PointValue(card) == 0).ToArray();
            var leads = safeLeads.Length > 0 ? safeLeads : observation.LegalCards;
            return observation.Seat == PlayerSeat.East
                ? leads.OrderByDescending(RankValue).First()
                : leads.OrderBy(RankValue).First();
        }

        var ledSuit = observation.CurrentTrick.Plays[0].Card.Suit;
        var following = observation.LegalCards.Where(card => card.Suit == ledSuit).ToArray();
        if (following.Length == 0)
        {
            return observation.LegalCards
                .OrderByDescending(card => HeartsRules.PointValue(card))
                .ThenByDescending(RankValue)
                .First();
        }

        var winningRank = observation.CurrentTrick.Plays
            .Where(play => play.Card.Suit == ledSuit)
            .Max(play => RankValue(play.Card));
        var safeDiscards = following.Where(card => RankValue(card) < winningRank).ToArray();
        return safeDiscards.Length > 0
            ? safeDiscards.OrderByDescending(RankValue).First()
            : following.OrderBy(RankValue).First();
    }

    private static int PassPriority(PlayingCard card) => card switch
    {
        { Suit: CardSuit.Spades, Rank: CardRank.Queen } => 1_000,
        { Suit: CardSuit.Hearts } => 700 + RankValue(card),
        _ => RankValue(card),
    };

    private static int RankValue(PlayingCard card) => card.Rank == CardRank.Ace ? 14 : (int)card.Rank;
}
