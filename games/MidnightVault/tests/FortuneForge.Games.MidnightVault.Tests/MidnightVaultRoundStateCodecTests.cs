using FortuneForge.Games.MidnightVault.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.MidnightVault.Tests;

public sealed class MidnightVaultRoundStateCodecTests
{
    private const string GuardId = "historic-midnight-guard";
    private const string V1 = MidnightVaultSlotDefinitions.LegacyPaytableId;
    private const string V2 = MidnightVaultSlotDefinitions.PreviousPaytableId;
    private const string V3 = MidnightVaultSlotDefinitions.PaytableId;

    [Theory]
    [InlineData("freeSpinWagerCents", 75, 3)]
    [InlineData("freeSpinWagerPoints", 100, 400)]
    public void MissingQueueRetainsUnversionedLegacyLockAndExactSealTotals(string wagerField, long wager, long points)
    {
        var historical = new Dictionary<string, object>
        {
            [wagerField] = wager,
            ["freeSpinFeatureMode"] = "sync-rand",
            ["consecutiveFiveMisses"] = 12L,
            ["sealCounts"] = new Dictionary<string, object> { ["rows"] = 3L, ["rand"] = "2" },
            ["sealWagerTotals"] = new Dictionary<string, object> { ["rows"] = 3L, ["rand"] = 7L },
        };
        var imported = MidnightVaultRoundStateCodec.Decode(historical, GuardId, signedLegacyCount: 37,
            legacySpecialPoints: 8, legacyEnergy: 75);
        Assert.Equal(new MidnightVaultBonusRound($"{GuardId}:legacy", 37, 37, points, "sync-rand", V1), Assert.Single(imported.Rounds));
        Assert.Equal((37L, 8, 75L, 12), (imported.LegacyRemaining, imported.SpecialPoints, imported.Energy, imported.Pity));
        Assert.Equal(3, imported.SealCounts["rows"]);
        Assert.Equal(2, imported.SealCounts["rand"]);
        Assert.Equal(12, imported.SealWagerPoints["rows"]);
        Assert.Equal(28, imported.SealWagerPoints["rand"]);
        Assert.Equal(0, imported.SealWagerPoints["sync"]);
        Assert.True(imported.Matches(MidnightVaultRoundStateCodec.Decode(MidnightVaultRoundStateCodec.Write(imported),
            GuardId, signedLegacyCount: 37, legacySpecialPoints: 200, legacyEnergy: 200)));
    }

    [Fact]
    public void ExplicitLegacyQueuePreservesMixedModelsAndAnOddWagerBehindItsHead()
    {
        var first = new MidnightVaultBonusRound("legacy-v1", 7, 1, 11, "sync-rand", V1);
        var second = new MidnightVaultBonusRound("legacy-v2", 10, 2, 87, "rows-paw", V2);
        var third = new MidnightVaultBonusRound("legacy-v3", 10, 9, 1999, null, V3);
        var historical = new Dictionary<string, object>
        {
            ["midnightBonusRounds"] = new[] { Encode(first), Encode(second), Encode(third) },
            ["freeSpinWagerCents"] = 12L,
            ["freeSpinMathModelId"] = "must-not-override-queue",
            ["sealCounts"] = new Dictionary<string, object> { ["paw"] = 2L },
            ["sealWagerCents"] = new Dictionary<string, object> { ["paw"] = 225L },
            ["sealWagerTotals"] = new Dictionary<string, object> { ["paw"] = 15L },
        };
        var imported = MidnightVaultRoundStateCodec.Decode(historical, GuardId, signedLegacyCount: 12);
        Assert.Equal(new[] { first, second, third }, imported.Rounds);
        Assert.Equal(9, imported.SealWagerPoints["paw"]);
        Assert.Throws<InvalidOperationException>(() => MidnightVaultRoundStateCodec.Decode(historical, GuardId, signedLegacyCount: 11));

        var native = new MidnightVaultBonusRound("native-retrigger", 7, 7, 9, "rows-paw", V1);
        var afterFirst = imported with
        {
            Rounds = [.. MidnightVaultBonusRounds.Consume(imported.Rounds), native],
            LegacyRemaining = 11,
        };
        var owned = MidnightVaultRoundStateCodec.Write(afterFirst);
        Assert.Equal(MidnightVaultRoundStateCodec.Owner, owned[MidnightVaultRoundStateCodec.OwnerField]);
        Assert.True(afterFirst.Matches(MidnightVaultRoundStateCodec.Decode(owned, GuardId, signedLegacyCount: 11)));
        Assert.Throws<InvalidOperationException>(() => MidnightVaultRoundStateCodec.Decode(owned, GuardId, signedLegacyCount: 10));
        Assert.Throws<InvalidOperationException>(() => MidnightVaultRoundStateCodec.Write(afterFirst with { LegacyRemaining = 10 }));
    }

    [Fact]
    public void EmptyHistoryIgnoresRetiredLockAndBadOwedTermsFailBeforeImport()
    {
        var historical = new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 101L,
            ["freeSpinFeatureMode"] = "stale",
            ["freeSpinMathModelId"] = "stale",
        };
        Assert.Empty(MidnightVaultRoundStateCodec.Decode(historical, GuardId, signedLegacyCount: 0).Rounds);
        Assert.Throws<InvalidOperationException>(() => MidnightVaultRoundStateCodec.Decode(historical, GuardId, signedLegacyCount: 1));
        historical["freeSpinWagerCents"] = 75L;
        Assert.Throws<InvalidOperationException>(() => MidnightVaultRoundStateCodec.Decode(historical, GuardId, signedLegacyCount: 1));
        historical[MidnightVaultRoundStateCodec.OwnerField] = "wrong-owner";
        Assert.Throws<InvalidOperationException>(() => MidnightVaultRoundStateCodec.Decode(historical, GuardId, signedLegacyCount: 0));
    }

    [MidnightImportFirestoreFact]
    public async Task FirestoreImportPreservesLegacyFifoAndFreeUnitsAreReadOnlyThroughTheirSignedCount()
    {
        const string account = "midnight-import-account";
        const string other = "midnight-import-other";
        const string game = MidnightVaultEngine.GameId;
        var db = new FirestoreDbBuilder
        {
            ProjectId = $"demo-midnight-import-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var guards = db.Collection(SlotSpinGuard.Collection);
        var guard = guards.Document(SlotSpinGuard.DocumentId(account, game));
        var wrongGuard = guards.Document(SlotSpinGuard.DocumentId(other, game));
        var balances = db.Collection("userBalances");
        var special = balances.Document($"{account}_specialPoints:{game}");
        var energy = balances.Document($"{account}_energy:{game}");
        var legacyEntitlement = balances.Document($"{account}_freeGames:{game}");
        var expected = new MidnightVaultBonusRound("historical-v2", 10, 4, 9, "rows-paw", V2);
        await Task.WhenAll(
            guard.SetAsync(new Dictionary<string, object>
            {
                ["midnightBonusRounds"] = new[] { Encode(expected) },
                ["consecutiveFiveMisses"] = 5L,
            }),
            special.SetAsync(new Dictionary<string, object> { ["available"] = 17L, ["userId"] = account }),
            energy.SetAsync(new Dictionary<string, object> { ["available"] = 74L }),
            legacyEntitlement.SetAsync(new Dictionary<string, object> { ["available"] = 77L }),
            balances.Document($"{other}_specialPoints:{game}").SetAsync(new Dictionary<string, object> { ["available"] = 99L }),
            balances.Document($"{account}_energy:other-game").SetAsync(new Dictionary<string, object> { ["available"] = 999L }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.RunTransactionAsync(async tx =>
            await MidnightVaultRoundStateCodec.ReadAsync(tx, db, await tx.GetSnapshotAsync(wrongGuard), account, 4, default)));

        async Task<MidnightVaultRoundBefore> ReadOrClaim(bool claim) => await db.RunTransactionAsync(async tx =>
        {
            var current = await tx.GetSnapshotAsync(guard);
            var state = await MidnightVaultRoundStateCodec.ReadAsync(tx, db, current, account, 4, default);
            if (claim) tx.Set(guard, MidnightVaultRoundStateCodec.Write(state), SetOptions.MergeAll);
            return state;
        });
        var claimed = await ReadOrClaim(claim: true);
        Assert.Equal(expected, Assert.Single(claimed.Rounds));
        Assert.Equal((4L, 17, 74L, 5), (claimed.LegacyRemaining, claimed.SpecialPoints, claimed.Energy, claimed.Pity));
        Assert.Equal(77L, (await legacyEntitlement.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(17L, (await special.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(74L, (await energy.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.True(claimed.Matches(MidnightVaultRoundStateCodec.Read(await guard.GetSnapshotAsync(), expectedLegacyCount: 4)));

        await special.SetAsync(new Dictionary<string, object> { ["available"] = 8L, ["userId"] = account });
        await energy.DeleteAsync();
        Assert.True(claimed.Matches(await ReadOrClaim(claim: false)));

        var undo = new Dictionary<string, object>();
        MidnightVaultRoundStateCodec.UndoImport(undo);
        await guard.SetAsync(undo, SetOptions.MergeAll);
        var reimported = await ReadOrClaim(claim: false);
        Assert.Equal(expected, Assert.Single(reimported.Rounds));
        Assert.Equal((8, 0L), (reimported.SpecialPoints, reimported.Energy));
    }

    private static Dictionary<string, object> Encode(MidnightVaultBonusRound round) => new()
    {
        ["id"] = round.Id, ["totalSpins"] = (long)round.TotalSpins, ["remainingSpins"] = (long)round.RemainingSpins,
        ["wagerPoints"] = round.WagerPoints, ["featureMode"] = round.FeatureMode ?? string.Empty, ["mathModelId"] = round.MathModelId,
    };

    private sealed class MidnightImportFirestoreFactAttribute : FactAttribute
    {
        public MidnightImportFirestoreFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
                Skip = "The Firestore emulator is required.";
        }
    }
}
