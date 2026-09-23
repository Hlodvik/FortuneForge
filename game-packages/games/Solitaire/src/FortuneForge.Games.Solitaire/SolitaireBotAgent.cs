using FortuneForge.Games.Cards;

namespace FortuneForge.Games.Solitaire;

public sealed class SolitaireBotAgent
{
    public SolitaireCommandRequest Choose(
        SolitaireGameState game,
        int expectedVersion,
        int skillLevel,
        ulong seed,
        CardBotGameOptions options)
    {
        CardBotSkillLevels.Validate(skillLevel);
        var legal = LegalCommands(game, expectedVersion);
        if (legal.Count == 0)
            throw new InvalidOperationException("No legal Solitaire command is available.");
        var random = new DeterministicBotRandom(seed, $"solitaire:{expectedVersion}:{skillLevel}");

        if (skillLevel == CardBotSkillLevels.Poor)
        {
            var draws = legal.Where(candidate => candidate.Command.Type == SolitaireCommandTypes.Draw).ToArray();
            return draws.Length > 0 && random.NextDouble() < 0.65
                ? random.Choose(draws).Command
                : random.Choose(legal).Command;
        }

        var errorRate = skillLevel == CardBotSkillLevels.Average
            ? options.ThreeStarErrorRate
            : options.FourStarImperfectionRate;
        if (random.NextDouble() < errorRate) return random.Choose(legal).Command;

        return legal
            .OrderByDescending(candidate => Score(game, candidate.Result, candidate.Command, skillLevel))
            .ThenBy(candidate => StableCommandKey(candidate.Command), StringComparer.Ordinal)
            .First()
            .Command;
    }

    public static IReadOnlyList<SolitaireCandidate> LegalCommands(
        SolitaireGameState game,
        int expectedVersion)
    {
        var candidates = new List<SolitaireCommandRequest>
        {
            new(SolitaireCommandTypes.Draw, expectedVersion, null, null, null, null),
        };
        for (var column = 0; column < 7; column++)
        {
            candidates.Add(new SolitaireCommandRequest(SolitaireCommandTypes.Flip, expectedVersion, null, null, null, column));
            var pile = game.Tableau[column];
            for (var start = 0; start < pile.Count; start++)
            {
                for (var target = 0; target < 7; target++)
                    candidates.Add(Move(expectedVersion, "tableau", column, start, "tableau", target));
                for (var target = 0; target < 4; target++)
                    candidates.Add(Move(expectedVersion, "tableau", column, start, "foundation", target));
            }
        }
        if (game.Waste.Count > 0)
        {
            var top = game.Waste.Count - 1;
            for (var target = 0; target < 7; target++)
                candidates.Add(Move(expectedVersion, "waste", 0, top, "tableau", target));
            for (var target = 0; target < 4; target++)
                candidates.Add(Move(expectedVersion, "waste", 0, top, "foundation", target));
        }
        for (var source = 0; source < 4; source++)
        {
            if (game.Foundations[source].Count == 0) continue;
            var top = game.Foundations[source].Count - 1;
            for (var target = 0; target < 7; target++)
                candidates.Add(Move(expectedVersion, "foundation", source, top, "tableau", target));
        }

        var legal = new List<SolitaireCandidate>();
        foreach (var command in candidates)
        {
            try { legal.Add(new SolitaireCandidate(command, SolitaireEngine.Apply(game, command))); }
            catch (SolitaireIllegalMoveException) { }
        }
        return legal;
    }

    private static int Score(
        SolitaireGameState before,
        SolitaireGameState after,
        SolitaireCommandRequest command,
        int skillLevel)
    {
        var score = (after.Score - before.Score) * 10;
        score += (after.Foundations.Sum(pile => pile.Count) - before.Foundations.Sum(pile => pile.Count)) * 60;
        score += (FaceDown(before) - FaceDown(after)) * 80;
        if (command.Type == SolitaireCommandTypes.Draw) score -= 8;
        if (command.From?.Zone == "foundation") score -= 100;
        if (skillLevel == CardBotSkillLevels.Strong)
        {
            score += LegalCommands(after, 1).Count * 2;
            score -= after.Stock.Count == 0 && after.Waste.Count == 0 ? 30 : 0;
        }
        return score;
    }

    private static int FaceDown(SolitaireGameState game) =>
        game.Tableau.Sum(pile => pile.Count(card => !card.FaceUp));

    private static SolitaireCommandRequest Move(
        int version,
        string fromZone,
        int fromIndex,
        int start,
        string toZone,
        int toIndex) => new(
            SolitaireCommandTypes.Move,
            version,
            new SolitairePileReference(fromZone, fromIndex),
            start,
            new SolitairePileReference(toZone, toIndex),
            null);

    private static string StableCommandKey(SolitaireCommandRequest value) =>
        $"{value.Type}:{value.From?.Zone}:{value.From?.Index}:{value.StartIndex}:{value.To?.Zone}:{value.To?.Index}:{value.Column}";
}

public sealed record SolitaireCandidate(SolitaireCommandRequest Command, SolitaireGameState Result);
