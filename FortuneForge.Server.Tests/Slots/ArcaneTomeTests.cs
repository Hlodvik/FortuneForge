// Edited: 2026-11-18T18:02:58-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:55:38-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:46:41-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:32:58-06:00 | Computer: Vinnabok
using System.Text.Json;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class ArcaneTomeTests
{
    [Theory]
    [InlineData("herbalism", "MANDRAKE_ROOT", "NIGHTSHADE_BERRIES", "QUICKSILVER")]
    [InlineData("moon-and-flame", "QUICKSILVER", "PHOENIX_FEATHER", "MOONCAP_MUSHROOM")]
    [InlineData("distillation", "MOONCAP_MUSHROOM", "WOLFSBANE_WINE", "QUICKSILVER")]
    public void AwakeningRequiresAGrimoireAndDoublesOnlyTheSelectedPairForFivePaidSpins(
        string page, string first, string second, string ordinary)
    {
        var selected = ArcaneTome.SelectPage(ArcaneTome.Empty, page, activate: false);
        Assert.Throws<ArcaneTomeConflictException>(() => ArcaneTome.SelectPage(selected, page, activate: true));
        var earned = ArcaneTome.CompleteSpin(selected, [[first, ArcaneTome.GrimoireSymbolId]], false);
        Assert.True(earned.State.GrimoireReady);
        Assert.Null(earned.State.ActivePage);
        Assert.Equal(1, earned.IngredientsAwarded[first]);
        Assert.Throws<ArcaneTomeActivationRequiredException>(() => ArcaneTome.EnsureSpinAllowed(earned.State));

        var active = ArcaneTome.SelectPage(earned.State, page, activate: true);
        Assert.Equal(5, active.BoostedSpinsRemaining);
        Assert.False(active.GrimoireReady);
        ArcaneTome.EnsureSpinAllowed(active);
        Assert.Same(active, ArcaneTome.SelectPage(active, page, activate: true));
        var otherPage = page == "herbalism" ? "distillation" : "herbalism";
        Assert.Throws<ArcaneTomeConflictException>(() => ArcaneTome.SelectPage(active, otherPage, activate: false));
        for (var remaining = 4; remaining >= 0; remaining--)
        {
            var progress = ArcaneTome.CompleteSpin(active, [[first, second, ordinary]], false);
            ArcaneTome.ValidateState(progress.State);
            Assert.Equal(2, progress.IngredientsAwarded[first]);
            Assert.Equal(2, progress.IngredientsAwarded[second]);
            Assert.Equal(1, progress.IngredientsAwarded[ordinary]);
            Assert.Equal(remaining, progress.State.BoostedSpinsRemaining);
            Assert.Equal(remaining == 0 ? null : page, progress.State.ActivePage);
            active = progress.State;
        }

        Assert.Equal(11, active.Ingredients[first]);
        Assert.Equal(10, active.Ingredients[second]);
        Assert.Equal(5, active.Ingredients[ordinary]);
        var unboosted = ArcaneTome.CompleteSpin(active, [[first, first]], false);
        Assert.Equal(2, unboosted.IngredientsAwarded[first]);
        Assert.Equal(13, unboosted.State.Ingredients[first]);
    }

    [Fact]
    public void SavedLegacyBoostKeepsItsRecipeUntilItExpires()
    {
        const string page = "moon-and-flame";
        var saved = ArcaneTome.Empty with { SelectedPage = page, ActivePage = page, BoostedSpinsRemaining = 1 };
        var json = JsonSerializer.Serialize(saved);
        Assert.DoesNotContain("RecipeVersion", json);
        var reloaded = JsonSerializer.Deserialize<ArcaneTomeState>(json)!;
        Assert.True(ArcaneTome.SameState(saved, reloaded));
        Assert.Equal("Moon & Flame", ArcaneTome.ToView(reloaded)!.Pages.Single(item => item.Id == page).Label);

        IReadOnlyList<IReadOnlyList<string>> reels = [["MOONCAP_MUSHROOM", "PHOENIX_FEATHER", "QUICKSILVER"]];
        var finished = ArcaneTome.CompleteSpin(reloaded, reels, false);
        Assert.Equal(2, finished.IngredientsAwarded["MOONCAP_MUSHROOM"]);
        Assert.Equal(1, finished.IngredientsAwarded["QUICKSILVER"]);
        Assert.Equal("Transmutation", ArcaneTome.ToView(finished.State)!.Pages.Single(item => item.Id == page).Label);

        var nextBoost = ArcaneTome.SelectPage(finished.State with { GrimoireReady = true }, page, true);
        var restored = JsonSerializer.Deserialize<ArcaneTomeState>(JsonSerializer.Serialize(nextBoost))!;
        Assert.True(ArcaneTome.SameState(nextBoost, restored));
        var updated = ArcaneTome.CompleteSpin(restored, reels, false);
        Assert.Equal(1, updated.IngredientsAwarded["MOONCAP_MUSHROOM"]);
        Assert.Equal(2, updated.IngredientsAwarded["QUICKSILVER"]);
    }

    [Fact]
    public void ZeroIngredientsCanLandOnConsecutiveSpinsAcrossTomeActivation()
    {
        var dry = ArcaneTome.CompleteSpin(ArcaneTome.Empty, [["2", ArcaneTome.GrimoireSymbolId]], false);
        Assert.Empty(dry.IngredientsAwarded);
        Assert.Equal(ArcaneTome.Empty.Ingredients, dry.State.Ingredients);

        var activated = ArcaneTome.SelectPage(dry.State, "herbalism", activate: true);
        var boostedDry = ArcaneTome.CompleteSpin(activated, [["2"]], false);
        Assert.Empty(boostedDry.IngredientsAwarded);
        Assert.Equal(activated.Ingredients, boostedDry.State.Ingredients);
        Assert.Equal(ArcaneTome.BoostedSpinCount - 1, boostedDry.State.BoostedSpinsRemaining);
        Assert.Throws<InvalidOperationException>(() =>
            ArcaneTome.CompleteSpin(activated, [["2"]], false, requireIngredient: true));
    }

    [Fact]
    public void BoostedReelModelRemovesAllGrimoiresAndKeepsTheIngredientPositions()
    {
        var regular = ArcaneArchivesEngine.CreateReelSet();
        var boosted = ArcaneArchivesEngine.CreateTomeReelSet();
        Assert.Equal(ArcaneArchivesEngine.TomeReelSetId, boosted.Id);
        Assert.Equal(regular.SymbolSetId, boosted.SymbolSetId);
        Assert.Contains(ArcaneTome.GrimoireSymbolId, regular.Reels.SelectMany(reel => reel));
        for (var reel = 0; reel < regular.Reels.Count; reel++)
        {
            var ordinaryStrip = regular.Reels[reel];
            var boostedStrip = boosted.Reels[reel];
            Assert.Equal(ordinaryStrip.Count, boostedStrip.Count);
            Assert.DoesNotContain(ArcaneTome.GrimoireSymbolId, boostedStrip);
            for (var stop = 0; stop < ordinaryStrip.Count; stop++)
            {
                if (ordinaryStrip[stop] != ArcaneTome.GrimoireSymbolId)
                    Assert.Equal(ordinaryStrip[stop], boostedStrip[stop]);
            }
        }
    }
}

[Collection(MidnightVaultPaidEmulatorCollection.Name)]
public sealed class ArcaneTomePaidFirestoreTests(MidnightVaultPaidEmulatorFixture emulator)
{
    [Fact]
    public async Task PaidTomeKeepsGrimoireOffAllFiveBoostedSpinsAndRestoresItAfterward()
    {
        const string userId = "local-arcane-tome-test";
        const string gameId = ArcaneArchivesEngine.GameId;
        var database = emulator.Database();
        var store = new FirestoreAccountStore(database);
        Assert.NotNull((await store.CreateAsync(userId, userId, userId.ToUpperInvariant(),
            userId + "@example.test", "local-test-password", "active", default)).Value);
        await database.Collection("userBalances").Document(userId + "_slotsCredits")
            .UpdateAsync("available", 1_000L);
        var now = DateTime.UtcNow.AddSeconds(-10);

        var ordinary = ArcaneArchivesEngine.CreateReelSet();
        var grimoireStops = ordinary.Reels.Select(strip => Math.Max(0, strip.IndexOf(ArcaneTome.GrimoireSymbolId)));
        var admission = await store.BeginSlotSpinAsync(userId, gameId, 12, 25, false, false,
            0, now, TimeSpan.Zero, default);
        Assert.NotNull(admission.ArcaneTomeBefore);
        await Assert.ThrowsAsync<ArcaneTomeConflictException>(() => store.SelectArcaneTomePageAsync(
            userId, "distillation", false, DateTime.UtcNow, default));
        var landed = Engine(grimoireStops).SpinAdmitted(gameId, admission, userId);
        var ordinarySettlement = await store.RecordSlotSpinAsync(userId, landed, admission, DateTime.UtcNow, default);
        Assert.True(ordinarySettlement.ArcaneTome?.GrimoireReady);
        Assert.NotEmpty(ordinarySettlement.ArcaneIngredientsAwarded!);
        await Assert.ThrowsAsync<ArcaneTomeActivationRequiredException>(() => store.BeginSlotSpinAsync(
            userId, gameId, 12, 25, false, false, 0, now.AddSeconds(1), TimeSpan.Zero, default));

        var reloadedStore = new FirestoreAccountStore(database);
        var state = await reloadedStore.GetSlotStateAsync(userId, gameId, 25, default);
        Assert.True(ArcaneTome.SameState(ordinarySettlement.ArcaneTome!, state.ArcaneTome!));
        var activated = await reloadedStore.SelectArcaneTomePageAsync(
            userId, "herbalism", true, DateTime.UtcNow, default);
        Assert.Equal(5, activated.BoostedSpinsRemaining);
        await Assert.ThrowsAsync<ArcaneTomeConflictException>(() => reloadedStore.SelectArcaneTomePageAsync(
            userId, "distillation", false, DateTime.UtcNow, default));

        for (var spinNumber = 1; spinNumber <= ArcaneTome.BoostedSpinCount; spinNumber++)
        {
            var shift = spinNumber - 1;
            var reelSetId = shift == 0 ? ArcaneArchivesEngine.TomeReelSetId :
                $"{ArcaneArchivesEngine.TomeReelSetId}-shift-{shift}";
            var boosted = ArcaneArchivesEngine.FindReelSet(reelSetId);
            Assert.NotNull(boosted);
            var ingredientStops = boosted.Reels.Select(strip => Math.Max(0, strip.IndexOf("MANDRAKE_ROOT")));
            var boostedAdmission = await reloadedStore.BeginSlotSpinAsync(userId, gameId, 12, 25, false, false,
                0, now.AddSeconds(spinNumber), TimeSpan.Zero, default);
            var spun = Engine(ingredientStops, shift).SpinAdmitted(gameId, boostedAdmission, userId);
            Assert.Equal(reelSetId, spun.ReelSetId);
            Assert.DoesNotContain(ArcaneTome.GrimoireSymbolId, spun.Reels.SelectMany(reel => reel));
            Assert.Empty(SlotRoundAudit.Check(spun, spun.Payout, isPaid: true));

            var settled = await reloadedStore.RecordSlotSpinAsync(userId, spun, boostedAdmission, DateTime.UtcNow, default);
            Assert.Equal(ArcaneTome.BoostedSpinCount - spinNumber, settled.ArcaneTome?.BoostedSpinsRemaining);
            var mandrakeLandings = spun.Reels.SelectMany(reel => reel).Count(id => id == "MANDRAKE_ROOT");
            Assert.True(mandrakeLandings >= 1);
            Assert.Equal(mandrakeLandings * 2L, settled.ArcaneIngredientsAwarded!["MANDRAKE_ROOT"]);
            var retried = await reloadedStore.RecordSlotSpinAsync(userId, spun, boostedAdmission, DateTime.UtcNow, default);
            Assert.True(ArcaneTome.SameState(settled.ArcaneTome!, retried.ArcaneTome!));
            var refreshed = await reloadedStore.GetSlotStateAsync(userId, gameId, 25, default);
            Assert.True(ArcaneTome.SameState(settled.ArcaneTome!, refreshed.ArcaneTome!));
        }
        var regularAdmission = await reloadedStore.BeginSlotSpinAsync(userId, gameId, 12, 25, false, false,
            0, now.AddSeconds(ArcaneTome.BoostedSpinCount + 1), TimeSpan.Zero, default);
        var regular = Engine(grimoireStops).SpinAdmitted(gameId, regularAdmission, userId);
        Assert.Equal(ArcaneArchivesEngine.ReelSetId, regular.ReelSetId);
        Assert.Contains(ArcaneTome.GrimoireSymbolId, regular.Reels.SelectMany(reel => reel));
        var regularSettlement = await reloadedStore.RecordSlotSpinAsync(
            userId, regular, regularAdmission, DateTime.UtcNow, default);
        Assert.True(regularSettlement.ArcaneTome?.GrimoireReady);
    }

    [Fact]
    public async Task PaidZeroIngredientSpinsSettleInBothModesAndAcrossReload()
    {
        const string userId = "local-arcane-zero-ingredient-test";
        const string gameId = ArcaneArchivesEngine.GameId;
        var database = emulator.Database();
        var store = new FirestoreAccountStore(database);
        Assert.NotNull((await store.CreateAsync(userId, userId, userId.ToUpperInvariant(),
            userId + "@example.test", "local-test-password", "active", default)).Value);
        await database.Collection("userBalances").Document(userId + "_slotsCredits")
            .UpdateAsync("available", 1_000L);
        var now = DateTime.UtcNow.AddSeconds(-10);
        var rows = ArcaneArchivesEngine.CreateGame().Layout.VisibleRows;
        var ordinary = ArcaneArchivesEngine.CreateReelSet();
        var dryStops = ordinary.Reels.Select((strip, reel) => Enumerable.Range(0, strip.Count).First(stop =>
        {
            var window = Enumerable.Range(0, rows).Select(row => strip[(stop + row) % strip.Count]).ToArray();
            return !window.Any(ArcaneTome.IngredientIds.Contains) &&
                (reel != ordinary.Reels.Count - 1 || window.Contains(ArcaneTome.GrimoireSymbolId));
        })).ToArray();
        var admission = await store.BeginSlotSpinAsync(userId, gameId, 12, 25, false, false,
            0, now, TimeSpan.Zero, default);
        var dry = Engine(dryStops).SpinAdmitted(gameId, admission, userId);
        Assert.Equal(ArcaneArchivesEngine.ReelSetId, dry.ReelSetId);
        Assert.DoesNotContain(dry.Reels.SelectMany(reel => reel), ArcaneTome.IngredientIds.Contains);
        var settledDry = await store.RecordSlotSpinAsync(userId, dry, admission, DateTime.UtcNow, default);
        Assert.Empty(settledDry.ArcaneIngredientsAwarded!);
        Assert.True(settledDry.ArcaneTome!.GrimoireReady);

        var reloaded = new FirestoreAccountStore(database);
        var state = await reloaded.GetSlotStateAsync(userId, gameId, 25, default);
        Assert.True(ArcaneTome.SameState(settledDry.ArcaneTome, state.ArcaneTome!));
        const string page = "moon-and-flame";
        var active = await reloaded.SelectArcaneTomePageAsync(userId, page, true, DateTime.UtcNow, default);
        var next = await reloaded.BeginSlotSpinAsync(userId, gameId, 12, 25, false, false,
            0, now.AddSeconds(1), TimeSpan.Zero, default);
        var boostedDry = Engine(dryStops).SpinAdmitted(gameId, next, userId);
        Assert.Equal(ArcaneArchivesEngine.TomeReelSetId, boostedDry.ReelSetId);
        Assert.DoesNotContain(ArcaneTome.GrimoireSymbolId, boostedDry.Reels.SelectMany(reel => reel));
        Assert.DoesNotContain(boostedDry.Reels.SelectMany(reel => reel), ArcaneTome.IngredientIds.Contains);
        var settled = await reloaded.RecordSlotSpinAsync(userId, boostedDry, next, DateTime.UtcNow, default);
        Assert.Empty(settled.ArcaneIngredientsAwarded!);
        Assert.Equal(active.Ingredients, settled.ArcaneTome!.Ingredients);
        Assert.Equal(4, settled.ArcaneTome.BoostedSpinsRemaining);

        var retry = await reloaded.RecordSlotSpinAsync(userId, boostedDry, next, DateTime.UtcNow, default);
        Assert.True(ArcaneTome.SameState(settled.ArcaneTome, retry.ArcaneTome!));
        var refreshed = await reloaded.GetSlotStateAsync(userId, gameId, 25, default);
        Assert.True(ArcaneTome.SameState(settled.ArcaneTome, refreshed.ArcaneTome!));
    }

    [Fact]
    public async Task PaidPotionsPreserveLegacyTotalsAndSettleExactlyOncePerSpin()
    {
        const string userId = "local-arcane-potions-test";
        const string gameId = ArcaneArchivesEngine.GameId;
        const string ingredient = "MANDRAKE_ROOT";
        var database = emulator.Database();
        var store = new FirestoreAccountStore(database);
        Assert.NotNull((await store.CreateAsync(userId, userId, userId.ToUpperInvariant(),
            userId + "@example.test", "local-test-password", "active", default)).Value);
        var wallet = database.Collection("userBalances").Document(userId + "_slotsCredits");
        await wallet.UpdateAsync("available", 10_000L);

        var model = ArcaneArchivesEngine.CreateReelSet();
        var rows = ArcaneArchivesEngine.CreateGame().Layout.VisibleRows;
        int[] StopsFor(bool brew) => model.Reels.Select((strip, reel) =>
            Enumerable.Range(0, strip.Count).First(stop =>
            {
                var window = Enumerable.Range(0, rows).Select(row => strip[(stop + row) % strip.Count]);
                var ingredients = window.Where(ArcaneTome.IngredientIds.Contains);
                return brew && reel == 0 ? ingredients.SequenceEqual([ingredient]) : !ingredients.Any();
            })).ToArray();
        var brewingStops = StopsFor(brew: true);
        var dryStops = StopsFor(brew: false);
        var now = DateTime.UtcNow.AddSeconds(-20);
        var bootstrap = await store.BeginSlotSpinAsync(userId, gameId, 12, 25, false, false,
            0, now, TimeSpan.Zero, default);
        await store.RecordSlotSpinAsync(userId, Engine(dryStops).SpinAdmitted(gameId, bootstrap, userId),
            bootstrap, DateTime.UtcNow, default);
        var guard = Assert.Single((await database.Collection("slotSpinGuards")
            .WhereEqualTo("userId", userId).GetSnapshotAsync()).Documents);
        var legacyTome = ArcaneTome.Empty with
        {
            Ingredients = ArcaneTome.IngredientIds.ToDictionary(id => id, _ => 11L)
        };
        await guard.Reference.UpdateAsync(new Dictionary<string, object>
        {
            ["arcaneTomeJson"] = JsonSerializer.Serialize(legacyTome),
            ["arcanePotionsJson"] = FieldValue.Delete
        });
        var refreshed = await store.GetSlotStateAsync(userId, gameId, 25, default);
        Assert.Equal(11, refreshed.ArcaneTome!.Ingredients[ingredient]);
        Assert.Empty(refreshed.ArcanePotions!.Pot);
        Assert.Empty(refreshed.ArcanePotions.Bottles);
        Assert.Null(refreshed.ArcanePotions.Preview);

        long[] ingredientWagers = [6, 8, 10, 14, 20];
        for (var number = 1; number <= ArcanePotions.Majority; number++)
        {
            var admission = await store.BeginSlotSpinAsync(userId, gameId, ingredientWagers[number - 1], 25, false, false,
                0, now.AddSeconds(number), TimeSpan.Zero, default);
            var spin = Engine(brewingStops).SpinAdmitted(gameId, admission, userId);
            var settlement = await store.RecordSlotSpinAsync(userId, spin, admission, DateTime.UtcNow, default);
            Assert.Equal(11 + number, settlement.ArcaneTome!.Ingredients[ingredient]);
            Assert.Equal(number % ArcanePotions.Majority, settlement.ArcanePotions!.Pot.Count);
            var retry = await store.RecordSlotSpinAsync(userId, spin, admission, DateTime.UtcNow, default);
            Assert.True(ArcanePotions.SameState(settlement.ArcanePotions, retry.ArcanePotions!));
        }

        store = new FirestoreAccountStore(database);
        var brewed = (await store.GetSlotStateAsync(userId, gameId, 25, default)).ArcanePotions!;
        var bottle = Assert.Single(brewed.Bottles);
        Assert.Equal(ingredient, bottle.IngredientId);
        Assert.Equal(10, bottle.LockedWagerPoints);
        Assert.Contains("Scribe Mark payline value", bottle.Effect);
        Assert.Empty(brewed.Pot);
        var activated = await store.ActivateArcanePotionAsync(userId, bottle.Id, DateTime.UtcNow, default);
        Assert.Empty(activated.Bottles);
        Assert.Equal(ArcanePotions.Duration, activated.Active!.SpinsRemaining);
        const int lockedWagerPoints = 10;

        for (var number = 1; number <= ArcanePotions.Duration + 1; number++)
        {
            var paidPotionSpin = number <= ArcanePotions.Duration;
            var startedAt = now.AddSeconds(ArcanePotions.Majority + number);
            if (paidPotionSpin)
            {
                var walletBefore = await wallet.GetSnapshotAsync();
                var conflict = await Assert.ThrowsAsync<ArcanePotionWagerLockedException>(() =>
                    store.BeginSlotSpinAsync(userId, gameId, 16, 25, false, false, 0, startedAt, TimeSpan.Zero, default));
                Assert.Equal(lockedWagerPoints, conflict.LockedWagerPoints);
                Assert.Equal(walletBefore.UpdateTime, (await wallet.GetSnapshotAsync()).UpdateTime);
                var unchanged = await store.GetSlotStateAsync(userId, gameId, 25, default);
                Assert.Equal(ArcanePotions.Duration - number + 1, unchanged.ArcanePotions!.Active!.SpinsRemaining);
            }

            var wager = paidPotionSpin ? lockedWagerPoints : 16;
            var admission = await store.BeginSlotSpinAsync(userId, gameId, wager, 25, false, false,
                0, startedAt, TimeSpan.Zero, default);
            Assert.Equal(wager * 25, admission.ChargedWagerCents);
            await Assert.ThrowsAsync<ArcanePotionConflictException>(() =>
                store.ActivateArcanePotionAsync(userId, bottle.Id, DateTime.UtcNow, default));
            var spin = Engine(dryStops).SpinAdmitted(gameId, admission, userId);
            Assert.Equal(paidPotionSpin ? bottle.Id : (long?)null, spin.EngineAudit!.ArcanePotionApplied?.Id);
            Assert.Empty(SlotRoundAudit.Check(spin, spin.Payout, isPaid: true));

            var settlement = await store.RecordSlotSpinAsync(userId, spin, admission, DateTime.UtcNow, default);
            var expectedRemaining = Math.Max(0, ArcanePotions.Duration - number);
            Assert.Equal(expectedRemaining, settlement.ArcanePotions!.Active?.SpinsRemaining ?? 0);
            var retry = await store.RecordSlotSpinAsync(userId, spin, admission, DateTime.UtcNow, default);
            Assert.True(ArcanePotions.SameState(settlement.ArcanePotions, retry.ArcanePotions!));
            var saved = await store.GetSlotStateAsync(userId, gameId, 25, default);
            Assert.Equal(expectedRemaining, saved.ArcanePotions!.Active?.SpinsRemaining ?? 0);
        }

        await Assert.ThrowsAsync<ArcanePotionConflictException>(() =>
            store.ActivateArcanePotionAsync(userId, bottle.Id, DateTime.UtcNow, default));
    }

    private static SpinService Engine(IEnumerable<int> indexes, int shift = 0)
    {
        var random = new ScriptedRandom(indexes.Prepend(shift));
        return new(new OptionsSlotsDefinitionProvider(), new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random);
    }

    private sealed class ScriptedRandom(IEnumerable<int> indexes) : IRandomIndexSource
    {
        private readonly Queue<int> values = new(indexes);
        public int Next(int maxExclusive) => values.Count > 0 ? values.Dequeue() % maxExclusive : 0;
    }
}
