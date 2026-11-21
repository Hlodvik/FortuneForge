using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.NordicLegends.Server;
using FortuneForge.Games.Slots.Persistence;
using Xunit;
using static FortuneForge.Games.NordicLegends.Tests.NordicLegendsTestSupport;

namespace FortuneForge.Games.NordicLegends.Tests;

public sealed class NordicLegendsPolicyTests
{
    [Theory]
    [InlineData("freeSpinWagerCents", 275L, 11L)]
    [InlineData("freeSpinWagerPoints", 100L, 400L)]
    public void LegacyMoneyAndDormantCountersImportWithoutChangingTheSavedMode(string field, long stored, long expected)
    {
        var source = new Dictionary<string, object>
        {
            [field] = stored, ["freeSpinFeatureMode"] = "rows", ["consecutiveFiveMisses"] = 9L,
            ["sealCounts"] = new Dictionary<string, object> { ["sync"] = 12L },
            ["sealWagerCents"] = new Dictionary<string, object> { ["sync"] = 900L }
        };
        var codec = NordicLegendsRoundStateCodec.Storage;
        var state = codec.Decode(source, 2, 15, 123);
        Assert.Equal(new ClassicSlotRoundCarry(2, expected, "rows"), state.Carry);
        Assert.Equal((15, 123L, 9), (state.SpecialPoints, state.Energy, state.Pity));
        Assert.Equal(36L, state.SealWagerPoints["sync"]);
        Assert.True(state.Matches(codec.Decode(codec.Write(state), 2)));
        Assert.Throws<InvalidOperationException>(() => codec.Decode(codec.Write(state), 1));
    }

    [Fact]
    public void AOnePointHistoricalLockIsVisibleAndAnOddWagerRequiresAnExistingFreeBank()
    {
        var state = NordicLegendsRoundStateCodec.Storage.Decode(new Dictionary<string, object> { ["freeSpinWagerCents"] = 25L }, 2);
        Assert.Equal(1, state.Carry!.WagerPoints);
        Assert.Throws<NordicLegendsLegacyFreeWagerUnplayableException>(() => NordicLegendsRoundPolicy.Prepare(Account,
            new("nordic-one-point-saved", 1, true), state, DateTime.UtcNow, null, new NoDraws()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NordicLegendsEngine(new HighDraws()).Spin(3, Account));
        Assert.Equal(3, new NordicLegendsEngine(new HighDraws()).Spin(3, Account, isFreeSpin: true).WagerPoints);
        Assert.Throws<NordicLegendsFreeSpinsUnavailableException>(() => NordicLegendsRoundPolicy.Prepare(Account,
            new("nordic-free-without-bank", 3, true), Empty(), DateTime.UtcNow, null, new NoDraws()));
    }

    [Fact]
    public void DirectValuesPayOnceWhileDormantEnergyAndCollectionsDoNotCreateAwards()
    {
        Assert.Equal((4, 6, "sync-paw", 1.158m), (NordicLegendsEngine.Profile.ScatterRequiredSymbols,
            NordicLegendsEngine.Profile.ScatterAwardedSpins, NordicLegendsEngine.Profile.ScatterFeatureMode,
            NordicLegendsEngine.Profile.PayoutMultiplier));
        var before = Empty(123) with
        {
            SealCounts = new Dictionary<string, long> { ["sync"] = 28 },
            SealWagerPoints = new Dictionary<string, long> { ["sync"] = 84 }
        };
        var random = new SeededDraws(7447);
        var round = Enumerable.Range(0, 512).Select(_ => NordicLegendsRoundPolicy.Prepare(Account,
            new("nordic-direct-values-report", 100, false), before, DateTime.UtcNow, null, random))
            .First(sample => sample.Outcome.CollectorPoints > 0);
        var response = NordicLegendsSpinResponse.From(Settle(round));
        Assert.Equal(23, round.Outcome.Audit.BaseGame.Paylines.Count);
        Assert.Equal(123, round.Outcome.Audit.EnergyBalanceBefore);
        Assert.Equal(round.Outcome.CollectorPoints, response.MoneyGrabPoints);
        Assert.Equal(response.MoneyGrabPoints, Assert.Single(response.Payout.Paylines, line => line.PaylineId == 901).AmountPoints);
        Assert.Equal(round.Outcome.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.False(response.EnergyMultiplierApplied);
        Assert.Equal(0, response.EnergyBalance);
        Assert.Empty(response.SealCollections);
        Assert.Empty(round.Outcome.CollectionsAwarded);
        Assert.Equal(28, round.Progress.After.SealCounts["sync"]);
        Assert.Equal(84, round.Progress.After.SealWagerPoints["sync"]);
        NordicLegendsRoundPolicy.Validate(round);
        VerifyProof(round);
    }

    [Fact]
    public void ScatterRetriggerAdoptsAuthoredSyncPawAndSpendsOneLegacyUnitAtTheSavedOddWager()
    {
        var before = Empty() with { LegacyRemaining = 2, NativeRemaining = 1, Carry = new(3, 3, "rows") };
        var random = new SeededDraws(9809);
        var round = Enumerable.Range(0, 1024).Select(_ => NordicLegendsRoundPolicy.Prepare(Account,
            new("nordic-saved-retrigger", 3, true), before, DateTime.UtcNow, null, random))
            .First(sample => sample.Outcome.FreeSpinsAwarded > 0);
        Assert.Equal(6, round.Outcome.FreeSpinsAwarded);
        Assert.Equal("rows", round.Outcome.Audit.FeatureMode);
        Assert.Equal(new ClassicSlotRoundCarry(8, 3, "sync-paw"), round.Progress.After.Carry);
        Assert.Equal(1, round.Progress.After.LegacyRemaining);
        Assert.True(round.SpendsLegacy);
        Assert.Equal(0, round.WagerCents);
        VerifyProof(round);
    }

    private static void VerifyProof(NordicLegendsPreparedRound round)
    {
        var settled = Settle(round);
        var report = NordicLegendsSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        var participant = Assert.Single(report.Round.Audit.Summary.Participants);
        Assert.Equal(round.WagerCents, participant.WagerCents);
        Assert.Equal(round.PayoutCents, participant.PayoutCents);
        Assert.Equal("nordic-legends-v1", report.Result["gameId"]);
        Assert.False(JsonSerializer.SerializeToElement(NordicLegendsSpinResponse.From(settled), RoundAuditCapture.Json)
            .TryGetProperty("chargedWagerCents", out _));
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(NordicLegendsSpinEvidence.InspectNative(report.Round.Audit.Summary, data));
        foreach (var mutation in new Action<JsonObject>[]
        {
            value => value["nordicLegendsDraws"] = Convert.ToBase64String(round.Engine.Draws.Append((byte)0).ToArray()),
            value => value["nordicLegendsProgress"]!["after"]!["specialPoints"] = 999,
            value => value["walletSettlementId"] = "forged-receipt",
            value => value["result"]!["moneyGrabPoints"] = round.Outcome.CollectorPoints + 1
        })
        {
            var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
            mutation(changed);
            Assert.Equal("slot-nordic-legends-native-proof-mismatch", Assert.Single(NordicLegendsSpinEvidence.InspectNative(
                report.Round.Audit.Summary, JsonSerializer.SerializeToElement(changed))).Code);
        }
        var legacy = JsonNode.Parse(data.GetRawText())!.AsObject();
        legacy.Remove("nordicLegendsDraws"); legacy.Remove("nordicLegendsProgress"); legacy.Remove("engineOutcomeSha256");
        Assert.False(NordicLegendsSpinEvidence.IsNative(JsonSerializer.SerializeToElement(legacy)));
        Assert.Throws<InvalidOperationException>(() => NordicLegendsSpinReport.Capture(round,
            settled with { Wallet = settled.Wallet with { BalanceAfterCents = settled.Wallet.BalanceAfterCents + 1 } }, DateTime.UtcNow));
    }
}
