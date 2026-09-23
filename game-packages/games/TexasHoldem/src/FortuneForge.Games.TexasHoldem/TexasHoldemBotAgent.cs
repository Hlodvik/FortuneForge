using FortuneForge.Games.Cards;

namespace FortuneForge.Games.TexasHoldem;

public static class HoldemActions
{
    public const string Fold = "fold";
    public const string Check = "check";
    public const string Call = "call";
    public const string Raise = "raise";
}

public sealed record TexasHoldemBotObservation(
    IReadOnlyList<string> HoleCards,
    IReadOnlyList<string> CommunityCards,
    int Pot,
    int AmountToCall,
    int Stack,
    int MinimumRaiseTo,
    int MaximumRaiseTo,
    IReadOnlyList<string> LegalActions);

public sealed record TexasHoldemBotDecision(string Action, int? RaiseTo);

public sealed class TexasHoldemBotAgent
{
    public TexasHoldemBotDecision Choose(
        TexasHoldemBotObservation observation,
        int skillLevel,
        ulong seed,
        int version,
        CardBotGameOptions options)
    {
        CardBotSkillLevels.Validate(skillLevel);
        if (observation.LegalActions.Count == 0)
            throw new InvalidOperationException("A Hold'em bot cannot act without a legal action.");
        var random = new DeterministicBotRandom(seed, $"holdem:{version}:{skillLevel}");
        if (skillLevel == CardBotSkillLevels.Poor)
        {
            var action = random.Choose(observation.LegalActions);
            if (action == HoldemActions.Raise)
            {
                return new TexasHoldemBotDecision(action, Math.Min(
                    observation.MinimumRaiseTo + random.Next(Math.Max(1, observation.Stack / 3)),
                    observation.MaximumRaiseTo));
            }
            return new TexasHoldemBotDecision(action, null);
        }

        var errorRate = skillLevel == CardBotSkillLevels.Average
            ? options.ThreeStarErrorRate
            : options.FourStarImperfectionRate;
        if (random.NextDouble() < errorRate)
            return RandomDecision(observation, random);

        var strength = Strength(observation);
        var potOdds = observation.AmountToCall <= 0
            ? 0
            : observation.AmountToCall / (double)(observation.Pot + observation.AmountToCall);
        var raiseThreshold = skillLevel == CardBotSkillLevels.Strong ? 0.72 : 0.82;
        if (strength >= raiseThreshold && observation.LegalActions.Contains(HoldemActions.Raise))
        {
            var raiseTo = Math.Min(
                observation.MaximumRaiseTo,
                Math.Max(observation.MinimumRaiseTo, observation.AmountToCall + Math.Max(20, observation.Pot / 2)));
            return new TexasHoldemBotDecision(HoldemActions.Raise, raiseTo);
        }
        if (observation.AmountToCall == 0)
            return new TexasHoldemBotDecision(HoldemActions.Check, null);
        if (strength + (skillLevel == CardBotSkillLevels.Strong ? 0.04 : -0.03) >= potOdds)
            return new TexasHoldemBotDecision(HoldemActions.Call, null);
        return new TexasHoldemBotDecision(HoldemActions.Fold, null);
    }

    public static double Strength(TexasHoldemBotObservation observation)
    {
        var all = observation.HoleCards.Concat(observation.CommunityCards).ToArray();
        if (all.Length >= 5)
        {
            var value = TexasHoldemRules.Evaluate(all);
            return Math.Min(0.98, 0.16 + value.Category * 0.105 + HighCardBonus(observation.HoleCards));
        }
        var cards = observation.HoleCards.Select(TexasHoldemRules.Parse).ToArray();
        var pair = cards[0].Rank == cards[1].Rank;
        var suited = cards[0].Suit == cards[1].Suit;
        var high = Math.Max(cards[0].Rank, cards[1].Rank);
        var low = Math.Min(cards[0].Rank, cards[1].Rank);
        var connected = Math.Abs(cards[0].Rank - cards[1].Rank) <= 2;
        return Math.Clamp(
            0.15 + high / 28.0 + (pair ? 0.25 + low / 50.0 : 0) + (suited ? 0.06 : 0) + (connected ? 0.04 : 0),
            0.05,
            0.95);
    }

    private static double HighCardBonus(IReadOnlyList<string> hole) =>
        hole.Select(TexasHoldemRules.Parse).Max(card => card.Rank) / 100.0;

    private static TexasHoldemBotDecision RandomDecision(
        TexasHoldemBotObservation observation,
        DeterministicBotRandom random)
    {
        var action = random.Choose(observation.LegalActions);
        return action == HoldemActions.Raise
            ? new TexasHoldemBotDecision(action, observation.MaximumRaiseTo)
            : new TexasHoldemBotDecision(action, null);
    }
}
