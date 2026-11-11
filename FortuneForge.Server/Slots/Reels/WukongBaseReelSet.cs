using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Reels;

public static class WukongBaseReelSet
{
    public const string Id = FortuneForge.Games.Wukong.WukongEngine.ReelSetId;

    // Compatibility surface for historical host tests and tools. The package is
    // now the only owner of the authored Wukong reel transformation.
    public static ReelSetDefinition Create(ReelSetDefinition _) =>
        FortuneForge.Games.Wukong.WukongEngine.CreateReelSet();
}
