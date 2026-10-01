using FortuneForge.Games.TexasHoldem;

namespace FortuneForge.Server.Bots.TexasHoldem;

internal sealed record TexasHoldemManagedDecision(string Action, int? RaiseTo);

internal static class TexasHoldemManagedActionPolicy
{
    public static TexasHoldemManagedDecision Choose(
        CreditHoldemMatch match,
        CreditHoldemPlayer player,
        int skillLevel)
    {
        ManagedPlayerSkillLevels.Validate(skillLevel);
        var legalActions = CreditHoldemEngine.LegalActions(match, player);
        if (legalActions.Count == 0)
            throw new InvalidOperationException("A managed Hold'em profile cannot act without a legal action.");
        var random = new DeterministicManagedPlayerRandom(match.DealSeed, $"holdem:{player.ActorId}:{match.Version}:{skillLevel}");
        if (skillLevel == ManagedPlayerSkillLevels.Poor)
            return RandomDecision(match, player, legalActions, random);

        var options = new ManagedPlayerDecisionOptions();
        var errorRate = skillLevel == ManagedPlayerSkillLevels.Average
            ? options.AverageErrorRate
            : options.StrongImperfectionRate;
        if (random.NextDouble() < errorRate)
            return RandomDecision(match, player, legalActions, random);

        var strength = Strength(player.HoleCards, match.Community);
        var amountToCall = Math.Max(0, match.CurrentBet - player.CommittedRound);
        var pot = match.Players.Sum(value => value.CommittedHand);
        var potOdds = amountToCall <= 0 ? 0 : amountToCall / (double)(pot + amountToCall);
        var raiseThreshold = skillLevel == ManagedPlayerSkillLevels.Strong ? 0.72 : 0.82;
        if (strength >= raiseThreshold && legalActions.Contains(CreditHoldemActions.Raise))
        {
            var raiseTo = Math.Min(
                player.CommittedRound + player.Stack,
                Math.Max(match.CurrentBet + match.MinimumRaise, amountToCall + Math.Max(20, pot / 2)));
            return new(CreditHoldemActions.Raise, raiseTo);
        }
        if (amountToCall == 0) return new(CreditHoldemActions.Check, null);
        if (strength + (skillLevel == ManagedPlayerSkillLevels.Strong ? 0.04 : -0.03) >= potOdds)
            return new(CreditHoldemActions.Call, null);
        return new(CreditHoldemActions.Fold, null);
    }

    private static double Strength(IReadOnlyList<string> hole, IReadOnlyList<string> community)
    {
        var all = hole.Concat(community).ToArray();
        if (all.Length >= 5)
        {
            var value = TexasHoldemRules.Evaluate(all);
            return Math.Min(0.98, 0.16 + value.Category * 0.105 +
                hole.Select(TexasHoldemRules.Parse).Max(card => card.Rank) / 100.0);
        }
        var cards = hole.Select(TexasHoldemRules.Parse).ToArray();
        var pair = cards[0].Rank == cards[1].Rank;
        var suited = cards[0].Suit == cards[1].Suit;
        var high = Math.Max(cards[0].Rank, cards[1].Rank);
        var low = Math.Min(cards[0].Rank, cards[1].Rank);
        var connected = Math.Abs(cards[0].Rank - cards[1].Rank) <= 2;
        return Math.Clamp(
            0.15 + high / 28.0 + (pair ? 0.25 + low / 50.0 : 0) +
            (suited ? 0.06 : 0) + (connected ? 0.04 : 0),
            0.05,
            0.95);
    }

    private static TexasHoldemManagedDecision RandomDecision(
        CreditHoldemMatch match,
        CreditHoldemPlayer player,
        IReadOnlyList<string> legalActions,
        DeterministicManagedPlayerRandom random)
    {
        var action = random.Choose(legalActions);
        return action == CreditHoldemActions.Raise
            ? new(action, player.CommittedRound + player.Stack)
            : new(action, null);
    }
}
