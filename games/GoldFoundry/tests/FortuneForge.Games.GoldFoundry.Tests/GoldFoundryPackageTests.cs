using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.GoldFoundry.Tests;

public sealed class GoldFoundryPackageTests
{
    [Fact]
    public void EngineOwnsLineCollectorHammerEnergyAndReplay()
    {
        var engine = new GoldFoundryEngine(new FixtureSource(
        [
            ["SEAL_RAND", "BANANA", "BANANA", "BANANA"],
            ["SEAL_RAND", "BANANA", "BANANA", "BANANA"],
            ["SEAL_RAND", "BANANA", "BANANA", "BANANA"],
            ["5", "SEAL_ROWS", "5", "SEAL_SYNC"],
            ["BOLT", "6", "SEAL_PAW", "PAW"]
        ]), new FirstStop());

        var result = engine.Spin(100, "package-test");

        Assert.Equal(GoldFoundryEngine.Version, result.EngineAudit.EngineVersion);
        Assert.Equal(GoldFoundryCrucibleRules.RuleVersion, result.EngineAudit.CrucibleRuleVersion);
        Assert.Equal(1, result.CollectorCount);
        Assert.Equal(900, result.CollectorPayoutPoints);
        Assert.Equal(1, result.EnergyAwarded);
        Assert.True(result.HammerBonusPoints > 0);
        Assert.Equal(result.Payout.TotalPoints,
            result.Payout.Paylines.Sum(line => line.AmountPoints) + result.CollectorPayoutPoints);

        var replayResult = new GoldFoundryEngine(new FirstStop()).Spin(100, "package-replay-test");
        Assert.Empty(GoldFoundryOutcomeReplay.Check(replayResult, replayResult.Payout));

        var tampered = replayResult with { CollectorPayoutPoints = replayResult.CollectorPayoutPoints + 1 };
        Assert.Contains(GoldFoundryOutcomeReplay.Check(tampered, replayResult.Payout),
            finding => finding.Code == "slot-feature-award-mismatch");

        var tamperedStrips = replayResult with
        {
            EngineAudit = replayResult.EngineAudit with
            {
                ReelSet = new ReelSetDefinition
                {
                    Id = replayResult.EngineAudit.ReelSet.Id,
                    SymbolSetId = replayResult.EngineAudit.ReelSet.SymbolSetId,
                    Reels = replayResult.EngineAudit.ReelSet.Reels
                        .Select((strip, index) => index == 0 ? strip.AsEnumerable().Reverse().ToList() : [.. strip])
                        .ToList()
                }
            }
        };
        Assert.Contains(GoldFoundryOutcomeReplay.Check(tamperedStrips, replayResult.Payout),
            finding => finding.Code == "slot-foundry-rules-mismatch");
    }

    [Fact]
    public void CrucibleRuleAccruesOnlyChargingWagersAndPoursAtCapacity()
    {
        var noCharge = GoldFoundryCrucibleRules.Settle(95, 0, 99_849, 50);
        Assert.Equal(99_849, noCharge.WageredCents);
        var charged = GoldFoundryCrucibleRules.Settle(noCharge.EnergyBalance, 2, noCharge.WageredCents, 100);
        var free = GoldFoundryCrucibleRules.Settle(charged.EnergyBalance, 1, charged.WageredCents, 0);
        var poured = GoldFoundryCrucibleRules.Settle(free.EnergyBalance, 2, free.WageredCents, 50);
        Assert.True(poured.Poured);
        Assert.Equal(14_999, poured.PayoutCents);
        Assert.Equal(0, poured.EnergyBalance);
        Assert.Equal(0, poured.WageredCents);
    }

    [Fact]
    public void PackageRejectsForeignPlayModes()
    {
        var engine = new GoldFoundryEngine(new FirstStop());
        Assert.Throws<ArgumentException>(() => engine.Spin(2, "test", isFreeSpin: true));
        Assert.Throws<ArgumentException>(() => engine.Spin(2, "test", specialBoostApplied: true));
        Assert.Throws<ArgumentException>(() => engine.Spin(2, "test", featureMode: "rows"));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Spin(3, "test"));
    }

    private sealed class FirstStop : IGameRandomSource
    {
        public int Next(int maximumExclusive) => 0;
    }

    private sealed class FixtureSource(List<List<string>> strips) : ISlotDefinitionSource
    {
        public GameDefinition? GetGame(string id) => GoldFoundrySlotDefinitions.CreateGame();
        public SymbolSetDefinition? GetSymbolSet(string id) => GoldFoundrySlotDefinitions.CreateSymbolSet();
        public PaytableDefinition? GetPaytable(string id) => GoldFoundrySlotDefinitions.CreatePaytable();
        public ReelSetDefinition? GetReelSet(string id) => new()
        {
            Id = GoldFoundrySlotDefinitions.ReelSetId,
            SymbolSetId = GoldFoundrySlotDefinitions.SymbolSetId,
            Reels = strips
        };
    }
}
