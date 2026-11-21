using FortuneForge.Games.CandyCarnival.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.CandyCarnival.Tests;

public sealed class CandyCarnivalRoundStateCodecTests
{
    [Theory]
    [InlineData("freeSpinWagerCents", 275L, 11L, "sealWagerCents", 725L, 29L)]
    [InlineData("freeSpinWagerPoints", 3L, 12L, "sealWagerTotals", 8L, 32L)]
    public void ImportsExactSugarParadeWagerAndDormantCounters(string wagerField, long wagerValue, long expectedWager,
        string sealWagerField, long sealWagerValue, long expectedSealWager)
    {
        var guard = new Dictionary<string, object>
        {
            [wagerField] = wagerValue,
            ["freeSpinFeatureMode"] = "rand",
            ["consecutiveFiveMisses"] = 6L,
            ["sealCounts"] = new Dictionary<string, object> { ["paw"] = 43L },
            [sealWagerField] = new Dictionary<string, object> { ["paw"] = sealWagerValue },
        };

        var storage = CandyCarnivalRoundStateCodec.Storage;
        var before = storage.Decode(guard, signedLegacyCount: 7, legacySpecialPoints: 8, legacyEnergy: 96);

        Assert.Equal(new ClassicSlotRoundCarry(7, expectedWager, "rand"), before.Carry);
        Assert.Equal((0, 7, 8, 96L, 6),
            (before.NativeRemaining, before.LegacyRemaining, before.SpecialPoints, before.Energy, before.Pity));
        Assert.Equal(43L, before.SealCounts["paw"]);
        Assert.Equal(expectedSealWager, before.SealWagerPoints["paw"]);
        Assert.False(CandyCarnivalEngine.Profile.UsesCollections);
        Assert.False(CandyCarnivalEngine.Profile.UsesEnergy);
        var owned = storage.Write(before);
        Assert.True(before.Matches(storage.Decode(owned, 7)));
        Assert.Throws<InvalidOperationException>(() => storage.Decode(owned, signedLegacyCount: 6));
    }

    [Theory]
    [InlineData(0L, null)]
    [InlineData(25L, 1L)]
    public void MissingAndUnplayableHistoricalWagersRoundTrip(long cents, long? expected)
    {
        var storage = CandyCarnivalRoundStateCodec.Storage;
        var before = storage.Decode(new Dictionary<string, object> { ["freeSpinWagerCents"] = cents }, 4);

        Assert.Equal(new ClassicSlotRoundCarry(4, expected, null), before.Carry);
        Assert.True(before.Matches(storage.Decode(storage.Write(before), 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CandyCarnivalEngine.ValidateWager(1, isFreeSpin: true));
    }

    [Fact]
    public void OddWagerIsPlayableOnlyForASavedSugarParadeAndRetainsRawAuditEnergy()
    {
        var engine = new CandyCarnivalEngine(new FirstIndexRandom());

        Assert.Throws<ArgumentOutOfRangeException>(() => CandyCarnivalEngine.ValidateWager(3, isFreeSpin: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Spin(3, "candy-carnival-state-test"));
        var spin = engine.Spin(3, "candy-carnival-state-test", currentEnergyBalance: 96, featureMode: "rand", isFreeSpin: true);
        Assert.True(spin.IsFreeSpin);
        Assert.Equal(3, spin.WagerPoints);
        Assert.Equal(96, spin.Audit.EnergyBalanceBefore);
        Assert.Equal(0, spin.EnergyAwarded);
        Assert.Empty(spin.CollectionsAwarded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sync")]
    [InlineData("rows")]
    [InlineData("paw")]
    [InlineData("rand")]
    [InlineData("sync-paw")]
    [InlineData("sync-rows-rand")]
    public void HistoricalModesStayPlayableUnderTheirSavedRules(string? mode)
    {
        var storage = CandyCarnivalRoundStateCodec.Storage;
        var before = storage.Decode(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 75L, ["freeSpinFeatureMode"] = mode ?? string.Empty
        }, signedLegacyCount: 2);
        var round = CandyCarnivalRoundPolicy.Prepare("candy-carnival-historical-mode",
            new("candy-carnival-historical-mode-001", 3, true), before, DateTime.UtcNow, null, new FirstIndexRandom());

        Assert.Equal(mode, round.Outcome.Audit.FeatureMode);
        Assert.Equal(mode == "rows" || mode?.Contains("rows", StringComparison.Ordinal) == true ? 6 : 4,
            round.Outcome.Audit.EffectiveGame.Layout.VisibleRows);
        Assert.Equal(0, round.WagerCents);
        Assert.Equal(1, round.Progress.After.LegacyRemaining);
        Assert.True(before.Matches(storage.Decode(storage.Write(before), 2)));
        Assert.DoesNotContain(round.Outcome.Reels.SelectMany(reel => reel), symbol => symbol.StartsWith("RAND_", StringComparison.Ordinal));
        CandyCarnivalRoundPolicy.Validate(round);
    }

    [CandyCarnivalStateFirestoreFact]
    public async Task CanonicalImportKeepsRawScopedBalancesAndIgnoresFirestoreFreeCount()
    {
        const string account = "candy-carnival-state-owner";
        const string other = "candy-carnival-other-owner";
        const string game = CandyCarnivalEngine.GameId;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-candy-carnival-state-{Guid.NewGuid():N}",
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
            guard.SetAsync(new Dictionary<string, object> { ["freeSpinWagerCents"] = 125L, ["freeSpinFeatureMode"] = "rand" }),
            energy.SetAsync(new Dictionary<string, object> { ["available"] = 99L, ["userId"] = account }),
            special.SetAsync(new Dictionary<string, object> { ["available"] = 9L, ["currencyId"] = $"specialPoints:{game}" }),
            free.SetAsync(new Dictionary<string, object> { ["available"] = 78L }),
            balances.Document($"{other}_energy:{game}").SetAsync(new Dictionary<string, object> { ["available"] = 23L }),
            balances.Document($"{account}_specialPoints:candy-carnival").SetAsync(new Dictionary<string, object> { ["available"] = 63L }));

        var storage = CandyCarnivalRoundStateCodec.Storage;
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
        Assert.Equal(new ClassicSlotRoundCarry(3, 5, "rand"), captured.Carry);
        Assert.Equal((9, 99L), (captured.SpecialPoints, captured.Energy));
        Assert.Equal(78L, (await free.GetSnapshotAsync()).GetValue<long>("available"));
        var claimed = await guard.GetSnapshotAsync();
        Assert.Equal(game, claimed.GetValue<string>(CandyCarnivalRoundStateCodec.OwnerField));
        Assert.True(captured.Matches(storage.Read(claimed, 3)));

        await Task.WhenAll(energy.DeleteAsync(), special.UpdateAsync("available", 12L));
        Assert.True(captured.Matches(await ReadOrClaim(claim: false)));
    }

    private sealed class FirstIndexRandom : IGameRandomSource
    {
        public int Next(int exclusiveUpperBound) => 0;
    }

    private sealed class CandyCarnivalStateFirestoreFactAttribute : FactAttribute
    {
        public CandyCarnivalStateFirestoreFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
                Skip = "The Firestore emulator is required.";
        }
    }
}
