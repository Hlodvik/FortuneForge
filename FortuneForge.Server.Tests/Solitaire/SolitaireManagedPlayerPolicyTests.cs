using FortuneForge.Games.Solitaire;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Solitaire;
using Xunit;

namespace FortuneForge.Server.Tests.Solitaire;

public sealed class SolitaireManagedPlayerPolicyTests
{
    private static readonly DateTime Start = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ManagedSeatStartsWithReservedIdentityAndTheSharedDeal()
    {
        var profile = new ManagedPlayerProfile("managed-reserved-profile", "CopperGarden", ManagedPlayerSkillLevels.Average,
            new HashSet<string>([ManagedPlayerGames.Solitaire], StringComparer.OrdinalIgnoreCase), Start);
        var match = Match();

        var player = SolitaireManagedPlayerPolicy.Start(match, 2, profile);

        Assert.Equal(profile.UserId, player.UserId);
        Assert.Equal(profile.PlayerName, player.DisplayName);
        Assert.False(player.IsAccountBacked);
        Assert.Equal(SolitairePlayerStatuses.Playing, player.Status);
        Assert.Null(player.CompletedAtUtc);
        Assert.Null(player.ElapsedMilliseconds);
        Assert.Equal(match.StartedAtUtc, player.StartedAtUtc);
        Assert.Equal(match.DeadlineAtUtc, player.DeadlineAtUtc);
        Assert.Equal(SolitaireEngine.CreateGame(match.DealSeed, match.DrawCount).Stock, player.Game.Stock);
        Assert.Equal(ManagedPlayerSkillLevels.Average, SolitaireManagedPlayerPolicy.Skill(player));
        Assert.DoesNotContain("managed-completion-elapsed", player.HostMetadata.Keys);
    }

    [Fact]
    public void ManagedPolicyOnlyReturnsEngineLegalCommands()
    {
        var game = SolitaireEngine.CreateGame(123, 3);
        var command = SolitaireManagedPlayerPolicy.Choose(game, 1, ManagedPlayerSkillLevels.Strong, 456);

        var result = SolitaireEngine.Apply(game, command);
        Assert.Equal(1, result.Moves);
    }

    [Fact]
    public void ChoiceCannotUseFaceDownTableauCardsOrStockOrder()
    {
        var hiddenAce = new SolitaireCard("hidden-ace", "spades", 1, false);
        var hiddenSeven = new SolitaireCard("hidden-seven", "clubs", 7, false);
        var first = new SolitaireGameState(
            [new SolitaireCard("stock-1", "hearts", 3, false),
             new SolitaireCard("stock-2", "diamonds", 8, false)],
            [],
            Enumerable.Range(0, 4)
                .Select(_ => (IReadOnlyList<SolitaireCard>)Array.Empty<SolitaireCard>()).ToArray(),
            new IReadOnlyList<SolitaireCard>[]
            {
                [hiddenAce, new SolitaireCard("red-ten-a", "hearts", 10, true)],
                [hiddenSeven, new SolitaireCard("red-ten-b", "diamonds", 10, true)],
                [new SolitaireCard("black-jack", "clubs", 11, true)],
                [], [], [], []
            },
            0, 0, 123, "Your move");
        var second = first with
        {
            Stock = first.Stock.Reverse().ToArray(),
            Tableau = new IReadOnlyList<SolitaireCard>[]
            {
                [hiddenSeven, first.Tableau[0][1]],
                [hiddenAce, first.Tableau[1][1]],
                first.Tableau[2], [], [], [], []
            }
        };

        foreach (var skill in new[] { ManagedPlayerSkillLevels.Average, ManagedPlayerSkillLevels.Strong })
        {
            var a = SolitaireManagedPlayerPolicy.Choose(first, 1, skill, 456);
            var b = SolitaireManagedPlayerPolicy.Choose(second, 1, skill, 456);
            Assert.Equal(a, b);
            Assert.NotNull(SolitaireEngine.Apply(first, a));
            Assert.NotNull(SolitaireEngine.Apply(second, b));
        }
    }

    [Fact]
    public void StrongBotDrawsInsteadOfLoopingAVisibleKingBetweenEmptyColumns()
    {
        var game = new SolitaireGameState(
            [new SolitaireCard("stock", "hearts", 8, false)], [],
            Enumerable.Range(0, 4)
                .Select(_ => (IReadOnlyList<SolitaireCard>)Array.Empty<SolitaireCard>()).ToArray(),
            new IReadOnlyList<SolitaireCard>[]
            {
                [new SolitaireCard("king", "clubs", 13, true)], [], [], [], [], [], []
            },
            0, 0, 123, "Your move");

        var draw = SolitaireManagedPlayerPolicy.Choose(game, 1, ManagedPlayerSkillLevels.Strong, 456);
        var recycle = SolitaireManagedPlayerPolicy.Choose(
            game with
            {
                Stock = [],
                Waste = [new SolitaireCard("waste", "hearts", 8, true)]
            },
            1, ManagedPlayerSkillLevels.Strong, 456);

        Assert.Equal(SolitaireCommandTypes.Draw, draw.Type);
        Assert.Equal(SolitaireCommandTypes.Draw, recycle.Type);
    }

    private static SolitaireMatch Match() => new(
        "match", 2, 100, 100, 100, 0, 123, Start,
        Start.AddMinutes(10), "playing", ["human"], ["Alice"], ["ticket"], [Start], null, null)
    {
        PartitionKey = "test",
        DrawCount = 3
    };
}
