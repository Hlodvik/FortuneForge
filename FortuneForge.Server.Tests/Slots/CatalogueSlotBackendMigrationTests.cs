using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots;
using FortuneForge.Games.SamuraiFortune;
using FortuneForge.Games.SamuraiFortune.Server;
using FortuneForge.Games.CandyCarnival;
using FortuneForge.Games.CandyCarnival.Server;
using FortuneForge.Games.PhantomManor;
using FortuneForge.Games.PhantomManor.Server;
using FortuneForge.Games.NordicLegends;
using FortuneForge.Games.NordicLegends.Server;
using FortuneForge.Games.DesertTreasures;
using FortuneForge.Games.DesertTreasures.Server;
using FortuneForge.Games.RobotRevolution;
using FortuneForge.Games.RobotRevolution.Server;
using FortuneForge.Games.Wukong;
using FortuneForge.Games.Wukong.Server;
using FortuneForge.Games.RainbowRealm;
using FortuneForge.Games.RainbowRealm.Server;
using FortuneForge.Games.PiratesFortune;
using FortuneForge.Games.PiratesFortune.Server;
using FortuneForge.Games.GodsOfOlympus;
using FortuneForge.Games.GodsOfOlympus.Server;
using FortuneForge.Games.ReelRiches;
using FortuneForge.Games.ReelRiches.Server;
using FortuneForge.Games.HighNoonFortune;
using FortuneForge.Games.HighNoonFortune.Server;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

// Package tests own individual rules; these cases exercise the migrated public boundary.
[Collection(MidnightVaultPaidEmulatorCollection.Name)]
public sealed class CatalogueSlotBackendMigrationTests(MidnightVaultPaidEmulatorFixture emulator)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Account = "catalogue-slot-player";
    private const string PreparedPath = "/api/slots/prepared-spins";
    private const string LegacyPath = "/api/slots/spins/";
    private static readonly string[] Ids =
    [
        SamuraiFortuneEngine.GameId, CandyCarnivalEngine.GameId, PhantomManorEngine.GameId,
        NordicLegendsEngine.GameId, DesertTreasuresEngine.GameId, RobotRevolutionEngine.GameId,
        WukongEngine.GameId, RainbowRealmEngine.GameId, PiratesFortuneEngine.GameId,
        GodsOfOlympusEngine.GameId, ReelRichesEngine.GameId, HighNoonFortuneEngine.GameId
    ];
    public static IEnumerable<object[]> Games => Ids.Select(id => new object[] { id });
    public static IEnumerable<object[]> PaidAndFree => Ids.SelectMany(id => new[] { false, true }
        .Select(free => new object[] { id, free }));

    [Theory]
    [MemberData(nameof(PaidAndFree))]
    public async Task SignedPublicPaidAndOddFreeRoundsRecoverAcrossWorkerRestartAndRolloutFlags(string gameId, bool free)
    {
        await using var h = await Harness.CreateAsync(emulator.Database(), Spec(gameId), free ? 2 : 0);
        var path = free ? LegacyPath : PreparedPath;
        var operation = $"catalogue-{h.Spec.AuditId}-{(free ? "free" : "paid")}";
        var command = new SpinRequest(gameId, free ? 3 : 6, free, false);

        h.RolloutEnabled = false;
        Assert.False((await h.Guard.GetSnapshotAsync()).ContainsField(h.Spec.OwnerField));
        Assert.Equal(503, (await h.SpinAsync(command, null, LegacyPath)).Status);
        Assert.Equal(0, await h.CountAsync("slotSpinAdmissions"));
        Assert.Equal(0, await h.CountAsync("slotSpinResults"));
        Assert.Empty(await h.LedgerAsync());
        Assert.Equal(free ? 2 : 0, await h.UnitsAsync("freeGames"));
        Assert.Empty(h.Faults.WorkerRequests);
        h.RolloutEnabled = true;

        var state = await h.StateAsync();
        Assert.True(state.Status == 200, $"State returned {state.Status}: {state.Raw}; wallet replies: {string.Join(" | ", h.Faults.WalletReplies)}; worker wallet URL={h.Worker.Configuration["GameWallet:Url"]}");
        Assert.Equal("1", state.PreparedHeader);
        Assert.Equal(free ? 2 : 0, state.Data.GetProperty("freeSpinsRemaining").GetInt32());
        if (!free) await h.AssertWorkerRejectsTamperingAsync(command);

        h.Faults.LoseWalletReply = true;
        Assert.Equal(503, (await h.SpinAsync(command, operation, path)).Status);
        Assert.Equal(1, await h.CountAsync("walletRoundSettlements"));
        Assert.Equal(0, await h.CountAsync("slotSpinResults"));
        Assert.Equal(free ? 1 : 0, await h.UnitsAsync("freeGames"));
        var pending = await h.StateAsync();
        Assert.Equal(operation, pending.Data.GetProperty("pendingOperation").GetProperty("operationId").GetString());
        Assert.Equal(free ? 2 : 0, pending.Data.GetProperty("freeSpinsRemaining").GetInt32());

        h.Random.ForbidDraws = true;
        await h.RestartWorkerAsync(allowStarts: false);
        var recovered = await h.SpinAsync(command, operation, path);
        Assert.Equal(200, recovered.Status);
        Assert.Equal(gameId, recovered.Data.GetProperty("gameId").GetString());
        Assert.Equal(command.WagerPoints, recovered.Data.GetProperty("wagerPoints").GetInt64());
        Assert.Equal(free, recovered.Data.GetProperty("isFreeSpin").GetBoolean());
        if (gameId == GodsOfOlympusEngine.GameId)
            Assert.Equal(!free, recovered.Data.TryGetProperty("olympusTumble", out var tumble) &&
                tumble.ValueKind == JsonValueKind.Object);
        var spin = recovered.Data.GetProperty("spinId").GetGuid().ToString("N");
        var payoutCents = recovered.Data.GetProperty("payout").GetProperty("totalPoints").GetInt64() * 25;
        Assert.Equal(100_000 - (free ? 0 : 150) + payoutCents, await h.CashCentsAsync());
        Assert.Equal(free ? 1 : 0, await h.UnitsAsync("freeGames"));
        Assert.Equal(1, await h.CountAsync("walletRoundSettlements"));
        Assert.Equal(1, await h.CountAsync("slotSpinResults"));
        Assert.Equal(!free, (await h.Database.Collection("balanceTransactions").Document(spin + "-wager").GetSnapshotAsync()).Exists);
        Assert.Equal(free, (await h.Database.Collection("balanceTransactions").Document(spin + "-free-game-use").GetSnapshotAsync()).Exists);
        Assert.Equal(2, h.Faults.WalletCalls);
        await h.AssertHostReplayAsync(spin);
        var ledger = await h.LedgerAsync();
        var requests = h.Faults.WorkerRequests.ToArray();
        Assert.Contains(requests, request => request.Method == "POST" && request.Path == path && request.OperationId == operation);
        Assert.All(requests.Where(request => request.Method == "POST"), request =>
        {
            Assert.Equal(command, JsonSerializer.Deserialize<SpinRequest>(request.Body, Json));
            Assert.Equal(Account, request.AccountId);
            Assert.Equal(h.Spec.AuditId, request.Observation!.GameId);
            Assert.Equal(request.Path, request.Observation.Path);
            Assert.Equal(operation, request.OperationId);
        });

        var exact = await h.SpinAsync(command, operation, path);
        Assert.Equal(recovered.Raw, exact.Raw);
        Assert.Equal(409, (await h.SpinAsync(command with { UseSpecialBoost = true }, operation, path)).Status);
        h.Faults.LoseWorkerReply = true;
        Assert.Equal(503, (await h.SpinAsync(command, operation, path)).Status);
        Assert.Equal(recovered.Raw, (await h.SpinAsync(command, operation, path)).Raw);
        Assert.Equal(503, (await h.SpinAsync(command, operation + "-new-paused", path)).Status);

        h.RolloutEnabled = false;
        Assert.Equal(503, (await h.SpinAsync(command, operation, PreparedPath)).Status);
        Assert.Equal(503, (await h.SpinAsync(command with { WagerPoints = 6 }, null, LegacyPath)).Status);
        Assert.Equal(503, (await h.StateAsync()).Status);
        h.RolloutEnabled = true;
        Assert.Equal(recovered.Raw, (await h.SpinAsync(command, operation, path)).Raw);
        Assert.Equal(403, (await h.SpinAsync(command, operation, path, expectedAccount: "another-player")).Status);
        Assert.Equal(ledger, await h.LedgerAsync());
        Assert.Equal(2, h.Faults.WalletCalls);
        Assert.Equal(0, await h.CountAsync("slotSpinAdmissions"));
        Assert.Equal(gameId, (await h.Guard.GetSnapshotAsync()).GetValue<string>(h.Spec.OwnerField));
    }

    [Theory]
    [MemberData(nameof(PaidAndFree))]
    public async Task FlagCutoverLeavesLiveLegacyWorkAloneAndRestoresExpiredProvenBenefitsOnce(string gameId, bool expired)
    {
        await using var h = await Harness.CreateAsync(emulator.Database(), Spec(gameId), 1);
        var admission = await h.LegacyAdmissionAsync(expired);
        Assert.Equal(0, await h.UnitsAsync("freeGames"));
        Assert.Equal(1, await h.UnitsAsync("specialPoints"));
        h.RolloutEnabled = true;
        h.Random.ForbidDraws = true;
        var command = new SpinRequest(gameId, 3, true, true);
        var operation = $"legacy-cutover-{h.Spec.AuditId}";
        var response = await h.SpinAsync(command, operation, PreparedPath);
        Assert.True(response.Status == (expired ? 409 : 429), $"Cutover returned {response.Status}: {response.Raw}; wallet replies: {string.Join(" | ", h.Faults.WalletReplies)}");
        Assert.Equal(expired ? "legacy-slot-round-restored" : "legacy-slot-round-active", response.Data.GetProperty("code").GetString());
        Assert.Equal(0, await h.CountAsync("slotSpinResults"));
        Assert.Equal(0, await h.CountAsync("walletRoundSettlements"));
        Assert.Equal(0, await h.CountAsync("slotPreparedOperations"));
        Assert.False((await h.Guard.GetSnapshotAsync()).ContainsField(h.Spec.OwnerField));
        Assert.Equal(100_000, await h.CashCentsAsync());
        if (!expired)
        {
            Assert.Equal("1", response.RetryAfter);
            Assert.Equal(admission.AdmissionId.ToString("N"), (await h.Guard.GetSnapshotAsync()).GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
            Assert.Equal(0, await h.UnitsAsync("freeGames"));
            Assert.Equal(1, await h.UnitsAsync("specialPoints"));
            var liveOutcome = LegacyEngine(new Draws()).SpinAdmitted(gameId, admission, Account);
            var settled = await h.Store.RecordSlotSpinAsync(Account, liveOutcome, admission, DateTime.UtcNow, default);
            await h.AssertHostReplayAsync(liveOutcome.SpinId.ToString("N"), native: false);
            Assert.Contains(SlotRoundAudit.Check(liveOutcome with { IsFreeSpin = false }, liveOutcome.Payout, isPaid: true),
                finding => finding.Code == "slot-wager-invalid");
            Assert.Contains(SlotRoundAudit.Check(liveOutcome with { PointValueInCents = 26m }, liveOutcome.Payout, isPaid: true),
                finding => finding.Code == "slot-wager-invalid");
            var stableLedger = await h.LedgerAsync();
            Assert.Equal(settled.SlotsCreditsBalance,
                (await h.Store.RecordSlotSpinAsync(Account, liveOutcome, admission, DateTime.UtcNow, default)).SlotsCreditsBalance);
            Assert.Equal(stableLedger, await h.LedgerAsync());
            Assert.Equal(string.Empty, (await h.Guard.GetSnapshotAsync()).GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
            Assert.Equal(1, await h.CountAsync("slotSpinResults"));
            Assert.Equal(1, await h.CountAsync("walletRoundSettlements"));
            h.Random.ForbidDraws = false;
            await Task.Delay(600); // The worker preserves the host's existing 500 ms admission cooldown.
            var native = await h.SpinAsync(new(gameId, 6, false, false), operation + "-after-live", PreparedPath);
            Assert.Equal(200, native.Status);
            Assert.Equal(2, await h.CountAsync("slotSpinResults"));
            Assert.Equal(2, await h.CountAsync("walletRoundSettlements"));
            Assert.Equal(gameId, (await h.Guard.GetSnapshotAsync()).GetValue<string>(h.Spec.OwnerField));
            return;
        }

        Assert.Equal(1, await h.UnitsAsync("freeGames"));
        Assert.Equal(9, await h.UnitsAsync("specialPoints"));
        Assert.Equal(LegacySlotRecoveryResult.NoLegacyAdmission,
            await h.Store.ReconcileExpiredLegacySlotAsync(Account, gameId, DateTime.UtcNow, default));
        var lateOutcome = LegacyEngine(new Draws()).SpinAdmitted(gameId, admission, Account);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Store.RecordSlotSpinAsync(
            Account, lateOutcome, admission, DateTime.UtcNow, default));
        var restored = await h.StateAsync();
        Assert.Equal(3, restored.Data.GetProperty("freeSpinWagerPoints").GetInt64());
        Assert.Equal("paw-rand", restored.Data.GetProperty("freeSpinFeatureMode").GetString());
        Assert.Equal(1, restored.Data.GetProperty("freeSpinsRemaining").GetInt32());
        h.Random.ForbidDraws = false;
        var paid = await h.SpinAsync(command, operation, PreparedPath);
        Assert.Equal(200, paid.Status);
        Assert.Equal(paid.Raw, (await h.SpinAsync(command, operation, PreparedPath)).Raw);
        Assert.Equal(0, await h.UnitsAsync("freeGames"));
        Assert.Equal(1, await h.CountAsync("walletRoundSettlements"));
        Assert.Equal(1, await h.CountAsync("slotSpinResults"));
        var refundIds = (await h.Database.Collection("balanceTransactions").GetSnapshotAsync()).Documents
            .Where(row => row.Id.StartsWith(admission.AdmissionId.ToString("N")) && row.Id.EndsWith("-refund")).Select(row => row.Id).Order().ToArray();
        Assert.Equal(new[] { $"{admission.AdmissionId:N}-free-game-refund", $"{admission.AdmissionId:N}-special-boost-refund" }, refundIds);
        Assert.Equal(gameId, (await h.Guard.GetSnapshotAsync()).GetValue<string>(h.Spec.OwnerField));
    }

    [Theory]
    [MemberData(nameof(Games))]
    public async Task ConcurrentExpiredLegacyReconciliationRestoresEachEntitlementOnce(string gameId)
    {
        await using var h = await Harness.CreateAsync(emulator.Database(), Spec(gameId), 1);
        var admission = await h.LegacyAdmissionAsync(expired: true);
        var results = await Task.WhenAll(
            h.Store.ReconcileExpiredLegacySlotAsync(Account, gameId, DateTime.UtcNow, default),
            h.Store.ReconcileExpiredLegacySlotAsync(Account, gameId, DateTime.UtcNow, default));
        Assert.Single(results, result => result == LegacySlotRecoveryResult.Reconciled);
        Assert.Single(results, result => result == LegacySlotRecoveryResult.NoLegacyAdmission);
        Assert.Equal(1, await h.UnitsAsync("freeGames"));
        Assert.Equal(9, await h.UnitsAsync("specialPoints"));
        Assert.Equal(100_000, await h.CashCentsAsync());
        Assert.Equal(0, await h.CountAsync("slotSpinResults"));
        Assert.Equal(0, await h.CountAsync("walletRoundSettlements"));
        Assert.Equal("recovered", (await h.Database.Collection("slotSpinAdmissions").Document(admission.AdmissionId.ToString("N")).GetSnapshotAsync()).GetValue<string>("status"));
    }

    [Theory]
    [MemberData(nameof(Games))]
    public async Task PausedNewNativeRoundsStillExposeExpiredLegacyAdmissionsForReconciliation(string gameId)
    {
        await using var h = await Harness.CreateAsync(emulator.Database(), Spec(gameId), 1);
        await h.LegacyAdmissionAsync(expired: true);
        h.Worker.Configuration[$"{h.Spec.Package}:AllowNewPaidRounds"] = "false";
        h.Random.ForbidDraws = true;
        h.RolloutEnabled = true;
        var reply = await h.SpinAsync(new(gameId, 3, true, false), $"paused-legacy-{h.Spec.AuditId}", PreparedPath);
        Assert.Equal(409, reply.Status);
        Assert.Equal("legacy-slot-round-restored", reply.Data.GetProperty("code").GetString());
        Assert.Equal(1, await h.UnitsAsync("freeGames"));
        Assert.Equal(9, await h.UnitsAsync("specialPoints"));
        Assert.Equal(0, await h.CountAsync("slotSpinResults"));
        Assert.Equal(0, await h.CountAsync("walletRoundSettlements"));
    }

    [Theory]
    [InlineData(OlympusTumbleRules.EngineVersionV2)]
    [InlineData(OlympusTumbleRules.EngineVersionV3)]
    public async Task HistoricalOlympusCashFinishesItsAdmittedRulesBeforeSignedCurrentWorkerTakesOwnership(string version)
    {
        const string gameId = GodsOfOlympusEngine.GameId;
        await using var h = await Harness.CreateAsync(emulator.Database(), Spec(gameId), 0);
        await h.SeedUnitsAsync("energy", 43);
        await h.Guard.UpdateAsync(new Dictionary<string, object>
        {
            ["consecutiveFiveMisses"] = 7L,
            ["sealCounts"] = new Dictionary<string, long> { ["sync"] = 7L },
            ["sealWagerCents"] = new Dictionary<string, long> { ["sync"] = 350L }
        });
        h.RolloutEnabled = false;
        var admission = await h.Store.BeginSlotSpinAsync(Account, gameId, 6, 25, false, false, 0,
            DateTime.UtcNow, TimeSpan.Zero, default);
        admission = admission with { MathModelId = version };
        await h.Database.Collection("slotSpinAdmissions").Document(admission.AdmissionId.ToString("N"))
            .UpdateAsync("mathModelId", version);
        var historical = LegacyEngine(new Draws()).SpinAdmitted(gameId, admission, Account);
        Assert.Equal(version, historical.EngineAudit!.EngineVersion);
        Assert.Equal(version, historical.ReelSetId);
        Assert.Equal(version, historical.PaytableId);
        Assert.NotNull(historical.OlympusTumble);
        Assert.True(historical.Payout.TotalPoints > 0);

        h.RolloutEnabled = true;
        h.Worker.Configuration["GodsOfOlympus:AllowNewPaidRounds"] = "false";
        h.Random.ForbidDraws = true;
        var command = new SpinRequest(gameId, 6, false, false);
        var operation = "historical-olympus-" + version;
        var waiting = await h.SpinAsync(command, operation, LegacyPath);
        Assert.Equal(429, waiting.Status);
        Assert.Equal("legacy-slot-round-active", waiting.Data.GetProperty("code").GetString());
        Assert.Equal(0, await h.CountAsync("walletRoundSettlements"));
        Assert.Equal(0, await h.CountAsync("slotSpinResults"));
        Assert.False((await h.Guard.GetSnapshotAsync()).ContainsField(h.Spec.OwnerField));
        Assert.Equal(100_000, await h.CashCentsAsync());

        var settled = await h.Store.RecordSlotSpinAsync(Account, historical, admission, DateTime.UtcNow, default);
        await h.AssertHostReplayAsync(historical.SpinId.ToString("N"), native: false);
        Assert.Equal(100_000 - 150 + historical.Payout.TotalPoints * 25, await h.CashCentsAsync());
        var historicalLedger = await h.LedgerAsync();
        Assert.Equal(settled.SlotsCreditsBalance,
            (await h.Store.RecordSlotSpinAsync(Account, historical, admission, DateTime.UtcNow, default)).SlotsCreditsBalance);
        Assert.Equal(historicalLedger, await h.LedgerAsync());
        Assert.Equal(1, await h.CountAsync("walletRoundSettlements"));
        Assert.Equal(43, await h.UnitsAsync("energy"));
        Assert.Equal(503, (await h.SpinAsync(command, operation, PreparedPath)).Status);

        h.Random.ForbidDraws = false;
        await h.RestartWorkerAsync(allowStarts: true);
        await Task.Delay(600);
        var current = await h.SpinAsync(command, operation, PreparedPath);
        Assert.True(current.Status == 200, $"Current Olympus returned {current.Status}: {current.Raw}");
        Assert.Equal(OlympusTumbleRules.EngineVersion, current.Data.GetProperty("reelSetId").GetString());
        Assert.Equal(JsonValueKind.Object, current.Data.GetProperty("olympusTumble").ValueKind);
        Assert.Equal(0, current.Data.GetProperty("payout").GetProperty("totalPoints").GetInt64());
        Assert.Equal(43, current.Data.GetProperty("energyBalance").GetInt64());
        Assert.Equal(9, current.Data.GetProperty("specialPointsBalance").GetInt32());
        Assert.Equal(7, current.Data.GetProperty("consecutiveFiveMisses").GetInt32());
        var collection = Assert.Single(current.Data.GetProperty("sealCollections").EnumerateArray(),
            item => item.GetProperty("sealId").GetString() == "sync");
        Assert.Equal(7, collection.GetProperty("count").GetInt32());
        Assert.Equal(2, collection.GetProperty("averageWagerPoints").GetInt64());
        Assert.Equal(100_000 - 300 + historical.Payout.TotalPoints * 25, await h.CashCentsAsync());
        Assert.Equal(2, await h.CountAsync("walletRoundSettlements"));
        Assert.Equal(2, await h.CountAsync("slotSpinResults"));
        Assert.Equal(0, await h.UnitsAsync("freeGames"));
        Assert.Equal(9, await h.UnitsAsync("specialPoints"));
        Assert.Equal(43, await h.UnitsAsync("energy"));
        var guard = await h.Guard.GetSnapshotAsync();
        Assert.Equal(gameId, guard.GetValue<string>(h.Spec.OwnerField));
        Assert.Equal(7, guard.GetValue<long>("consecutiveFiveMisses"));
        Assert.Equal(7, guard.GetValue<Dictionary<string, long>>("sealCounts")["sync"]);
        Assert.Equal(350, guard.GetValue<Dictionary<string, long>>("sealWagerCents")["sync"]);
        await h.AssertHostReplayAsync(current.Data.GetProperty("spinId").GetGuid().ToString("N"));
        var finalLedger = await h.LedgerAsync();
        Assert.Equal(current.Raw, (await h.SpinAsync(command, operation, LegacyPath)).Raw);
        Assert.Equal(finalLedger, await h.LedgerAsync());
    }

    private static SpinService LegacyEngine(IRandomIndexSource random) => new(new OptionsSlotsDefinitionProvider(),
        new CryptoReelGenerator(random), new CombinationEvaluator(), new PayoutCalculator(), random);

    private sealed record GameSpec(string GameId, string Package, string AuditId, string StatePath, string OwnerField,
        Action<IServiceCollection> Register, Action<WebApplication> Map, string? ProofPrefix = null,
        string? LegacyStorageGameId = null)
    {
        public string Camel => char.ToLowerInvariant(Package[0]) + Package[1..];
        public string StorageGameId => LegacyStorageGameId ?? GameId;
    }
    private static GameSpec Spec(string gameId) => gameId switch
    {
        SamuraiFortuneEngine.GameId => new(gameId, "SamuraiFortune", SamuraiFortunePaidRouteContract.AuditGameId,
            SamuraiFortunePaidRouteContract.StatePath, SamuraiFortuneRoundStateCodec.OwnerField,
            services => services.AddSingleton<SamuraiFortuneRoundStore>().AddSingleton<SamuraiFortuneRoundService>(),
            app => app.MapSamuraiFortunePaidRounds(GameWorkerAuthentication.GetAccountId)),
        CandyCarnivalEngine.GameId => new(gameId, "CandyCarnival", CandyCarnivalPaidRouteContract.AuditGameId,
            CandyCarnivalPaidRouteContract.StatePath, CandyCarnivalRoundStateCodec.OwnerField,
            services => services.AddSingleton<CandyCarnivalRoundStore>().AddSingleton<CandyCarnivalRoundService>(),
            app => app.MapCandyCarnivalPaidRounds(GameWorkerAuthentication.GetAccountId)),
        PhantomManorEngine.GameId => new(gameId, "PhantomManor", PhantomManorPaidRouteContract.AuditGameId,
            PhantomManorPaidRouteContract.StatePath, PhantomManorRoundStateCodec.OwnerField,
            services => services.AddSingleton<PhantomManorRoundStore>().AddSingleton<PhantomManorRoundService>(),
            app => app.MapPhantomManorPaidRounds(GameWorkerAuthentication.GetAccountId)),
        NordicLegendsEngine.GameId => new(gameId, "NordicLegends", NordicLegendsPaidRouteContract.AuditGameId,
            NordicLegendsPaidRouteContract.StatePath, NordicLegendsRoundStateCodec.OwnerField,
            services => services.AddSingleton<NordicLegendsRoundStore>().AddSingleton<NordicLegendsRoundService>(),
            app => app.MapNordicLegendsPaidRounds(GameWorkerAuthentication.GetAccountId)),
        DesertTreasuresEngine.GameId => new(gameId, "DesertTreasures", DesertTreasuresPaidRouteContract.AuditGameId,
            DesertTreasuresPaidRouteContract.StatePath, DesertTreasuresRoundStateCodec.OwnerField,
            services => services.AddSingleton<DesertTreasuresRoundStore>().AddSingleton<DesertTreasuresRoundService>(),
            app => app.MapDesertTreasuresPaidRounds(GameWorkerAuthentication.GetAccountId)),
        RobotRevolutionEngine.GameId => new(gameId, "RobotRevolution", RobotRevolutionPaidRouteContract.AuditGameId,
            RobotRevolutionPaidRouteContract.StatePath, RobotRevolutionRoundStateCodec.OwnerField,
            services => services.AddSingleton<RobotRevolutionRoundStore>().AddSingleton<RobotRevolutionRoundService>(),
            app => app.MapRobotRevolutionPaidRounds(GameWorkerAuthentication.GetAccountId)),
        WukongEngine.GameId => new(gameId, "Wukong", WukongPaidRouteContract.AuditGameId,
            WukongPaidRouteContract.StatePath, WukongRoundStateCodec.OwnerField,
            services => services.AddSingleton<WukongRoundStore>().AddSingleton<WukongRoundService>(),
            app => app.MapWukongPaidRounds(GameWorkerAuthentication.GetAccountId), "wukong", WukongEngine.LegacyStorageGameId),
        RainbowRealmEngine.GameId => new(gameId, "RainbowRealm", RainbowPaidRouteContract.AuditGameId,
            RainbowPaidRouteContract.StatePath, RainbowRoundStateCodec.OwnerField,
            services => services.AddSingleton<RainbowRoundStore>().AddSingleton<RainbowRoundService>(),
            app => app.MapRainbowPaidRounds(GameWorkerAuthentication.GetAccountId), "rainbow"),
        PiratesFortuneEngine.GameId => new(gameId, "PiratesFortune", PiratesPaidRouteContract.AuditGameId,
            PiratesPaidRouteContract.StatePath, PiratesRoundStateCodec.OwnerField,
            services => services.AddSingleton<PiratesRoundStore>().AddSingleton<PiratesRoundService>(),
            app => app.MapPiratesPaidRounds(GameWorkerAuthentication.GetAccountId), "pirates"),
        GodsOfOlympusEngine.GameId => new(gameId, "GodsOfOlympus", OlympusPaidRouteContract.AuditGameId,
            OlympusPaidRouteContract.StatePath, OlympusRoundStateCodec.OwnerField,
            services => services.AddSingleton<OlympusRoundStore>().AddSingleton<OlympusRoundService>(),
            app => app.MapOlympusPaidRounds(GameWorkerAuthentication.GetAccountId), "olympus"),
        ReelRichesEngine.GameId => new(gameId, "ReelRiches", ReelRichesPaidRouteContract.AuditGameId,
            ReelRichesPaidRouteContract.StatePath, ReelRichesRoundStateCodec.OwnerField,
            services => services.AddSingleton<ReelRichesRoundStore>().AddSingleton<ReelRichesRoundService>(),
            app => app.MapReelRichesPaidRounds(GameWorkerAuthentication.GetAccountId), "reel-riches"),
        HighNoonFortuneEngine.GameId => new(gameId, "HighNoonFortune", HighNoonPaidRouteContract.AuditGameId,
            HighNoonPaidRouteContract.StatePath, HighNoonRoundStateCodec.OwnerField,
            services => services.AddSingleton<HighNoonRoundStore>().AddSingleton<HighNoonRoundService>(),
            app => app.MapHighNoonPaidRounds(GameWorkerAuthentication.GetAccountId), "high-noon"),
        _ => throw new ArgumentOutOfRangeException(nameof(gameId))
    };

    private sealed record Reply(int Status, string Raw, JsonElement Data, string? PreparedHeader, string? RetryAfter);
    private sealed record WorkerRequest(string Method, string Path, string? OperationId, string? AccountId,
        string Body, GameRequestObservation? Observation);
    private sealed class Faults
    {
        public bool LoseWalletReply;
        public bool LoseWorkerReply;
        public int WalletCalls;
        public ConcurrentQueue<string> WalletReplies { get; } = new();
        public ConcurrentQueue<WorkerRequest> WorkerRequests { get; } = new();
    }
    private sealed class Draws : IRandomIndexSource
    {
        public volatile bool ForbidDraws;
        public int Next(int maximumExclusive) => ForbidDraws
            ? throw new InvalidOperationException("An exact retry or legacy reconciliation must not redraw.") : maximumExclusive - 1;
    }
    private sealed class Harness : IAsyncDisposable
    {
        public required FirestoreDb Database { get; init; }
        public required GameSpec Spec { get; init; }
        public required FirestoreAccountStore Store { get; init; }
        public required string Token { get; init; }
        public required WebApplication Wallet { get; init; }
        public required byte[] WorkerKey { get; init; }
        public required byte[] WalletKey { get; init; }
        public required HttpClient ForwardHttp { get; init; }
        public required IConfigurationRoot Configuration { get; init; }
        public required Faults Faults { get; init; }
        public Draws Random { get; } = new();
        public WebApplication Worker { get; private set; } = null!;
        public DocumentReference Guard => Database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Account, Spec.StorageGameId));
        public bool RolloutEnabled { set => Configuration[$"Slots:{Spec.Package}WorkerRoundsEnabled"] = value.ToString(); }

        public static async Task<Harness> CreateAsync(FirestoreDb database, GameSpec spec, long free)
        {
            var store = new FirestoreAccountStore(database);
            Assert.NotNull((await store.CreateAsync(Account, Account, Account.ToUpperInvariant(), Account + "@example.test", "unused-local-test", "active", default)).Value);
            await database.Collection("userBalances").Document(Account + "_slotsCredits").UpdateAsync(new Dictionary<string, object>
                { ["available"] = 1000L, ["availableFractionalCents"] = 0L });
            var token = Guid.NewGuid().ToString("N");
            await store.CreateSessionAsync(Hash(token), Account, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), null, default);
            var key = RandomNumberGenerator.GetBytes(32);
            var builder = Builder();
            builder.Configuration[$"GameWallet:Games:{spec.GameId}:SigningKey"] = Convert.ToBase64String(key);
            builder.Configuration[$"GameWallet:Games:{spec.GameId}:LegacySlotLedger"] = "true";
            builder.Configuration[$"GameWallet:Games:{spec.GameId}:AllowLegacyFreeGames"] = "true";
            builder.Configuration[$"GameWallet:Games:{spec.GameId}:AllowAccountBalanceRead"] = "true";
            builder.Services.AddSingleton(database);
            builder.Services.AddSingleton<IWalletRoundSettlementStore>(new FirestoreWalletRoundSettlementStore(database));
            var wallet = builder.Build();
            wallet.MapGameWalletSettlement();
            await wallet.StartAsync();
            var faults = new Faults();
            var h = new Harness
            {
                Database = database, Spec = spec, Store = store, Token = token, Wallet = wallet, WalletKey = key,
                WorkerKey = RandomNumberGenerator.GetBytes(32), Faults = faults,
                ForwardHttp = new HttpClient(new DropReply(faults, wallet: false) { InnerHandler = new HttpClientHandler() }),
                Configuration = new ConfigurationBuilder().AddInMemoryCollection().Build()
            };
            await h.SeedUnitsAsync("freeGames", free);
            await h.SeedUnitsAsync("specialPoints", 9);
            await h.Guard.SetAsync(new Dictionary<string, object>
                { ["userId"] = Account, ["freeSpinWagerCents"] = 75L, ["freeSpinFeatureMode"] = "paw-rand" });
            h.RolloutEnabled = true;
            await h.RestartWorkerAsync(allowStarts: true);
            return h;
        }

        public async Task RestartWorkerAsync(bool allowStarts)
        {
            if (Worker is not null) await Worker.DisposeAsync();
            var builder = Builder();
            builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(WorkerKey);
            builder.Configuration["GameWallet:Url"] = Wallet.Urls.Single();
            builder.Configuration["GameWallet:SigningKey"] = Convert.ToBase64String(WalletKey);
            builder.Configuration[$"{Spec.Package}:AllowNewPaidRounds"] = allowStarts.ToString();
            builder.Services.AddSingleton(Database);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IGameRandomSource>(Random);
            builder.Services.AddGameWalletClient(Spec.GameId).AddHttpMessageHandler(() => new DropReply(Faults, wallet: true));
            Spec.Register(builder.Services);
            Worker = builder.Build();
            Worker.UseRouting();
            Worker.UseGameWorkerAuthentication(Spec.GameId);
            Worker.Use(async (context, next) =>
            {
                if (context.Request.Method == "POST")
                {
                    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
                    var body = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0;
                    var observation = GameSecurityContext.Current;
                    Assert.NotNull(observation);
                    Assert.Equal(Spec.AuditId, observation.GameId);
                    Assert.Equal(context.Request.Path.Value, observation.Path);
                    Faults.WorkerRequests.Enqueue(new(context.Request.Method, context.Request.Path.Value!,
                        context.Request.Headers["Idempotency-Key"].ToString(), GameWorkerAuthentication.GetAccountId(context), body, observation));
                }
                await next(context);
            });
            Spec.Map(Worker);
            await Worker.StartAsync();
            Configuration[$"GameWorkers:{Spec.GameId}:Url"] = Worker.Urls.Single();
            Configuration[$"GameWorkers:{Spec.GameId}:SigningKey"] = Convert.ToBase64String(WorkerKey);
        }

        public Task<Reply> StateAsync() => InvokeAsync(null, null, "/api/slots/state", Account);
        public Task<Reply> SpinAsync(SpinRequest request, string? operation, string path, string? expectedAccount = Account) =>
            InvokeAsync(request, operation, path, expectedAccount);
        private async Task<Reply> InvokeAsync(SpinRequest? request, string? operation, string path, string? expected)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = request is null ? "GET" : "POST";
            context.Request.Path = path;
            context.Request.Headers.Cookie = AccountSessionCookie.Name + "=" + Token;
            if (expected is not null) context.Request.Headers["X-FortuneForge-Expected-Account"] = expected;
            if (operation is not null) context.Request.Headers["Idempotency-Key"] = operation;
            var accessor = new HttpContextAccessor { HttpContext = context };
            var account = new AccountService(Store, null!, null!, accessor);
            var embedded = LegacyEngine(new NoDraws());
            var client = new GameWorkerClient(ForwardHttp, Configuration, Worker.Environment, TimeProvider.System,
                NullLogger<GameWorkerClient>.Instance);
            var controller = new SlotsController(embedded, account, NullLogger<SlotsController>.Instance, client, Configuration)
                { ControllerContext = new ControllerContext { HttpContext = context } };
            using var observation = GameSecurityContext.Push(new(Guid.NewGuid().ToString("N"), Account, "private-test-session",
                "203.0.113.23", DateTime.UtcNow, context.Request.Method, path, Spec.AuditId,
                null, operation, null, null, "sanitized", "direct"));
            var result = request is null ? await controller.State(Spec.GameId, default) : await controller.Spin(request, default);
            var status = result switch { ContentResult content => content.StatusCode ?? 200, ObjectResult value => value.StatusCode ?? 200, _ => throw new InvalidOperationException(result.GetType().Name) };
            var raw = result is ContentResult contentResult ? contentResult.Content! : JsonSerializer.Serialize(((ObjectResult)result).Value, Json);
            return new(status, raw, JsonDocument.Parse(raw).RootElement.Clone(), context.Response.Headers["X-FortuneForge-Prepared-Spin"].FirstOrDefault(), context.Response.Headers.RetryAfter.FirstOrDefault());
        }

        public async Task<SlotSpinAdmission> LegacyAdmissionAsync(bool expired)
        {
            RolloutEnabled = false;
            var start = expired ? DateTime.UtcNow - SlotSpinGuard.ActiveAdmissionTimeout - TimeSpan.FromSeconds(2) : DateTime.UtcNow;
            // Different requested/effective last-free wagers prove the historical lock.
            return await Store.BeginSlotSpinAsync(Account, Spec.GameId, 4, 25, true, true, 8, start, TimeSpan.Zero, default);
        }
        public async Task AssertWorkerRejectsTamperingAsync(SpinRequest command)
        {
            using var http = new HttpClient { BaseAddress = new Uri(Worker.Urls.Single()) };
            var body = JsonSerializer.SerializeToUtf8Bytes(command, Json);
            foreach (var mutation in new[] { "unsigned", "body", "key", "path", "game" })
            {
                var path = mutation == "path" ? LegacyPath : PreparedPath;
                using var message = new HttpRequestMessage(HttpMethod.Post, path)
                    { Content = new ByteArrayContent(mutation == "body" ? JsonSerializer.SerializeToUtf8Bytes(command with { WagerPoints = 8 }, Json) : body) };
                message.Content.Headers.ContentType = new("application/json");
                message.Headers.Add("Idempotency-Key", mutation == "key" ? "tampered-operation-key" : "original-operation-key");
                if (mutation != "unsigned")
                    message.Headers.Add(GameWorkerAuthentication.AssertionHeader, GameWorkerAuthentication.CreateAssertion(
                        mutation == "game" ? "foreign-slot-v1" : Spec.GameId, Account, "POST", PreparedPath, body,
                        WorkerKey, DateTimeOffset.UtcNow, headers: new Dictionary<string, string> { ["Idempotency-Key"] = "original-operation-key" }));
                using var response = await http.SendAsync(message);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
            Assert.Equal(0, await CountAsync("walletRoundSettlements"));
            Assert.Equal(0, await CountAsync("slotPreparedOperations"));
        }
        public async Task AssertHostReplayAsync(string spin, bool native = true)
        {
            var summary = Assert.Single((await Database.Collection(RoundAuditDocuments.Summaries).WhereEqualTo("roundId", spin).GetSnapshotAsync()).Documents);
            var reader = new FirestoreGameRoundAuditStore(Database, Options.Create(new AdminOperationsOptions
                { CursorSigningKey = "catalogue-slot-local-audit-signing-key" }));
            var detail = Assert.IsType<GameRoundAuditDetail>(await reader.ReadAsync(summary.Id, default));
            Assert.Empty(SlotEvidenceReplayAudit.Inspect(detail.Summary, detail.PayloadJson!));
            if (!native) return;
            var changed = JsonNode.Parse(detail.PayloadJson!)!.AsObject();
            if (Spec.ProofPrefix is not null)
                changed["data"]!["engineOutcomeSha256"] = new string('0', 64);
            else
            {
                var draws = changed["data"]![Spec.Camel + "Draws"]!.GetValue<string>();
                changed["data"]![Spec.Camel + "Draws"] = Convert.ToBase64String(Convert.FromBase64String(draws).Append((byte)0).ToArray());
            }
            Assert.Contains(SlotEvidenceReplayAudit.Inspect(detail.Summary, changed.ToJsonString()),
                finding => finding.Code == "slot-" + (Spec.ProofPrefix ?? Spec.AuditId) + "-native-proof-mismatch");
        }
        public Task SeedUnitsAsync(string kind, long amount) => Units(kind).SetAsync(new Dictionary<string, object>
            { ["userId"] = Account, ["currencyId"] = kind + ":" + Spec.StorageGameId, ["available"] = amount, ["version"] = 0L });
        public async Task<long> UnitsAsync(string kind) => (await Units(kind).GetSnapshotAsync()).GetValue<long>("available");
        private DocumentReference Units(string kind) => Database.Collection("userBalances").Document($"{Account}_{kind}:{Spec.StorageGameId}");
        public async Task<long> CashCentsAsync()
        {
            var balance = await Database.Collection("userBalances").Document(Account + "_slotsCredits").GetSnapshotAsync();
            return balance.GetValue<long>("available") * 100 + balance.GetValue<long>("availableFractionalCents");
        }
        public async Task<int> CountAsync(string collection) => (await Database.Collection(collection).GetSnapshotAsync()).Count;
        public async Task<string[]> LedgerAsync() => (await Database.Collection("balanceTransactions").GetSnapshotAsync()).Documents
            .Select(row => row.Id + ":" + row.UpdateTime).Order().ToArray();
        public async ValueTask DisposeAsync()
        {
            ForwardHttp.Dispose();
            if (Worker is not null) await Worker.DisposeAsync();
            await Wallet.DisposeAsync();
        }
        private static WebApplicationBuilder Builder()
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            return builder;
        }
        private sealed class NoDraws : IRandomIndexSource
        {
            public int Next(int maximumExclusive) => throw new InvalidOperationException("The account host must not generate a migrated spin.");
        }
        private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private sealed class DropReply(Faults faults, bool wallet) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var settlement = request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/settlements");
            if (wallet && settlement) Interlocked.Increment(ref faults.WalletCalls);
            var response = await base.SendAsync(request, token);
            if (wallet) faults.WalletReplies.Enqueue($"{request.Method} {request.RequestUri!.AbsolutePath} {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(token)}");
            if (wallet && settlement && faults.LoseWalletReply || !wallet && request.Method == HttpMethod.Post && faults.LoseWorkerReply)
            {
                if (wallet) faults.LoseWalletReply = false; else faults.LoseWorkerReply = false;
                response.Dispose();
                throw new HttpRequestException("Injected lost reply after the remote endpoint committed.");
            }
            return response;
        }
    }
}
