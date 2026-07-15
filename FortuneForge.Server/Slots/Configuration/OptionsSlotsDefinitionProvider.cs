using FortuneForge.Server.Slots.Models;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Slots.Configuration;

public sealed class OptionsSlotsDefinitionProvider(IOptions<SlotsOptions> options) : ISlotsDefinitionProvider
{
    public GameDefinition? GetGame(string id) =>
        options.Value.GameDefinitions.SingleOrDefault(game => string.Equals(game.Id, id, StringComparison.Ordinal));

    public SymbolSetDefinition? GetSymbolSet(string id) =>
        options.Value.SymbolSets.SingleOrDefault(set => string.Equals(set.Id, id, StringComparison.Ordinal));

    public ReelSetDefinition? GetReelSet(string id) =>
        options.Value.ReelSets.SingleOrDefault(set => string.Equals(set.Id, id, StringComparison.Ordinal));

    public PaytableDefinition? GetPaytable(string id) =>
        options.Value.Paytables.SingleOrDefault(table => string.Equals(table.Id, id, StringComparison.Ordinal));
}
