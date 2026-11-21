using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.CasinoWar;

public static class CasinoWarModule
{
    public const string GameId = "casino-war";
    public const string ApiBasePath = "/api/games/" + GameId;
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            GameId,
            "Casino War",
            GameCategory.Casino,
            "0.1.0",
            "/games/" + GameId,
            ApiBasePath,
            GameCapability.FreePlay);
        descriptor.Validate();
        return descriptor;
    }
}
