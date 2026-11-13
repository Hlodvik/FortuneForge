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
        Assert.Equal(NeonArcadeBonus.PaidCarFeatureMode, state.FreeSpinFeatureMode);
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
    public async Task PendingNitroChoiceSurvivesReloadAndSettlesOnceAtTheLockedWager()
    {
        const string playerId = "local-neon-pending-nitro-player";
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
            playerId, 150, 1m, arcadeRows: true, discoBall: false, cancellationToken: default));

        var strips = NeonArcadeSlotDefinitions.CreateCarReelSet().Reels;
        var stops = strips.Select((strip, reel) => Enumerable.Range(0, strip.Count).First(stop =>
            Enumerable.Range(0, 7).Any(row => strip[(stop + row) % strip.Count] == "NITRO") ==
            (reel >= 3))).ToArray();
        var draws = new ReelStops(stops);
        var engine = new SpinService(NeonArcadeTests.Definitions(), new CryptoReelGenerator(draws),
            new CombinationEvaluator(), new PayoutCalculator(), draws);
        var admission = await store.BeginSlotSpinAsync(playerId, GameId, 50, 1m,
            useFreeSpin: true, useSpecialBoost: false, specialBoostCost: 0,
            startedAtUtc: DateTime.UtcNow.AddSeconds(-5), cooldown: TimeSpan.Zero,
            cancellationToken: default,
            resolveOutcome: (accepted, _) => Task.FromResult(engine.SpinAdmitted(GameId, accepted, playerId)));
        var offer = Assert.IsType<NeonNitroChoiceOffer>(admission.PendingNeonNitroChoice);
        Assert.Equal("spin", offer.SourceKind);
        Assert.Equal([0, 1, 2, 3, 4], offer.EligibleReels);
        Assert.Equal(150, admission.WagerPoints);
        Assert.Equal(150, admission.ChargedWagerCents);
        Assert.True(admission.ResolvedOutcome?.NeonNitroWild?.RequiresChoice);
        var pendingWallet = await wallet.GetSnapshotAsync();
        Assert.Equal(1_000L, pendingWallet.GetValue<long>("available"));
        var reloaded = new FirestoreAccountStore(database);
        var state = await reloaded.GetSlotStateAsync(playerId, GameId, 1m, default);
        Assert.Equal(offer.SpinId, state.PendingNeonNitroChoice?.SpinId);
        Assert.Equal(6, state.FreeSpinsRemaining);
        await Assert.ThrowsAsync<NeonNitroChoiceConflictException>(() => reloaded.BeginSlotSpinAsync(
            playerId, GameId, 150, 1m, true, false, 0, DateTime.UtcNow,
            TimeSpan.Zero, default));
        Assert.False(await reloaded.GrantNeonAdminSpecialPlaysAsync(
            playerId, 150, 1m, arcadeRows: true, discoBall: false, cancellationToken: default));

        var chosen = await reloaded.FinalizeNeonNitroChoiceAsync(playerId, offer.SpinId, 2, default);
        Assert.False(chosen.Result.NeonNitroWild?.RequiresChoice);
        Assert.Equal(2, chosen.Result.NeonNitroWild?.ExpandedReel);
        Assert.All(chosen.Result.Reels[2], symbol => Assert.Equal("ACE", symbol));
        Assert.Equal(chosen.Result.Payout.TotalPoints, chosen.Settlement.Payout.TotalPoints);
        Assert.Null((await reloaded.GetSlotStateAsync(playerId, GameId, 1m, default)).PendingNeonNitroChoice);
        var repeated = await reloaded.FinalizeNeonNitroChoiceAsync(playerId, offer.SpinId, 2, default);
        Assert.Equal(chosen.Result.Payout.TotalPoints, repeated.Result.Payout.TotalPoints);
        Assert.Equal(chosen.Settlement.SlotsCreditsBalance, repeated.Settlement.SlotsCreditsBalance);
        await Assert.ThrowsAsync<NeonNitroChoiceConflictException>(() =>
            reloaded.FinalizeNeonNitroChoiceAsync(playerId, offer.SpinId, 1, default));
    }

    [Fact]
    public async Task EachLosingPaidCarSpinOffersOneRespinAtTheLockedWager()
    {
        const string playerId = "local-neon-car-respin-player";
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
            playerId, 150, 1m, arcadeRows: true, discoBall: false, cancellationToken: default));
        var draws = new SeededDraws(73);
        var engine = new SpinService(NeonArcadeTests.Definitions(), new CryptoReelGenerator(draws),
            new CombinationEvaluator(), new PayoutCalculator(), draws);
        var started = DateTime.UtcNow.AddSeconds(-20);
        async Task<SlotSpinAdmission> Admit() => await store.BeginSlotSpinAsync(playerId, GameId, 50, 1m,
            useFreeSpin: true, useSpecialBoost: false, specialBoostCost: 0,
            startedAtUtc: started = started.AddSeconds(1), cooldown: TimeSpan.Zero,
            cancellationToken: default);
        Task<SlotSpinSettlement> Settle(SlotSpinAdmission admission, bool winning = false)
        {
            var spin = Enumerable.Range(0, 100)
                .Select(_ => engine.SpinAdmitted(GameId, admission, playerId))
                .First(result => (result.Payout.TotalPoints > 0) == winning &&
                    result.NeonNitroWild is null && result.FreeSpinsAwarded == 0);
            return store.RecordSlotSpinAsync(playerId, spin, admission, DateTime.UtcNow, default);
        }

        var firstAdmission = await Admit();
        Assert.Equal(150, firstAdmission.WagerPoints);
        Assert.Equal(150, firstAdmission.ChargedWagerCents);
        var first = await Settle(firstAdmission);
        Assert.NotNull(first.NeonCarRespinOffer);
        var staleSpinId = first.NeonCarRespinOffer.SpinId;

        var pending = await Admit();
        await Assert.ThrowsAsync<NeonCarRespinConflictException>(() => store.RespinNeonCarReelAsync(
            playerId, staleSpinId, first.NeonCarRespinOffer.EligibleReels[0], default));
        var latest = await Settle(pending);
        var offer = latest.NeonCarRespinOffer;
        Assert.NotNull(offer);
        var source = await database.Collection("slotSpinResults")
            .Document(offer.SpinId.ToString("N")).GetSnapshotAsync();
        Assert.True(Encoding.UTF8.GetByteCount(source.GetValue<string>("neonCarRespinSourceJson")) < 500_000);
        var beforeState = await store.GetSlotStateAsync(playerId, GameId, 1m, default);
        Assert.Equal(offer.SpinId, beforeState.NeonCarRespinOffer?.SpinId);
        Assert.Equal(offer.EligibleReels, beforeState.NeonCarRespinOffer?.EligibleReels);
        var freeBefore = beforeState.FreeSpinsRemaining;
        var balanceBefore = await wallet.GetSnapshotAsync();
        var beforeCents = balanceBefore.GetValue<long>("available") * 100 +
            balanceBefore.GetValue<long>("availableFractionalCents");

        var selected = offer.EligibleReels[0];
        var result = await store.RespinNeonCarReelAsync(playerId, offer.SpinId, selected, default);
        Assert.False(result.AlreadyApplied);
        Assert.Equal(offer.SpinId, result.SourceSpinId);
        Assert.Equal(selected, result.ReelIndex);
        Assert.Equal(Math.Max(0, result.Payout.TotalPoints - result.OriginalPayoutPoints), result.ImprovementPoints);
        Assert.Equal((beforeCents + result.ImprovementPoints * 1m) / 100m, result.SlotsCreditsBalance);
        var state = await store.GetSlotStateAsync(playerId, GameId, 1m, default);
        Assert.Null(state.NeonCarRespinOffer);
        Assert.Equal(freeBefore, state.FreeSpinsRemaining);
        Assert.Equal(150, state.FreeSpinWagerPoints);

        var nextAdmission = await Admit();
        var nextLoss = await Settle(nextAdmission);
        var nextOffer = Assert.IsType<NeonCarRespinOffer>(nextLoss.NeonCarRespinOffer);
        Assert.NotEqual(offer.SpinId, nextOffer.SpinId);
        var laterBalance = await wallet.GetSnapshotAsync();
        var repeated = await store.RespinNeonCarReelAsync(playerId, offer.SpinId, selected, default);
        Assert.True(repeated.AlreadyApplied);
        Assert.Equal(result.Payout.TotalPoints, repeated.Payout.TotalPoints);
        Assert.Equal(laterBalance.GetValue<long>("available") +
            laterBalance.GetValue<long>("availableFractionalCents") / 100m,
            repeated.SlotsCreditsBalance);
        await Assert.ThrowsAsync<NeonCarRespinConflictException>(() => store.RespinNeonCarReelAsync(
            playerId, offer.SpinId, (selected + 1) % 5, default));
        await Assert.ThrowsAsync<NeonCarRespinConflictException>(() => store.RespinNeonCarReelAsync(
            playerId, staleSpinId, first.NeonCarRespinOffer.EligibleReels[0], default));
        var receipts = database.Collection("neonCarRespinReceipts");
        var receipt = await receipts.Document(offer.SpinId.ToString("N")).GetSnapshotAsync();
        Assert.True(receipt.Exists);
        Assert.Equal(result.ImprovementPoints, receipt.GetValue<long>("improvementPoints"));

        var nextRespin = await store.RespinNeonCarReelAsync(playerId, nextOffer.SpinId, nextOffer.EligibleReels[0], default);
        Assert.False(nextRespin.AlreadyApplied);
        Assert.Equal(nextOffer.SpinId, nextRespin.SourceSpinId);
        Assert.True((await receipts.Document(nextOffer.SpinId.ToString("N")).GetSnapshotAsync()).Exists);
        Assert.Null((await store.GetSlotStateAsync(playerId, GameId, 1m, default)).NeonCarRespinOffer);

        var win = await Settle(await Admit(), winning: true);
        Assert.True(win.Payout.TotalPoints > 0);
        Assert.Null(win.NeonCarRespinOffer);

        await Settle(await Admit(), winning: true);
        await Settle(await Admit(), winning: true);
        var finalLoss = await Settle(await Admit());
        Assert.Equal(0, finalLoss.FreeSpinsRemaining);
        var finalOffer = Assert.IsType<NeonCarRespinOffer>(finalLoss.NeonCarRespinOffer);
        var finalRespin = await store.RespinNeonCarReelAsync(playerId, finalOffer.SpinId, finalOffer.EligibleReels[0], default);
        Assert.False(finalRespin.AlreadyApplied);
        Assert.Null((await store.GetSlotStateAsync(playerId, GameId, 1m, default)).NeonCarRespinOffer);
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
