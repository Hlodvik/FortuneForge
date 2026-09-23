using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.LiarsDice;

public static class LiarsDiceModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "liars-dice",
            "Liar's Dice",
            GameCategory.Dice,
            "0.1.1",
            "/games/liars-dice",
            "/api/games/liars-dice",
            GameCapability.FreePlay | GameCapability.Multiplayer | GameCapability.Bots | GameCapability.History);
        descriptor.Validate();
        return descriptor;
    }
}
