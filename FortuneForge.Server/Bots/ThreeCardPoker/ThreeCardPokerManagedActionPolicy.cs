using FortuneForge.Games.ThreeCardPoker;

namespace FortuneForge.Server.Bots.ThreeCardPoker;

internal static class ThreeCardPokerManagedActionPolicy
{
    public static string Choose(
        ThreeCardPokerTableState table,
        ThreeCardPokerTablePlayer player,
        IReadOnlyList<string> legalActions)
    {
        if (legalActions.Count == 0)
            throw new InvalidOperationException("A Three Card Poker profile cannot act without a legal action.");

        var skillLevel = ThreeCardPokerManagedSeat.SkillLevel(player);
        ManagedPlayerSkillLevels.Validate(skillLevel);
        var random = new DeterministicManagedPlayerRandom(
            table.RoundSeed, $"three-card-poker:{player.ActorId}:{table.RoundNumber}:{table.Version}:{skillLevel}");
        if (skillLevel == ManagedPlayerSkillLevels.Poor)
            return random.Choose(legalActions);

        var preferred = ShouldPlay(player.Cards) ? "play" : "fold";
        var options = new ManagedPlayerDecisionOptions();
        var errorRate = skillLevel == ManagedPlayerSkillLevels.Average
            ? options.AverageErrorRate
            : options.StrongImperfectionRate;
        if (random.NextDouble() < errorRate)
            return random.Choose(legalActions);
        return legalActions.Contains(preferred) ? preferred : legalActions[0];
    }

    // Only this player's cards are considered, never the hidden dealer's hand.
    internal static bool ShouldPlay(IReadOnlyList<string> cards) => ThreeCardPokerRules.ShouldPlay(cards);
}