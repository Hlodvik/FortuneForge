using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class GoldFoundryCrucibleTests
{
    internal const string GameId = GoldFoundrySlotDefinitions.GameId;

    [Fact]
    public void PaidWagersAccrueOncePerChargingSpinUntilOneWholeCentPayout()
    {
        var noCharge = GoldFoundryCrucible.Settle(95, 0, 99_849, 50);
        Assert.Equal(95, noCharge.EnergyBalance);
        Assert.Equal(99_849, noCharge.WageredCents);
        Assert.False(noCharge.Poured);

        var twoCharges = GoldFoundryCrucible.Settle(noCharge.EnergyBalance, 2, noCharge.WageredCents, 100);
        Assert.Equal(97, twoCharges.EnergyBalance);
        Assert.Equal(99_949, twoCharges.WageredCents);

        var free = GoldFoundryCrucible.Settle(twoCharges.EnergyBalance, 1, twoCharges.WageredCents, 0);
        Assert.Equal(98, free.EnergyBalance);
        Assert.Equal(twoCharges.WageredCents, free.WageredCents);

        var poured = GoldFoundryCrucible.Settle(free.EnergyBalance, 2, free.WageredCents, 50);
        Assert.True(poured.Poured);
        Assert.Equal(14_999, poured.PayoutCents);
        Assert.Equal(0, poured.WageredCents);
        Assert.Equal(0, poured.EnergyBalance);
    }

    [Fact]
    public async Task DemoPourUsesCurrentAndPriorQualifyingWagersWithoutLegacyMultiplier()
    {
        var auditStore = new RecordingStore();
        var controller = new DemoSlotsController(FoundryEngine(landsCoal: true), auditStore,
            NullLogger<DemoSlotsController>.Instance);
        var response = await controller.Spin(new(GameId, 2, false, 0, null, 99,
            CrucibleWageredCents: 99_950), default);
        var result = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.True(result.CruciblePoured);
        Assert.Equal(15_000, result.CruciblePayoutCents);
        Assert.Equal(0, result.CrucibleWageredCents);
        Assert.Equal(0, result.CruciblePourValueCents);
        Assert.Equal(0, result.EnergyBalance);
        Assert.False(result.EnergyMultiplierApplied);
        Assert.Equal(1m, result.PayoutMultiplier);
        Assert.Equal(0, Assert.Single(auditStore.Capture!.Summary.Participants).PayoutCents);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(auditStore.Capture.Summary, auditStore.Capture.PayloadJson));
    }

    [Fact]
    public async Task DemoRetiresBonusStateAndDoesNotAddNonchargingWagers()
    {
        var auditStore = new RecordingStore();
        var controller = new DemoSlotsController(FoundryEngine(landsCoal: false), auditStore,
            NullLogger<DemoSlotsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var stale = new DemoSpinRequest(GameId, 2, false, 4, 2, 35,
            [new("sync", 39, 2, 40)], "rows-paw-rand", CrucibleWageredCents: 1_000);
        var response = await controller.Spin(stale, default);
        var result = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal(0, result.EnergyAwarded);
        Assert.Equal(35, result.EnergyBalance);
        Assert.Equal(1_000, result.CrucibleWageredCents);
        Assert.Equal(150, result.CruciblePourValueCents);
        Assert.Equal(0, result.FreeSpinsRemaining);
        Assert.Equal(0, result.FreeSpinsAwarded);
        Assert.Null(result.FreeSpinWagerPoints);
        Assert.Null(result.FreeSpinFeatureMode);
        Assert.Empty(result.SealCollections);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(auditStore.Capture!.Summary, auditStore.Capture.PayloadJson));

        Assert.IsType<BadRequestObjectResult>(await controller.Spin(stale with { UseFreeSpin = true }, default));
        Assert.IsType<BadRequestObjectResult>(await controller.Spin(stale with
        {
            MidnightBonusState = new([], new Dictionary<string, long>(), new Dictionary<string, long>())
        }, default));
    }

    [Fact]
    public async Task WukongRetainsItsEnergyMultiplierAndOmitsCrucibleFields()
    {
        var auditStore = new RecordingStore();
        var controller = new DemoSlotsController(Engine(6), auditStore, NullLogger<DemoSlotsController>.Instance);
        var response = await controller.Spin(new(SlotSpecialRoundProfiles.WukongGameId, 2, false, 0, null, 99,
            CrucibleWageredCents: 99_950), default);
        var result = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.True(result.EnergyMultiplierApplied);
        Assert.Equal(1.5m, result.PayoutMultiplier);
        Assert.Equal(0, result.EnergyBalance);
        Assert.Null(result.CruciblePoured);
        Assert.Null(result.CruciblePayoutCents);
        Assert.Null(result.CrucibleWageredCents);
        Assert.Null(result.CruciblePourValueCents);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(auditStore.Capture!.Summary, auditStore.Capture.PayloadJson));
    }

    internal static SpinService Engine(int seed) => Engine(new SeededRandom(seed));

    internal static SpinService FoundryEngine(bool landsCoal)
    {
        var game = GoldFoundrySlotDefinitions.CreateGame();
        var stops = GoldFoundrySlotDefinitions.CreateReelSet().Reels.Select((strip, reel) =>
            Enumerable.Range(0, strip.Count).First(stop =>
                Enumerable.Range(0, game.Layout.VisibleRows)
                    .Any(row => strip[(stop + row) % strip.Count] == game.Energy!.SymbolId) ==
                (landsCoal && reel == 0))).ToArray();
        return Engine(new ScriptedStops(stops));
    }

    private static SpinService Engine(IRandomIndexSource random)
    {
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        var options = configuration.RootElement.GetProperty("Slots")
            .Deserialize<SlotsOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var provider = new OptionsSlotsDefinitionProvider(Options.Create(options));
        return new(provider, new CryptoReelGenerator(random), new CombinationEvaluator(), new PayoutCalculator(), random);
    }

    private sealed class ScriptedStops(IReadOnlyList<int> stops) : IRandomIndexSource
    {
        private int next;

        public int Next(int maximumExclusive)
        {
            var stop = stops[next++];
            Assert.InRange(stop, 0, maximumExclusive - 1);
            return stop;
        }
    }

    private sealed class SeededRandom(int seed) : IRandomIndexSource
    {
        private readonly Random random = new(seed);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }

    private sealed class RecordingStore : IDemoSlotAuditStore
    {
        internal FortuneForge.Games.Abstractions.Auditing.RoundAuditCapture? Capture { get; private set; }

        public Task RecordAsync(DemoSlotAuditEntry entry, CancellationToken cancellationToken)
        {
            Capture = DemoSlotRoundRecording.Capture(entry);
            return Task.CompletedTask;
        }
    }
}

[Collection(MidnightVaultPaidEmulatorCollection.Name)]
public sealed class GoldFoundryCrucibleFirestoreTests(MidnightVaultPaidEmulatorFixture emulator)
{
    [Fact]
    public async Task PaidPourPersistsByGameAndDoesNotCreditASettlementRetry()
    {
        const string player = "local-foundry-pour";
        const string gameId = GoldFoundryCrucibleTests.GameId;
        var database = emulator.Database();
        var store = new FirestoreAccountStore(database);
        Assert.NotNull((await store.CreateAsync(player, player, player.ToUpperInvariant(), player + "@example.test",
            "unused-local-test", "active", default)).Value);
        var wallet = database.Collection("userBalances").Document(player + "_slotsCredits");
        await wallet.UpdateAsync(new Dictionary<string, object> { ["available"] = 1000L, ["availableFractionalCents"] = 0L });
        var gameKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(gameId)));
        await database.Collection("slotSpinGuards").Document(player + "_" + gameKey)
            .SetAsync(new Dictionary<string, object> { ["userId"] = player, ["crucibleWageredCents"] = 99_950L });
        await database.Collection("userBalances").Document(player + "_energy:" + gameId)
            .SetAsync(new Dictionary<string, object> { ["available"] = 99L, ["version"] = 0L });

        var resumed = await new FirestoreAccountStore(database).GetSlotStateAsync(player, gameId, 25m, default);
        Assert.Equal(99, resumed.EnergyBalance);
        Assert.Equal(99_950, resumed.CrucibleWageredCents);
        Assert.Equal(14_992, resumed.CruciblePourValueCents);
        var wukong = await store.GetSlotStateAsync(player, SlotSpecialRoundProfiles.WukongGameId, 25m, default);
        Assert.Null(wukong.CrucibleWageredCents);
        Assert.Null(wukong.CruciblePourValueCents);

        var admission = await store.BeginSlotSpinAsync(player, gameId, 2, 25m, false, false, 0,
            DateTime.UtcNow, TimeSpan.Zero, default);
        var spin = GoldFoundryCrucibleTests.FoundryEngine(landsCoal: true).SpinAdmitted(gameId, admission, player);
        var first = await store.RecordSlotSpinAsync(player, spin, admission, DateTime.UtcNow, default);
        Assert.True(first.CruciblePoured);
        Assert.Equal(15_000, first.CruciblePayoutCents);
        Assert.Equal(1000m - 0.5m + 150m + spin.Payout.TotalPoints * 0.25m, first.SlotsCreditsBalance);
        var beforeRetry = await wallet.GetSnapshotAsync();
        var retry = await store.RecordSlotSpinAsync(player, spin, admission, DateTime.UtcNow, default);
        var afterRetry = await wallet.GetSnapshotAsync();
        Assert.Equal(first.CruciblePayoutCents, retry.CruciblePayoutCents);
        Assert.Equal(beforeRetry.UpdateTime, afterRetry.UpdateTime);
        var after = await store.GetSlotStateAsync(player, gameId, 25m, default);
        Assert.Equal(0, after.EnergyBalance);
        Assert.Equal(0, after.CrucibleWageredCents);
    }

    [Fact]
    public async Task PaidStateIsIsolatedFromOtherGamesAndFreeSpinsAreRejected()
    {
        const string player = "local-foundry-isolation";
        const string gameId = GoldFoundryCrucibleTests.GameId;
        const string otherGameId = SlotSpecialRoundProfiles.WukongGameId;
        var database = emulator.Database();
        var store = new FirestoreAccountStore(database);
        Assert.NotNull((await store.CreateAsync(player, player, player.ToUpperInvariant(), player + "@example.test",
            "unused-local-test", "active", default)).Value);
        var otherGameKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(otherGameId)));
        await database.Collection("slotSpinGuards").Document(player + "_" + otherGameKey)
            .SetAsync(new Dictionary<string, object> { ["crucibleWageredCents"] = 1_000L });
        await database.Collection("userBalances").Document(player + "_energy:" + otherGameId)
            .SetAsync(new Dictionary<string, object> { ["available"] = 99L, ["version"] = 0L });

        var state = await store.GetSlotStateAsync(player, gameId, 25m, default);
        Assert.Equal(0, state.FreeSpinsRemaining);
        Assert.Equal(0, state.EnergyBalance);
        Assert.Equal(0, state.CrucibleWageredCents);
        Assert.Empty(state.SealCollections);
        var engine = GoldFoundryCrucibleTests.FoundryEngine(landsCoal: true);
        Assert.Throws<ArgumentException>(() => engine.ValidateRequest(gameId, 2, useFreeSpin: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.ValidateRequest(gameId, 3));
        await Assert.ThrowsAsync<ArgumentException>(() => store.BeginSlotSpinAsync(player, gameId, 2, 25m,
            true, false, 0, DateTime.UtcNow, TimeSpan.Zero, default));
    }
}
