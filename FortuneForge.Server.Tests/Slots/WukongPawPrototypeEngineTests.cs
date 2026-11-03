using System.Text.Json;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class WukongPawPrototypeEngineTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 150)]
    [InlineData(2, 300)]
    [InlineData(3, 450)]
    public void PreviewEveryPawCollectsEveryDisplayedCashValue(int paws, long expected)
    {
        var engine = Engine(Grid(paws, cash: true));
        var result = engine.SpinWukongPawPrototype(Admission());
        Assert.Equal(paws, result.MonkeyPawCount);
        Assert.Equal(expected, result.MoneyGrabPoints);
        Assert.Equal(expected, result.Payout.TotalPoints);
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
    }

    [Fact]
    public void SeveralPawsWithNoCashCanPayZero()
    {
        var result = Engine(Grid(3, cash: false)).SpinWukongPawPrototype(Admission());
        Assert.Equal(3, result.MonkeyPawCount);
        Assert.Equal(0, result.MoneyGrabPoints);
        Assert.Equal(0, result.Payout.TotalPoints);
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
    }

    [Fact]
    public void PlacementParticipatesBeforeEvaluationAndIsCapturedForReplay()
    {
        var result = Engine(Grid(0, cash: true)).SpinWukongPawPrototype(Admission(column: 0, earn: true));
        Assert.Equal("PAW", result.Reels[0][0]);
        Assert.Equal(WukongPawPrototypeEvidence.EarnSymbolId, result.Reels[1][^1]);
        Assert.Equal(1, result.MonkeyPawCount);
        Assert.Equal(150, result.MoneyGrabPoints);
        Assert.NotNull(result.EngineAudit!.WukongPawPrototype);
        Assert.Equal(WukongPawPrototypeEvidence.EngineVersion, result.EngineAudit.EngineVersion);
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
        var changed = result with { EngineAudit = result.EngineAudit with
        {
            WukongPawPrototype = result.EngineAudit.WukongPawPrototype! with { ForcedPawColumn = 2 }
        }};
        Assert.Contains(SlotRoundAudit.Check(changed, changed.Payout), finding => finding.Code == "slot-wukong-prototype-invalid");
        var stripped = result with { EngineAudit = result.EngineAudit with { WukongPawPrototype = null } };
        Assert.Contains(SlotRoundAudit.Check(stripped, stripped.Payout), finding => finding.Code == "slot-wukong-prototype-version-mismatch");
    }

    [Fact]
    public void LegacySpinKeepsItsOriginalTwoPawCapScaleAndEvidenceShape()
    {
        var result = Engine(Grid(3, cash: true)).Spin(SlotSpecialRoundProfiles.WukongGameId, 100, "legacy", false);
        Assert.Equal(3, result.MonkeyPawCount);
        Assert.Equal(284, result.MoneyGrabPoints); // 100 * 1.5 * 2 * .946, rounded once.
        Assert.Equal(SpinService.AuditEngineVersion, result.EngineAudit!.EngineVersion);
        Assert.Null(result.EngineAudit.WukongPawPrototype);
        Assert.DoesNotContain("wukongPawPrototype", JsonSerializer.Serialize(result.EngineAudit,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReplayRejectsAnEarnInjectionFlagThatDoesNotMatchTheCapturedGrid(bool injected)
    {
        var result = Engine(Grid(0, cash: true)).SpinWukongPawPrototype(Admission(column: 0, earn: injected));
        var tampered = result with { EngineAudit = result.EngineAudit! with
        {
            WukongPawPrototype = result.EngineAudit!.WukongPawPrototype! with { InjectEarnSymbol = !injected }
        }};
        Assert.Contains(SlotRoundAudit.Check(tampered, tampered.Payout), finding => finding.Code == "slot-wukong-earn-marker-mismatch");
    }

    [Fact]
    public void ReplayRejectsAMovedNonpayingEarnMarker()
    {
        var result = Engine(Grid(0, cash: true)).SpinWukongPawPrototype(Admission(column: 0, earn: true));
        var changed = result.Reels.Select(reel => reel.ToArray()).ToArray();
        changed[1][^1] = "2";
        changed[2][^1] = WukongPawPrototypeEvidence.EarnSymbolId;
        var tampered = result with { Reels = changed };
        Assert.Contains(SlotRoundAudit.Check(tampered, tampered.Payout), finding => finding.Code == "slot-wukong-earn-marker-mismatch");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void InvalidColumnIsRejectedBeforeGeneratingAResult(int column)
    {
        var generator = new FixedReels(Grid(0, cash: true));
        var engine = Engine(generator);
        Assert.Throws<ArgumentException>(() => engine.SpinWukongPawPrototype(Admission(column)));
        Assert.Equal(0, generator.Calls);
    }

    private static WukongPawPrototypeAdmission Admission(int? column = null, bool earn = false) =>
        new("synthetic-test-session", 100, column, earn, "synthetic-admission", column is null ? null : "fixture-grant");

    private static IReadOnlyList<IReadOnlyList<string>> Grid(int paws, bool cash)
    {
        var grid = Enumerable.Range(0, 5).Select(_ => new[] { "2", "2", "2", "2" }).ToArray();
        for (var i = 0; i < paws; i++) grid[i][0] = "PAW";
        if (cash) { grid[3][0] = "RAND_1"; grid[4][0] = "RAND_05"; }
        return grid;
    }

    private static SpinService Engine(IReadOnlyList<IReadOnlyList<string>> grid) => Engine(new FixedReels(grid));
    private static SpinService Engine(FixedReels reels) => new(new Definitions(reels.Grid), reels,
        new CombinationEvaluator(), new PayoutCalculator(), new NoFeatureInjection());

    private sealed class NoFeatureInjection : IRandomIndexSource
    {
        private int index;
        private readonly int[] draws = [0, 1, 1, 0, 99];
        public int Next(int maximumExclusive) => Math.Min(draws[index++ % draws.Length], maximumExclusive - 1);
    }

    private sealed class FixedReels(IReadOnlyList<IReadOnlyList<string>> grid) : IReelGenerator
    {
        public IReadOnlyList<IReadOnlyList<string>> Grid { get; } = grid;
        public int Calls { get; private set; }
        public ReelOutcome Generate(GameDefinition game, ReelSetDefinition reelSet, SymbolSetDefinition symbols)
        { Calls++; return new([0, 0, 0, 0, 0], Grid); }
    }

    private sealed class Definitions(IReadOnlyList<IReadOnlyList<string>> grid) : ISlotsDefinitionProvider
    {
        private readonly GameDefinition game = new()
        {
            Id = SlotSpecialRoundProfiles.WukongGameId,
            Layout = new() { ReelCount = 5, VisibleRows = 4, PaylineCount = 1 },
            Symbols = new() { SymbolSetId = "fixture-symbols", WildSymbolId = "ACE" },
            Matching = new() { MinimumRunLength = 3, AllowMultipleRunsPerPayline = true },
            Math = new() { ReelSetId = "fixture-reels", PaytableId = "fixture-paytable", PayoutMultiplier = .946m,
                PaylinePayoutSteps = [0], Targets = new() },
            Wagering = new() { PointValueInCents = 25, MinimumWagerPoints = 2, MaximumWagerPoints = 2000,
                WagerIncrementPoints = 2 },
            Paylines = [[0, 0, 0, 0, 0]]
        };
        public GameDefinition? GetGame(string id) => id == game.Id ? game : null;
        public SymbolSetDefinition? GetSymbolSet(string id) => id == "fixture-symbols" ? new()
        { Id = id, Symbols = new[] { "2", "ACE", "PAW", "RAND_1", "RAND_05" }.Select(value => new SymbolDefinition { Id = value }).ToList() } : null;
        public ReelSetDefinition? GetReelSet(string id) => id == "fixture-reels" ? new()
        { Id = id, SymbolSetId = "fixture-symbols", Reels = grid.Select(reel => reel.ToList()).ToList() } : null;
        public PaytableDefinition? GetPaytable(string id) => id == "fixture-paytable" ? new()
        { Id = id, SymbolSetId = "fixture-symbols", Rules = [] } : null;
    }
}
