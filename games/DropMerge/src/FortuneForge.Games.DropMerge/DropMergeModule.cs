using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.DropMerge;

public static class DropMergeModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "drop-merge",
            "Drop Merge",
            GameCategory.Arcade,
            "0.1.0",
            "/games/drop-merge",
            "/api/games/drop-merge",
            GameCapability.FreePlay | GameCapability.History);
        descriptor.Validate();
        return descriptor;
    }
}
