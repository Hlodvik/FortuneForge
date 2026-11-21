using FortuneForge.Games.GodsOfOlympus.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.GodsOfOlympus.Tests;

public sealed class OlympusRoundStateCodecTests
{
    [Theory]
    [InlineData("freeSpinWagerCents", 275, 11)]
    [InlineData("freeSpinWagerPoints", 100, 400)]
    public void ImportPreservesExactClassicCarryAndCollectionWagers(string field, long saved, long expected)
    {
        var guard = new Dictionary<string, object>
        {
            [field] = saved,
            ["freeSpinFeatureMode"] = "paw-rand",
            ["consecutiveFiveMisses"] = 7L,
            ["sealCounts"] = new Dictionary<string, object> { ["paw"] = 18L, ["rand"] = 15L },
            ["sealWagerCents"] = new Dictionary<string, object> { ["paw"] = 2_775L, ["rand"] = 1_125L },
            ["sealWagerTotals"] = new Dictionary<string, object> { ["paw"] = 4L, ["rand"] = 5L },
        };

        var storage = OlympusRoundStateCodec.Storage;
        var before = storage.Decode(guard, signedLegacyCount: 11, legacySpecialPoints: 6, legacyEnergy: 111);

        Assert.Equal(new ClassicSlotRoundCarry(11, expected, "paw-rand"), before.Carry);
        Assert.Equal((0, 11, 6, 111L, 7),
            (before.NativeRemaining, before.LegacyRemaining, before.SpecialPoints, before.Energy, before.Pity));
        Assert.Equal(18L, before.SealCounts["paw"]);
        Assert.Equal(15L, before.SealCounts["rand"]);
        Assert.Equal(111L, before.SealWagerPoints["paw"]);
        Assert.Equal(45L, before.SealWagerPoints["rand"]);
        var owned = storage.Write(before);
        Assert.True(before.Matches(storage.Decode(owned, 11)));
        Assert.Throws<InvalidOperationException>(() => storage.Decode(owned, signedLegacyCount: 10));
    }

    [Theory]
    [InlineData(0L, null)]
    [InlineData(25L, 1L)]
    public void MissingAndHistoricallyUnplayableWagersRoundTripWithoutChangingAdmissionRules(long cents, long? wager)
    {
        var storage = OlympusRoundStateCodec.Storage;
        var carried = storage.Decode(new Dictionary<string, object> { ["freeSpinWagerCents"] = cents }, 3);

        Assert.Equal(new ClassicSlotRoundCarry(3, wager, null), carried.Carry);
        Assert.True(carried.Matches(storage.Decode(storage.Write(carried), 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => GodsOfOlympusEngine.ValidateWager(1, isFreeSpin: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => GodsOfOlympusEngine.ValidateWager(3, isFreeSpin: false));
        GodsOfOlympusEngine.ValidateWager(3, isFreeSpin: true);
    }

    [OlympusStateFirestoreFact]
    public async Task ImportsCanonicalGameAndActorWithoutReadingOrRewritingWalletUnits()
    {
        const string actor = "olympus-import-actor";
        const string other = "olympus-other-actor";
        const string game = GodsOfOlympusEngine.GameId;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-olympus-state-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var guards = database.Collection(SlotSpinGuard.Collection);
        var guard = guards.Document(SlotSpinGuard.DocumentId(actor, game));
        var otherGuard = guards.Document(SlotSpinGuard.DocumentId(other, game));
        var balances = database.Collection("userBalances");
        var special = balances.Document($"{actor}_specialPoints:{game}");
        var energy = balances.Document($"{actor}_energy:{game}");
        var legacyFree = balances.Document($"{actor}_freeGames:{game}");
        await Task.WhenAll(
            guard.SetAsync(new Dictionary<string, object> { ["freeSpinWagerCents"] = 125L, ["freeSpinFeatureMode"] = "paw-rand" }),
            special.SetAsync(new Dictionary<string, object> { ["available"] = 8L, ["userId"] = actor }),
            energy.SetAsync(new Dictionary<string, object> { ["available"] = 131L, ["currencyId"] = $"energy:{game}" }),
            legacyFree.SetAsync(new Dictionary<string, object> { ["available"] = 77L }),
            balances.Document($"{other}_energy:{game}").SetAsync(new Dictionary<string, object> { ["available"] = 91L }),
            balances.Document($"{actor}_specialPoints:classic-demo-v1").SetAsync(new Dictionary<string, object> { ["available"] = 99L }));
        var storage = OlympusRoundStateCodec.Storage;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.RunTransactionAsync(async transaction =>
            await storage.ReadAsync(transaction, database, await transaction.GetSnapshotAsync(otherGuard), actor, 2, default)));

        Task<ClassicSlotRoundState> ReadOrClaim(bool claim) => database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(guard);
            var state = await storage.ReadAsync(transaction, database, snapshot, actor, 2, default);
            if (claim) transaction.Set(guard, storage.Write(state), SetOptions.MergeAll);
            return state;
        });

        var captured = await ReadOrClaim(claim: true);
        Assert.Equal(new ClassicSlotRoundCarry(2, 5, "paw-rand"), captured.Carry);
        Assert.Equal((8, 131L), (captured.SpecialPoints, captured.Energy));
        Assert.Equal(77L, (await legacyFree.GetSnapshotAsync()).GetValue<long>("available"));
        var claimedGuard = await guard.GetSnapshotAsync();
        Assert.Equal(game, claimedGuard.GetValue<string>(OlympusRoundStateCodec.OwnerField));
        Assert.Equal(125L, claimedGuard.GetValue<long>("freeSpinWagerCents"));
        Assert.True(captured.Matches(storage.Read(claimedGuard, 2)));

        await Task.WhenAll(energy.DeleteAsync(), special.UpdateAsync("available", 12L));
        Assert.True(captured.Matches(await ReadOrClaim(claim: false)));
    }

    private sealed class OlympusStateFirestoreFactAttribute : FactAttribute
    {
        public OlympusStateFirestoreFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
                Skip = "The Firestore emulator is required.";
        }
    }
}
