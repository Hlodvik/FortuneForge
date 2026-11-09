namespace FortuneForge.Games.EmptySlots;

/// <summary>Backend role information only. Symbol IDs have no presentation names or assets.</summary>
public sealed record EmptySlotsSymbolMetadata(
    string SymbolId,
    string Role,
    IReadOnlyList<PayoutRule> PaylinePayoutRules,
    bool PresentInStoredStrips,
    bool EligibleForBaseReels,
    bool CanBeInjected,
    decimal? ValueMultiplier = null,
    string? CollectionTrackId = null);

public static class EmptySlotsSymbols
{
    internal static IReadOnlyList<EmptySlotsSymbolMetadata> CreateMetadata(
        GameDefinition game, SymbolSetDefinition symbols, ReelSetDefinition reels,
        PaytableDefinition paytable) => symbols.Symbols.Select(symbol =>
    {
        var id = symbol.Id;
        var role = id switch
        {
            "S01" or "S02" or "S03" or "S04" or "S05" or "S06" => "payline",
            "S07" => "wild",
            "S08" => "scatter",
            "S09" => "special-points",
            "S10" => "meter",
            "S11" => "pattern",
            "S12" => "collector",
            "S13" or "S14" or "S15" or "S16" or "S17" or "S18" or "S19" => "value",
            "S20" or "S21" or "S22" or "S23" => "collection",
            _ => throw new InvalidOperationException($"Missing backend role for symbol {id}.")
        };
        decimal? multiplier = id switch
        {
            "S13" => 0.5m, "S14" => 1m, "S15" => 1.5m, "S16" => 2m,
            "S17" => 3m, "S18" => 4m, "S19" => 5m, _ => null
        };
        var track = id switch
        {
            "S20" => "track1", "S21" => "track2", "S22" => "track3", "S23" => "track4", _ => null
        };
        var onStrips = reels.Reels.Any(strip => strip.Contains(id, StringComparer.Ordinal));
        return new EmptySlotsSymbolMetadata(id, role,
            paytable.Rules.Where(rule => rule.SymbolId == id).Select(rule => new PayoutRule
            {
                SymbolId = rule.SymbolId, MatchLength = rule.MatchLength, Multiplier = rule.Multiplier
            }).ToArray(), onStrips, onStrips && (id != "S10" || game.Energy is not null),
            id is "S07" or "S11" or "S20" or "S21" or "S22" or "S23", multiplier, track);
    }).ToArray();
}
