using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Configuration;

public interface ISlotsDefinitionProvider
{
    GameDefinition? GetGame(string id);
    SymbolSetDefinition? GetSymbolSet(string id);
    ReelSetDefinition? GetReelSet(string id);
    PaytableDefinition? GetPaytable(string id);
}
