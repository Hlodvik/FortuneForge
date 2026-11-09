

namespace FortuneForge.Server.Slots.Models;

public sealed class SlotsOptions
{
    public const string SectionName = "Slots";

    public List<GameDefinition> GameDefinitions { get; init; } = [];
    public GameDefinition? ClassicProfileTemplate { get; init; }
    public List<SymbolSetDefinition> SymbolSets { get; init; } = [];
    public List<ReelSetDefinition> ReelSets { get; init; } = [];
    public List<PaytableDefinition> Paytables { get; init; } = [];
}
