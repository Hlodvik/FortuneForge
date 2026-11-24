using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RobotRevolution;

/// <summary>The Sunstone Eclipse vocabulary, paying rules and immutable reel strips belong only to this game.</summary>
public static class FifthSunDefinitions
{
    public const string SymbolSetId = "fifth-sun-symbols-v1";
    public const string ReelSetId = "fifth-sun-reels-v1";
    public const string PaytableId = "fifth-sun-paytable-v1";
    public const long MinimumWagerPoints = 12;
    public const long DefaultWagerPoints = 12;
    public const long MaximumWagerPoints = 300;
    public const long WagerIncrementPoints = 2;

    private static readonly (string Id, int Weight, long Three, long Five)[] PayingSymbols =
    [
        ("GLYPH_FLAME", 15, 1, 3), ("GLYPH_RAIN", 14, 1, 4),
        ("GLYPH_WIND", 13, 1, 5), ("GLYPH_EARTH", 11, 1, 6),
        ("GLYPH_REED", 9, 1, 7), ("FEATHER", 8, 2, 8),
        ("SERPENT", 7, 3, 10), ("EAGLE", 6, 4, 12),
        ("JAGUAR", 5, 5, 16), ("SUNSTONE", 7, 8, 32),
        ("WILD", 2, 6, 24)
    ];

    public static FrozenSlotDefinitions Create(SlotProfile profile)
    {
        var paylines = ClassicSlotDefinitionFactory.CreatePaylines();
        var symbolIds = PayingSymbols.Select(symbol => symbol.Id).Append("ECLIPSE").ToArray();
        var game = new GameDefinition
        {
            Id = profile.GameId,
            Layout = new() { ReelCount = 5, VisibleRows = 4, PaylineCount = paylines.Count },
            Symbols = new()
            {
                SymbolSetId = SymbolSetId, WildSymbolId = "WILD",
                NativeWildMatchLengths = [3, 5], WildSubstitutionMatchLengths = [3, 5],
                PaylineSymbolIds = PayingSymbols.Where(symbol => symbol.Id != "WILD").Select(symbol => symbol.Id).ToList()
            },
            Matching = new() { MinimumRunLength = 3, AllowMultipleRunsPerPayline = true },
            Math = new()
            {
                ReelSetId = ReelSetId, PaytableId = PaytableId, PayoutMultiplier = profile.PayoutMultiplier,
                PaylinePayoutSteps = paylines.Select(_ => 0).ToList(), FiveMatchPityMissLimit = 12,
                Targets = new() { HitRate = profile.TargetHitRate }
            },
            Wagering = new()
            {
                PointValueInCents = 25, MinimumWagerPoints = MinimumWagerPoints,
                MaximumWagerPoints = MaximumWagerPoints, WagerIncrementPoints = WagerIncrementPoints
            },
            FreeGames = new() { SymbolId = "ECLIPSE", RequiredSymbols = 3, AwardedSpins = 7, VisibleFrequencyDivisor = 2 },
            SpecialPoints = new()
            {
                SymbolId = "SUNSTONE", ThreeMatchPoints = 1, FiveMatchPoints = 2, ActivationCost = 8,
                CommonSymbolIds = PayingSymbols.Take(5).Select(symbol => symbol.Id).ToList()
            },
            Paylines = paylines
        };
        var symbols = new SymbolSetDefinition { Id = SymbolSetId, Symbols = symbolIds.Select(id => new SymbolDefinition { Id = id }).ToList() };
        var strips = Enumerable.Range(0, 5).Select(reel =>
        {
            var strip = PayingSymbols.SelectMany(symbol => Enumerable.Repeat(symbol.Id, symbol.Weight))
                .Concat(Enumerable.Repeat("ECLIPSE", 3)).ToList();
            // This explicit generator fixes the public strip layout across framework versions.
            // Actual spins still use the caller's audited cryptographic random source.
            var state = 0xF1F75A19u ^ unchecked((uint)(reel + 1) * 0x9E3779B9u);
            for (var index = strip.Count - 1; index > 0; index--)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                var other = (int)(state % (uint)(index + 1));
                (strip[index], strip[other]) = (strip[other], strip[index]);
            }
            return strip;
        }).ToList();
        var reels = new ReelSetDefinition { Id = ReelSetId, SymbolSetId = SymbolSetId, Reels = strips };
        var paytable = new PaytableDefinition
        {
            Id = PaytableId, SymbolSetId = SymbolSetId,
            Rules = PayingSymbols.SelectMany(symbol => new[]
            {
                new PayoutRule { SymbolId = symbol.Id, MatchLength = 3, Multiplier = symbol.Three },
                new PayoutRule { SymbolId = symbol.Id, MatchLength = 5, Multiplier = symbol.Five }
            }).ToList()
        };
        return new(game, symbols, reels, paytable);
    }
}
