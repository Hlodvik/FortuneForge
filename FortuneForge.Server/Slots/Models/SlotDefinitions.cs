namespace FortuneForge.Server.Slots.Models;

public sealed class SlotsOptions
{
    public const string SectionName = "Slots";

    public List<GameDefinition> GameDefinitions { get; init; } = [];
    public List<SymbolSetDefinition> SymbolSets { get; init; } = [];
    public List<ReelSetDefinition> ReelSets { get; init; } = [];
    public List<PaytableDefinition> Paytables { get; init; } = [];
}

public sealed class GameDefinition
{
    public required string Id { get; init; }
    public required GameLayoutDefinition Layout { get; init; }
    public required GameSymbolRules Symbols { get; init; }
    public required GameMatchingRules Matching { get; init; }
    public required GameMathDefinition Math { get; init; }
    public required GameWageringDefinition Wagering { get; init; }
    public List<List<int>> Paylines { get; init; } = [];
}

public sealed class GameLayoutDefinition
{
    public int ReelCount { get; init; }
    public int VisibleRows { get; init; }
}

public sealed class GameSymbolRules
{
    public required string SymbolSetId { get; init; }
    public required string WildSymbolId { get; init; }
    public List<int> NativeWildMatchLengths { get; init; } = [];
}

public sealed class GameMatchingRules
{
    public int MinimumRunLength { get; init; }
    public bool AllowMultipleRunsPerPayline { get; init; }
}

public sealed class GameMathDefinition
{
    public required string ReelSetId { get; init; }
    public required string PaytableId { get; init; }
    public required GameMathTargets Targets { get; init; }
}

public sealed class GameMathTargets
{
    public decimal? Rtp { get; init; }
    public decimal? HitRate { get; init; }
    public decimal? Volatility { get; init; }
}

public sealed class GameWageringDefinition
{
    public decimal PointValueInCents { get; init; }
    public long MinimumWagerPoints { get; init; }
    public long? MaximumWagerPoints { get; init; }
}

public sealed class SymbolSetDefinition
{
    public required string Id { get; init; }
    public List<SymbolDefinition> Symbols { get; init; } = [];
}

public sealed class SymbolDefinition
{
    public required string Id { get; init; }
}

public sealed class ReelSetDefinition
{
    public required string Id { get; init; }
    public required string SymbolSetId { get; init; }
    public List<List<string>> Reels { get; init; } = [];
}

public sealed class PaytableDefinition
{
    public required string Id { get; init; }
    public required string SymbolSetId { get; init; }
    public List<PayoutRule> Rules { get; init; } = [];
}

public sealed class PayoutRule
{
    public required string SymbolId { get; init; }
    public int MatchLength { get; init; }
    public long Multiplier { get; init; }
}
