using FortuneForge.Games.EmptySlots.Models;

namespace FortuneForge.Games.EmptySlots.Configuration;

public interface ISlotsDefinitionProvider
{
    GameDefinition? GetGame(string id);
    SymbolSetDefinition? GetSymbolSet(string id);
    ReelSetDefinition? GetReelSet(string id);
    PaytableDefinition? GetPaytable(string id);
}
