using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

[Collection(MidnightVaultPaidEmulatorCollection.Name)]
public sealed class NeonArcadePaidFirestoreTests(MidnightVaultPaidEmulatorFixture emulator)
{
    private const string GameId = SlotSpecialRoundProfiles.NeonNightsGameId;
    private const string UserId = "local-neon-player";

    [Fact]
    public async Task ReloadRecoversAwardedPlaysAndLegacyCarryRetriggersAtTheSavedWager()
    {
        var database = emulator.Database();
        var store = new FirestoreAccountStore(database);
        Assert.NotNull((await store.CreateAsync(UserId, UserId, UserId.ToUpperInvariant(),
            UserId + "@example.test", "unused-local-test", "active", default)).Value);
        var balances = database.Collection("userBalances");
        var wallet = balances.Document(UserId + "_slotsCredits");
        await wallet.UpdateAsync(new Dictionary<string, object>
        {
            ["available"] = 1_000L,
            ["availableFractionalCents"] = 0L
        });

        var time = DateTime.UtcNow.AddSeconds(-10);
        Task<SlotSpinAdmission> Admit(FirestoreAccountStore target, long wager, bool useFreeSpin) =>
            target.BeginSlotSpinAsync(UserId, GameId, wager, 1m, useFreeSpin, false, 0,
                time = time.AddSeconds(1), TimeSpan.Zero, default);

        var engine = ActivatorEngine();
        var paid = await Admit(store, 150, false);
        var trigger = engine.SpinAdmitted(GameId, paid, UserId);
        Assert.Equal(7, trigger.FreeSpinsAwarded);
        await store.RecordSlotSpinAsync(UserId, trigger, paid, DateTime.UtcNow, default);

        var reloaded = new FirestoreAccountStore(database);
        var state = await reloaded.GetSlotStateAsync(UserId, GameId, 1m, default);
        Assert.Equal(7, state.FreeSpinsRemaining);
        Assert.Equal(150L, state.FreeSpinWagerPoints);
        Assert.Null(state.FreeSpinFeatureMode);
        await Assert.ThrowsAsync<SlotFreeSpinsRequiredException>(() => Admit(reloaded, 50, false));

        // Legacy collections could lock an odd quarter-rand wager. A cent value
        // takes precedence over the obsolete points field when both exist.
        var bank = balances.Document(UserId + "_freeGames:" + GameId);
        await bank.UpdateAsync("available", 1L);
        var guardKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(GameId)));
        var guard = database.Collection("slotSpinGuards").Document(UserId + "_" + guardKey);
        await guard.UpdateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 75L,
            ["freeSpinWagerPoints"] = 3L,
            ["freeSpinFeatureMode"] = "sync"
        });
        state = await reloaded.GetSlotStateAsync(UserId, GameId, 1m, default);
        Assert.Equal(1, state.FreeSpinsRemaining);
        Assert.Equal(75L, state.FreeSpinWagerPoints);
        Assert.Equal("sync", state.FreeSpinFeatureMode);
        engine.ValidateRequest(GameId, state.FreeSpinWagerPoints!.Value, useFreeSpin: true);

        var free = await Admit(reloaded, 500, true);
        Assert.Equal(0L, free.ChargedWagerCents);
        Assert.Equal(75L, free.WagerPoints);
        var retrigger = ActivatorEngine().SpinAdmitted(GameId, free, UserId);
        Assert.Equal(7, retrigger.FreeSpinsAwarded);
        Assert.Empty(SlotRoundAudit.Check(retrigger, retrigger.Payout, isPaid: true));
        var settled = await reloaded.RecordSlotSpinAsync(UserId, retrigger, free, DateTime.UtcNow, default);
        Assert.Equal(7, settled.FreeSpinsRemaining);
        Assert.Equal(75L, settled.FreeSpinWagerPoints);
        Assert.Equal("sync", settled.FreeSpinFeatureMode);
        var wager = await database.Collection("balanceTransactions")
            .Document(retrigger.SpinId.ToString("N") + "-wager").GetSnapshotAsync();
        Assert.False(wager.Exists);
    }

    [Fact]
    public async Task DiscoAdmissionChargesEachPlayAndRejectsAnUnverifiedCascade()
    {
        const string playerId = "local-neon-disco-player";
        var database = emulator.Database();
        var store = new FirestoreAccountStore(database);
        Assert.NotNull((await store.CreateAsync(playerId, playerId, playerId.ToUpperInvariant(),
            playerId + "@example.test", "unused-local-test", "active", default)).Value);
        var wallet = database.Collection("userBalances").Document(playerId + "_slotsCredits");
        await wallet.UpdateAsync(new Dictionary<string, object>
        {
            ["available"] = 1_000L,
            ["availableFractionalCents"] = 0L
        });

        Assert.True(await store.GrantNeonAdminSpecialPlaysAsync(
            playerId, 150, 1m, arcadeRows: false, discoBall: true, cancellationToken: default));
        var state = await store.GetSlotStateAsync(playerId, GameId, 1m, default);
        Assert.Equal(NeonArcadeSlotDefinitions.DiscoPlaySpins, state.NeonDiscoSpinsRemaining);
        Assert.Equal(0, state.FreeSpinsRemaining);
        Assert.False(await store.GrantNeonAdminSpecialPlaysAsync(
            playerId, 150, 1m, arcadeRows: false, discoBall: false, cancellationToken: default));

        var admission = await store.BeginSlotSpinAsync(playerId, GameId, 150, 1m,
            useFreeSpin: false, useSpecialBoost: false, specialBoostCost: 0,
            startedAtUtc: DateTime.UtcNow.AddSeconds(-5), cooldown: TimeSpan.Zero,
            cancellationToken: default);
        Assert.True(admission.IsNeonDiscoPlay);
        Assert.Equal(150L, admission.ChargedWagerCents);
        var draws = new SeededDraws(73);
        var engine = new SpinService(NeonArcadeTests.Definitions(), new CryptoReelGenerator(draws),
            new CombinationEvaluator(), new PayoutCalculator(), draws);
        var result = engine.SpinAdmitted(GameId, admission, playerId);
        Assert.NotNull(result.NeonDiscoCascade);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordSlotSpinAsync(
            playerId, result with { NeonDiscoCascade = null },
            admission with { IsNeonDiscoPlay = false }, DateTime.UtcNow, default));
        var tampered = result with
        {
            Payout = result.Payout with { TotalPoints = checked(result.Payout.TotalPoints + 1) }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordSlotSpinAsync(
            playerId, tampered, admission, DateTime.UtcNow, default));
        state = await store.GetSlotStateAsync(playerId, GameId, 1m, default);
        Assert.Equal(NeonArcadeSlotDefinitions.DiscoPlaySpins, state.NeonDiscoSpinsRemaining);

        var settled = await store.RecordSlotSpinAsync(playerId, result, admission, DateTime.UtcNow, default);
        var discoTriggered = result.NeonArcade?.Activators.Any(activator => activator.SymbolId == "POWER") == true;
        var expectedRemaining = discoTriggered ? NeonArcadeSlotDefinitions.DiscoPlaySpins :
            NeonArcadeSlotDefinitions.DiscoPlaySpins - 1;
        Assert.Equal(expectedRemaining, settled.NeonDiscoSpinsRemaining);
        state = await store.GetSlotStateAsync(playerId, GameId, 1m, default);
        Assert.Equal(expectedRemaining, state.NeonDiscoSpinsRemaining);
        var wagerReceipt = await database.Collection("balanceTransactions")
            .Document(result.SpinId.ToString("N") + "-wager").GetSnapshotAsync();
        Assert.True(wagerReceipt.Exists);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordSlotSpinAsync(
            playerId, tampered, admission, DateTime.UtcNow, default));
    }

    private static SpinService ActivatorEngine()
    {
        var provider = NeonArcadeTests.Definitions();
        var game = provider.GetGame(GameId)!;
        var strips = provider.GetReelSet(game.Math.ReelSetId)!.Reels;
        var stops = strips.Select((strip, reel) => strip.IndexOf(reel == 4 ? "6" : "2")).ToArray();
        var draws = new ReelStops(stops);
        return new SpinService(provider, new CryptoReelGenerator(draws),
            new CombinationEvaluator(), new PayoutCalculator(), draws);
    }

    private sealed class ReelStops(int[] stops) : IRandomIndexSource
    {
        private int cursor;

        public int Next(int maximumExclusive) => cursor < stops.Length ? stops[cursor++] : 0;
    }

    private sealed class ZeroDraws : IRandomIndexSource
    {
        public int Next(int maximumExclusive) => 0;
    }

    private sealed class SeededDraws(int seed) : IRandomIndexSource
    {
        private readonly Random random = new(seed);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
