using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RobotRevolution;

/// <summary>Versioned centre-reel Eclipse strips and paid ritual economics.</summary>
public static class FifthSunTotalityDefinitions
{
    public const string SymbolSetId = "fifth-sun-symbols-v2";
    public const string ReelSetId = "fifth-sun-reels-v2";
    public const string PaytableId = "fifth-sun-paytable-v2";
    public const string SolarSymbol = "SOLAR_ECLIPSE";
    public const string LunarSymbol = "LUNAR_ECLIPSE";
    public const string SolarMode = "solar-eclipse";
    public const string LunarMode = "lunar-eclipse";
    public const int EclipseRounds = 5;
    public const int AlignmentsPerRound = 3;
    public const decimal EclipseWheelScale = 1.10m;
    public const decimal TotalityAwardMultiplier = 1m;
    public const decimal OrdinaryPayoutMultiplier = 0.98m;
    public const decimal EclipsePayoutMultiplier = 0.35m;

    public static bool IsMode(string? mode) => mode is SolarMode or LunarMode;
    public static bool IsEclipseSymbol(string symbol) => symbol is SolarSymbol or LunarSymbol;
    public static string? TriggerMode(IReadOnlyList<IReadOnlyList<string>> reels) =>
        reels[2].Contains(SolarSymbol) ? SolarMode : reels[2].Contains(LunarSymbol) ? LunarMode : null;

    public static FrozenSlotDefinitions Create(SlotProfile profile, string? mode = null)
    {
        if (mode is not null && !IsMode(mode)) throw new ArgumentException("Unknown Eclipse mode.", nameof(mode));
        var previous = FifthSunDefinitions.Create(profile);
        var old = previous.CreateGame();
        var game = new GameDefinition
        {
            Id = old.Id, Layout = old.Layout, Matching = old.Matching, Wagering = old.Wagering,
            SpecialPoints = old.SpecialPoints, Energy = old.Energy, Paylines = old.Paylines,
            Symbols = new() { SymbolSetId = SymbolSetId, WildSymbolId = old.Symbols.WildSymbolId,
                PaylineSymbolIds = old.Symbols.PaylineSymbolIds, NativeWildMatchLengths = old.Symbols.NativeWildMatchLengths,
                WildSubstitutionMatchLengths = old.Symbols.WildSubstitutionMatchLengths },
            Math = new() { ReelSetId = mode is null ? ReelSetId : ReelSetId + "-" + mode, PaytableId = PaytableId,
                PayoutMultiplier = old.Math.PayoutMultiplier, PaylinePayoutSteps = old.Math.PaylinePayoutSteps,
                FiveMatchPityMissLimit = old.Math.FiveMatchPityMissLimit, Targets = old.Math.Targets }
        };
        var symbols = new SymbolSetDefinition { Id = SymbolSetId,
            Symbols = previous.CreateSymbolSet().Symbols.Where(symbol => symbol.Id != "ECLIPSE").ToList() };
        symbols.Symbols.Add(new SymbolDefinition { Id = SolarSymbol });
        symbols.Symbols.Add(new SymbolDefinition { Id = LunarSymbol });
        var paytable = new PaytableDefinition { Id = PaytableId, SymbolSetId = SymbolSetId, Rules = previous.CreatePaytable().Rules };
        var previousStrips = previous.CreateReelSet().Reels;
        var strips = Enumerable.Range(0, 5).Select(reel =>
        {
            var strip = mode is null ? previousStrips[reel].Where(symbol => symbol != "ECLIPSE").ToList()
                : ModeStrip(reel);
            if (mode is null && reel == 2)
            {
                // Two rare, widely separated markers. Even after a common-symbol boost,
                // every four-cell window contains at most one Eclipse.
                var ordinary = strip.ToArray();
                strip.Clear();
                for (var repeat = 0; repeat < 8; repeat++)
                {
                    strip.AddRange(ordinary);
                    if (repeat == 1) strip.Add(SolarSymbol);
                    if (repeat == 5) strip.Add(LunarSymbol);
                }
            }
            return strip;
        }).ToList();
        return new(game, symbols, new ReelSetDefinition
        { Id = game.Math.ReelSetId, SymbolSetId = SymbolSetId, Reels = strips }, paytable);
    }

    private static List<string> ModeStrip(int reel)
    {
        var weights = new (string Symbol, int Weight)[]
        {
            ("GLYPH_FLAME", 6), ("GLYPH_RAIN", 6), ("GLYPH_WIND", 6), ("GLYPH_EARTH", 6), ("GLYPH_REED", 6),
            ("FEATHER", 8), ("SERPENT", 11), ("EAGLE", 11), ("JAGUAR", 11), ("SUNSTONE", 11), ("WILD", 3)
        };
        var strip = weights.SelectMany(value => Enumerable.Repeat(value.Symbol, value.Weight)).ToList();
        var state = 0xEC11A5E2u ^ unchecked((uint)(reel + 1) * 0x9E3779B9u);
        for (var index = strip.Count - 1; index > 0; index--)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            var other = (int)(state % (uint)(index + 1));
            (strip[index], strip[other]) = (strip[other], strip[index]);
        }
        return strip;
    }
}
