using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Solitaire;

namespace FortuneForge.Server.Bots.Solitaire;

internal static class SolitaireManagedPlayerPolicy
{
    private const string SkillKey = "managed-skill";

    public static SolitairePlayerState Start(SolitaireMatch match, int seat, ManagedPlayerProfile profile)
    {
        ManagedPlayerSkillLevels.Validate(profile.SkillLevel);
        return new SolitairePlayerState(
            match.MatchId, profile.UserId, profile.PlayerName, seat,
            SolitairePlayerStatuses.Playing, SolitaireEngine.CreateGame(match.DealSeed, match.DrawCount),
            1, null, null, 0, false)
        {
            StartedAtUtc = match.StartedAtUtc,
            DeadlineAtUtc = match.DeadlineAtUtc,
            IsAccountBacked = false,
            HostMetadata = new(StringComparer.Ordinal)
            {
                [SkillKey] = profile.SkillLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        };
    }

    internal static int Skill(SolitairePlayerState player) =>
        player.HostMetadata.TryGetValue(SkillKey, out var stored) &&
        int.TryParse(stored, out var skill) && skill is >= 2 and <= 4
            ? skill : ManagedPlayerSkillLevels.Average;

    internal static SolitaireCommandRequest Choose(
        SolitaireGameState game,
        int expectedVersion,
        int skillLevel,
        ulong seed,
        IReadOnlySet<string>? recentVisibleStates = null)
    {
        ManagedPlayerSkillLevels.Validate(skillLevel);
        var legal = LegalCommands(game, expectedVersion);
        if (legal.Count == 0)
            throw new InvalidOperationException("No legal Solitaire command is available.");
        var random = new DeterministicManagedPlayerRandom(seed, $"solitaire:{expectedVersion}:{skillLevel}");
        if (skillLevel == ManagedPlayerSkillLevels.Poor)
        {
            var draws = legal.Where(candidate => candidate.Command.Type == SolitaireCommandTypes.Draw).ToArray();
            return draws.Length > 0 && random.NextDouble() < 0.65
                ? random.Choose(draws).Command
                : random.Choose(legal).Command;
        }

        var options = new ManagedPlayerDecisionOptions();
        var errorRate = skillLevel == ManagedPlayerSkillLevels.Average
            ? options.AverageErrorRate
            : options.StrongImperfectionRate;
        if (random.NextDouble() < errorRate) return random.Choose(legal).Command;
        return legal
            .OrderByDescending(candidate => Score(game, candidate.Result, candidate.Command, recentVisibleStates))
            .ThenBy(candidate => Key(candidate.Command), StringComparer.Ordinal)
            .First().Command;
    }

    internal static IReadOnlyList<SolitaireCandidate> LegalCommands(
        SolitaireGameState game,
        int expectedVersion)
    {
        var candidates = new List<SolitaireCommandRequest>
        {
            new(SolitaireCommandTypes.Draw, expectedVersion, null, null, null, null),
        };
        for (var column = 0; column < 7; column++)
        {
            candidates.Add(new(SolitaireCommandTypes.Flip, expectedVersion, null, null, null, column));
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
            try { legal.Add(new(command, SolitaireEngine.Apply(game, command))); }
            catch (SolitaireIllegalMoveException) { }
        }
        return legal;
    }

    internal static string VisibleStateFingerprint(SolitaireGameState game)
    {
        var visible = new StringBuilder();
        visible.Append(game.Stock.Count).Append('|').Append(game.Waste.Count).Append(':')
            .Append(game.Waste.LastOrDefault()?.Id).Append('|');
        foreach (var pile in game.Foundations)
            visible.Append(pile.Count).Append(':').Append(pile.LastOrDefault()?.Id).Append('|');
        foreach (var pile in game.Tableau)
        {
            visible.Append(pile.Count).Append(':');
            foreach (var card in pile)
                visible.Append(card.FaceUp ? card.Id : "?").Append(',');
            visible.Append('|');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(visible.ToString()));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private static int Score(
        SolitaireGameState before,
        SolitaireGameState after,
        SolitaireCommandRequest command,
        IReadOnlySet<string>? recentVisibleStates)
    {
        // Stock identities and cards revealed by a move are hidden until the
        // action happens. A draw's merit must not depend on its simulated result.
        if (command.Type == SolitaireCommandTypes.Draw)
            return before.Stock.Count == 0 ? -10 : -8;

        var score = (after.Score - before.Score) * 10;
        score += (after.Foundations.Sum(pile => pile.Count) - before.Foundations.Sum(pile => pile.Count)) * 60;
        score += (FaceDown(before) - FaceDown(after)) * 80;
        if (command.From?.Zone == "foundation") score -= 100;
        if (score == 0)
        {
            // Moving an entire face-up pile into an empty column just relocates
            // it; other neutral moves can set up a useful next move.
            score = command.From?.Zone == "tableau" && command.To?.Zone == "tableau" &&
                after.Tableau[command.From.Index].Count == 0 &&
                before.Tableau[command.To.Index].Count == 0
                    ? -12 : -6;
        }
        // Only compare a candidate's layout if it did not reveal a hidden
        // tableau card. Its identity must not influence the pre-move choice.
        if (FaceDown(before) == FaceDown(after) &&
            recentVisibleStates?.Contains(VisibleStateFingerprint(after)) == true)
            score -= 50;
        return score;
    }

    private static int FaceDown(SolitaireGameState game) =>
        game.Tableau.Sum(pile => pile.Count(card => !card.FaceUp));

    private static SolitaireCommandRequest Move(
        int version, string fromZone, int fromIndex, int start, string toZone, int toIndex) => new(
            SolitaireCommandTypes.Move,
            version,
            new SolitairePileReference(fromZone, fromIndex),
            start,
            new SolitairePileReference(toZone, toIndex),
            null);

    private static string Key(SolitaireCommandRequest value) =>
        $"{value.Type}:{value.From?.Zone}:{value.From?.Index}:{value.StartIndex}:{value.To?.Zone}:{value.To?.Index}:{value.Column}";

    internal sealed record SolitaireCandidate(SolitaireCommandRequest Command, SolitaireGameState Result);
}
