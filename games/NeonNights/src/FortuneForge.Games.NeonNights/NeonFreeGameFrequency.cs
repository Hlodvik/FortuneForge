using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

public static class NeonFreeGameFrequency
{
    public static ReelOutcome Apply(
        ReelOutcome outcome,
        GameDefinition game,
        IGameRandomSource random)
    {
        var freeGames = game.FreeGames;
        if (freeGames is null || freeGames.VisibleFrequencyDivisor <= 1)
        {
            return outcome;
        }

        var replacements = game.SpecialPoints?.CommonSymbolIds;
        if (replacements is null || replacements.Count == 0)
        {
            throw new InvalidOperationException(
                $"Game '{game.Id}' cannot reduce free-game frequency without replacement symbols.");
        }

        var reels = outcome.VisibleReels
            .Select(reel => reel
                .Select(symbol =>
                    string.Equals(symbol, freeGames.SymbolId, StringComparison.Ordinal) &&
                    random.Next(freeGames.VisibleFrequencyDivisor) != 0
                        ? replacements[random.Next(replacements.Count)]
                        : symbol)
                .ToArray())
            .Select(reel => (IReadOnlyList<string>)reel)
            .ToArray();

        return outcome with { VisibleReels = reels };
    }
}
