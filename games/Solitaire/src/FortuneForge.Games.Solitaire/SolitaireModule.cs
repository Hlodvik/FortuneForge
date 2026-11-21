using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.Solitaire;

public static class SolitaireModule
{
    public const string GameId = "solitaire";
    public const string ApiBasePath = "/api/" + GameId;
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            GameId,
            "Competitive Solitaire",
            GameCategory.Card,
            "0.4.0",
            "/cards/solitaire",
            ApiBasePath,
            GameCapability.Credits | GameCapability.FreePlay | GameCapability.Multiplayer | GameCapability.History);
        descriptor.Validate();
        return descriptor;
    }
}
