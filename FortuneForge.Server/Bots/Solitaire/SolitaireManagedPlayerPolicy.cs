using FortuneForge.Games.Solitaire;

namespace FortuneForge.Server.Bots.Solitaire;

internal static class SolitaireManagedPlayerPolicy
{
    private const string SkillKey = "managed-skill";
    public static IReadOnlyList<ManagedPlayerProfile> CreateLocalProfiles(int count, DateTime nowUtc) =>
        Enumerable.Range(0, Math.Max(0, count))
            .Select(index => new ManagedPlayerProfile(
                $"managed-local-solitaire-{Guid.NewGuid():N}",
                new[] { "Avery", "Blake", "Casey", "Drew", "Emery" }[index % 5],
                ManagedPlayerSkillLevels.Poor + index % 3,
                new HashSet<string>([ManagedPlayerGames.Solitaire], StringComparer.OrdinalIgnoreCase),
                nowUtc))
            .ToArray();

    public static SolitairePlayerState Complete(
        SolitaireMatch match,
        int seat,
        ManagedPlayerProfile profile,
        DateTime completedAtUtc)
    {
        var game = Play(match.DealSeed, match.DrawCount, seat, profile.SkillLevel);
        return new SolitairePlayerState(
            match.MatchId,
            profile.UserId,
            profile.PlayerName,
            seat,
            SolitairePlayerStatuses.Finished,
            game,
            1,
            ElapsedMilliseconds(match.DealSeed, seat, profile.UserId, profile.SkillLevel),
            completedAtUtc,
            0,
            false)
        {
            StartedAtUtc = match.StartedAtUtc,
            DeadlineAtUtc = completedAtUtc,
            IsAccountBacked = false,
            HostMetadata = new(StringComparer.Ordinal)
            {
                [SkillKey] = profile.SkillLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        };
    }

    internal static int Skill(SolitairePlayerState player) =>
        player.HostMetadata.TryGetValue(SkillKey, out var stored) &&
        int.TryParse(stored, out var skill) ? skill : 0;

    internal static SolitaireCommandRequest Choose(
        SolitaireGameState game,
        int expectedVersion,
        int skillLevel,
        ulong seed)
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
            .OrderByDescending(candidate => Score(game, candidate.Result, candidate.Command, skillLevel))
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

    private static SolitaireGameState Play(uint dealSeed, int drawCount, int seat, int skill)
    {
        var game = SolitaireEngine.CreateGame(dealSeed, drawCount);
        var maximumCommands = skill switch { 2 => 72, 3 => 120, _ => 168 };
        for (var version = 1; version <= maximumCommands && !SolitaireEngine.IsWon(game); version++)
        {
            var legal = LegalCommands(game, version);
            if (legal.Count == 0) break;
            var selected = skill == 2 || skill == 3 && version % 4 == 0
                ? new DeterministicManagedPlayerRandom(((ulong)dealSeed << 32) | (uint)seat, $"solitaire-run:{profileKey(seat)}:{version}").Choose(legal)
                : legal.OrderByDescending(candidate => candidate.Result.Score)
                    .ThenByDescending(candidate => candidate.Result.Foundations.Sum(pile => pile.Count))
                    .ThenBy(candidate => candidate.Result.Tableau.Sum(pile => pile.Count(card => !card.FaceUp)))
                    .ThenBy(candidate => Key(candidate.Command), StringComparer.Ordinal)
                    .First();
            game = selected.Result;
        }
        return game with { Message = SolitaireEngine.IsWon(game) ? "Game complete" : "Time expired" };

        static string profileKey(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static long ElapsedMilliseconds(uint dealSeed, int seat, string profileId, int skill)
    {
        var stable = new DeterministicManagedPlayerRandom(((ulong)dealSeed << 32) | (uint)seat, $"solitaire-elapsed:{profileId}");
        var spread = stable.Next(90_000);
        return Math.Min(599_000, 420_000 - skill * 30_000L + spread);
    }

    private static int Score(SolitaireGameState before, SolitaireGameState after, SolitaireCommandRequest command, int skill)
    {
        var score = (after.Score - before.Score) * 10;
        score += (after.Foundations.Sum(pile => pile.Count) - before.Foundations.Sum(pile => pile.Count)) * 60;
        score += (FaceDown(before) - FaceDown(after)) * 80;
        if (command.Type == SolitaireCommandTypes.Draw) score -= 8;
        if (command.From?.Zone == "foundation") score -= 100;
        if (skill == ManagedPlayerSkillLevels.Strong) score += LegalCommands(after, 1).Count * 2;
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
