using FortuneForge.Games.JadeDragon.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.JadeDragon.Tests;

public sealed class JadeRoundStateCodecTests
{
    [Fact]
    public void LegacyCopyExpandCarryPreservesGrandfatheredStakeAndFrozenGameCounters()
    {
        var historical = new Dictionary<string, object>
        {
            ["freeSpinWagerPoints"] = 500L,
            ["freeSpinFeatureMode"] = JadeDragonCollections.CollectionFeatureMode,
            ["consecutiveFiveMisses"] = 5L,
            ["sealCounts"] = new Dictionary<string, object> { ["track1"] = 9L, ["track4"] = "11" },
            ["sealWagerTotals"] = new Dictionary<string, object> { ["track1"] = 2L, ["track4"] = 7L },
        };
        var imported = JadeRoundStateCodec.Decode(historical, signedLegacyCount: 3, legacySpecialPoints: 8, legacyEnergy: 99);
        Assert.Equal(new JadeCarry(3, 2_000, JadeDragonCollections.CollectionFeatureMode, JadeDragonEngine.Version), imported.Carry);
        Assert.Equal((0, 3, 8, 99L, 5), (imported.NativeRemaining, imported.LegacyRemaining, imported.SpecialPoints, imported.Energy, imported.Pity));
        Assert.Equal(9, imported.SealCounts["track1"]);
        Assert.Equal(11, imported.SealCounts["track4"]);
        Assert.Equal(8, imported.SealWagerPoints["track1"]);
        Assert.Equal(28, imported.SealWagerPoints["track4"]);
        Assert.Equal(0, imported.SealCounts["track2"]);
        Assert.Equal(0, imported.SealWagerPoints["track3"]);

        var owned = JadeRoundStateCodec.Write(imported);
        Assert.Equal(JadeRoundStateCodec.Owner, owned[JadeRoundStateCodec.OwnerField]);
        Assert.True(imported.Matches(JadeRoundStateCodec.Decode(owned, signedLegacyCount: 3,
            legacySpecialPoints: 999, legacyEnergy: 999)));
        Assert.False(owned.ContainsKey("freeSpinWagerPoints"));
        Assert.False(owned.ContainsKey("freeSpinMathModelId"));
    }

    [Fact]
    public void CascadeCarryUsesExplicitCentsAndNativeAwardsCanShareItsFrozenWager()
    {
        var historical = new Dictionary<string, object>
        {
            ["freeSpinMathModelId"] = JadeDragonCascadeRules.Version,
            ["freeSpinWagerCents"] = 10_000L,
            ["freeSpinWagerPoints"] = 10L,
            ["sealWagerCents"] = new Dictionary<string, object> { ["track2"] = 75L },
            ["sealWagerTotals"] = new Dictionary<string, object> { ["track2"] = 100L },
        };
        var initial = JadeRoundStateCodec.Decode(historical, signedLegacyCount: 2);
        Assert.Equal(new JadeCarry(2, 400, null, JadeDragonCascadeRules.Version), initial.Carry);
        Assert.Equal(3, initial.SealWagerPoints["track2"]);

        var retriggered = initial with
        {
            NativeRemaining = 5,
            LegacyRemaining = 1,
            Carry = new JadeCarry(6, 400, null, JadeDragonCascadeRules.Version),
        };
        var frozen = JadeRoundStateCodec.Write(retriggered);
        Assert.True(retriggered.Matches(JadeRoundStateCodec.Decode(frozen, signedLegacyCount: 1)));
        var nativeOnly = JadeRoundStateCodec.Decode(frozen, signedLegacyCount: 0);
        Assert.Equal(new JadeCarry(5, 400, null, JadeDragonCascadeRules.Version), nativeOnly.Carry);
        Assert.Equal(0, nativeOnly.LegacyRemaining);
    }

    [Fact]
    public void NoOutstandingUnitsIgnoreRetiredLegacyWagerAndModeWithoutLosingGameProgress()
    {
        var historical = new Dictionary<string, object>
        {
            ["freeSpinMathModelId"] = "retired-metadata",
            ["freeSpinFeatureMode"] = JadeDragonCollections.CollectionFeatureMode,
            ["freeSpinWagerCents"] = 101L,
            ["consecutiveFiveMisses"] = 12,
        };
        var state = JadeRoundStateCodec.Decode(historical, signedLegacyCount: 0, legacySpecialPoints: 7, legacyEnergy: 29);
        Assert.Null(state.Carry);
        Assert.Equal((7, 29L, 12), (state.SpecialPoints, state.Energy, state.Pity));
        Assert.True(state.Matches(JadeRoundStateCodec.Decode(JadeRoundStateCodec.Write(state), signedLegacyCount: 0)));
    }

    [Theory]
    [InlineData("unknown", 500, null)]
    [InlineData(null, 601, null)]
    [InlineData(null, 500, "unknown")]
    [InlineData(JadeDragonCascadeRules.Version, 500, JadeDragonCollections.CollectionFeatureMode)]
    public void InvalidOutstandingCarryFailsWithoutChangingItsModel(string? model, long cents, string? mode)
    {
        var historical = new Dictionary<string, object>
        {
            ["freeSpinMathModelId"] = model ?? string.Empty,
            ["freeSpinWagerCents"] = cents,
            ["freeSpinFeatureMode"] = mode ?? string.Empty,
        };
        Assert.Throws<InvalidOperationException>(() => JadeRoundStateCodec.Decode(historical, signedLegacyCount: 1));
    }

    [Theory]
    [InlineData(10_050)]
    [InlineData(525)]
    public void HistoricalCascadeCarryStillChecksLiveV2WagerPolicy(long cents)
    {
        var historical = new Dictionary<string, object>
        {
            ["freeSpinMathModelId"] = JadeDragonCascadeRules.Version,
            ["freeSpinWagerCents"] = cents,
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => JadeRoundStateCodec.Decode(historical, signedLegacyCount: 1));
    }

    [Fact]
    public void AnotherOwnerOrCorruptOwnedDataDoesNotFallBackToAnOlderSnapshot()
    {
        var guard = new Dictionary<string, object> { [JadeRoundStateCodec.OwnerField] = "different-owner" };
        Assert.Throws<InvalidOperationException>(() => JadeRoundStateCodec.Decode(guard, signedLegacyCount: 0));
        guard[JadeRoundStateCodec.OwnerField] = JadeRoundStateCodec.Owner;
        Assert.Throws<InvalidOperationException>(() => JadeRoundStateCodec.Decode(guard, signedLegacyCount: 0));
    }

    [ImportFirestoreFact]
    public async Task FirestoreClaimImportsOnlyThisAccountsCountersAndNeverReadsThemAgain()
    {
        const string account = "jade-history";
        const string other = "jade-other";
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-jade-import-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var guard = database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(account, JadeDragonEngine.GameId));
        var balances = database.Collection("userBalances");
        var special = balances.Document($"{account}_specialPoints:{JadeDragonEngine.GameId}");
        var energy = balances.Document($"{account}_energy:{JadeDragonEngine.GameId}");
        await Task.WhenAll(
            guard.SetAsync(new Dictionary<string, object> { ["freeSpinWagerCents"] = 4_000L }),
            special.SetAsync(new Dictionary<string, object> { ["available"] = 23L, ["userId"] = account }),
            energy.SetAsync(new Dictionary<string, object> { ["available"] = 19L }),
            balances.Document($"{other}_specialPoints:{JadeDragonEngine.GameId}")
                .SetAsync(new Dictionary<string, object> { ["available"] = 150L }),
            balances.Document($"{account}_specialPoints:another-game")
                .SetAsync(new Dictionary<string, object> { ["available"] = 415L }));

        async Task<JadeRoundBefore> ReadState(bool claim) => await database.RunTransactionAsync(async tx =>
        {
            var snapshot = await tx.GetSnapshotAsync(guard);
            var state = await JadeRoundStateCodec.ReadAsync(tx, database, snapshot, account, signedLegacyCount: 2, CancellationToken.None);
            if (claim) tx.Set(guard, JadeRoundStateCodec.Write(state), SetOptions.MergeAll);
            return state;
        });

        var claimed = await ReadState(claim: true);
        Assert.Equal((23, 19L), (claimed.SpecialPoints, claimed.Energy));
        Assert.Equal(new JadeCarry(2, 160, null, JadeDragonEngine.Version), claimed.Carry);
        Assert.Equal(23L, (await special.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(19L, (await energy.GetSnapshotAsync()).GetValue<long>("available"));

        await special.SetAsync(new Dictionary<string, object> { ["available"] = 0L, ["userId"] = other });
        await energy.DeleteAsync();
        Assert.True(claimed.Matches(await ReadState(claim: false)));
    }

    private sealed class ImportFirestoreFactAttribute : FactAttribute
    {
        public ImportFirestoreFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
                Skip = "The Firestore emulator is required.";
        }
    }
}
