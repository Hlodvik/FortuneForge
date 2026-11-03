using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Configuration;

internal static class RainbowRealmSlotDefinitions
{
    internal const string EngineVersion = "rainbow-realm-charm-harvest-v1";
    internal const string SymbolSetId = "rainbow-realm-symbols-v1";
    internal const string ReelSetId = "rainbow-realm-reels-v1";
    internal const string PaytableId = "rainbow-realm-paytable-v1";

    private static readonly (string Symbol, int Count)[] Weights =
    [
        ("A", 90), ("K", 95), ("Q", 100), ("J", 105),
        ("2", 75), ("3", 72), ("4", 68), ("5", 60), ("6", 56), ("7", 49),
        ("ACE", 38), ("POWER", 64), ("BANANA", 49), ("FREE", 35),
        ("SEAL_SYNC", 11), ("SEAL_ROWS", 11), ("SEAL_PAW", 11), ("SEAL_RAND", 11)
    ];

    private static readonly (string Symbol, long Three, long Five)[] Payouts =
    [
        ("J", 1, 2), ("Q", 1, 3), ("K", 1, 4), ("A", 1, 5),
        ("2", 1, 4), ("3", 1, 2), ("4", 1, 7), ("5", 2, 6), ("6", 2, 8), ("7", 3, 11),
        ("ACE", 5, 18), ("POWER", 2, 4)
    ];

    internal static ReelSetDefinition Reels { get; } = new()
    {
        Id = ReelSetId,
        SymbolSetId = SymbolSetId,
        Reels = Enumerable.Range(0, 5).Select(CreateStrip).ToList()
    };

    internal static bool IsModel(GameDefinition game) =>
        game.Id == SlotSpecialRoundProfiles.RainbowRealmGameId && game.Math.PaytableId == PaytableId;

    internal static GameSymbolRules CreateSymbolRules(GameSymbolRules source) => new()
    {
        SymbolSetId = SymbolSetId,
        WildSymbolId = source.WildSymbolId,
        NativeWildMatchLengths = [.. source.NativeWildMatchLengths],
        WildSubstitutionMatchLengths = [.. source.WildSubstitutionMatchLengths]
    };

    internal static SymbolSetDefinition CreateSymbolSet() => new()
    {
        Id = SymbolSetId,
        Symbols = Weights.Select(row => new SymbolDefinition { Id = row.Symbol })
            .Append(new SymbolDefinition { Id = "PAW" }).ToList()
    };

    internal static PaytableDefinition CreatePaytable() => new()
    {
        Id = PaytableId,
        SymbolSetId = SymbolSetId,
        Rules = Payouts.SelectMany(row => new[]
        {
            new PayoutRule { SymbolId = row.Symbol, MatchLength = 3, Multiplier = row.Three },
            new PayoutRule { SymbolId = row.Symbol, MatchLength = 5, Multiplier = row.Five }
        }).ToList()
    };

    private static List<string> CreateStrip(int reel)
    {
        var strip = Weights.SelectMany(row => Enumerable.Repeat(row.Symbol, row.Count)).ToList();
        // Keep the authored order reproducible. The engine selects stops cryptographically.
        var state = unchecked(0x5241494eu + (uint)reel * 0x9e3779b9u);
        for (var index = strip.Count - 1; index > 0; index--)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            var other = (int)(state % (uint)(index + 1));
            (strip[index], strip[other]) = (strip[other], strip[index]);
        }
        return strip;
    }
}
