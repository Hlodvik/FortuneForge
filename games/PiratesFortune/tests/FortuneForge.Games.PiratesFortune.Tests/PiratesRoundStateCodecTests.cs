using FortuneForge.Games.PiratesFortune.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.PiratesFortune.Tests;

public sealed class PiratesRoundStateCodecTests
{
    [Theory]
    [InlineData("freeSpinWagerCents", 275, 11)]
    [InlineData("freeSpinWagerPoints", 100, 400)]
    public void ImportsMergedBankAndExactGemWagersWithTheHistoricalAuditEnergy(string key, long saved, long expected)
    {
        var legacy = new Dictionary<string, object>
        {
            [key] = saved,
            ["freeSpinFeatureMode"] = "sync-paw-rand",
            ["consecutiveFiveMisses"] = 9L,
            ["sealCounts"] = new Dictionary<string, object> { ["sync"] = 24L, ["paw"] = 7L },
            ["sealWagerCents"] = new Dictionary<string, object> { ["sync"] = 7_275L, ["paw"] = 625L },
            ["sealWagerTotals"] = new Dictionary<string, object> { ["sync"] = 100L, ["paw"] = 300L },
        };
        var state = PiratesRoundStateCodec.Decode(legacy, signedLegacyCount: 19, legacySpecialPoints: 14, legacyEnergy: 93);
        Assert.Equal(new PiratesCarry(19, expected, "sync-paw-rand"), state.Carry);
        Assert.Equal((0, 19, 14, 93L, 9), (state.NativeRemaining, state.LegacyRemaining, state.SpecialPoints, state.Energy, state.Pity));
        Assert.Equal(24L, state.SealCounts["sync"]);
        Assert.Equal(7L, state.SealCounts["paw"]);
        Assert.Equal(291L, state.SealWagerPoints["sync"]);
        Assert.Equal(25L, state.SealWagerPoints["paw"]);
        Assert.True(state.Matches(PiratesRoundStateCodec.Decode(PiratesRoundStateCodec.Write(state), signedLegacyCount: 19)));
    }

    [Fact]
    public void UnpricedUnitsRemainUnpinnedAndOnlyARealAwardedWagerMayBeOdd()
    {
        var imported = PiratesRoundStateCodec.Decode(new Dictionary<string, object>(), signedLegacyCount: 3);
        Assert.Equal(new PiratesCarry(3, null, null), imported.Carry);
        Assert.True(imported.Matches(PiratesRoundStateCodec.Decode(PiratesRoundStateCodec.Write(imported), signedLegacyCount: 3)));
        Assert.Throws<InvalidOperationException>(() => PiratesRoundStateCodec.Write(imported with
        {
            NativeRemaining = 1,
            Carry = new(4, null, null),
        }));
        var engine = new PiratesFortuneEngine(new ZeroRandom());
        Assert.Throws<ArgumentOutOfRangeException>(() => PiratesFortuneEngine.ValidateWager(3, isFreeSpin: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Spin(3, "pirates-codec"));
        var spin = engine.Spin(3, "pirates-codec", currentEnergyBalance: 74, featureMode: "sync-rand", isFreeSpin: true);
        Assert.True(spin.IsFreeSpin);
        Assert.Equal(3, spin.WagerPoints);
        Assert.Equal(74, spin.Audit.EnergyBalanceBefore);
        Assert.Equal(0, spin.EnergyAwarded);
    }

    [Fact]
    public void UnplayableOnePointLegacyAwardRemainsVisibleWithoutOpeningTheEngineWager()
    {
        var saved = PiratesRoundStateCodec.Decode(new Dictionary<string, object> { ["freeSpinWagerCents"] = 25L },
            signedLegacyCount: 3);
        Assert.Equal(new PiratesCarry(3, 1, null), saved.Carry);
        Assert.True(saved.Matches(PiratesRoundStateCodec.Decode(PiratesRoundStateCodec.Write(saved), signedLegacyCount: 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PiratesFortuneEngine.ValidateWager(1, isFreeSpin: true));
    }

    [PiratesImportFirestoreFact]
    public async Task ClaimKeepsCanonicalGameAndActorScopeAndRetainsOnlyItsOwnedSnapshot()
    {
        const string actor = "pirates-import-actor";
        const string other = "pirates-other-actor";
        const string game = PiratesFortuneEngine.GameId;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-pirates-import-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var guards = database.Collection(SlotSpinGuard.Collection);
        var guard = guards.Document(SlotSpinGuard.DocumentId(actor, game));
        var otherGuard = guards.Document(SlotSpinGuard.DocumentId(other, game));
        var balances = database.Collection("userBalances");
        var special = balances.Document($"{actor}_specialPoints:{game}");
        var energy = balances.Document($"{actor}_energy:{game}");
        var rawFree = balances.Document($"{actor}_freeGames:{game}");
        await Task.WhenAll(
            guard.SetAsync(new Dictionary<string, object> { ["freeSpinWagerCents"] = 125L, ["consecutiveFiveMisses"] = 2L }),
            special.SetAsync(new Dictionary<string, object> { ["available"] = 8L, ["userId"] = actor }),
            energy.SetAsync(new Dictionary<string, object> { ["available"] = 92L }),
            rawFree.SetAsync(new Dictionary<string, object> { ["available"] = 77L }),
            balances.Document($"{other}_energy:{game}").SetAsync(new Dictionary<string, object> { ["available"] = 91L }),
            balances.Document($"{actor}_specialPoints:classic-demo-v1").SetAsync(new Dictionary<string, object> { ["available"] = 99L }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.RunTransactionAsync(async tx =>
            await PiratesRoundStateCodec.ReadAsync(tx, database, await tx.GetSnapshotAsync(otherGuard), actor, 2, default)));

        async Task<PiratesRoundBefore> ReadOrClaim(bool claim) => await database.RunTransactionAsync(async tx =>
        {
            var snapshot = await tx.GetSnapshotAsync(guard);
            var state = await PiratesRoundStateCodec.ReadAsync(tx, database, snapshot, actor, 2, default);
            if (claim) tx.Set(guard, PiratesRoundStateCodec.Write(state), SetOptions.MergeAll);
            return state;
        });
        var claimed = await ReadOrClaim(claim: true);
        Assert.Equal(new PiratesCarry(2, 5, null), claimed.Carry);
        Assert.Equal((8, 92L, 2), (claimed.SpecialPoints, claimed.Energy, claimed.Pity));
        Assert.Equal(77L, (await rawFree.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.True(claimed.Matches(PiratesRoundStateCodec.Read(await guard.GetSnapshotAsync(), 2)));
        await energy.DeleteAsync();
        await special.UpdateAsync("available", 12L);
        Assert.True(claimed.Matches(await ReadOrClaim(claim: false)));
    }

    private sealed class ZeroRandom : IGameRandomSource
    {
        public int Next(int exclusiveUpperBound) => 0;
    }

    private sealed class PiratesImportFirestoreFactAttribute : FactAttribute
    {
        public PiratesImportFirestoreFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
                Skip = "The Firestore emulator is required.";
        }
    }
}
