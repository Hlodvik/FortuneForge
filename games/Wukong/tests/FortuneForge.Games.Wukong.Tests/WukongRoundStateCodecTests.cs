using FortuneForge.Games.Slots;
using FortuneForge.Games.Wukong.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.Wukong.Tests;

public sealed class WukongRoundStateCodecTests
{
    [Theory]
    [InlineData("freeSpinWagerCents", 75, 3)]
    [InlineData("freeSpinWagerPoints", 100, 400)]
    public void ImportsExactNativeStakeAndHistoricalCollectionTotals(string field, long value, long expectedWager)
    {
        var historic = new Dictionary<string, object>
        {
            [field] = value,
            ["freeSpinFeatureMode"] = "rows-paw",
            ["sealCounts"] = new Dictionary<string, object> { ["rows"] = 7L, ["rand"] = "2" },
            ["sealWagerTotals"] = new Dictionary<string, object> { ["rows"] = 17L, ["rand"] = 5L },
            ["consecutiveFiveMisses"] = 9L,
        };
        var imported = WukongRoundStateCodec.Decode(historic, signedLegacyCount: 3, legacySpecialPoints: 12, legacyEnergy: 99);
        Assert.Equal(new WukongCarry(3, expectedWager, "rows-paw"), imported.Carry);
        Assert.Equal((0, 3, 12, 99L, 9), (imported.NativeRemaining, imported.LegacyRemaining, imported.SpecialPoints, imported.Energy, imported.Pity));
        Assert.Equal(7, imported.SealCounts["rows"]);
        Assert.Equal(2, imported.SealCounts["rand"]);
        Assert.Equal(68, imported.SealWagerPoints["rows"]);
        Assert.Equal(20, imported.SealWagerPoints["rand"]);
        Assert.True(imported.Matches(WukongRoundStateCodec.Decode(WukongRoundStateCodec.Write(imported), signedLegacyCount: 3)));
    }

    [Fact]
    public void AnUnpricedMigratedBankRemainsUnpinnedUntilItPlays()
    {
        var historic = new Dictionary<string, object> { ["freeSpinFeatureMode"] = "paw" };
        var imported = WukongRoundStateCodec.Decode(historic, signedLegacyCount: 5);
        Assert.Equal(new WukongCarry(5, null, "paw"), imported.Carry);
        var frozen = WukongRoundStateCodec.Write(imported);
        Assert.True(imported.Matches(WukongRoundStateCodec.Decode(frozen, signedLegacyCount: 5)));
        Assert.Throws<InvalidOperationException>(() => WukongRoundStateCodec.Decode(frozen, signedLegacyCount: 4));
        Assert.Throws<InvalidOperationException>(() => WukongRoundStateCodec.Write(imported with
        {
            NativeRemaining = 2,
            Carry = new(7, null, "paw"),
        }));

        var played = imported with { LegacyRemaining = 4, NativeRemaining = 2, Carry = new(6, 12, "sync") };
        Assert.True(played.Matches(WukongRoundStateCodec.Decode(WukongRoundStateCodec.Write(played), signedLegacyCount: 4)));
    }

    [Fact]
    public void AnOddWagerIsOnlyLegalForAnAwardedPlay()
    {
        var engine = new WukongEngine(new ZeroRandom());
        Assert.Throws<ArgumentOutOfRangeException>(() => WukongEngine.ValidateWager(3, isFreeSpin: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Spin(3, "wukong-test"));
        WukongEngine.ValidateWager(3, isFreeSpin: true);
        var free = engine.Spin(3, "wukong-test", featureMode: "paw", persistedPityMisses: 0, isFreeSpin: true);
        Assert.Equal(3, free.WagerPoints);
        Assert.True(free.IsFreeSpin);
        Assert.Throws<ArgumentOutOfRangeException>(() => WukongEngine.ValidateWager(2001, isFreeSpin: true));
    }

    [Fact]
    public void ExplicitCentsWinAndUnalignedOrUnrecognizedOutstandingTermsFailClosed()
    {
        var historic = new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 75L,
            ["freeSpinWagerPoints"] = 1000L,
            ["sealWagerCents"] = new Dictionary<string, object> { ["sync"] = 75L },
            ["sealWagerTotals"] = new Dictionary<string, object> { ["sync"] = 8L },
        };
        var imported = WukongRoundStateCodec.Decode(historic, signedLegacyCount: 1);
        Assert.Equal(3, imported.Carry!.WagerPoints);
        Assert.Equal(3, imported.SealWagerPoints["sync"]);
        historic["freeSpinWagerCents"] = 76L;
        Assert.Throws<InvalidOperationException>(() => WukongRoundStateCodec.Decode(historic, signedLegacyCount: 1));
        Assert.Null(WukongRoundStateCodec.Decode(historic, signedLegacyCount: 0).Carry);
        historic["freeSpinWagerCents"] = 75L;
        historic["freeSpinFeatureMode"] = "unrecognized";
        Assert.Throws<InvalidOperationException>(() => WukongRoundStateCodec.Decode(historic, signedLegacyCount: 1));
    }

    [WukongImportFirestoreFact]
    public async Task ClaimReadsHistoricalGameOnlyFieldsAndKeepsTheOwnedCopy()
    {
        const string actor = "wukong-history";
        const string other = "wukong-other";
        const string oldId = WukongEngine.LegacyStorageGameId;
        var db = new FirestoreDbBuilder
        {
            ProjectId = $"demo-wukong-import-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var guards = db.Collection(SlotSpinGuard.Collection);
        var guard = guards.Document(SlotSpinGuard.DocumentId(actor, oldId));
        var wrongScope = guards.Document(SlotSpinGuard.DocumentId(actor, WukongEngine.GameId));
        var balances = db.Collection("userBalances");
        var special = balances.Document($"{actor}_specialPoints:{oldId}");
        var energy = balances.Document($"{actor}_energy:{oldId}");
        var priorFree = balances.Document($"{actor}_freeGames:{oldId}");
        await Task.WhenAll(
            guard.SetAsync(new Dictionary<string, object> { ["consecutiveFiveMisses"] = 5L, ["freeSpinWagerCents"] = 75L }),
            special.SetAsync(new Dictionary<string, object> { ["available"] = 23L, ["userId"] = actor }),
            energy.SetAsync(new Dictionary<string, object> { ["available"] = 85L }),
            priorFree.SetAsync(new Dictionary<string, object> { ["available"] = 15L }),
            balances.Document($"{actor}_specialPoints:{WukongEngine.GameId}").SetAsync(new Dictionary<string, object> { ["available"] = 999L }),
            balances.Document($"{other}_energy:{oldId}").SetAsync(new Dictionary<string, object> { ["available"] = 999L }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.RunTransactionAsync(async tx =>
            await WukongRoundStateCodec.ReadAsync(tx, db, await tx.GetSnapshotAsync(wrongScope), actor, 3, default)));

        async Task<WukongRoundBefore> ReadOrClaim(bool claim) => await db.RunTransactionAsync(async tx =>
        {
            var snapshot = await tx.GetSnapshotAsync(guard);
            var state = await WukongRoundStateCodec.ReadAsync(tx, db, snapshot, actor, 3, default);
            if (claim) tx.Set(guard, WukongRoundStateCodec.Write(state), SetOptions.MergeAll);
            return state;
        });
        var claimed = await ReadOrClaim(claim: true);
        Assert.Equal(new WukongCarry(3, 3, null), claimed.Carry);
        Assert.Equal((23, 85L, 5), (claimed.SpecialPoints, claimed.Energy, claimed.Pity));
        Assert.Equal(15L, (await priorFree.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.True(claimed.Matches(WukongRoundStateCodec.Read(await guard.GetSnapshotAsync(), 3)));

        await special.SetAsync(new Dictionary<string, object> { ["available"] = 8L, ["userId"] = actor });
        await energy.DeleteAsync();
        Assert.True(claimed.Matches(await ReadOrClaim(claim: false)));
        var undo = new Dictionary<string, object>();
        WukongRoundStateCodec.UndoImport(undo);
        await guard.SetAsync(undo, SetOptions.MergeAll);
        var restored = await ReadOrClaim(claim: false);
        Assert.Equal((8, 0L), (restored.SpecialPoints, restored.Energy));
        Assert.Equal(claimed.Carry, restored.Carry);
    }

    private sealed class ZeroRandom : IGameRandomSource
    {
        public int Next(int exclusiveUpperBound) => 0;
    }

    private sealed class WukongImportFirestoreFactAttribute : FactAttribute
    {
        public WukongImportFirestoreFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
                Skip = "The Firestore emulator is required.";
        }
    }
}
