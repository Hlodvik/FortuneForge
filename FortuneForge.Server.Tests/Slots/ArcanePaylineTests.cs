using System.Text.Json;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Games;
using FortuneForge.Server.Slots.Payouts;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class ArcanePaylineTests
{
    private const long Wager = 12;

    [Theory]
    [InlineData(0, -1)]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, -1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(2, -1)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    public void AnExactStraightTriplePaysFromEveryEligibleStart(int start, int step)
    {
        var firstRow = step < 0 ? 2 : 0;
        var positions = Enumerable.Range(0, 3).Select(offset =>
            new GridPosition(start + offset, firstRow + offset * step)).ToArray();
        var game = ArcaneArchivesEngine.CreateGame();
        var payout = PriceAndCompareWithHost(game, ReelWindow(positions));
        var line = Assert.Single(payout.Paylines);
        var paid = Assert.Single(line.Matches);
        Assert.True(line.PaylineId > game.Layout.PaylineCount);
        Assert.Equal(3, paid.Match.MatchLength);
        Assert.Equal(positions, paid.Match.Positions);
        Assert.Equal((long)Math.Round(Wager * game.Math.PayoutMultiplier, MidpointRounding.AwayFromZero), payout.TotalPoints);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, -1)]
    public void FourOnAStraightSpanDoNotPayOrFallBackToThree(int start, int step)
    {
        var firstRow = step < 0 ? 3 : 0;
        var positions = Enumerable.Range(0, 4).Select(offset =>
            new GridPosition(start + offset, firstRow + offset * step)).ToArray();
        Assert.Empty(PriceAndCompareWithHost(ArcaneArchivesEngine.CreateGame(), ReelWindow(positions)).Paylines);
    }

    [Fact]
    public void ASeparateCleanTripleCanPayBesideABentFourthSymbol()
    {
        GridPosition[] positions = [new(0, 0), new(1, 1), new(2, 2), new(3, 1)];
        var paid = Assert.Single(Assert.Single(
            PriceAndCompareWithHost(ArcaneArchivesEngine.CreateGame(), ReelWindow(positions)).Paylines).Matches);
        Assert.Equal(positions.Take(3), paid.Match.Positions);
    }

    [Fact]
    public void AFullPaylinePaysOnceWithoutPayingForItsThreeCellSubsets()
    {
        GridPosition[] positions = [new(0, 0), new(1, 1), new(2, 2), new(3, 1), new(4, 0)];
        var line = Assert.Single(PriceAndCompareWithHost(ArcaneArchivesEngine.CreateGame(), ReelWindow(positions)).Paylines);
        Assert.Equal(5, line.PaylineId);
        var paid = Assert.Single(line.Matches);
        Assert.Equal(5, paid.Match.MatchLength);
        Assert.Equal(positions, paid.Match.Positions);
    }

    [Theory]
    [InlineData(ArcaneArchivesEngine.PreviousPotionEngineVersion, SlotPaylineMatchMode.Classic, 716, 1, false)]
    [InlineData(ArcaneArchivesEngine.PreviousExactPaylineEngineVersion, SlotPaylineMatchMode.ExactStraightThreeAndFullPayline, 459, 1, true)]
    [InlineData(ArcaneArchivesEngine.EngineVersion, SlotPaylineMatchMode.ExactStraightThreeAndFullPayline, 357, 2, true)]
    public void ThePaidWorkerHonorsEachVersionAndItsPayoutsReplay(
        string version, SlotPaylineMatchMode mode, int multiplierThousandths, int scribeTripleMultiplier, bool hasStrongerMandrake)
    {
        var potion = new ArcanePotionBottle(1, "MANDRAKE_ROOT", 0);
        var options = new PaidArcaneSpinOptions(version, true, new(potion.Id, potion.IngredientId, potion.Potency));
        var request = options.WriteTo(new(Guid.NewGuid(), ArcaneArchivesEngine.GameId, "arcane-payline-test",
            Wager, false, false, 0, null, 0, null));
        var worker = new ArcanePaidSlotOutcomes(new SeededRandom());
        var response = worker.Resolve(request);
        var outcome = response.Outcome.Deserialize<ClassicSlotSpinOutcome>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(outcome);
        Assert.Equal(ArcanePaidSlotOutcomes.OutcomeKind, response.OutcomeKind);
        Assert.Equal(version, outcome.Audit.EngineVersion);
        Assert.Equal(mode, outcome.Audit.BaseGame.Matching.PaylineMatchMode);
        Assert.Equal(multiplierThousandths / 1_000m, outcome.Audit.BaseGame.Math.PayoutMultiplier);
        Assert.Equal(version, ArcaneArchivesEngine.WandEngineVersion(outcome.Audit.BaseGame));
        Assert.Equal(hasStrongerMandrake, !ArcaneArchivesEngine.UsesLegacyMandrake(version));
        Assert.True(ArcaneArchivesEngine.IsBoostedReelSet(outcome.ReelSetId));
        Assert.NotNull(ArcaneArchivesEngine.FindReelSet(outcome.ReelSetId));
        var paytable = ArcaneArchivesEngine.FindPaytable(outcome.Audit.BaseGame.Math.PaytableId);
        Assert.NotNull(paytable);
        Assert.Equal(scribeTripleMultiplier,
            Assert.Single(paytable.Rules, rule => rule.SymbolId == "4" && rule.MatchLength == 3).Multiplier);
        var tome = ArcaneTome.SelectPage(ArcaneTome.Empty with { GrimoireReady = true }, "herbalism", true);
        ArcaneNativeReplay.Validate(outcome, "arcane-payline-test", Wager, false, version, tome, potion, 0);

        GridPosition[] four = [new(0, 0), new(1, 0), new(2, 0), new(3, 0)];
        var comparison = PriceAndCompareWithHost(outcome.Audit.BaseGame, ReelWindow(four));
        if (mode == SlotPaylineMatchMode.Classic)
            Assert.Equal(4, Assert.Single(Assert.Single(comparison.Paylines).Matches).Match.MatchLength);
        else Assert.Empty(comparison.Paylines);

        var triple = PriceAndCompareWithHost(outcome.Audit.BaseGame, ReelWindow(four.Take(3), "4"));
        Assert.Equal(scribeTripleMultiplier,
            Assert.Single(Assert.Single(triple.Paylines).Matches).Multiplier);

        var tamperedPaytable = new PaytableDefinition
        {
            Id = paytable.Id,
            SymbolSetId = paytable.SymbolSetId,
            Rules = paytable.Rules.Skip(1).ToList(),
        };
        var tampered = outcome with { Audit = outcome.Audit with { Paytable = tamperedPaytable } };
        Assert.Throws<InvalidOperationException>(() => ArcaneNativeReplay.Validate(tampered,
            "arcane-payline-test", Wager, false, version, tome, potion, 0));
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReelWindow(IEnumerable<GridPosition> positions, string symbolId = "2")
    {
        var reels = Enumerable.Range(0, 5).Select(_ =>
            Enumerable.Repeat(ArcaneWandCollector.WandSymbolId, 4).ToArray()).ToArray();
        foreach (var position in positions)
            reels[position.Reel][position.Row] = symbolId;
        return reels;
    }

    private static SpinPayout PriceAndCompareWithHost(GameDefinition game, IReadOnlyList<IReadOnlyList<string>> reels)
    {
        var symbols = ArcaneArchivesEngine.CreateSymbolSet();
        var paytable = ArcaneArchivesEngine.FindPaytable(game.Math.PaytableId)!;
        var payout = ClassicSlotEngine.CalculatePayout(ClassicSlotEngine.Evaluate(reels, game, symbols), game, paytable, Wager);
        var host = new PayoutCalculator().Calculate(new CombinationEvaluator().Evaluate(reels, game, symbols), game, paytable, Wager);
        Assert.Equal(JsonSerializer.Serialize(payout), JsonSerializer.Serialize(host));
        return payout;
    }

    private sealed class SeededRandom : IGameRandomSource
    {
        private readonly Random random = new(20261120);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
