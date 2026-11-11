using FortuneForge.Games.JadeDragon.Models;

namespace FortuneForge.Games.JadeDragon.Configuration;

public interface ISlotsDefinitionProvider
{
    GameDefinition? GetGame(string id);
    SymbolSetDefinition? GetSymbolSet(string id);
    ReelSetDefinition? GetReelSet(string id);
    PaytableDefinition? GetPaytable(string id);
}
