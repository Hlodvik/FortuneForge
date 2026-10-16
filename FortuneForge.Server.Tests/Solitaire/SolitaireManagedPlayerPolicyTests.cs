using FortuneForge.Games.Solitaire;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Solitaire;
using Xunit;

namespace FortuneForge.Server.Tests.Solitaire;

public sealed class SolitaireManagedPlayerPolicyTests
{
    private static readonly DateTime Start = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CompletedManagedSeatUsesTheReservedProfileIdentity()
    {
        var profile = new ManagedPlayerProfile("managed-reserved-profile", "CopperGarden", ManagedPlayerSkillLevels.Average,
            new HashSet<string>([ManagedPlayerGames.Solitaire], StringComparer.OrdinalIgnoreCase), Start);
        var match = Match();

        var player = SolitaireManagedPlayerPolicy.Complete(match, 2, profile, Start.AddMinutes(4));

        Assert.Equal(profile.UserId, player.UserId);
        Assert.Equal(profile.PlayerName, player.DisplayName);
        Assert.False(player.IsAccountBacked);
        Assert.Equal(SolitairePlayerStatuses.Finished, player.Status);
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
    public void ManagedSeatStartsWithTheCommonDealAndCompletesOnItsPersistedSchedule()
    {
        var profile = new ManagedPlayerProfile("managed-scheduled-profile", "SilverMeteor", ManagedPlayerSkillLevels.Strong,
            new HashSet<string>([ManagedPlayerGames.Solitaire], StringComparer.OrdinalIgnoreCase), Start);
        var match = Match();
        var player = SolitaireManagedPlayerPolicy.Start(match, 2, profile);

        Assert.Equal(SolitairePlayerStatuses.Playing, player.Status);
        Assert.Null(player.CompletedAtUtc);
        Assert.Null(player.ElapsedMilliseconds);
        Assert.Equal(match.StartedAtUtc, player.StartedAtUtc);
        Assert.Equal(match.DeadlineAtUtc, player.DeadlineAtUtc);
        Assert.Equal(SolitaireEngine.CreateGame(match.DealSeed, match.DrawCount).Stock, player.Game.Stock);
        var scheduledAt = SolitaireManagedPlayerPolicy.CompletionAt(match, player);
        Assert.InRange(scheduledAt, Start.AddMinutes(3), Start.AddMinutes(7));

        var completed = SolitaireManagedPlayerPolicy.CompleteScheduled(match, player);

        Assert.Equal(SolitairePlayerStatuses.Finished, completed.Status);
        Assert.Equal(scheduledAt, completed.CompletedAtUtc);
        Assert.Equal((long)(scheduledAt - Start).TotalMilliseconds, completed.ElapsedMilliseconds);
        Assert.Equal(profile.UserId, completed.UserId);
        Assert.Equal(profile.PlayerName, completed.DisplayName);
        Assert.False(completed.IsAccountBacked);
    }

    private static SolitaireMatch Match() => new(
        "match", 2, 100, 100, 100, 0, 123, Start,
        Start.AddMinutes(10), "playing", ["human"], ["Alice"], ["ticket"], [Start], null, null)
    {
        PartitionKey = "test",
        DrawCount = 3
    };
}
