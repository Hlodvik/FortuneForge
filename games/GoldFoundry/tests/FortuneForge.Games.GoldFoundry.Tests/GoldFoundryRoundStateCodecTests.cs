using FortuneForge.Games.GoldFoundry.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.GoldFoundry.Tests;

public sealed class GoldFoundryRoundStateCodecTests
{
    [Fact]
    public void LegacyImportRetainsExactWagerCentsAndChargeForTheNextPour()
    {
        var historical = new Dictionary<string, object>
        {
            ["consecutiveFiveMisses"] = 12L,
            ["crucibleWageredCents"] = 19_937L,
            ["freeSpinWagerPoints"] = 40L,
            ["freeSpinFeatureMode"] = "another-game",
        };
        var imported = GoldFoundryRoundStateCodec.Decode(historical, legacyEnergy: 99);
        Assert.Equal(new GoldFoundryRoundBefore(99, 19_937, 12), imported);
        var noBundle = GoldFoundryCrucibleRules.Settle(imported.Energy, 0, imported.CrucibleWageredCents, 225);
        Assert.Equal(19_937, noBundle.WageredCents);
        Assert.False(noBundle.Poured);
        var pour = GoldFoundryCrucibleRules.Settle(imported.Energy, 2, imported.CrucibleWageredCents, 225);
        Assert.True(pour.Poured);
        Assert.Equal(3_024, pour.PayoutCents);
        Assert.Equal(0, pour.WageredCents);
        Assert.Equal(0, pour.EnergyBalance);

        var owned = GoldFoundryRoundStateCodec.Write(imported);
        Assert.Equal(GoldFoundryRoundStateCodec.Owner, owned[GoldFoundryRoundStateCodec.OwnerField]);
        Assert.Equal(imported, GoldFoundryRoundStateCodec.Decode(owned, legacyEnergy: 1));
        Assert.False(owned.ContainsKey("crucibleWageredCents"));
        Assert.False(owned.ContainsKey("consecutiveFiveMisses"));
    }

    [Fact]
    public void FullOldMeterRetainsUnnormalizedChargeBeforeCalculation()
    {
        var historical = new Dictionary<string, object> { ["crucibleWageredCents"] = 79L };
        var imported = GoldFoundryRoundStateCodec.Decode(historical, legacyEnergy: 117);
        Assert.Equal(117, imported.Energy);
        var pour = GoldFoundryCrucibleRules.Settle(imported.Energy, 0, imported.CrucibleWageredCents, 200);
        Assert.True(pour.Poured);
        Assert.Equal(100, pour.MeterBalanceBeforeReset);
        Assert.Equal(11, pour.PayoutCents);
        Assert.Equal(imported, GoldFoundryRoundStateCodec.Decode(GoldFoundryRoundStateCodec.Write(imported)));
    }

    [Fact]
    public void BlankHistoryStartsAtZeroAndInvalidImportDoesNotSilentlyReset()
    {
        var guard = new Dictionary<string, object>();
        Assert.Equal(new GoldFoundryRoundBefore(0, 0, 0), GoldFoundryRoundStateCodec.Decode(guard));
        Assert.Throws<InvalidOperationException>(() => GoldFoundryRoundStateCodec.Decode(guard, legacyEnergy: -1));
        Assert.Throws<InvalidOperationException>(() => GoldFoundryRoundStateCodec.Write(new(0, -1, 0)));
        guard["crucibleWageredCents"] = -1L;
        Assert.Throws<InvalidOperationException>(() => GoldFoundryRoundStateCodec.Decode(guard));
        guard["crucibleWageredCents"] = 1L;
        guard[GoldFoundryRoundStateCodec.OwnerField] = "foreign-owner";
        Assert.Throws<InvalidOperationException>(() => GoldFoundryRoundStateCodec.Decode(guard));
        guard[GoldFoundryRoundStateCodec.OwnerField] = GoldFoundryRoundStateCodec.Owner;
        Assert.Throws<InvalidOperationException>(() => GoldFoundryRoundStateCodec.Decode(guard));
    }

    [FoundryImportFirestoreFact]
    public async Task FirestoreClaimReadsOnlyItsScopedEnergyOnceAndPreservesLegacyRecord()
    {
        const string account = "gold-import-pilot";
        const string anotherAccount = "gold-import-other";
        const string game = GoldFoundryEngine.GameId;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-gold-import-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var guards = database.Collection(SlotSpinGuard.Collection);
        var guard = guards.Document(SlotSpinGuard.DocumentId(account, game));
        var unrelatedGuard = guards.Document(SlotSpinGuard.DocumentId(anotherAccount, game));
        var balances = database.Collection("userBalances");
        var original = balances.Document($"{account}_energy:{game}");
        await Task.WhenAll(
            guard.SetAsync(new Dictionary<string, object> { ["consecutiveFiveMisses"] = 77L, ["crucibleWageredCents"] = 1099L }),
            original.SetAsync(new Dictionary<string, object> { ["available"] = 88L, ["currencyId"] = $"energy:{game}" }),
            balances.Document($"{anotherAccount}_energy:{game}")
                .SetAsync(new Dictionary<string, object> { ["available"] = 70L }),
            balances.Document($"{account}_energy:another-game")
                .SetAsync(new Dictionary<string, object> { ["available"] = 415L }),
            balances.Document($"{account}_slotsCredits")
                .SetAsync(new Dictionary<string, object> { ["available"] = 9999L }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.RunTransactionAsync(async tx =>
            await GoldFoundryRoundStateCodec.ReadAsync(tx, database, await tx.GetSnapshotAsync(unrelatedGuard), account, default)));

        async Task<GoldFoundryRoundBefore> ReadOrClaim(bool claim) => await database.RunTransactionAsync(async tx =>
        {
            var snapshot = await tx.GetSnapshotAsync(guard);
            var state = await GoldFoundryRoundStateCodec.ReadAsync(tx, database, snapshot, account, default);
            if (claim) tx.Set(guard, GoldFoundryRoundStateCodec.Write(state), SetOptions.MergeAll);
            return state;
        });

        var claimed = await ReadOrClaim(claim: true);
        Assert.Equal(new GoldFoundryRoundBefore(88, 1099, 77), claimed);
        Assert.Equal(88L, (await original.GetSnapshotAsync()).GetValue<long>("available"));
        var stored = await guard.GetSnapshotAsync();
        Assert.Equal(1099L, stored.GetValue<long>("crucibleWageredCents"));
        Assert.Equal(claimed, GoldFoundryRoundStateCodec.Read(stored));

        await original.SetAsync(new Dictionary<string, object> { ["userId"] = anotherAccount, ["available"] = 0L });
        await guard.UpdateAsync("crucibleWageredCents", 500L);
        Assert.Equal(claimed, await ReadOrClaim(claim: false));
    }

    private sealed class FoundryImportFirestoreFactAttribute : FactAttribute
    {
        public FoundryImportFirestoreFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
                Skip = "The Firestore emulator is required.";
        }
    }
}
