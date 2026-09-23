using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.Solitaire;

public static class SolitaireModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "solitaire",
            "Competitive Solitaire",
            GameCategory.Card,
            "0.4.0",
            "/cards/solitaire",
            "/api/solitaire",
            GameCapability.Credits | GameCapability.FreePlay | GameCapability.Multiplayer | GameCapability.Bots | GameCapability.History);
        descriptor.Validate();
        return descriptor;
    }
}
