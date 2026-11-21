using FortuneForge.Games.RainbowRealm.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.RainbowRealm.Tests;

public sealed class RainbowRoundStateCodecTests
{
    [Theory]
    [InlineData("freeSpinWagerCents", 75, 3)]
    [InlineData("freeSpinWagerPoints", 100, 400)]
    public void ImportsTheExactLockAndQualifyingSealWagers(string field, long rawWager, long wager)
    {
        var legacy = new Dictionary<string, object>
        {
            [field] = rawWager,
            ["freeSpinFeatureMode"] = "rows-paw",
            ["consecutiveFiveMisses"] = 7L,
            ["sealCounts"] = new Dictionary<string, object> { ["rows"] = 12L },
            ["sealWagerTotals"] = new Dictionary<string, object> { ["rows"] = 19L },
        };
        var imported = RainbowRoundStateCodec.Decode(legacy, signedLegacyCount: 3, legacySpecialPoints: 17);
        Assert.Equal(new RainbowCarry(3, wager, "rows-paw"), imported.Carry);
        Assert.Equal((0, 3, 17, 7), (imported.NativeRemaining, imported.LegacyRemaining, imported.SpecialPoints, imported.Pity));
        Assert.Equal(12, imported.SealCounts["rows"]);
        Assert.Equal(76, imported.SealWagerPoints["rows"]);
        Assert.True(imported.Matches(RainbowRoundStateCodec.Decode(RainbowRoundStateCodec.Write(imported), signedLegacyCount: 3)));
    }

    [Fact]
    public void UnpricedLegacyUnitsRemainUnpinnedWithoutCreatingAnEnergyMeter()
    {
        var legacy = new Dictionary<string, object> { ["freeSpinFeatureMode"] = "paw" };
        var imported = RainbowRoundStateCodec.Decode(legacy, signedLegacyCount: 2);
        Assert.Equal(new RainbowCarry(2, null, "paw"), imported.Carry);
        var fields = RainbowRoundStateCodec.Write(imported);
        var owned = Assert.IsType<Dictionary<string, object>>(fields["rainbowRealmRoundState"]);
        Assert.False(owned.ContainsKey("energy"));
        Assert.True(imported.Matches(RainbowRoundStateCodec.Decode(fields, signedLegacyCount: 2)));
        Assert.Throws<InvalidOperationException>(() => RainbowRoundStateCodec.Decode(fields, signedLegacyCount: 1));
        Assert.Throws<InvalidOperationException>(() => RainbowRoundStateCodec.Write(imported with
        {
            NativeRemaining = 1,
            Carry = new(3, null, "paw"),
        }));
    }

    [Fact]
    public void AwardedOddWagersArePlayableWhileCashWagersRemainEven()
    {
        var engine = new RainbowRealmEngine(new ZeroRandom());
        Assert.Throws<ArgumentOutOfRangeException>(() => RainbowRealmEngine.ValidateWager(3, isFreeSpin: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Spin(3, "rainbow-codec-test"));
        RainbowRealmEngine.ValidateWager(3, isFreeSpin: true);
        var free = engine.Spin(3, "rainbow-codec-test", featureMode: "paw", persistedPityMisses: 0, isFreeSpin: true);
        Assert.Equal(3, free.WagerPoints);
        Assert.True(free.IsFreeSpin);
        Assert.Equal(0, free.EnergyAwarded);
    }

    [RainbowImportFirestoreFact]
    public async Task ClaimReadsOnlyTheCanonicalSpecialPointBalanceAndIgnoresRetiredEnergy()
    {
        const string player = "rainbow-import-player";
        const string other = "rainbow-import-other";
        const string scope = RainbowRealmEngine.GameId;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-rainbow-import-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var guards = database.Collection(SlotSpinGuard.Collection);
        var guard = guards.Document(SlotSpinGuard.DocumentId(player, scope));
        var wrongGuard = guards.Document(SlotSpinGuard.DocumentId(other, scope));
        var balances = database.Collection("userBalances");
        var special = balances.Document($"{player}_specialPoints:{scope}");
        var retiredEnergy = balances.Document($"{player}_energy:{scope}");
        var legacyFree = balances.Document($"{player}_freeGames:{scope}");
        await Task.WhenAll(
            guard.SetAsync(new Dictionary<string, object> { ["consecutiveFiveMisses"] = 4L, ["freeSpinWagerCents"] = 275L }),
            special.SetAsync(new Dictionary<string, object> { ["available"] = 23L }),
            retiredEnergy.SetAsync(new Dictionary<string, object> { ["available"] = "retired", ["userId"] = other }),
            legacyFree.SetAsync(new Dictionary<string, object> { ["available"] = 67L }),
            balances.Document($"{player}_specialPoints:classic-demo-v1").SetAsync(new Dictionary<string, object> { ["available"] = 911L }),
            balances.Document($"{other}_specialPoints:{scope}").SetAsync(new Dictionary<string, object> { ["available"] = 912L }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.RunTransactionAsync(async tx =>
            await RainbowRoundStateCodec.ReadAsync(tx, database, await tx.GetSnapshotAsync(wrongGuard), player, 3, default)));

        async Task<RainbowRoundBefore> ReadOrClaim(bool claim) => await database.RunTransactionAsync(async tx =>
        {
            var saved = await tx.GetSnapshotAsync(guard);
            var state = await RainbowRoundStateCodec.ReadAsync(tx, database, saved, player, 3, default);
            if (claim) tx.Set(guard, RainbowRoundStateCodec.Write(state), SetOptions.MergeAll);
            return state;
        });

        var claimed = await ReadOrClaim(claim: true);
        Assert.Equal(new RainbowCarry(3, 11, null), claimed.Carry);
        Assert.Equal((23, 4), (claimed.SpecialPoints, claimed.Pity));
        Assert.Equal(67L, (await legacyFree.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal("retired", (await retiredEnergy.GetSnapshotAsync()).GetValue<string>("available"));
        Assert.True(claimed.Matches(RainbowRoundStateCodec.Read(await guard.GetSnapshotAsync(), expectedLegacyCount: 3)));
        await special.DeleteAsync();
        Assert.True(claimed.Matches(await ReadOrClaim(claim: false)));
    }

    private sealed class ZeroRandom : IGameRandomSource
    {
        public int Next(int exclusiveUpperBound) => 0;
    }

    private sealed class RainbowImportFirestoreFactAttribute : FactAttribute
    {
        public RainbowImportFirestoreFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
                Skip = "The Firestore emulator is required.";
        }
    }
}
