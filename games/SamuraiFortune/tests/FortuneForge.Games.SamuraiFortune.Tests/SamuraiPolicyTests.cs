using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.SamuraiFortune.Server;
using FortuneForge.Games.Slots.Persistence;
using Xunit;
using static FortuneForge.Games.SamuraiFortune.Tests.SamuraiTestSupport;

namespace FortuneForge.Games.SamuraiFortune.Tests;

public sealed class SamuraiPolicyTests
{
    [Theory]
    [InlineData("freeSpinWagerCents", 275L, 11L)]
    [InlineData("freeSpinWagerPoints", 100L, 400L)]
    public void HistoricalWagersWeightedCollectionsAndDormantEnergyImportExactly(string field, long stored, long expected)
    {
        var legacy = new Dictionary<string, object>
        {
            [field] = stored, ["freeSpinFeatureMode"] = "sync-paw", ["consecutiveFiveMisses"] = 8L,
            ["sealCounts"] = new Dictionary<string, object> { ["sync"] = 25L, ["paw"] = 4L },
            ["sealWagerCents"] = new Dictionary<string, object> { ["sync"] = 1_875L, ["paw"] = 1_000L }
        };
        var storage = SamuraiFortuneRoundStateCodec.Storage;
        var imported = storage.Decode(legacy, 3, 13, 106);
        Assert.Equal(new ClassicSlotRoundCarry(3, expected, "sync-paw"), imported.Carry);
        Assert.Equal((3, 13, 106L, 8), (imported.LegacyRemaining, imported.SpecialPoints, imported.Energy, imported.Pity));
        Assert.Equal(75L, imported.SealWagerPoints["sync"]);
        Assert.Equal(40L, imported.SealWagerPoints["paw"]);
        Assert.True(imported.Matches(storage.Decode(storage.Write(imported), 3)));
        Assert.Throws<InvalidOperationException>(() => storage.Decode(storage.Write(imported), 2));
    }

    [Fact]
    public void OddHistoricalWagerPlaysOnlyAsFreeSpinAndOnePointLockStaysVisible()
    {
        var imported = SamuraiFortuneRoundStateCodec.Storage.Decode(new Dictionary<string, object> { ["freeSpinWagerCents"] = 25L }, 2);
        Assert.Equal(1, imported.Carry!.WagerPoints);
        Assert.True(imported.Matches(SamuraiFortuneRoundStateCodec.Storage.Decode(SamuraiFortuneRoundStateCodec.Storage.Write(imported), 2)));
        Assert.Throws<SamuraiFortuneLegacyFreeWagerUnplayableException>(() => SamuraiFortuneRoundPolicy.Prepare(Account,
            new("samurai-one-point-legacy", 1, true), imported, DateTime.UtcNow, null, new NoDraws()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SamuraiFortuneEngine(new HighDraws()).Spin(3, Account));
        var free = new SamuraiFortuneEngine(new HighDraws()).Spin(3, Account, isFreeSpin: true);
        Assert.True(free.IsFreeSpin);
        Assert.Equal(3, free.WagerPoints);
    }

    [Fact]
    public void CompletedWeightedCollectionAwardsSevenSyncPawSpinsAndClearsDormantEnergyWithoutMultiplier()
    {
        var before = Empty(123) with
        {
            SealCounts = new Dictionary<string, long> { ["sync"] = 26, ["rows"] = 2 },
            SealWagerPoints = new Dictionary<string, long> { ["sync"] = 78, ["rows"] = 12 }
        };
        var round = SamuraiFortuneRoundPolicy.Prepare(Account, new("samurai-weighted-award", 6, false), before,
            DateTime.UtcNow, null, new HighDraws());
        Assert.Empty(round.Outcome.CollectionsAwarded);
        Assert.Equal(7, round.Progress.CollectionFreeSpinsAwarded);
        Assert.Equal(new ClassicSlotRoundCarry(7, 3, "sync-paw"), round.Progress.After.Carry);
        Assert.Equal(0, round.Progress.After.SealCounts["sync"]);
        Assert.Equal(2, round.Progress.After.SealCounts["rows"]);
        Assert.Equal(123, round.Outcome.Audit.EnergyBalanceBefore);
        Assert.Equal(0, round.Progress.After.Energy);
        Assert.False(round.Progress.EnergySettlement.MultiplierApplied);
        Assert.Equal(1m, round.Progress.EnergySettlement.PayoutMultiplier);
        Assert.Equal(round.Outcome.Payout.TotalPoints * 25, round.PayoutCents);
        SamuraiFortuneRoundPolicy.Validate(round);
    }

    [Fact]
    public void FrozenHistoryPreservesSignedMoneyAndRejectsTamperedDrawsProgressAndReceipt()
    {
        var before = Empty(106) with
        {
            SealCounts = new Dictionary<string, long> { ["sync"] = 26 },
            SealWagerPoints = new Dictionary<string, long> { ["sync"] = 78 }
        };
        var round = SamuraiFortuneRoundPolicy.Prepare(Account, new("samurai-history-proof", 6, false), before,
            DateTime.UtcNow, null, new SeededDraws(841));
        var settled = Settle(round);
        var report = SamuraiFortuneSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        var response = SamuraiFortuneSpinResponse.From(settled);
        Assert.Equal(round.Outcome.FreeSpinsAwarded, response.FreeSpinsAwarded);
        Assert.Equal(round.Outcome.FreeSpinsAwarded, SamuraiFortuneSpinResponse.Capture(round).FreeSpinsAwarded);
        Assert.Equal("samurai-fortune-v1", report.Result["gameId"]);
        Assert.Equal("samurai-fortune", report.Round.Audit.Summary.GameId);
        Assert.Equal(150, Assert.Single(report.Round.Audit.Summary.Participants).WagerCents);
        Assert.Equal(round.PayoutCents, Assert.Single(report.Round.Audit.Summary.Participants).PayoutCents);
        Assert.Equal(round.Progress.SettledPayout, response.Payout);
        Assert.False(JsonSerializer.SerializeToElement(response, RoundAuditCapture.Json).TryGetProperty("chargedWagerCents", out _));
        using var document = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = document.RootElement.GetProperty("data");
        Assert.Empty(SamuraiFortuneSpinEvidence.InspectNative(report.Round.Audit.Summary, data));
        foreach (var mutation in new Action<JsonObject>[]
        {
            value => value["samuraiFortuneDraws"] = Convert.ToBase64String(round.Engine.Draws.Append((byte)0).ToArray()),
            value => value["samuraiFortuneProgress"]!["after"]!["specialPoints"] = 999,
            value => value["walletSettlementId"] = "forged-settlement"
        })
        {
            var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
            mutation(changed);
            Assert.Equal("slot-samurai-fortune-native-proof-mismatch", Assert.Single(SamuraiFortuneSpinEvidence.InspectNative(
                report.Round.Audit.Summary, JsonSerializer.SerializeToElement(changed))).Code);
        }
        var old = JsonNode.Parse(data.GetRawText())!.AsObject();
        old.Remove("samuraiFortuneDraws"); old.Remove("samuraiFortuneProgress"); old.Remove("engineOutcomeSha256");
        Assert.False(SamuraiFortuneSpinEvidence.IsNative(JsonSerializer.SerializeToElement(old)));
        Assert.Throws<InvalidOperationException>(() => SamuraiFortuneSpinReport.Capture(round,
            settled with { Wallet = settled.Wallet with { BalanceAfterCents = settled.Wallet.BalanceAfterCents + 1 } }, DateTime.UtcNow));
    }

    [Fact]
    public void SignedLegacyBankSpendsFirstAndRejectsChangingTheLockedWagerBeforeDrawing()
    {
        var before = Empty() with { LegacyRemaining = 2, NativeRemaining = 1, Carry = new(3, 3, "sync-paw") };
        Assert.Throws<SamuraiFortuneFreeSpinsRequiredException>(() => SamuraiFortuneRoundPolicy.Prepare(Account,
            new("samurai-blocked-cash", 6, false), before, DateTime.UtcNow, null, new NoDraws()));
        Assert.Throws<SamuraiFortuneFreeWagerLockedException>(() => SamuraiFortuneRoundPolicy.Prepare(Account,
            new("samurai-wrong-free-wager", 6, true), before, DateTime.UtcNow, null, new NoDraws()));
        var round = SamuraiFortuneRoundPolicy.Prepare(Account, new("samurai-saved-odd-free", 3, true), before,
            DateTime.UtcNow, null, new HighDraws());
        Assert.True(round.SpendsLegacy);
        Assert.Equal(0, round.WagerCents);
        Assert.Equal(1, round.Progress.After.LegacyRemaining);
        Assert.Equal(1, round.Progress.After.NativeRemaining);
        Assert.Equal(new ClassicSlotRoundCarry(2, 3, "sync-paw"), round.Progress.After.Carry);
        SamuraiFortuneRoundPolicy.Validate(round);
    }
}
