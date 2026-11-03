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

public sealed class GoldFoundryReelCollectorTests
{
    [Theory]
    [InlineData(false, 1, 900L)]
    [InlineData(true, 2, 1800L)]
    public void AllFourMetalsCanPayAnywhereWhileTheSameGoldAlsoWinsItsLine(
        bool secondCollector, int collectorCount, long collectionPoints)
    {
        var result = Spin(
        [
            ["SEAL_RAND", "2", "2", secondCollector ? "PAW" : "2"],
            ["SEAL_RAND", "3", "3", "3"],
            ["SEAL_RAND", "4", "4", "4"],
            ["5", "SEAL_ROWS", "5", "SEAL_SYNC"],
            ["6", "BOLT", "SEAL_PAW", "PAW"]
        ]);

        Assert.Equal(GoldFoundrySlotDefinitions.SymbolSetId, result.SymbolSetId);
        Assert.Equal(GoldFoundryReelCollector.EngineVersion, result.EngineAudit?.EngineVersion);
        Assert.Equal(collectorCount, result.MonkeyPawCount);
        Assert.Equal(collectionPoints, result.MoneyGrabPoints);
        var line = Assert.Single(result.Payout.Paylines);
        Assert.Equal(1, line.PaylineId);
        Assert.Equal(60, line.AmountPoints);
        Assert.Equal("SEAL_RAND", Assert.Single(line.Matches).Match.SymbolId);
        Assert.Equal(line.AmountPoints + collectionPoints, result.Payout.TotalPoints);
        Assert.Equal(1, result.EnergyAwarded);
        Assert.Equal(0, result.FreeSpinsAwarded);
        Assert.Empty(result.SealsAwarded);
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));

        var tampered = result with { MoneyGrabPoints = result.MoneyGrabPoints + 1 };
        Assert.Contains(SlotRoundAudit.Check(tampered, result.Payout),
            finding => finding.Code == "slot-feature-award-mismatch");
    }

    [Fact]
    public void CruciblesAndChargingIngotsCannotWinLinesOrTriggerACollectionPayout()
    {
        var result = Spin(
        [
            ["PAW", "BOLT", "2", "2"],
            ["PAW", "BOLT", "3", "3"],
            ["PAW", "BOLT", "4", "4"],
            ["5", "5", "5", "5"],
            ["6", "6", "6", "6"]
        ]);

        Assert.Equal(3, result.MonkeyPawCount);
        Assert.Equal(3, result.EnergyAwarded);
        Assert.Equal(0, result.MoneyGrabPoints);
        Assert.Equal(0, result.Payout.TotalPoints);
        Assert.Empty(result.Payout.Paylines);
        Assert.Empty(result.SealsAwarded);
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
    }

    [Fact]
    public void ThreeAnvilsAreARegularLineWithoutFreeSpins()
    {
        var result = Spin(
        [
            ["FREE", "2", "2", "2"],
            ["FREE", "3", "3", "3"],
            ["FREE", "4", "4", "4"],
            ["5", "5", "5", "5"],
            ["6", "6", "6", "6"]
        ]);

        var line = Assert.Single(result.Payout.Paylines);
        Assert.Equal("FREE", Assert.Single(line.Matches).Match.SymbolId);
        Assert.Equal(25, result.Payout.TotalPoints);
        Assert.Equal(0, result.FreeSpinsAwarded);
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
    }

    [Fact]
    public void ThreeIronIngotsPayOnePointAtTheMinimumWager()
    {
        var result = Spin(
        [
            ["SEAL_ROWS", "2", "2", "2"],
            ["SEAL_ROWS", "3", "3", "3"],
            ["SEAL_ROWS", "4", "4", "4"],
            ["5", "5", "5", "5"],
            ["6", "6", "6", "6"]
        ], wagerPoints: 2);

        Assert.Equal(1, result.Payout.TotalPoints);
        Assert.Equal("SEAL_ROWS", Assert.Single(Assert.Single(result.Payout.Paylines).Matches).Match.SymbolId);
        Assert.Equal(0, result.MoneyGrabPoints);
    }

    [Fact]
    public void HistoricFoundryOutcomeStillReplaysWithItsRecordedCollectorLine()
    {
        var result = Spin(
        [
            ["PAW", "2", "2", "2"],
            ["3", "3", "3", "3"],
            ["4", "4", "4", "4"],
            ["5", "5", "RAND_1", "5"],
            ["6", "RAND_05", "6", "6"]
        ], legacy: true);

        Assert.Equal(GoldFoundryCrucible.LegacyRuleVersion, result.EngineAudit?.CrucibleRuleVersion);
        Assert.Equal(SpinService.AuditEngineVersion, result.EngineAudit?.EngineVersion);
        Assert.Equal(91, result.MoneyGrabPoints);
        Assert.Equal(901, Assert.Single(result.Payout.Paylines).PaylineId);
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
    }

    private static SpinResult Spin(List<List<string>> strips, long wagerPoints = 100, bool legacy = false)
    {
        var random = new FirstStop();
        return new SpinService(new FoundryFixture(strips, legacy), new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random)
            .Spin(GoldFoundrySlotDefinitions.GameId, wagerPoints, "foundry-fixture", false);
    }

    private sealed class FirstStop : IRandomIndexSource
    {
        public int Next(int maximumExclusive) => 0;
    }

    private sealed class FoundryFixture(List<List<string>> strips, bool legacy) : ISlotsDefinitionProvider
    {
        private string SymbolSetId => legacy ? "wukong-treasures-v3" : GoldFoundrySlotDefinitions.SymbolSetId;

        public GameDefinition GetGame(string id)
        {
            var foundry = GoldFoundrySlotDefinitions.CreateGame();
            if (!legacy)
                return foundry;

            return new GameDefinition
            {
                Id = foundry.Id,
                Layout = foundry.Layout,
                Symbols = new() { SymbolSetId = SymbolSetId, WildSymbolId = "ACE" },
                Matching = foundry.Matching,
                Math = new()
                {
                    ReelSetId = "classic-reels-v4",
                    PaytableId = "classic-paytable-v4",
                    PayoutMultiplier = 0.607m,
                    PaylinePayoutSteps = foundry.Math.PaylinePayoutSteps,
                    Targets = new()
                },
                Wagering = foundry.Wagering,
                Energy = foundry.Energy,
                Paylines = foundry.Paylines
            };
        }

        public SymbolSetDefinition GetSymbolSet(string id) => legacy ? new()
        {
            Id = id,
            Symbols = strips.SelectMany(strip => strip).Append("ACE").Distinct()
                .Select(symbol => new SymbolDefinition { Id = symbol }).ToList()
        } : GoldFoundrySlotDefinitions.CreateSymbolSet();

        public PaytableDefinition GetPaytable(string id) => legacy
            ? new() { Id = id, SymbolSetId = SymbolSetId }
            : GoldFoundrySlotDefinitions.CreatePaytable();

        public ReelSetDefinition GetReelSet(string id) => new()
        {
            Id = id,
            SymbolSetId = SymbolSetId,
            Reels = strips
        };
    }
}
