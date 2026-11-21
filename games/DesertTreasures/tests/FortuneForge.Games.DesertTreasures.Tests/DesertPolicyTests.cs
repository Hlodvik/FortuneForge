using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.DesertTreasures.Server;
using FortuneForge.Games.Slots.Persistence;
using Xunit;
using static FortuneForge.Games.DesertTreasures.Tests.DesertTestSupport;

namespace FortuneForge.Games.DesertTreasures.Tests;

public sealed class DesertPolicyTests
{
    [Theory]
    [InlineData("freeSpinWagerCents", 275L, 11L)]
    [InlineData("freeSpinWagerPoints", 100L, 400L)]
    public void HistoricalWagersWeightedCollectionsAndActiveEnergyImportExactly(string field, long stored, long expected)
    {
        var legacy = new Dictionary<string, object>
        {
            [field] = stored, ["freeSpinFeatureMode"] = "rows", ["consecutiveFiveMisses"] = 8L,
            ["sealCounts"] = new Dictionary<string, object> { ["sync"] = 25L, ["paw"] = 4L },
            ["sealWagerCents"] = new Dictionary<string, object> { ["sync"] = 1_875L, ["paw"] = 1_000L }
        };
        var storage = DesertTreasuresRoundStateCodec.Storage;
        var imported = storage.Decode(legacy, 3, 13, 106);
        Assert.Equal(new ClassicSlotRoundCarry(3, expected, "rows"), imported.Carry);
        Assert.Equal((3, 13, 106L, 8), (imported.LegacyRemaining, imported.SpecialPoints, imported.Energy, imported.Pity));
        Assert.Equal(75L, imported.SealWagerPoints["sync"]);
        Assert.Equal(40L, imported.SealWagerPoints["paw"]);
        Assert.True(imported.Matches(storage.Decode(storage.Write(imported), 3)));
        Assert.Throws<InvalidOperationException>(() => storage.Decode(storage.Write(imported), 2));
    }

    [Fact]
    public void OddHistoricalWagerPlaysOnlyAsFreeSpinAndOnePointLockStaysVisible()
    {
        var imported = DesertTreasuresRoundStateCodec.Storage.Decode(new Dictionary<string, object> { ["freeSpinWagerCents"] = 25L }, 2);
        Assert.Equal(1, imported.Carry!.WagerPoints);
        Assert.True(imported.Matches(DesertTreasuresRoundStateCodec.Storage.Decode(DesertTreasuresRoundStateCodec.Storage.Write(imported), 2)));
        Assert.Throws<DesertTreasuresLegacyFreeWagerUnplayableException>(() => DesertTreasuresRoundPolicy.Prepare(Account,
            new("desert-one-point-legacy", 1, true), imported, DateTime.UtcNow, null, new NoDraws()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DesertTreasuresEngine(new HighDraws()).Spin(3, Account));
        var free = new DesertTreasuresEngine(new HighDraws()).Spin(3, Account, isFreeSpin: true);
        Assert.True(free.IsFreeSpin);
        Assert.Equal(3, free.WagerPoints);
    }

    [Fact]
    public void EnergyCompletesTheWeightedCollectionAndRoundsEveryCollectorMatchSeparately()
    {
        var before = Empty(99) with
        {
            SealCounts = new Dictionary<string, long> { ["sync"] = 7 },
            SealWagerPoints = new Dictionary<string, long> { ["sync"] = 700 }
        };
        var random = new SeededDraws(7429);
        var round = Enumerable.Range(0, 512)
            .Select(_ => DesertTreasuresRoundPolicy.Prepare(Account, new("desert-energy-collector-proof", 100, false), before,
                DateTime.UtcNow, null, random))
            .First(sample => sample.Outcome.CollectorPoints > 0 && sample.Outcome.EnergyAwarded > 0 &&
                sample.Outcome.FreeSpinsAwarded == 0 && sample.Outcome.CollectionsAwarded.Count == 0);
        var settled = Settle(round);
        var response = DesertTreasuresSpinResponse.From(settled);
        Assert.Equal(round.Outcome.FreeSpinsAwarded, response.FreeSpinsAwarded);
        Assert.Equal(round.Outcome.FreeSpinsAwarded, DesertTreasuresSpinResponse.Capture(round).FreeSpinsAwarded);
        var report = DesertTreasuresSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.True(response.EnergyMultiplierApplied);
        Assert.Equal(1.5m, response.PayoutMultiplier);
        Assert.Equal(0, response.EnergyBalance);
        Assert.Equal(7, response.FreeSpinsRemaining);
        Assert.Equal("rows", response.FreeSpinFeatureMode);
        Assert.Equal(100, response.FreeSpinWagerPoints);
        Assert.Equal(7, report.Result["sealFreeSpinsAwarded"]);
        Assert.Equal("rows", report.Result["sealFreeSpinFeatureMode"]);
        var collector = Assert.Single(response.Payout.Paylines, line => line.PaylineId == 901);
        Assert.Equal(Multiply(round.Outcome.CollectorPoints), collector.AmountPoints);
        Assert.Equal(round.Outcome.Payout.Paylines.Sum(line => line.Matches.Sum(match => Multiply(match.AmountPoints))), response.Payout.TotalPoints);
        Assert.Equal(response.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.Equal((double)(round.PayoutCents / 100m), report.Result["wonSlotsCredits"]);
        var spin = round.Outcome.SpinId.ToString("N");
        Assert.Equal(1L, report.GameplayWitnesses[$"{spin}-energy-award"]["amount"]);
        Assert.Equal(-100L, report.GameplayWitnesses[$"{spin}-energy-multiplier-use"]["amount"]);
        Assert.Equal(7L, report.GameplayWitnesses[$"{spin}-free-games-award"]["amount"]);
        using var document = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        Assert.Empty(DesertTreasuresSpinEvidence.InspectNative(report.Round.Audit.Summary, document.RootElement.GetProperty("data")));
        DesertTreasuresRoundPolicy.Validate(round);
    }

    [Fact]
    public void FrozenHistoryPreservesSignedMoneyAndRejectsTamperedDrawsProgressAndReceipt()
    {
        var before = Empty() with
        {
            SealCounts = new Dictionary<string, long> { ["sync"] = 24 },
            SealWagerPoints = new Dictionary<string, long> { ["sync"] = 72 }
        };
        var round = DesertTreasuresRoundPolicy.Prepare(Account, new("desert-history-proof", 6, false), before,
            DateTime.UtcNow, null, new SeededDraws(841));
        var settled = Settle(round);
        var report = DesertTreasuresSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        var response = DesertTreasuresSpinResponse.From(settled);
        Assert.Equal(round.Outcome.FreeSpinsAwarded, response.FreeSpinsAwarded);
        Assert.Equal(round.Outcome.FreeSpinsAwarded, DesertTreasuresSpinResponse.Capture(round).FreeSpinsAwarded);
        Assert.Equal("desert-treasures-v1", report.Result["gameId"]);
        Assert.Equal("desert-treasures", report.Round.Audit.Summary.GameId);
        Assert.Equal(150, Assert.Single(report.Round.Audit.Summary.Participants).WagerCents);
        Assert.Equal(round.PayoutCents, Assert.Single(report.Round.Audit.Summary.Participants).PayoutCents);
        Assert.Equal(round.Progress.SettledPayout, response.Payout);
        Assert.False(JsonSerializer.SerializeToElement(response, RoundAuditCapture.Json).TryGetProperty("chargedWagerCents", out _));
        using var document = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = document.RootElement.GetProperty("data");
        Assert.Empty(DesertTreasuresSpinEvidence.InspectNative(report.Round.Audit.Summary, data));
        foreach (var mutation in new Action<JsonObject>[]
        {
            value => value["desertTreasuresDraws"] = Convert.ToBase64String(round.Engine.Draws.Append((byte)0).ToArray()),
            value => value["desertTreasuresProgress"]!["after"]!["specialPoints"] = 999,
            value => value["walletSettlementId"] = "forged-settlement"
        })
        {
            var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
            mutation(changed);
            Assert.Equal("slot-desert-treasures-native-proof-mismatch", Assert.Single(DesertTreasuresSpinEvidence.InspectNative(
                report.Round.Audit.Summary, JsonSerializer.SerializeToElement(changed))).Code);
        }
        var old = JsonNode.Parse(data.GetRawText())!.AsObject();
        old.Remove("desertTreasuresDraws"); old.Remove("desertTreasuresProgress"); old.Remove("engineOutcomeSha256");
        Assert.False(DesertTreasuresSpinEvidence.IsNative(JsonSerializer.SerializeToElement(old)));
        Assert.Throws<InvalidOperationException>(() => DesertTreasuresSpinReport.Capture(round,
            settled with { Wallet = settled.Wallet with { BalanceAfterCents = settled.Wallet.BalanceAfterCents + 1 } }, DateTime.UtcNow));
    }

    private static long Multiply(long points) => checked((long)Math.Round(points * 1.5m, MidpointRounding.AwayFromZero));

    [Fact]
    public void FullEnergyMeterWaitsForAPayingSpinBeforeCompletingACollection()
    {
        var before = Empty(100) with
        {
            SealCounts = new Dictionary<string, long> { ["sync"] = 23 },
            SealWagerPoints = new Dictionary<string, long> { ["sync"] = 69 }
        };
        var random = new SeededDraws(943);
        var round = Enumerable.Range(0, 512)
            .Select(_ => DesertTreasuresRoundPolicy.Prepare(Account, new("desert-full-meter-loss", 6, false), before,
                DateTime.UtcNow, null, random))
            .First(sample => sample.Outcome.Payout.TotalPoints == 0 && sample.Outcome.CollectionsAwarded.Count == 0 &&
                sample.Outcome.FreeSpinsAwarded == 0);
        Assert.False(round.Progress.EnergySettlement.MultiplierApplied);
        Assert.Equal(100, round.Progress.After.Energy);
        Assert.Equal(0, round.Progress.CollectionFreeSpinsAwarded);
        Assert.Equal(23, round.Progress.After.SealCounts["sync"]);
        Assert.Null(round.Progress.After.Carry);
        Assert.Equal(0, round.PayoutCents);
        DesertTreasuresRoundPolicy.Validate(round);
    }

    [Fact]
    public void SpecialBoostRequiresItsGameBalanceAndSpendsExactlyOneActivation()
    {
        var cost = DesertTreasuresEngine.CreateGame().SpecialPoints!.ActivationCost;
        Assert.Throws<DesertTreasuresInsufficientSpecialPointsException>(() => DesertTreasuresRoundPolicy.Prepare(Account,
            new("desert-unfunded-special", 6, false, true), Empty(), DateTime.UtcNow, null, new NoDraws()));
        var before = Empty() with { SpecialPoints = cost };
        var round = DesertTreasuresRoundPolicy.Prepare(Account, new("desert-funded-special", 6, false, true), before,
            DateTime.UtcNow, null, new HighDraws());
        Assert.True(round.Outcome.SpecialBoostApplied);
        Assert.Equal(round.Outcome.SpecialPointsAwarded, round.Progress.After.SpecialPoints);
        Assert.Equal(150, round.WagerCents);
        DesertTreasuresRoundPolicy.Validate(round);
    }

    [Fact]
    public void SignedLegacyBankSpendsFirstAndRejectsChangingTheLockedWagerBeforeDrawing()
    {
        var before = Empty() with { LegacyRemaining = 2, NativeRemaining = 1, Carry = new(3, 3, "rows") };
        Assert.Throws<DesertTreasuresFreeSpinsRequiredException>(() => DesertTreasuresRoundPolicy.Prepare(Account,
            new("desert-blocked-cash", 6, false), before, DateTime.UtcNow, null, new NoDraws()));
        Assert.Throws<DesertTreasuresFreeWagerLockedException>(() => DesertTreasuresRoundPolicy.Prepare(Account,
            new("desert-wrong-free-wager", 6, true), before, DateTime.UtcNow, null, new NoDraws()));
        var round = DesertTreasuresRoundPolicy.Prepare(Account, new("desert-saved-odd-free", 3, true), before,
            DateTime.UtcNow, null, new HighDraws());
        Assert.True(round.SpendsLegacy);
        Assert.Equal(0, round.WagerCents);
        Assert.Equal(1, round.Progress.After.LegacyRemaining);
        Assert.Equal(1, round.Progress.After.NativeRemaining);
        Assert.Equal(new ClassicSlotRoundCarry(2, 3, "rows"), round.Progress.After.Carry);
        DesertTreasuresRoundPolicy.Validate(round);
    }
}
