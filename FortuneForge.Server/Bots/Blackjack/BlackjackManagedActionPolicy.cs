using FortuneForge.Games.Blackjack;
using FortuneForge.Games.Cards;

namespace FortuneForge.Server.Bots.Blackjack;

internal static class BlackjackManagedActionPolicy
{
    public static string Choose(
        BlackjackTableState table,
        BlackjackTablePlayer player,
        IReadOnlyList<string> legalActions)
    {
        if (legalActions.Count == 0)
            throw new InvalidOperationException("A managed Blackjack profile cannot act without a legal action.");

        if (table.Phase == BlackjackTablePhases.Insurance)
            return BlackjackActions.DeclineInsurance;

        var skillLevel = BlackjackManagedSeat.SkillLevel(player);
        CardBotSkillLevels.Validate(skillLevel);
        var random = new DeterministicBotRandom(table.RoundSeed, $"blackjack:{table.Version}:{skillLevel}");
        if (skillLevel == CardBotSkillLevels.Poor)
        {
            if (legalActions.Contains(BlackjackActions.Hit) && random.NextDouble() < 0.72)
                return BlackjackActions.Hit;
            return random.Choose(legalActions);
        }

        var preferred = BasicStrategy(table, player, legalActions);
        var options = new CardBotGameOptions();
        var errorRate = skillLevel == CardBotSkillLevels.Average
            ? options.ThreeStarErrorRate
            : options.FourStarImperfectionRate;
        if (random.NextDouble() < errorRate)
            return random.Choose(legalActions);
        return legalActions.Contains(preferred)
            ? preferred
            : legalActions.Contains(BlackjackActions.Hit)
                ? BlackjackActions.Hit
                : legalActions[0];
    }

    private static string BasicStrategy(
        BlackjackTableState table,
        BlackjackTablePlayer player,
        IReadOnlyList<string> legalActions)
    {
        var cards = player.ActiveHandIndex == 0
            ? player.Cards
            : player.SecondaryHand?.Cards
              ?? throw new InvalidOperationException("The active split hand is missing.");
        var hand = BlackjackRules.Score(cards);
        var dealer = BlackjackRules.Score([table.DealerCards[0]]).Score;
        var canDouble = legalActions.Contains(BlackjackActions.Double);

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
