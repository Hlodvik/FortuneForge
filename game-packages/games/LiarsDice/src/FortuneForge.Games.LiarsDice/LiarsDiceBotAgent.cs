using System.Collections.Immutable;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Dice;

namespace FortuneForge.Games.LiarsDice;

public sealed class LiarsDiceBotAgent
{
    public LiarsDiceBotDecision ChooseDecision(
        LiarsDiceBotObservation observation,
        int skillLevel,
        ulong seed,
        int version,
        CardBotGameOptions options)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(options);
        CardBotSkillLevels.Validate(skillLevel);

        if (observation.CurrentBid is null)
        {
            var face = MostCommonFace(observation.Hand);
            return new LiarsDiceBotDecision(false, new LiarsDiceBid(1, face));
        }

        var bid = observation.CurrentBid;
        var ownMatches = observation.Hand.Count(die => die == bid.Face);
        var opponents = observation.TotalDiceInPlay - observation.Hand.Length;
        var expected = ownMatches + ((opponents + 5) / 6);
        var challengeMargin = skillLevel == CardBotSkillLevels.Poor ? 2 : 1;
        if (bid.Quantity > expected + challengeMargin || bid.Quantity == observation.TotalDiceInPlay && bid.Face.Value == 6)
            return new LiarsDiceBotDecision(true, null);

        return new LiarsDiceBotDecision(false, Raise(bid, observation.TotalDiceInPlay));
    }

    private static DieValue MostCommonFace(ImmutableArray<DieValue> hand) => hand
        .GroupBy(die => die.Value)
        .OrderByDescending(group => group.Count())
        .ThenByDescending(group => group.Key)
        .Select(group => new DieValue(group.Key))
        .First();

    internal static LiarsDiceBid Raise(LiarsDiceBid current, int totalDice)
    {
        var next = current.Face.Value < 6
            ? new LiarsDiceBid(current.Quantity, new DieValue(current.Face.Value + 1))
            : new LiarsDiceBid(current.Quantity + 1, new DieValue(1));
        if (next.Quantity > totalDice)
            throw new LiarsDiceRuleException("The current bid cannot be raised; it must be challenged.");
        return next;
    }
}
