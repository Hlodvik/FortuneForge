using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Bonuses;
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

    private static SpinService ActivatorEngine()
    {
        var provider = NeonArcadeTests.Definitions();
        var game = provider.GetGame(GameId)!;
        var strips = provider.GetReelSet(game.Math.ReelSetId)!.Reels;
        var stops = strips.Select((strip, reel) => strip.IndexOf(reel == 4 ? "6" : "2")).ToArray();
        return new SpinService(provider, new CryptoReelGenerator(new ReelStops(stops)),
            new CombinationEvaluator(), new PayoutCalculator(), new ZeroDraws());
    }

    private sealed class ReelStops(int[] stops) : IRandomIndexSource
    {
        private int cursor;

        public int Next(int maximumExclusive) => stops[cursor++];
    }

    private sealed class ZeroDraws : IRandomIndexSource
    {
        public int Next(int maximumExclusive) => 0;
    }
}
