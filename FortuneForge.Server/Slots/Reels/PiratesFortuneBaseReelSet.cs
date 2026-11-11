using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Reels;

public static class PiratesFortuneBaseReelSet
{
    public const string Id = FortuneForge.Games.PiratesFortune.PiratesFortuneEngine.ReelSetId;

    // Compatibility surface only. Authored strips live in the Pirates package.
    public static readonly ReelSetDefinition Definition =
        FortuneForge.Games.PiratesFortune.PiratesFortuneEngine.CreateReelSet();
}
