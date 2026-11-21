using FortuneForge.Games.ReelRiches.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.ReelRiches.Tests;

public sealed class ReelRichesRoundStateCodecTests
{
    [Theory]
    [InlineData("freeSpinWagerCents", 275L, 11L)]
    [InlineData("freeSpinWagerPoints", 100L, 400L)]
    public void ImportsExactTrophyCarryAndWeightedTrackProgress(string field, long stored, long expected)
    {
        var legacy = new Dictionary<string, object>
        {
            [field] = stored,
            ["freeSpinFeatureMode"] = ReelRichesEngine.Profile.CollectionFeatureMode!,
            ["consecutiveFiveMisses"] = 8L,
            ["sealCounts"] = new Dictionary<string, object> { ["sync"] = 34L, ["rand"] = 37L },
            ["sealWagerCents"] = new Dictionary<string, object> { ["sync"] = 4_075L, ["rand"] = 5_050L },
            ["sealWagerTotals"] = new Dictionary<string, object> { ["sync"] = 90L, ["rand"] = 110L },
        };

        var storage = ReelRichesRoundStateCodec.Storage;
        var before = storage.Decode(legacy, signedLegacyCount: 15, legacySpecialPoints: 13, legacyEnergy: 106);

        Assert.Equal(new ClassicSlotRoundCarry(15, expected, "sync-rows-rand"), before.Carry);
        Assert.Equal((0, 15, 13, 106L, 8),
            (before.NativeRemaining, before.LegacyRemaining, before.SpecialPoints, before.Energy, before.Pity));
        Assert.Equal(34L, before.SealCounts["sync"]);
        Assert.Equal(37L, before.SealCounts["rand"]);
        Assert.Equal(163L, before.SealWagerPoints["sync"]);
        Assert.Equal(202L, before.SealWagerPoints["rand"]);
        var owned = storage.Write(before);
        Assert.True(before.Matches(storage.Decode(owned, 15)));
        Assert.Throws<InvalidOperationException>(() => storage.Decode(owned, signedLegacyCount: 14));
    }

    [Theory]
    [InlineData(0L, null)]
    [InlineData(25L, 1L)]
    public void UnpricedAndUnplayableHistoricalWagersRemainVisible(long cents, long? expected)
    {
        var storage = ReelRichesRoundStateCodec.Storage;
        var before = storage.Decode(new Dictionary<string, object> { ["freeSpinWagerCents"] = cents }, 4);

        Assert.Equal(new ClassicSlotRoundCarry(4, expected, null), before.Carry);
        Assert.True(before.Matches(storage.Decode(storage.Write(before), 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReelRichesEngine.ValidateWager(1, isFreeSpin: true));
    }

    [Fact]
    public void OddWagerIsAcceptedByTheEngineForAFreeCastOnly()
    {
        var engine = new ReelRichesEngine(new FirstIndexRandom());

        Assert.Throws<ArgumentOutOfRangeException>(() => ReelRichesEngine.ValidateWager(3, isFreeSpin: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Spin(3, "reel-riches-test"));
        var freeCast = engine.Spin(3, "reel-riches-test", currentEnergyBalance: 95, isFreeSpin: true);
        Assert.True(freeCast.IsFreeSpin);
        Assert.Equal(3, freeCast.WagerPoints);
        Assert.Equal(95, freeCast.Audit.EnergyBalanceBefore);
    }

    [ReelRichesStateFirestoreFact]
    public async Task ClaimsTheCanonicalGameStateAndUsesTheSignedLegacyCount()
    {
        const string account = "reel-riches-import";
        const string other = "another-import-account";
        const string game = ReelRichesEngine.GameId;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-reel-riches-state-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var guards = database.Collection(SlotSpinGuard.Collection);
        var guard = guards.Document(SlotSpinGuard.DocumentId(account, game));
        var otherGuard = guards.Document(SlotSpinGuard.DocumentId(other, game));
        var balances = database.Collection("userBalances");
        var energy = balances.Document($"{account}_energy:{game}");
        var special = balances.Document($"{account}_specialPoints:{game}");
        var free = balances.Document($"{account}_freeGames:{game}");
        await Task.WhenAll(
            guard.SetAsync(new Dictionary<string, object> { ["freeSpinWagerCents"] = 125L, ["consecutiveFiveMisses"] = 5L }),
            energy.SetAsync(new Dictionary<string, object> { ["available"] = 93L, ["userId"] = account }),
            special.SetAsync(new Dictionary<string, object> { ["available"] = 9L, ["currencyId"] = $"specialPoints:{game}" }),
            free.SetAsync(new Dictionary<string, object> { ["available"] = 71L }),
            balances.Document($"{other}_energy:{game}").SetAsync(new Dictionary<string, object> { ["available"] = 86L }),
            balances.Document($"{account}_specialPoints:reel-riches").SetAsync(new Dictionary<string, object> { ["available"] = 77L }));

        var storage = ReelRichesRoundStateCodec.Storage;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.RunTransactionAsync(async transaction =>
            await storage.ReadAsync(transaction, database, await transaction.GetSnapshotAsync(otherGuard), account, 3, default)));

        Task<ClassicSlotRoundState> ReadOrClaim(bool claim) => database.RunTransactionAsync(async transaction =>
        {
            var current = await transaction.GetSnapshotAsync(guard);
            var before = await storage.ReadAsync(transaction, database, current, account, 3, default);
            if (claim) transaction.Set(guard, storage.Write(before), SetOptions.MergeAll);
            return before;
        });

        var captured = await ReadOrClaim(claim: true);
        Assert.Equal(new ClassicSlotRoundCarry(3, 5, null), captured.Carry);
        Assert.Equal((9, 93L, 5), (captured.SpecialPoints, captured.Energy, captured.Pity));
        Assert.Equal(71L, (await free.GetSnapshotAsync()).GetValue<long>("available"));
        var claimed = await guard.GetSnapshotAsync();
        Assert.Equal(game, claimed.GetValue<string>(ReelRichesRoundStateCodec.OwnerField));
        Assert.Equal(125L, claimed.GetValue<long>("freeSpinWagerCents"));
        Assert.True(captured.Matches(storage.Read(claimed, 3)));

        await Task.WhenAll(special.DeleteAsync(), energy.UpdateAsync("available", 46L));
        Assert.True(captured.Matches(await ReadOrClaim(claim: false)));
    }

    private sealed class FirstIndexRandom : IGameRandomSource
    {
        public int Next(int exclusiveUpperBound) => 0;
    }

    private sealed class ReelRichesStateFirestoreFactAttribute : FactAttribute
    {
        public ReelRichesStateFirestoreFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
                Skip = "The Firestore emulator is required.";
        }
    }
}
