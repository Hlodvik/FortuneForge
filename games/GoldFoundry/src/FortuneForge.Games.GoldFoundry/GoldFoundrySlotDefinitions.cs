using FortuneForge.Games.Slots;

namespace FortuneForge.Games.GoldFoundry;

/// <summary>The complete, versioned Gold Foundry definition.</summary>
public static class GoldFoundrySlotDefinitions
{
    public const string GameId = "gold-foundry-v1";
    public const string SymbolSetId = "gold-foundry-symbols-v1";
    public const string ReelSetId = "gold-foundry-reels-v1";
    public const string PaytableId = "gold-foundry-paytable-v1";
    public const decimal LinePayoutUnit = 0.05m;

    private static readonly (string Symbol, long Three, long Four, long Five)[] LineAwards =
    [
        ("2", 2, 6, 20), ("3", 2, 7, 22), ("4", 3, 8, 26), ("5", 3, 9, 30),
        ("6", 4, 10, 36), ("7", 5, 12, 40), ("FREE", 5, 13, 44), ("POWER", 6, 15, 48),
        ("SEAL_ROWS", 5, 10, 30), ("SEAL_SYNC", 6, 15, 50),
        ("SEAL_PAW", 8, 20, 70), ("SEAL_RAND", 12, 30, 100), ("ACE", 16, 40, 140)
    ];

    private static readonly (string Symbol, int Count)[] StripWeights =
    [
        ("2", 9), ("3", 9), ("4", 9), ("5", 9), ("6", 5), ("7", 5),
        ("FREE", 3), ("POWER", 3),
        ("SEAL_ROWS", 2), ("SEAL_SYNC", 2), ("SEAL_PAW", 2), ("SEAL_RAND", 2),
        ("ACE", 1), ("BANANA", 1), ("BOLT", 1), ("PAW", 1)
    ];

    public static GoldFoundryProfile Profile { get; } = new(
        GameId, 0, 0, 0, 0, false, true, false, null, null, null, null,
        [1, 2, 3, 4, 5, 6, 16, 17, 18, 19, 20, 21, 22, 23], LinePayoutUnit);

    public static bool IsModel(GameDefinition game) =>
        game.Id == GameId && game.Math.PaytableId == PaytableId;

    public static void ValidateSpinRequest(bool useFreeSpin)
    {
        if (useFreeSpin)
            throw new ArgumentException("Gold Foundry does not offer free spins.", nameof(useFreeSpin));
    }

    public static GameDefinition CreateGame() => new()
    {
        Id = GameId,
        Layout = new() { ReelCount = 5, VisibleRows = 4, PaylineCount = 14 },
        Symbols = new()
        {
            SymbolSetId = SymbolSetId,
            WildSymbolId = "ACE",
            PaylineSymbolIds = LineAwards.Select(row => row.Symbol).ToList(),
            NativeWildMatchLengths = [3, 4, 5],
            WildSubstitutionMatchLengths = [5]
        },
        Matching = new() { MinimumRunLength = 3, AllowMultipleRunsPerPayline = true },
        Math = new()
        {
            ReelSetId = ReelSetId,
            PaytableId = PaytableId,
            PayoutMultiplier = LinePayoutUnit,
            PaylinePayoutSteps = Enumerable.Repeat(0, 14).ToList(),
            Targets = new()
        },
        Wagering = new()
        {
            PointValueInCents = 25m,
            MinimumWagerPoints = 2,
            MaximumWagerPoints = 2000,
            WagerIncrementPoints = 2
        },
        Energy = new() { SymbolId = "BOLT", PointsPerVisibleSymbol = 1 },
        Paylines =
        [
            [0, 0, 0, 0, 0], [1, 1, 1, 1, 1], [2, 2, 2, 2, 2], [3, 3, 3, 3, 3],
            [0, 1, 2, 1, 0], [1, 2, 3, 2, 1], [0, 1, 2, 3, 3], [3, 2, 1, 0, 0],
            [0, 1, 0, 1, 0], [1, 2, 1, 2, 1], [2, 3, 2, 3, 2], [1, 0, 1, 0, 1],
            [2, 1, 2, 1, 2], [3, 2, 3, 2, 3]
        ]
    };

    public static SymbolSetDefinition CreateSymbolSet() => new()
    {
        Id = SymbolSetId,
        Symbols = StripWeights.Select(row => new SymbolDefinition { Id = row.Symbol }).ToList()
    };

    public static PaytableDefinition CreatePaytable() => new()
    {
        Id = PaytableId,
        SymbolSetId = SymbolSetId,
        Rules = LineAwards.SelectMany(row => new[]
        {
            new PayoutRule { SymbolId = row.Symbol, MatchLength = 3, Multiplier = row.Three },
            new PayoutRule { SymbolId = row.Symbol, MatchLength = 4, Multiplier = row.Four },
            new PayoutRule { SymbolId = row.Symbol, MatchLength = 5, Multiplier = row.Five }
        }).ToList()
    };

    public static ReelSetDefinition CreateReelSet() => new()
    {
        Id = ReelSetId,
        SymbolSetId = SymbolSetId,
        Reels = Enumerable.Range(0, 5).Select(CreateStrip).ToList()
    };

    private static List<string> CreateStrip(int reel)
    {
        var strip = StripWeights.SelectMany(row => Enumerable.Repeat(
            row.Symbol == "PAW" && reel is not (0 or 4) ? "2" : row.Symbol, row.Count)).ToList();
        var state = unchecked(0x474f4c44u + (uint)reel * 0x9e3779b9u);
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

public sealed class GoldFoundryDefinitionSource : ISlotDefinitionSource
{
    public GameDefinition? GetGame(string id) => id == GoldFoundrySlotDefinitions.GameId
        ? GoldFoundrySlotDefinitions.CreateGame() : null;
    public SymbolSetDefinition? GetSymbolSet(string id) => id == GoldFoundrySlotDefinitions.SymbolSetId
        ? GoldFoundrySlotDefinitions.CreateSymbolSet() : null;
    public ReelSetDefinition? GetReelSet(string id) => id == GoldFoundrySlotDefinitions.ReelSetId
        ? GoldFoundrySlotDefinitions.CreateReelSet() : null;
    public PaytableDefinition? GetPaytable(string id) => id == GoldFoundrySlotDefinitions.PaytableId
        ? GoldFoundrySlotDefinitions.CreatePaytable() : null;
}
