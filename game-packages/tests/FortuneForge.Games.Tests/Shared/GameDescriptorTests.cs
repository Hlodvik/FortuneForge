using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.Tests;

public sealed class GameDescriptorTests
{
    [Fact]
    public void DescriptorAcceptsVersionedApplicationRoutes()
    {
        var descriptor = new GameDescriptor(
            "texas-holdem",
            "Texas Hold'em",
            GameCategory.Card,
            "0.1.0",
            "/cards/texas-holdem",
            "/api/cards/texas-holdem/credit",
            GameCapability.Credits | GameCapability.Multiplayer | GameCapability.Bots | GameCapability.History);

        descriptor.Validate();
    }

    [Theory]
    [InlineData("TexasHoldem")]
    [InlineData("texas_holdem")]
    [InlineData("texas holdem")]
    public void DescriptorRejectsUnstableIds(string id)
    {
        var descriptor = new GameDescriptor(id, "Hold'em", GameCategory.Card, "0.1.0", "/cards/holdem", null, GameCapability.None);

        Assert.Throws<ArgumentException>(descriptor.Validate);
    }
}
