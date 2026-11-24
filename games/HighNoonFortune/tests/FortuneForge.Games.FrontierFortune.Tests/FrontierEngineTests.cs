using FortuneForge.Games.FrontierFortune.Server;
using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.FrontierFortune.Tests;

public sealed class FrontierEngineTests
{
    internal static readonly string BonusId = "0123456789abcdef0123456789abcdef";
    internal static string[] Kinds => ["ricochet", "ricochet", "pouch", "pouch", "pouch", "ricochet", "bounty",
        "pouch", "pouch", "strongbox", "supplies", "supplies", "bounty", "strongbox", "strongbox"];
    internal static FrontierBonus Board(long wager = 4) => FrontierBounties.Create(BonusId, wager, Kinds);

    [Fact]
    public void TwelveSymbolsAndTwentyIndependentLinesHaveOneRulesOwner()
    {
        var rules = FrontierRules.Definition;
        Assert.Equal(12, rules.Symbols.Length);
        Assert.Equal(1000, rules.Symbols.Sum(x => x.Weight));
        Assert.Equal("WILD", rules.WildSymbol);
        Assert.NotEmpty(rules.Symbols.Single(x => x.Id == "BADGE").Payouts);
        Assert.NotEmpty(rules.Symbols.Single(x => x.Id == "BAR").Payouts);
        Assert.Equal(20, rules.Paylines.Select(x => string.Join(',', x)).Distinct().Count());
    }

    [Fact]
    public void LongestLeftRunPaysOnceAndScatterDoesNotSubstitute()
    {
        var reels = new[] { new[] { "J", "WANTED", "WANTED" }, new[] { "WILD", "WANTED", "WANTED" },
            new[] { "J", "WANTED", "WANTED" }, new[] { "J", "WANTED", "WANTED" }, new[] { "Q", "WANTED", "WANTED" } };
        var line = Assert.Single(FrontierFortuneEngine.Evaluate(reels, 4), x => x.Line == 1);
        Assert.Equal("J", line.Symbol);
        Assert.Equal(4, line.Count);
        Assert.Equal(8, line.AwardPoints);
        Assert.Equal(new[] { 0, 1, 2, 3 }, line.Cells.Select(x => x.Column));
        Assert.DoesNotContain(FrontierFortuneEngine.Evaluate(reels, 4), x => x.Line == 2);
    }

    [Fact]
    public void EveryWildBoardUsesHighestEligibleAwardAndEachLineOnce()
    {
        var reels = Enumerable.Range(0, 5).Select(_ => new[] { "WILD", "WILD", "WILD" }).ToArray();
        var wins = FrontierFortuneEngine.Evaluate(reels, 4);
        Assert.Equal(20, wins.Length);
        Assert.Equal(5600, wins.Sum(x => x.AwardPoints));
        Assert.All(wins, x => Assert.Equal("WILD", x.Symbol));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(5)] [InlineData(404)]
    public void WagerBoundsAndExactPointStepsAreEnforced(long wager) =>
        Assert.Throws<ArgumentException>(() => FrontierRules.ValidateWager(wager));

    [Fact]
    public void WantedDrawsCreateSixShotBoardAndReplayRequiresEveryExactDraw()
    {
        var outcome = new FrontierFortuneEngine(new WantedRandom()).Spin(4, BonusId);
        Assert.NotNull(outcome.Bonus);
        Assert.Equal(6, outcome.Bonus.ShotsRemaining);
        Assert.Equal(29, outcome.Draws.Length);
        var replay = new FrontierReplayRandom(outcome.Draws);
        var repeated = new FrontierFortuneEngine(replay).Spin(4, BonusId);
        replay.Complete();
        Assert.Equal(FrontierRoundIdentity.Hash(outcome), FrontierRoundIdentity.Hash(repeated));
        Assert.Throws<InvalidOperationException>(() => new FrontierFortuneEngine(new FrontierReplayRandom(outcome.Draws[..14])).Spin(4, BonusId));
    }

    [Fact]
    public void RicochetChainsHitEachNeighborOnceAndPreviewIsExactWithoutMutatingOriginal()
    {
        var board = Board();
        var preview = board.Previews.Single(x => x.TargetIndex == 0);
        var result = FrontierBounties.Shoot(board, 0);
        Assert.Equal(new[] { 0, 1, 5, 2, 6, 10 }, result.Shot!.HitIndices);
        Assert.Equal(6, result.Shot.HitIndices.Distinct().Count());
        Assert.Equal(1, result.Bonus.Cells[6].HitPoints);
        Assert.Equal(8, result.Bonus.CollectedPoints);
        Assert.Equal(6, result.Bonus.ShotsRemaining);
        Assert.Equal(1, result.Shot.ShotsRestored);
        Assert.Equal(FrontierRoundIdentity.Hash(preview), FrontierRoundIdentity.Hash(result.Shot));
        Assert.Equal(2, board.Cells[6].HitPoints);
        Assert.Equal(0, board.CollectedPoints);
        Assert.Equal(0, result.PayoutPoints);
    }

    [Fact]
    public void DamagePaysOnlyOnClearAndBankEndsWithoutExtraWager()
    {
        var first = FrontierBounties.Shoot(Board(), 6);
        Assert.Equal(0, first.Bonus.CollectedPoints);
        var second = FrontierBounties.Shoot(first.Bonus, 6);
        Assert.Equal(80, second.Bonus.CollectedPoints);
        var bank = FrontierBounties.Bank(second.Bonus);
        Assert.True(bank.Bonus.Completed);
        Assert.Equal(80, bank.PayoutPoints);
        Assert.Empty(bank.Bonus.Previews);
        Assert.Throws<ArgumentException>(() => FrontierBounties.Bank(bank.Bonus));
        Assert.Throws<ArgumentException>(() => FrontierBounties.Shoot(second.Bonus, 6));
    }

    [Fact]
    public void ClearingEveryPrizeAutoBanksEvenIfSuppliesRemain()
    {
        var bonus = Board();
        bonus = bonus with { Cells = bonus.Cells.Select(x => x.RewardPoints > 0 ? x with { HitPoints = 0 } : x).ToArray() };
        var target = bonus.Cells[2] with { HitPoints = 1 };
        bonus.Cells[2] = target;
        bonus = bonus with { CollectedPoints = bonus.Cells.Where(x => x.HitPoints == 0).Sum(x => x.RewardPoints) };
        var result = FrontierBounties.Shoot(bonus, 2);
        Assert.True(result.Bonus.Completed);
        Assert.Equal(260, result.PayoutPoints);
        Assert.Contains(result.Bonus.Cells, x => x.Kind == "supplies" && x.HitPoints > 0);
    }

    [Fact]
    public void LastShotSupplyRestoresBudgetBeforeCompletionIsChecked()
    {
        var board = Board();
        foreach (var index in new[] { 9, 9, 13, 13, 14 }) board = FrontierBounties.Shoot(board, index).Bonus;
        Assert.Equal(1, board.ShotsRemaining);
        var restored = FrontierBounties.Shoot(board, 10);
        Assert.Equal(2, restored.Bonus.ShotsRemaining);
        Assert.Equal(2, restored.Shot!.ShotsRestored);
        Assert.False(restored.Bonus.Completed);
        Assert.Equal(0, restored.PayoutPoints);
    }

    [Fact]
    public void PoliciesRejectActiveBonusSpinsStaleActionsAndTamperedReplay()
    {
        var before = FrontierAccountState.Empty() with { Revision = 3, Bonus = Board() };
        Assert.Throws<FrontierConflictException>(() => FrontierRoundPolicy.Prepare("player", new("operation-00000001", "spin", 4), before, new WantedRandom(), DateTime.UtcNow));
        Assert.Throws<FrontierConflictException>(() => FrontierRoundPolicy.Prepare("player", new("operation-00000002", "shoot", BonusId: BonusId, TargetIndex: 0, ExpectedRevision: 2), before, new WantedRandom(), DateTime.UtcNow));
        var round = FrontierRoundPolicy.Prepare("player", new("operation-00000003", "shoot", BonusId: BonusId, TargetIndex: 0, ExpectedRevision: 3), before, new WantedRandom(), DateTime.UtcNow);
        FrontierRoundPolicy.Validate(round);
        var tampered = round with { PayoutCents = 100 };
        tampered = tampered with { OutcomeSha256 = FrontierRoundPolicy.Digest(tampered) };
        Assert.Throws<InvalidOperationException>(() => FrontierRoundPolicy.Validate(tampered));
    }

    [Theory]
    [InlineData("activeAdmissionId")] [InlineData("preparedAdmissionId")]
    public void LegacyOpenAdmissionsAreNeverDiscarded(string field) =>
        Assert.Throws<FrontierConflictException>(() => FrontierLegacyGate.Validate(new Dictionary<string, object> { [field] = "old" }, 0, false));

    [Fact]
    public void LegacyFreeGamesAndUnknownNativeStateBlockWithoutConversion()
    {
        Assert.Throws<FrontierConflictException>(() => FrontierLegacyGate.Validate(new Dictionary<string, object>(), 1, false));
        var fields = new Dictionary<string, object> { ["highNoonFortuneRoundState"] = new Dictionary<string, object>
            { ["version"] = 1L, ["nativeRemaining"] = 0L, ["legacyRemaining"] = 0L, ["nativeStateJson"] = "unknown" } };
        Assert.Throws<FrontierConflictException>(() => FrontierLegacyGate.Validate(fields, 0, false));
    }

    internal sealed class WantedRandom : IGameRandomSource
    {
        public int Next(int maximumExclusive) => maximumExclusive == 1000 ? 999 : 0;
    }
}
