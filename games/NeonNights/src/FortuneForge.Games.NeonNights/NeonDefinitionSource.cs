using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

public sealed class NeonDefinitionSource : ISlotDefinitionSource
{
    public GameDefinition? GetGame(string id) => id == NeonArcadeSlotDefinitions.GameId
        ? NeonArcadeSlotDefinitions.CreateGame() : null;
    public SymbolSetDefinition? GetSymbolSet(string id) => id switch
    {
        NeonArcadeSlotDefinitions.SymbolSetId => NeonArcadeSlotDefinitions.CreateSymbolSet(),
        NeonDiscoDefinitions.SymbolSetId => NeonDiscoDefinitions.CreateSymbolSet(),
        _ => null
    };
    public ReelSetDefinition? GetReelSet(string id) => id switch
    {
        NeonArcadeSlotDefinitions.ReelSetId => NeonArcadeSlotDefinitions.CreateReelSet(),
        NeonDiscoDefinitions.ReelSetId => NeonDiscoDefinitions.CreateReelSet(),
        NeonDiscoDefinitions.PreviousReelSetId => NeonDiscoDefinitions.CreatePreviousReelSet(),
        _ => null
    };
    public PaytableDefinition? GetPaytable(string id) => id switch
    {
        NeonArcadeSlotDefinitions.PaytableId => NeonArcadeSlotDefinitions.CreatePaytable(),
        NeonArcadeSlotDefinitions.PreviousPaytableId => NeonArcadeSlotDefinitions.CreatePreviousPaytable(),
        NeonDiscoDefinitions.PaytableId => NeonDiscoDefinitions.CreatePaytable(),
        NeonDiscoDefinitions.PreviousPaytableId => NeonDiscoDefinitions.CreatePreviousPaytable(),
        _ => null
    };
}
