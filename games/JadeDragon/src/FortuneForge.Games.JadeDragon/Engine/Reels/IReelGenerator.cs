using FortuneForge.Games.JadeDragon.Models;

namespace FortuneForge.Games.JadeDragon.Reels;

public interface IReelGenerator
{
    ReelOutcome Generate(
        GameDefinition game,
        ReelSetDefinition reelSet,
        SymbolSetDefinition symbolSet);
}
