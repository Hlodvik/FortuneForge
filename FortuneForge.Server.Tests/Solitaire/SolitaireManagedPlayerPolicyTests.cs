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
        var profile = Assert.Single(SolitaireManagedPlayerPolicy.CreateLocalProfiles(1, Start));
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

    private static SolitaireMatch Match() => new(
        "match", 2, 100, 100, 100, 0, 123, Start,
        Start.AddMinutes(10), "playing", ["human"], ["Alice"], ["ticket"], [Start], null, null)
    {
        PartitionKey = "test",
        DrawCount = 3
    };
}
