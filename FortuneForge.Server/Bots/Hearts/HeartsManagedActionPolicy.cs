using FortuneForge.Games.Cards;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Bots;

namespace FortuneForge.Server.Bots.Hearts;

internal static class HeartsManagedActionPolicy
{
    public static IReadOnlyList<PlayingCard> ChoosePass(
        HeartsState round,
        PlayerSeat seat,
        int skillLevel,
        ulong seed,
        int version)
    {
        ManagedPlayerSkillLevels.Validate(skillLevel);
        var hand = round.HandFor(seat);
        if (hand.Count < 3)
            throw new InvalidOperationException("A managed Hearts player cannot pass from an incomplete hand.");

        var random = new DeterministicManagedPlayerRandom(seed, $"hearts-pass:{version}:{skillLevel}");
        if (skillLevel == ManagedPlayerSkillLevels.Poor && random.NextDouble() < 0.35)
            return hand.OrderBy(_ => random.Next(10_000)).Take(3).ToArray();

        return hand
            .OrderByDescending(PassPriority)
            .ThenBy(_ => random.Next(10_000))
            .Take(3)
            .ToArray();
    }

    public static PlayingCard ChooseCard(
        HeartsState round,
        PlayerSeat seat,
        int skillLevel,
        ulong seed,
        int version)
    {
        ManagedPlayerSkillLevels.Validate(skillLevel);
        var legalCards = HeartsEngine.LegalCards(round, seat);
        if (legalCards.Count == 0)
            throw new InvalidOperationException("A managed Hearts player cannot act without a legal card.");

        var random = new DeterministicManagedPlayerRandom(seed, $"hearts-play:{version}:{skillLevel}");
        var personalityVariance = seat switch
        {
            PlayerSeat.East => 0.03,
            PlayerSeat.West => 0.10,
            _ => 0,
        };
        var errorRate = skillLevel switch
        {
            ManagedPlayerSkillLevels.Poor => 0.42,
            ManagedPlayerSkillLevels.Average => 0.10,
            _ => 0.01,
        } + personalityVariance;
        if (random.NextDouble() < errorRate)
            return random.Choose(legalCards);

        if (round.CurrentTrick.Plays.Count == 0)
        {
            var safeLeads = legalCards.Where(card => HeartsRules.PointValue(card) == 0).ToArray();
            var leads = safeLeads.Length > 0 ? safeLeads : legalCards;
            return seat == PlayerSeat.East
                ? leads.OrderByDescending(RankValue).First()
                : leads.OrderBy(RankValue).First();
        }

        var ledSuit = round.CurrentTrick.Plays[0].Card.Suit;
        var following = legalCards.Where(card => card.Suit == ledSuit).ToArray();
        if (following.Length == 0)
        {
            return legalCards
                .OrderByDescending(card => HeartsRules.PointValue(card))
                .ThenByDescending(RankValue)
                .First();
        }

        var winningRank = round.CurrentTrick.Plays
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

    private static int RankValue(PlayingCard card) =>
        card.Rank == CardRank.Ace ? 14 : (int)card.Rank;
}
