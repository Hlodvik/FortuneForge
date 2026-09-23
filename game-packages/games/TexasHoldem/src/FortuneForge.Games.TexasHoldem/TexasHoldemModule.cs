using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.TexasHoldem;

public static class TexasHoldemModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "texas-holdem",
            "Texas Hold'em",
            GameCategory.Card,
            "0.4.0",
            "/cards/texas-holdem",
            "/api/cards/texas-holdem/credit",
            GameCapability.Credits | GameCapability.Multiplayer | GameCapability.Bots | GameCapability.History);
        descriptor.Validate();
        return descriptor;
    }
}
