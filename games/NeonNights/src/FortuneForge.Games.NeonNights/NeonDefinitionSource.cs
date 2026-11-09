using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

public sealed class NeonDefinitionSource : ISlotDefinitionSource
{
    public GameDefinition? GetGame(string id) => id == NeonArcadeSlotDefinitions.GameId
        ? NeonArcadeSlotDefinitions.CreateGame() : null;
    public SymbolSetDefinition? GetSymbolSet(string id) => id == NeonArcadeSlotDefinitions.SymbolSetId
        ? NeonArcadeSlotDefinitions.CreateSymbolSet() : null;
    public ReelSetDefinition? GetReelSet(string id) => id == NeonArcadeSlotDefinitions.ReelSetId
        ? NeonArcadeSlotDefinitions.CreateReelSet() : null;
    public PaytableDefinition? GetPaytable(string id) => id == NeonArcadeSlotDefinitions.PaytableId
        ? NeonArcadeSlotDefinitions.CreatePaytable() : null;
}
