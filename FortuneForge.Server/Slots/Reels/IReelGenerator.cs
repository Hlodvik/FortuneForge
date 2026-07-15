using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Reels;

public interface IReelGenerator
{
    ReelOutcome Generate(
        GameDefinition game,
        ReelSetDefinition reelSet,
        SymbolSetDefinition symbolSet);
}
