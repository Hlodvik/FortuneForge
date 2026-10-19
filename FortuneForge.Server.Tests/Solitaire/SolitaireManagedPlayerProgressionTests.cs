using System.Diagnostics;
using System.Text.Json;
using FortuneForge.Games.Solitaire;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Solitaire;
using Xunit;

namespace FortuneForge.Server.Tests.Solitaire;

public sealed class SolitaireManagedPlayerProgressionTests
{
    private static readonly DateTime Start = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EachBotGetsItsOwnPersistentActionClockAndRealLegalFirstMove()
    {
        var match = Match();
        var a = SolitaireManagedPlayerProgression.Initialize(match, Player(match, "bot-alpha", 2));
        var b = SolitaireManagedPlayerProgression.Initialize(match, Player(match, "bot-bravo", 3));
        var aDue = SolitaireManagedPlayerProgression.NextActionAt(a);
        var bDue = SolitaireManagedPlayerProgression.NextActionAt(b);

        Assert.NotEqual(aDue, bDue);
        Assert.Equal(a, SolitaireManagedPlayerProgression.Advance(match, a, aDue.AddMilliseconds(-1)));

        var advanced = SolitaireManagedPlayerProgression.Advance(match, a, aDue);
        Assert.Equal(1, advanced.Game.Moves);
        Assert.Equal(a.Version + 1, advanced.Version);
        Assert.Equal(SolitairePlayerStatuses.Playing, advanced.Status);
        Assert.True(SolitaireManagedPlayerProgression.NextActionAt(advanced) > aDue);
        var legalResults = SolitaireManagedPlayerPolicy.LegalCommands(a.Game, a.Version)
            .Select(candidate => JsonSerializer.Serialize(candidate.Result));
        Assert.Contains(JsonSerializer.Serialize(advanced.Game), legalResults);
        Assert.Equal(a.Game.Seed, advanced.Game.Seed);
    }

    [Fact]
    public void ProgressionIsDeterministicAndNormalCatchUpIsBounded()
    {
        var match = Match();
        var original = Player(match, "bot-deterministic", 4);
        var midway = Start.AddMinutes(6);

        var first = SolitaireManagedPlayerProgression.Advance(match, original, midway);
        var replay = SolitaireManagedPlayerProgression.Advance(match, original, midway);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(replay));
        Assert.InRange(first.Game.Moves, 1, 8);
        Assert.Equal(SolitairePlayerStatuses.Playing, first.Status);
        var next = SolitaireManagedPlayerProgression.Advance(match, first, midway);
        Assert.InRange(next.Game.Moves, first.Game.Moves + 1, first.Game.Moves + 8);
        Assert.True(SolitaireManagedPlayerProgression.NextActionAt(next) >
                    SolitaireManagedPlayerProgression.NextActionAt(first));
    }

    [Fact]
    public void QuietMatchCatchesUpAllDueRealMovesAndFinishesAtDeadline()
    {
        var match = Match();
        var original = Player(match, "bot-quiet", 2);
        var stopwatch = Stopwatch.StartNew();

        var finished = SolitaireManagedPlayerProgression.Advance(match, original, match.DeadlineAtUtc);

        Assert.Equal(SolitairePlayerStatuses.Finished, finished.Status);
        Assert.InRange(finished.Game.Moves, 2, 180);
        Assert.NotNull(finished.ElapsedMilliseconds);
        Assert.InRange(finished.CompletedAtUtc!.Value, match.StartedAtUtc, match.DeadlineAtUtc);
        Assert.Equal(finished.Game.Score, SolitaireScoreRules.FinalScore(
            finished.Game with { Score = finished.Game.Score -
                (SolitaireEngine.IsWon(finished.Game)
                    ? SolitaireScoreRules.WinBonus(finished.ElapsedMilliseconds!.Value) : 0) },
            finished.ElapsedMilliseconds!.Value));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15),
            $"One bot's deadline catch-up took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void BotsOnTheSameDealDoNotSharePaceOrOutcome()
    {
        var match = Match();
        var players = new[]
        {
            Player(match, "bot-copper", 2),
            Player(match, "bot-garden", 3),
            Player(match, "bot-meteor", 4)
        };

        var finished = players.Select(player =>
            SolitaireManagedPlayerProgression.Advance(match, player, match.DeadlineAtUtc)).ToArray();

        Assert.All(finished, player => Assert.Equal(SolitairePlayerStatuses.Finished, player.Status));
        Assert.All(finished, player => Assert.Equal(match.DealSeed, player.Game.Seed));
        Assert.True(finished.Select(player => player.Game.Moves).Distinct().Count() > 1);
        Assert.True(finished.Select(player => player.Game.Score).Distinct().Count() > 1,
            string.Join(", ", finished.Select(player =>
                $"{player.UserId}: {player.Game.Score} points, {player.Game.Moves} moves, {player.Game.Foundations.Sum(pile => pile.Count)} foundation cards, {player.Game.Tableau.Sum(pile => pile.Count(card => !card.FaceUp))} hidden")));
        Assert.True(finished.Select(player => player.CompletedAtUtc).Distinct().Count() > 1);
    }

    [Theory]
    [InlineData(ManagedPlayerSkillLevels.Poor)]
    [InlineData(ManagedPlayerSkillLevels.Average)]
    [InlineData(ManagedPlayerSkillLevels.Strong)]
    public void AllSkillLevelsPlayARealDealWithinTheDeadline(int skill)
    {
        var match = Match();
        var player = Player(match, $"bot-skill-{skill}", 2, skill);
        var stopwatch = Stopwatch.StartNew();

        var finished = SolitaireManagedPlayerProgression.Advance(match, player, match.DeadlineAtUtc);

        Assert.Equal(SolitairePlayerStatuses.Finished, finished.Status);
        Assert.True(finished.Game.Moves > 0);
        Assert.InRange(finished.CompletedAtUtc!.Value, match.StartedAtUtc, match.DeadlineAtUtc);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15),
            $"Skill {skill} deadline catch-up took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void SeededDealsProduceVariedRealScoresWithoutEmptyRecyclingLoops()
    {
        var results = new List<(uint Seed, int Skill, int Score, int Moves, int Foundations, bool Won)>();
        for (uint seed = 100; seed < 200; seed++)
        {
            var match = Match() with { DealSeed = seed };
            foreach (var skill in new[]
                     {
                         ManagedPlayerSkillLevels.Poor,
                         ManagedPlayerSkillLevels.Average,
                         ManagedPlayerSkillLevels.Strong
                     })
            {
                var player = Player(match, $"bot-{seed}-{skill}", skill, skill);
                var finished = SolitaireManagedPlayerProgression.Advance(match, player, match.DeadlineAtUtc);
                results.Add((seed, skill, finished.Game.Score, finished.Game.Moves,
                    finished.Game.Foundations.Sum(pile => pile.Count), SolitaireEngine.IsWon(finished.Game)));
            }
        }

        var summary = $"{results.Count} runs: positive={results.Count(item => item.Score > 0)}, " +
            $"zero={results.Count(item => item.Score == 0)}, wins={results.Count(item => item.Won)}, " +
            $"max={results.Max(item => item.Score)}, median={results.Select(item => item.Score).Order().ElementAt(results.Count / 2)}, " +
            $"100+ moves and zero={results.Count(item => item.Moves >= 100 && item.Score == 0)}; " +
            string.Join("; ", results.GroupBy(item => item.Skill)
                .Select(group => $"skill {group.Key}: positive {group.Count(item => item.Score > 0)}/{group.Count()}, " +
                    $"max {group.Max(item => item.Score)}, foundation max {group.Max(item => item.Foundations)}"));
        Assert.True(results.Count(item => item.Score > 0) >= 250, summary);
        Assert.True(results.Select(item => item.Score).Distinct().Count() >= 10, summary);
        Assert.True(results.Count(item => item.Moves >= 100 && item.Score == 0) <= 5, summary);
        Assert.True(results.Max(item => item.Foundations) >= 40, summary);
        Assert.Contains(results, item => item.Won);
    }

    [Fact]
    public void AWonBoardReceivesTheSameSpeedBonusAsAHuman()
    {
        var match = Match();
        var player = Player(match, "bot-winner", 2);
        var allCards = player.Game.Stock.Concat(player.Game.Waste)
            .Concat(player.Game.Tableau.SelectMany(pile => pile))
            .Concat(player.Game.Foundations.SelectMany(pile => pile)).ToArray();
        var foundations = new[] { "clubs", "diamonds", "hearts", "spades" }
            .Select(suit => (IReadOnlyList<SolitaireCard>)allCards
                .Where(card => card.Suit == suit).OrderBy(card => card.Rank)
                .Select(card => card with { FaceUp = true }).ToArray()).ToArray();
        var won = player with
        {
            Game = player.Game with
            {
                Stock = [], Waste = [],
                Tableau = Enumerable.Range(0, 7)
                    .Select(_ => (IReadOnlyList<SolitaireCard>)Array.Empty<SolitaireCard>()).ToArray(),
                Foundations = foundations, Score = 500
            }
        };

        var finished = SolitaireManagedPlayerProgression.Advance(match, won, Start.AddMinutes(2));

        Assert.Equal(SolitairePlayerStatuses.Finished, finished.Status);
        Assert.Equal(500 + SolitaireScoreRules.WinBonus(120_000), finished.Game.Score);
        Assert.Equal(120_000, finished.ElapsedMilliseconds);
    }

    [Fact]
    public void ManagedPolicyCannotAdvanceARealAccount()
    {
        var match = Match();
        var bot = Player(match, "bot-protected", 2);
        var human = bot with { UserId = "real-user", IsAccountBacked = true };

        Assert.Throws<ArgumentException>(() => SolitaireManagedPlayerProgression.Initialize(match, human));
        Assert.Throws<ArgumentException>(() => SolitaireManagedPlayerProgression.Advance(match, human, Start.AddMinutes(1)));
    }

    private static SolitaireMatch Match() => new(
        "progress-match", 4, 500, 2_000, 1_800, 200, 123_456, Start,
        Start.AddMinutes(10), "playing", ["human"], ["Alice"], ["ticket"], [Start], null, null)
    {
        PartitionKey = "test",
        DrawCount = 3
    };

    private static SolitairePlayerState Player(
        SolitaireMatch match, string id, int seat, int skill = ManagedPlayerSkillLevels.Average)
    {
        var profile = new ManagedPlayerProfile(id, id, skill,
            new HashSet<string>([ManagedPlayerGames.Solitaire], StringComparer.OrdinalIgnoreCase), Start);
        return SolitaireManagedPlayerPolicy.Start(match, seat, profile);
    }
}
