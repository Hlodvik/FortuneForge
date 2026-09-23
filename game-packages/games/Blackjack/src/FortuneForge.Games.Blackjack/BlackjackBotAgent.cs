using FortuneForge.Games.Cards;

namespace FortuneForge.Games.Blackjack;

public sealed record BlackjackBotObservation(
    IReadOnlyList<string> OwnCards,
    string DealerUpCard,
    IReadOnlyList<string> LegalActions);

public sealed class BlackjackBotAgent
{
    public string Choose(
        BlackjackBotObservation observation,
        int skillLevel,
        ulong seed,
        int version,
        CardBotGameOptions options)
    {
        CardBotSkillLevels.Validate(skillLevel);
        if (observation.LegalActions.Count == 0)
            throw new InvalidOperationException("A Blackjack bot cannot act without a legal action.");

        var random = new DeterministicBotRandom(seed, $"blackjack:{version}:{skillLevel}");
        if (skillLevel == CardBotSkillLevels.Poor)
        {
            if (observation.LegalActions.Contains(BlackjackActions.Hit) && random.NextDouble() < 0.72)
                return BlackjackActions.Hit;
            return random.Choose(observation.LegalActions);
        }

        var preferred = BasicStrategy(observation);
        var errorRate = skillLevel == CardBotSkillLevels.Average
            ? options.ThreeStarErrorRate
            : options.FourStarImperfectionRate;
        if (random.NextDouble() < errorRate)
            return random.Choose(observation.LegalActions);
        return observation.LegalActions.Contains(preferred)
            ? preferred
            : observation.LegalActions.Contains(BlackjackActions.Hit)
                ? BlackjackActions.Hit
                : observation.LegalActions[0];
    }

    private static string BasicStrategy(BlackjackBotObservation observation)
    {
        var hand = BlackjackRules.Score(observation.OwnCards);
        var dealer = BlackjackRules.Score([observation.DealerUpCard]).Score;
        var canDouble = observation.LegalActions.Contains(BlackjackActions.Double);

        if (canDouble && !hand.Soft && hand.Score == 11) return BlackjackActions.Double;
        if (canDouble && !hand.Soft && hand.Score == 10 && dealer <= 9) return BlackjackActions.Double;
        if (canDouble && !hand.Soft && hand.Score == 9 && dealer is >= 3 and <= 6) return BlackjackActions.Double;
        if (hand.Soft)
        {
            if (hand.Score >= 19) return BlackjackActions.Stand;
            if (hand.Score == 18 && dealer is 2 or 7 or 8) return BlackjackActions.Stand;
            return BlackjackActions.Hit;
        }
        if (hand.Score >= 17) return BlackjackActions.Stand;
        if (hand.Score is >= 13 and <= 16 && dealer <= 6) return BlackjackActions.Stand;
        if (hand.Score == 12 && dealer is >= 4 and <= 6) return BlackjackActions.Stand;
        return BlackjackActions.Hit;
    }
}
