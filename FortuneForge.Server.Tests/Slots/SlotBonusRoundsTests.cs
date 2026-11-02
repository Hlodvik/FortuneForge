using System.Text.Json;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class SlotBonusRoundsTests
{
    private static readonly Guid SpinId = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly CollectedBonusRules Small = new(1, 5,
        [new("red", "RED", 2, "glow")], [new("small-v1", 4, 6, ["glow"])]);
    private static readonly CollectedBonusRules Other = new(10, 20,
        [new("blue", "BLUE", 3, "wave")], [new("other-v1", 2, 5, ["wave"])]);

    [Fact]
    public void Configurations_IsolateTargetsAwardsBoundsModesAndModelRegistrations()
    {
        var first = SlotBonusRounds.Advance(Small, [], new Dictionary<string, long> { ["red"] = 1 },
            new Dictionary<string, long> { ["red"] = 1 }, new Dictionary<string, int> { ["RED"] = 2 },
            5, 4, null, "small-v1", SpinId);
        Assert.Equal(2, first.AwardedRounds.Count);
        Assert.Equal((4, 5L, (string?)null),
            (first.AwardedRounds[0].TotalSpins, first.AwardedRounds[0].WagerPoints, first.AwardedRounds[0].FeatureMode));
        Assert.Equal((6, 3L, "glow"),
            (first.AwardedRounds[1].TotalSpins, first.AwardedRounds[1].WagerPoints, first.AwardedRounds[1].FeatureMode));
        Assert.Equal(1, first.CollectionCounts["red"]);
        Assert.Equal(5, first.CollectionWagerPoints["red"]);

        var second = SlotBonusRounds.Advance(Other, [], new Dictionary<string, long> { ["blue"] = 1 },
            new Dictionary<string, long> { ["blue"] = 10 }, new Dictionary<string, int> { ["BLUE"] = 2 },
            20, 0, null, "other-v1", SpinId);
        var round = Assert.Single(second.Rounds);
        Assert.Equal((5, 17L, "wave"), (round.TotalSpins, round.WagerPoints, round.FeatureMode));
        Assert.Equal(0, second.CollectionCounts["blue"]);
        Assert.Equal(0, second.CollectionWagerPoints["blue"]);
        Assert.Equal(5, first.CollectionWagerPoints["red"]);
        Assert.Throws<InvalidOperationException>(() => SlotBonusRounds.Advance(Other, [],
            new Dictionary<string, long>(), new Dictionary<string, long>(), new Dictionary<string, int>(),
            10, 0, null, "small-v1", SpinId));
        Assert.Throws<ArgumentOutOfRangeException>(() => SlotBonusRounds.Advance(Other, [],
            new Dictionary<string, long>(), new Dictionary<string, long>(), new Dictionary<string, int>(),
            5, 0, null, "other-v1", SpinId));
        Assert.Throws<InvalidOperationException>(() => SlotBonusRounds.Validate(Other,
            [new("foreign", 6, 2, 10, "glow", "small-v1")]));
    }

    [Fact]
    public void Queue_ConsumesOnlyHeadAndAppendsSeparatelyLockedRounds()
    {
        SlotBonusRound[] prior = [new("head", 6, 2, 2, "glow", "small-v1"), new("tail", 4, 4, 4, null, "small-v1")];
        var consumed = SlotBonusRounds.Consume(Small, prior);
        var progress = SlotBonusRounds.Advance(Small, consumed, new Dictionary<string, long>(),
            new Dictionary<string, long>(), new Dictionary<string, int> { ["RED"] = 4 },
            5, 0, null, "small-v1", SpinId);
        Assert.Equal(4, progress.Rounds.Count);
        Assert.Equal(prior[0] with { RemainingSpins = 1 }, progress.Rounds[0]);
        Assert.Equal(prior[1], progress.Rounds[1]);
        Assert.All(progress.Rounds.Skip(2), round => Assert.Equal((6, 6, 5L),
            (round.TotalSpins, round.RemainingSpins, round.WagerPoints)));
        Assert.NotEqual(progress.Rounds[2].Id, progress.Rounds[3].Id);
        Assert.Equal(2, prior[0].RemainingSpins);
        var promoted = SlotBonusRounds.Consume(Small, progress.Rounds);
        Assert.Equal(prior[1], promoted[0]);
    }

    [Fact]
    public void MidnightWrapper_KeepsPriorRoundJsonAndWeightedOverflowContract()
    {
        MidnightVaultBonusRound[] prior = [new("existing", 10, 2, 4, "rows-paw", MidnightVaultSlotDefinitions.PaytableId)];
        var progress = MidnightVaultBonusRounds.Advance(MidnightVaultBonusRounds.Consume(prior),
            new Dictionary<string, long> { ["sync"] = 29 }, new Dictionary<string, long> { ["sync"] = 87 },
            new Dictionary<string, int> { ["SEAL_SYNC"] = 2 }, 4, 0, null, MidnightVaultSlotDefinitions.PaytableId, SpinId);
        const string expected = "[{\"id\":\"existing\",\"totalSpins\":10,\"remainingSpins\":1,\"wagerPoints\":4,\"featureMode\":\"rows-paw\",\"mathModelId\":\"midnight-vault-paytable-v2\"},{\"id\":\"00000000-0000-4000-8000-000000000001:collection:sync:0\",\"totalSpins\":10,\"remainingSpins\":10,\"wagerPoints\":3,\"featureMode\":\"rows-paw\",\"mathModelId\":\"midnight-vault-paytable-v2\"}]";
        Assert.Equal(expected, JsonSerializer.Serialize(progress.Rounds, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(1, progress.CollectionCounts["sync"]);
        Assert.Equal(4, progress.CollectionWagerPoints["sync"]);
        Assert.Single(progress.AwardedRounds);
        Assert.Equal(2, prior[0].RemainingSpins);
    }

    [Fact]
    public void MidnightWrapper_PreservesLegacyCarryAndStrictNewTenSpinValidation()
    {
        MidnightVaultBonusRound[] legacy = [new("legacy", 23, 17, 3, " rows--paw ", MidnightVaultSlotDefinitions.LegacyPaytableId)];
        MidnightVaultBonusRounds.Validate(legacy);
        var consumed = Assert.Single(MidnightVaultBonusRounds.Consume(legacy));
        Assert.Equal(16, consumed.RemainingSpins);
        Assert.Equal(" rows--paw ", consumed.FeatureMode);
        Assert.Equal(23, consumed.TotalSpins);
        Assert.Throws<InvalidOperationException>(() => MidnightVaultBonusRounds.Validate(
            [legacy[0] with { MathModelId = MidnightVaultSlotDefinitions.PaytableId }]));
        Assert.Throws<InvalidOperationException>(() => MidnightVaultBonusRounds.Validate(
            [legacy[0] with { MathModelId = "unknown" }]));
        var award = MidnightVaultBonusRounds.Advance([], new Dictionary<string, long>(), new Dictionary<string, long>(),
            new Dictionary<string, int>(), 3, 6, null, MidnightVaultSlotDefinitions.LegacyPaytableId, SpinId);
        Assert.Equal(6, Assert.Single(award.Rounds).TotalSpins);
    }
}
