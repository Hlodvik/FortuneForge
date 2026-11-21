using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.TexasHoldem;

public static class TexasHoldemModule
{
    public const string GameId = "texas-holdem";
    public const string ApiPath = "/api/cards/texas-holdem/credit";
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            GameId,
            "Texas Hold'em",
            GameCategory.Card,
            "0.4.0",
            "/cards/texas-holdem",
            ApiPath,
            GameCapability.Credits | GameCapability.Multiplayer | GameCapability.History);
        descriptor.Validate();
        return descriptor;
    }
}
