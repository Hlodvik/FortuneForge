using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.ThreeCardPoker;

public static class ThreeCardPokerModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "three-card-poker",
            "Three Card Poker",
            GameCategory.Card,
            "0.1.0",
            "/cards/three-card-poker",
            "/api/cards/three-card-poker/table",
            GameCapability.Credits | GameCapability.Multiplayer | GameCapability.History);
        descriptor.Validate();
        return descriptor;
    }
}
