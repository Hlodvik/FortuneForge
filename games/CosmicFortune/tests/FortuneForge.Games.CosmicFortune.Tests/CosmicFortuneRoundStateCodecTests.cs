using FortuneForge.Games.CosmicFortune.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.CosmicFortune.Tests;

public sealed class CosmicFortuneRoundStateCodecTests
{
    [Theory]
    [InlineData("freeSpinWagerCents", 325L, 13L)]
    [InlineData("freeSpinWagerPoints", 3L, 12L)]
    public void ImportsExactWagerEnergyAndAllFourSealTracks(string field, long value, long expectedWager)
    {
        var legacy = new Dictionary<string, object>
        {
            [field] = value,
            ["freeSpinFeatureMode"] = "sync-rows",
            ["consecutiveFiveMisses"] = 7L,
            ["sealCounts"] = new Dictionary<string, object>
            {
                ["sync"] = 11L, ["rows"] = 21L, ["paw"] = 18L, ["rand"] = 23L,
            },
            ["sealWagerCents"] = new Dictionary<string, object>
            {
                ["sync"] = 1_875L, ["rows"] = 2_125L, ["paw"] = 925L, ["rand"] = 1_225L,
            },
            ["sealWagerTotals"] = new Dictionary<string, object> { ["rows"] = 30L },
        };

        var storage = CosmicFortuneRoundStateCodec.Storage;
        var before = storage.Decode(legacy, signedLegacyCount: 8, legacySpecialPoints: 10, legacyEnergy: 94);
        Assert.Equal(new ClassicSlotRoundCarry(8, expectedWager, "sync-rows"), before.Carry);
        Assert.Equal((0, 8, 10, 94L, 7),
            (before.NativeRemaining, before.LegacyRemaining, before.SpecialPoints, before.Energy, before.Pity));
        Assert.Equal((11L, 21L, 18L, 23L),
            (before.SealCounts["sync"], before.SealCounts["rows"], before.SealCounts["paw"], before.SealCounts["rand"]));
        Assert.Equal((75L, 85L, 37L, 49L),
            (before.SealWagerPoints["sync"], before.SealWagerPoints["rows"], before.SealWagerPoints["paw"], before.SealWagerPoints["rand"]));
        var saved = storage.Write(before);
        Assert.True(before.Matches(storage.Decode(saved, signedLegacyCount: 8)));
        Assert.Throws<InvalidOperationException>(() => storage.Decode(saved, signedLegacyCount: 7));
    }

    [Theory]
    [InlineData(0L, null)]
    [InlineData(25L, 1L)]
    public void UnpricedAndUnplayableHistoricalWagersRemainVisible(long cents, long? expectedWager)
    {
        var storage = CosmicFortuneRoundStateCodec.Storage;
        var imported = storage.Decode(new Dictionary<string, object> { ["freeSpinWagerCents"] = cents }, 3);
        Assert.Equal(new ClassicSlotRoundCarry(3, expectedWager, null), imported.Carry);
        Assert.True(imported.Matches(storage.Decode(storage.Write(imported), 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CosmicFortuneEngine.ValidateWager(1, isFreeSpin: true));
    }

    [Fact]
    public void AnOddWagerWorksForAnAwardedRoundAndStillFailsForCash()
    {
        var engine = new CosmicFortuneEngine(new FirstIndexRandom());

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Spin(3, "cosmic-fortune-state-test"));
        var round = engine.Spin(3, "cosmic-fortune-state-test", currentEnergyBalance: 94, featureMode: "sync-rows", isFreeSpin: true);
        Assert.True(round.IsFreeSpin);
        Assert.Equal(3, round.WagerPoints);
        Assert.Equal("sync-rows", round.Audit.FeatureMode);
        Assert.Equal(94, round.Audit.EnergyBalanceBefore);
    }

    [CosmicStateFirestoreFact]
    public async Task CanonicalScopeUsesOnlyTheSignedFreeBankAndCapturesItsOwnGameUnits()
    {
        const string account = "cosmic-fortune-state-test";
        const string other = "cosmic-fortune-other-account";
        const string game = CosmicFortuneEngine.GameId;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-cosmic-state-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var guardCollection = database.Collection(SlotSpinGuard.Collection);
        var guard = guardCollection.Document(SlotSpinGuard.DocumentId(account, game));
        var otherGuard = guardCollection.Document(SlotSpinGuard.DocumentId(other, game));
        var balances = database.Collection("userBalances");
        var special = balances.Document($"{account}_specialPoints:{game}");
        var energy = balances.Document($"{account}_energy:{game}");
        var free = balances.Document($"{account}_freeGames:{game}");
        await Task.WhenAll(
            guard.SetAsync(new Dictionary<string, object> { ["freeSpinWagerCents"] = 325L }),
            special.SetAsync(new Dictionary<string, object> { ["available"] = 11L, ["userId"] = account }),
            energy.SetAsync(new Dictionary<string, object> { ["available"] = 94L, ["currencyId"] = $"energy:{game}" }),
            free.SetAsync(new Dictionary<string, object> { ["available"] = 84L }),
            balances.Document($"{other}_energy:{game}").SetAsync(new Dictionary<string, object> { ["available"] = 28L }),
            balances.Document($"{account}_specialPoints:cosmic-fortune").SetAsync(new Dictionary<string, object> { ["available"] = 71L }));

        var storage = CosmicFortuneRoundStateCodec.Storage;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.RunTransactionAsync(async transaction =>
            await storage.ReadAsync(transaction, database, await transaction.GetSnapshotAsync(otherGuard), account, 4, default)));
        var state = await database.RunTransactionAsync(async transaction =>
        {
            var original = await transaction.GetSnapshotAsync(guard);
            var imported = await storage.ReadAsync(transaction, database, original, account, 4, default);
            transaction.Set(guard, storage.Write(imported), SetOptions.MergeAll);
            return imported;
        });
        Assert.Equal(new ClassicSlotRoundCarry(4, 13L, null), state.Carry);
        Assert.Equal((11, 94L), (state.SpecialPoints, state.Energy));
        Assert.Equal(84L, (await free.GetSnapshotAsync()).GetValue<long>("available"));
        var claimed = await guard.GetSnapshotAsync();
        Assert.Equal(game, claimed.GetValue<string>(CosmicFortuneRoundStateCodec.OwnerField));
        Assert.True(state.Matches(storage.Read(claimed, 4)));
    }

    private sealed class FirstIndexRandom : IGameRandomSource
    {
        public int Next(int exclusiveUpperBound) => 0;
    }

    private sealed class CosmicStateFirestoreFactAttribute : FactAttribute
    {
        public CosmicStateFirestoreFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
                Skip = "The Firestore emulator is required.";
        }
    }
}
