using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

// Disco owns its seven-symbol pool and cluster paytable independently of the
// base game's line awards and feature symbols.
public static class NeonDiscoDefinitions
{
    public const string SymbolSetId = "neon-disco-symbols-v1";
    public const string PreviousReelSetId = "neon-disco-reels-v1";
    public const string PreviousPaytableId = "neon-disco-paytable-v1";
    public const string ReelSetId = "neon-disco-reels-v2";
    public const string PaytableId = "neon-disco-paytable-v2";
    public const int MinimumClusterSize = 4;
    public const decimal PayoutScale = 0.01m;
    private const int MaximumClusterSize = 20;
    private const int StopsPerSymbol = 128;
    private const int PairedBlocksPerSymbol = 13;
    public static IReadOnlyList<string> SymbolIds { get; } =
        Array.AsReadOnly(new[] { "2", "4", "5", "7", "DISCO_MARTINI", "DISCO_SEVEN", "DISCO_BALL" });

    private static readonly long[] PreviousFourSymbolAwards = [10, 20, 30, 40, 60, 80, 100];
    private static readonly long[] FourSymbolAwards = [275, 280, 286, 291, 302, 313, 324];
    private static readonly int[] PreviousSizeHalfUnits = [2, 3, 4, 6, 8, 10, 12, 16, 20, 24, 30, 36, 42, 50, 60, 70, 80];

    public static GameDefinition CreateGame(GameDefinition baseGame) => new()
    {
        Id = baseGame.Id,
        Layout = new() { ReelCount = 5, VisibleRows = 4, PaylineCount = 0 },
        Symbols = new()
        {
            SymbolSetId = SymbolSetId, WildSymbolId = string.Empty,
            PaylineSymbolIds = [.. SymbolIds]
        },
        Matching = new() { MinimumRunLength = MinimumClusterSize },
        Math = new()
        {
            ReelSetId = ReelSetId, PaytableId = PaytableId,
            PayoutMultiplier = PayoutScale, Targets = new()
        },
        Wagering = baseGame.Wagering
    };

    public static SymbolSetDefinition CreateSymbolSet() => new()
    {
        Id = SymbolSetId,
        Symbols = SymbolIds.Select(id => new SymbolDefinition { Id = id }).ToList()
    };

    public static PaytableDefinition CreatePaytable() =>
        CreatePaytable(PaytableId, FourSymbolAwards, static (award, extraSymbols) => award * (100 + 5 * extraSymbols) / 100);

    public static PaytableDefinition CreatePreviousPaytable() =>
        CreatePaytable(PreviousPaytableId, PreviousFourSymbolAwards,
            static (award, extraSymbols) => award * PreviousSizeHalfUnits[extraSymbols] / 2);

    private static PaytableDefinition CreatePaytable(string id, IReadOnlyList<long> fourSymbolAwards,
        Func<long, int, long> sizeAward) => new()
    {
        Id = id, SymbolSetId = SymbolSetId,
        Rules = SymbolIds.SelectMany((symbol, index) =>
            Enumerable.Range(0, MaximumClusterSize - MinimumClusterSize + 1).Select(extraSymbols => new PayoutRule
            {
                SymbolId = symbol, MatchLength = MinimumClusterSize + extraSymbols,
                Multiplier = sizeAward(fourSymbolAwards[index], extraSymbols)
            })).ToList()
    };

    public static ReelSetDefinition CreateReelSet() => CreateReelSet(ReelSetId, CreateStrip);

    public static ReelSetDefinition CreatePreviousReelSet() => CreateReelSet(PreviousReelSetId, CreatePreviousStrip);

    private static ReelSetDefinition CreateReelSet(string id, Func<int, List<string>> createStrip) => new()
    {
        Id = id, SymbolSetId = SymbolSetId,
        Reels = Enumerable.Range(0, 5).Select(createStrip).ToList()
    };

    private static List<string> CreateStrip(int reel)
    {
        // Pairs tune the initial window's hit rate. Each symbol retains the
        // same number of stops, so single-symbol cascade refills stay uniform.
        var blocks = SymbolIds.SelectMany(id => Enumerable.Repeat((Symbol: id, Length: 2), PairedBlocksPerSymbol)
            .Concat(Enumerable.Repeat((Symbol: id, Length: 1), StopsPerSymbol - 2 * PairedBlocksPerSymbol))).ToList();
        Shuffle(blocks, reel, 0x44495345u);
        return blocks.SelectMany(block => Enumerable.Repeat(block.Symbol, block.Length)).ToList();
    }

    private static List<string> CreatePreviousStrip(int reel)
    {
        var strip = SymbolIds.SelectMany(id => Enumerable.Repeat(id, StopsPerSymbol)).ToList();
        Shuffle(strip, reel, 0x44495343u);
        return strip;
    }

    private static void Shuffle<T>(List<T> strip, int reel, uint seed)
    {
        var state = unchecked(seed + (uint)reel * 0x9e3779b9u);
        for (var index = strip.Count - 1; index > 0; index--)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            var other = (int)(state % (uint)(index + 1));
            (strip[index], strip[other]) = (strip[other], strip[index]);
        }
    }
}
