using FortuneForge.Games.RoyalDraw.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.RoyalDraw.Tests;

public sealed class RoyalDrawRoundStateCodecTests
{
    [Theory]
    [InlineData("freeSpinWagerCents", 325L, 13L)]
    [InlineData("freeSpinWagerPoints", 4L, 16L)]
    public void ImportsExactBonusWagerAndAllFourWeightedTracks(string field, long value, long expected)
    {
        var guard = new Dictionary<string, object>
        {
            [field] = value,
            ["freeSpinFeatureMode"] = "rows-rand",
            ["consecutiveFiveMisses"] = 6L,
            ["sealCounts"] = new Dictionary<string, object>
            {
                ["sync"] = 11L, ["rows"] = 22L, ["paw"] = 17L, ["rand"] = 23L,
            },
            ["sealWagerCents"] = new Dictionary<string, object>
            {
                ["sync"] = 875L, ["rows"] = 1_725L, ["paw"] = 1_425L, ["rand"] = 2_125L,
            },
            ["sealWagerTotals"] = new Dictionary<string, object> { ["rows"] = 300L },
        };

        var storage = RoyalDrawRoundStateCodec.Storage;
        var before = storage.Decode(guard, signedLegacyCount: 12, legacySpecialPoints: 10, legacyEnergy: 97);

        Assert.Equal(new ClassicSlotRoundCarry(12, expected, "rows-rand"), before.Carry);
        Assert.Equal((0, 12, 10, 97L, 6),
            (before.NativeRemaining, before.LegacyRemaining, before.SpecialPoints, before.Energy, before.Pity));
        Assert.Equal((11L, 22L, 17L, 23L),
            (before.SealCounts["sync"], before.SealCounts["rows"], before.SealCounts["paw"], before.SealCounts["rand"]));
        Assert.Equal((35L, 69L, 57L, 85L),
            (before.SealWagerPoints["sync"], before.SealWagerPoints["rows"], before.SealWagerPoints["paw"], before.SealWagerPoints["rand"]));
        var owned = storage.Write(before);
        Assert.True(before.Matches(storage.Decode(owned, 12)));
        Assert.Throws<InvalidOperationException>(() => storage.Decode(owned, signedLegacyCount: 11));
    }

    [Theory]
    [InlineData(0L, null)]
    [InlineData(25L, 1L)]
    public void MissingAndUnplayableHistoricalWagersRemainInState(long cents, long? expected)
    {
        var storage = RoyalDrawRoundStateCodec.Storage;
        var before = storage.Decode(new Dictionary<string, object> { ["freeSpinWagerCents"] = cents }, 4);

        Assert.Equal(new ClassicSlotRoundCarry(4, expected, null), before.Carry);
        Assert.True(before.Matches(storage.Decode(storage.Write(before), 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoyalDrawEngine.ValidateWager(1, isFreeSpin: true));
    }

    [Fact]
    public void CashWagersKeepTheirIncrementWhileAValidOddBonusWagerReachesTheEngine()
    {
        var engine = new RoyalDrawEngine(new FirstIndexRandom());

        Assert.Throws<ArgumentOutOfRangeException>(() => RoyalDrawEngine.ValidateWager(3, isFreeSpin: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Spin(3, "royal-draw-state-test"));
        var bonus = engine.Spin(3, "royal-draw-state-test", currentEnergyBalance: 95, featureMode: "rows-rand", isFreeSpin: true);
        Assert.True(bonus.IsFreeSpin);
        Assert.Equal(3, bonus.WagerPoints);
        Assert.Equal(95, bonus.Audit.EnergyBalanceBefore);
        Assert.Equal("rows-rand", bonus.Audit.FeatureMode);
    }

    [RoyalDrawStateFirestoreFact]
    public async Task ScopedClaimUsesItsWalletCountAndRetainsOnlyItsOwnMeterSnapshot()
    {
        const string account = "royal-draw-state-owner";
        const string other = "royal-draw-other-owner";
        const string game = RoyalDrawEngine.GameId;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-royal-draw-state-{Guid.NewGuid():N}",
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
            guard.SetAsync(new Dictionary<string, object> { ["freeSpinWagerCents"] = 175L, ["consecutiveFiveMisses"] = 5L }),
            energy.SetAsync(new Dictionary<string, object> { ["available"] = 93L, ["userId"] = account }),
            special.SetAsync(new Dictionary<string, object> { ["available"] = 14L, ["currencyId"] = $"specialPoints:{game}" }),
            free.SetAsync(new Dictionary<string, object> { ["available"] = 81L }),
            balances.Document($"{other}_energy:{game}").SetAsync(new Dictionary<string, object> { ["available"] = 18L }),
            balances.Document($"{account}_specialPoints:royal-draw").SetAsync(new Dictionary<string, object> { ["available"] = 76L }));

        var storage = RoyalDrawRoundStateCodec.Storage;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.RunTransactionAsync(async transaction =>
            await storage.ReadAsync(transaction, database, await transaction.GetSnapshotAsync(otherGuard), account, 3, default)));

        Task<ClassicSlotRoundState> ReadOrClaim(bool claim) => database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(guard);
            var state = await storage.ReadAsync(transaction, database, snapshot, account, 3, default);
            if (claim) transaction.Set(guard, storage.Write(state), SetOptions.MergeAll);
            return state;
        });

        var captured = await ReadOrClaim(claim: true);
        Assert.Equal(new ClassicSlotRoundCarry(3, 7, null), captured.Carry);
        Assert.Equal((14, 93L, 5), (captured.SpecialPoints, captured.Energy, captured.Pity));
        Assert.Equal(81L, (await free.GetSnapshotAsync()).GetValue<long>("available"));
        var claimed = await guard.GetSnapshotAsync();
        Assert.Equal(game, claimed.GetValue<string>(RoyalDrawRoundStateCodec.OwnerField));
        Assert.True(captured.Matches(storage.Read(claimed, 3)));

        await Task.WhenAll(energy.DeleteAsync(), special.UpdateAsync("available", 12L));
        Assert.True(captured.Matches(await ReadOrClaim(claim: false)));
    }

    private sealed class FirstIndexRandom : IGameRandomSource
    {
        public int Next(int exclusiveUpperBound) => 0;
    }

    private sealed class RoyalDrawStateFirestoreFactAttribute : FactAttribute
    {
        public RoyalDrawStateFirestoreFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
                Skip = "The Firestore emulator is required.";
        }
    }
}
