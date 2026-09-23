using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.Blackjack;

public static class BlackjackModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "blackjack",
            "Fortune Blackjack",
            GameCategory.Card,
            "0.4.0",
            "/cards/blackjack",
            "/api/cards/blackjack/table",
            GameCapability.Credits | GameCapability.Multiplayer | GameCapability.Bots | GameCapability.History);
        descriptor.Validate();
        return descriptor;
    }
}
