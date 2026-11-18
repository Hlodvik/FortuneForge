using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

public sealed class NeonDefinitionSource : ISlotDefinitionSource
{
    public GameDefinition? GetGame(string id) => id == NeonArcadeSlotDefinitions.GameId
        ? NeonArcadeSlotDefinitions.CreateGame() : null;
    public SymbolSetDefinition? GetSymbolSet(string id) => id switch
    {
        NeonArcadeSlotDefinitions.SymbolSetId => NeonArcadeSlotDefinitions.CreateSymbolSet(),
        NeonArcadeSlotDefinitions.CarSymbolSetId => NeonArcadeSlotDefinitions.CreateCarSymbolSet(),
        NeonDiscoDefinitions.SymbolSetId => NeonDiscoDefinitions.CreateSymbolSet(),
        NeonDiscoDefinitions.PreviousSymbolSetId => NeonDiscoDefinitions.CreatePreviousSymbolSet(),
        _ => null
    };
    public ReelSetDefinition? GetReelSet(string id) => id switch
    {
        NeonArcadeSlotDefinitions.ReelSetId => NeonArcadeSlotDefinitions.CreateReelSet(),
        NeonArcadeSlotDefinitions.CarReelSetId => NeonArcadeSlotDefinitions.CreateCarReelSet(),
        NeonDiscoDefinitions.ReelSetId => NeonDiscoDefinitions.CreateReelSet(),
        NeonDiscoDefinitions.PreviousSevenSymbolReelSetId => NeonDiscoDefinitions.CreatePreviousSevenSymbolReelSet(),
        NeonDiscoDefinitions.PreviousReelSetId => NeonDiscoDefinitions.CreatePreviousReelSet(),
        _ => null
    };
    public PaytableDefinition? GetPaytable(string id) => id switch
    {
        NeonArcadeSlotDefinitions.PaytableId => NeonArcadeSlotDefinitions.CreatePaytable(),
        NeonArcadeSlotDefinitions.CarPaytableId => NeonArcadeSlotDefinitions.CreateCarPaytable(),
        NeonArcadeSlotDefinitions.PreviousPaytableId => NeonArcadeSlotDefinitions.CreatePreviousPaytable(),
        NeonDiscoDefinitions.PaytableId => NeonDiscoDefinitions.CreatePaytable(),
        NeonDiscoDefinitions.PreviousSevenSymbolPaytableId => NeonDiscoDefinitions.CreatePreviousSevenSymbolPaytable(),
        NeonDiscoDefinitions.PreviousPairedPaytableId => NeonDiscoDefinitions.CreatePreviousPairedPaytable(),
        NeonDiscoDefinitions.PreviousPaytableId => NeonDiscoDefinitions.CreatePreviousPaytable(),
        _ => null
    };
}
