using FortuneForge.Games.EmptySlots.Models;

namespace FortuneForge.Games.EmptySlots.Reels;

public interface IReelGenerator
{
    ReelOutcome Generate(
        GameDefinition game,
        ReelSetDefinition reelSet,
        SymbolSetDefinition symbolSet);
}
