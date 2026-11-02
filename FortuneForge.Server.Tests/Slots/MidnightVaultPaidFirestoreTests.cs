using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

[Collection(MidnightVaultPaidEmulatorCollection.Name)]
public sealed class MidnightVaultPaidFirestoreTests(MidnightVaultPaidEmulatorFixture emulator)
{
    private const string GameId = SlotSpecialRoundProfiles.DragonHoardGameId;
    private const string V1 = "midnight-vault-paytable-v1";
    private const string V2 = "midnight-vault-paytable-v2";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Theory]
    [InlineData("v1-paid-win.json")]
    [InlineData("v1-pending-bonus.json")]
    public void GenuineV1PaidReceiptsReplayFromTheirFrozenRulesAfterV2Activation(string name)
    {
        using var saved = JsonDocument.Parse(File.ReadAllText(FixturePath(name)));
        var detail = saved.RootElement.GetProperty("audit").Deserialize<GameRoundAuditDetail>(Json)!;
        using var payload = JsonDocument.Parse(detail.PayloadJson!);
        var audit = payload.RootElement.GetProperty("data").GetProperty("engineAudit");
        Assert.Equal(V1, audit.GetProperty("paytable").GetProperty("id").GetString());
        Assert.Equal(1.09m, audit.GetProperty("baseGame").GetProperty("math").GetProperty("payoutMultiplier").GetDecimal());
        var provider = Provider();
        var activeGame = provider.GetGame(GameId)!;
        Assert.Equal(V2, activeGame.Math.PaytableId);
        var frozenPaytable = audit.GetProperty("paytable").Deserialize<PaytableDefinition>(Json)!;
        static IEnumerable<(string, int, long)> Rules(PaytableDefinition table) => table.Rules
            .OrderBy(rule => rule.SymbolId, StringComparer.Ordinal).ThenBy(rule => rule.MatchLength)
            .Select(rule => (rule.SymbolId, rule.MatchLength, rule.Multiplier));
        Assert.Equal(Rules(frozenPaytable), Rules(provider.GetPaytable(V1)!));
        var compatibleGame = MidnightVaultSlotDefinitions.CreateGameForPaytable(activeGame, V1);
        Assert.Equal(1.09m, compatibleGame.Math.PayoutMultiplier);
        Assert.Equal(6, compatibleGame.FreeGames!.AwardedSpins);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(detail.Summary, detail.PayloadJson!));
    }

    [Fact]
    public async Task EveryAdmittedCashStakeSettlesThroughTheAuthenticatedControllerInExactQuarterRandUnits()
    {
        var database = emulator.Database();
        var player = await Player(database, "every-stake", initialRand: 1_000_000);
        long expectedCents = 100_000_000;
        long totalChargedCents = 0;
        long totalPayoutCents = 0;
        for (long stake = 2; stake <= 2000; stake += 2)
        {
            // Advance only the stored cooldown fixture clock; never reset money,
            // admissions, pity, results or ledger between these real paid requests.
            if (stake > 2) await ReadyForNextRequest(player);
            var response = Success(await player.Spin(new(GameId, stake, false, false)));
            Assert.False(response.IsFreeSpin);
            Assert.Equal(stake, response.WagerPoints);
            Assert.Equal(V2, response.PaytableId);
            Assert.True(response.Payout.TotalPoints > 0);
            Assert.All(response.Payout.Paylines.SelectMany(line => line.Matches), match =>
                Assert.Equal(checked(stake * match.Multiplier), match.AmountPoints));
            Assert.Equal(response.Payout.Paylines.Sum(line => line.AmountPoints), response.Payout.TotalPoints);
            var chargedCents = checked(stake * 25);
            var payoutCents = checked(response.Payout.TotalPoints * 25);
            expectedCents = checked(expectedCents - chargedCents + payoutCents);
            totalChargedCents += chargedCents;
            totalPayoutCents += payoutCents;
            Assert.Equal(expectedCents / 100m, response.SlotsCreditsBalance);
            Assert.Equal(expectedCents, await WalletCents(player));
            var ledger = database.Collection("balanceTransactions");
            var wager = await ledger.Document(response.SpinId.ToString("N") + "-wager").GetSnapshotAsync();
            var payout = await ledger.Document(response.SpinId.ToString("N") + "-payout").GetSnapshotAsync();
            Assert.Equal(-chargedCents / 100m, (decimal)wager.GetValue<double>("amount"));
            Assert.Equal(payoutCents / 100m, (decimal)payout.GetValue<double>("amount"));
            Assert.Equal(expectedCents / 100m, (decimal)payout.GetValue<double>("balanceAfter"));
        }
        Assert.Equal(1000, (await database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Count);
        Assert.Equal(1000, (await database.Collection("slotSpinResults").GetSnapshotAsync()).Count);
        Assert.Equal(100_000_000 - totalChargedCents + totalPayoutCents, await WalletCents(player));
    }

    [Fact]
    public async Task OddCashWagerIsRejectedBeforeAnyAdmissionOrWalletMutation()
    {
        var player = await Player(emulator.Database(), "odd-cash");
        Assert.IsType<BadRequestObjectResult>(await player.Spin(new(GameId, 3, false, false)));
        Assert.Equal(100_000, await WalletCents(player));
        Assert.Empty((await player.Database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
        Assert.Empty((await player.Database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(2000)]
    public async Task LosingPaidSpinChargesTheExactStakeAndCreatesNoPayoutLedgerEntry(long stake)
    {
        var player = await Player(emulator.Database(), "paid-loss-" + stake, stopsOverride: FindLosingStops());
        var response = Success(await player.Spin(new(GameId, stake, false, false)));
        Assert.Equal(0, response.Payout.TotalPoints);
        Assert.Equal(100_000 - stake * 25, await WalletCents(player));
        var wager = Assert.Single((await player.Database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal("slot-wager", wager.GetValue<string>("type"));
        Assert.Equal(-stake * .25m, (decimal)wager.GetValue<double>("amount"));
        Assert.False((await player.Database.Collection("balanceTransactions").Document(response.SpinId.ToString("N") + "-payout").GetSnapshotAsync()).Exists);
        await AssertSavedAudit(player.Database, response, derived: false);
    }

    [Fact]
    public async Task InsufficientCashIsRejectedWithoutAnAdmissionOrLedgerWrite()
    {
        var player = await Player(emulator.Database(), "insufficient-cash", initialRand: 0);
        Assert.IsType<ConflictObjectResult>(await player.Spin(new(GameId, 2, false, false)));
        Assert.Equal(0, await WalletCents(player));
        Assert.Empty((await player.Database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
        Assert.Empty((await player.Database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task InsufficientBoostPointsCannotConsumeTheAwardedFreeSpin()
    {
        var player = await Player(emulator.Database(), "insufficient-boost");
        await SeedBank(player, 3, 2, V2);
        Assert.IsType<ConflictObjectResult>(await player.Spin(new(GameId, 3, true, true)));
        Assert.Equal(2, (await FreeGames(player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(100_000, await WalletCents(player));
        Assert.Empty((await player.Database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
        Assert.Empty((await player.Database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task MissingFreeSpinCannotChargeCashOrCreateAnAdmission()
    {
        var player = await Player(emulator.Database(), "no-free-spin");
        Assert.IsType<ConflictObjectResult>(await player.Spin(new(GameId, 2, true, false)));
        Assert.Equal(100_000, await WalletCents(player));
        Assert.Empty((await player.Database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
        Assert.Empty((await player.Database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task AcceptedBoostUsesTheBoostedStripAndSettlesItsPointCostAndCashExactlyOnce()
    {
        var player = await Player(emulator.Database(), "valid-boost", boosted: true);
        var cost = player.Engine.GetSpecialBoostCost(GameId);
        await SpecialPoints(player).SetAsync(new Dictionary<string, object> { ["available"] = cost + 11L });
        var response = Success(await player.Spin(new(GameId, 2, false, true)));
        Assert.True(response.SpecialBoostApplied);
        Assert.EndsWith("-power-boost", response.ReelSetId);
        Assert.Equal(V2, response.PaytableId);
        Assert.Equal(11 + response.SpecialPointsAwarded, response.SpecialPointsBalance);
        Assert.Equal(100_000 - 50 + response.Payout.TotalPoints * 25, await WalletCents(player));
        var uses = (await player.Database.Collection("balanceTransactions").WhereEqualTo("type", "special-boost-use").GetSnapshotAsync()).Documents;
        var use = Assert.Single(uses);
        Assert.Equal(-cost, use.GetValue<long>("amount"));
        Assert.Equal(11, use.GetValue<long>("balanceAfter"));
        await AssertSavedAudit(player.Database, response, derived: false);
    }

    [Fact]
    public async Task WeightedPaidCollectionCreatesThreePointBankAndConsumesItWithoutChargingCash()
    {
        var player = await Player(emulator.Database(), "weighted-carry", featureRandom: new OneSyncSeal());
        await player.Guard.SetAsync(new Dictionary<string, object>
        {
            ["userId"] = player.UserId,
            ["sealCounts"] = Modes(29),
            // Fifteen units at2points, fourteen at4; the trigger adds one4-point
            // gem:90points/30gems =3points, exactlyR0.75.
            ["sealWagerCents"] = Modes((15 * 2 + 14 * 4) * 25)
        });
        var trigger = Success(await player.Spin(new(GameId, 4, false, false)));
        Assert.Equal(10, trigger.FreeSpinsRemaining);
        Assert.Equal(3, trigger.FreeSpinWagerPoints);
        Assert.Equal(10, trigger.BonusRound!.TotalSpins);
        Assert.Equal(3, trigger.BonusRound.WagerPoints);
        Assert.Equal(V2, trigger.BonusRound.MathModelId);
        Assert.Equal(75, (await player.Guard.GetSnapshotAsync()).GetValue<long>("freeSpinWagerCents"));
        var before = await WalletCents(player);
        await ReadyForNextRequest(player);
        var bonus = Success(await player.Spin(new(GameId, 3, true, false)));
        Assert.True(bonus.IsFreeSpin);
        Assert.Equal(3, bonus.WagerPoints);
        Assert.Equal(V2, bonus.PaytableId);
        Assert.Equal(before + bonus.Payout.TotalPoints * 25, await WalletCents(player));
        Assert.False((await player.Database.Collection("balanceTransactions").Document(bonus.SpinId.ToString("N") + "-wager").GetSnapshotAsync()).Exists);
        await AssertSavedAudit(player.Database, bonus, derived: true);
    }

    [Fact]
    public async Task TriggeredRoundQueuesBehindTheActiveRoundWithoutRepricingItsRemainingSpin()
    {
        var player = await Player(emulator.Database(), "queued-round", featureRandom: new OneSyncSeal());
        await SeedBank(player, 4, 2, V2);
        await player.Guard.UpdateAsync(new Dictionary<string, object>
        {
            ["sealCounts"] = Modes(29), ["sealWagerCents"] = Modes((15 * 2 + 14 * 4) * 25)
        });
        var first = Success(await player.Spin(new(GameId, 4, true, false)));
        Assert.Equal(1, first.FreeSpinsRemaining);
        Assert.Equal(4, first.FreeSpinWagerPoints);
        Assert.Single(first.QueuedBonusRounds);
        Assert.Equal(3, first.QueuedBonusRounds[0].WagerPoints);
        Assert.Equal(10, first.QueuedBonusRounds[0].TotalSpins);
        Assert.Equal(10, first.QueuedBonusRounds[0].RemainingSpins);
        Assert.Equal(V2, first.QueuedBonusRounds[0].MathModelId);
        var queuedId = first.QueuedBonusRounds[0].Id;
        var reconnected = Assert.IsType<SlotStateResponse>(Assert.IsType<OkObjectResult>(await player.State()).Value);
        Assert.Equal(1, reconnected.FreeSpinsRemaining);
        Assert.Equal(4, reconnected.FreeSpinWagerPoints);
        Assert.Equal(queuedId, Assert.Single(reconnected.QueuedBonusRounds).Id);
        await ReadyForNextRequest(player);
        var lastOldSpin = Success(await player.Spin(new(GameId, 4, true, false)));
        Assert.Equal(4, lastOldSpin.WagerPoints);
        Assert.Equal(10, lastOldSpin.FreeSpinsRemaining);
        Assert.Equal(3, lastOldSpin.FreeSpinWagerPoints);
        Assert.Equal(queuedId, lastOldSpin.BonusRound!.Id);
        Assert.Empty(lastOldSpin.QueuedBonusRounds);
        await ReadyForNextRequest(player);
        var firstQueuedSpin = Success(await player.Spin(new(GameId, 3, true, false)));
        Assert.Equal(3, firstQueuedSpin.WagerPoints);
        Assert.Equal(9, firstQueuedSpin.FreeSpinsRemaining);
        Assert.Equal(V2, firstQueuedSpin.PaytableId);
        Assert.Equal(queuedId, firstQueuedSpin.BonusRound!.Id);
        Assert.Equal(10, firstQueuedSpin.BonusRound.TotalSpins);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2001)]
    public async Task InvalidStoredCarryIsRejectedBeforeFreeSpinOrBoostIsConsumed(long carry)
    {
        var player = await Player(emulator.Database(), "bad-carry-" + carry);
        await SeedBank(player, carry, 2, V2);
        await SpecialPoints(player).SetAsync(new Dictionary<string, object> { ["available"] = 100L });
        Assert.IsType<BadRequestObjectResult>(await player.Spin(new(GameId, 2, true, true)));
        Assert.Equal(2, (await FreeGames(player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(100, (await SpecialPoints(player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(100_000, await WalletCents(player));
        Assert.Empty((await player.Database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task UnknownStoredModelIsRejectedBeforeFreeSpinIsConsumed()
    {
        var player = await Player(emulator.Database(), "bad-model");
        await SeedBank(player, 3, 2, "unrecognized-midnight-model");
        Assert.Equal(500, Assert.IsType<ObjectResult>(await player.Spin(new(GameId, 2, true, false))).StatusCode);
        Assert.Equal(2, (await FreeGames(player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Empty((await player.Database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task UnpricedLegacyFreeSpinCannotUseAnOddClientSuppliedFallback()
    {
        var player = await Player(emulator.Database(), "unpriced-bank");
        await FreeGames(player).SetAsync(new Dictionary<string, object> { ["available"] = 1L });
        Assert.IsType<BadRequestObjectResult>(await player.Spin(new(GameId, 3, true, false)));
        Assert.Equal(1, (await FreeGames(player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Empty((await player.Database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task PaidRequestCannotDiscardAnOutstandingBonusPool()
    {
        var player = await Player(emulator.Database(), "preserve-bank");
        await SeedBank(player, 3, 2, V2);
        Assert.IsType<BadRequestObjectResult>(await player.Spin(new(GameId, 2, false, false)));
        Assert.Equal(2, (await FreeGames(player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(100_000, await WalletCents(player));
        Assert.Empty((await player.Database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task GenuineUnversionedV1BankSurvivesReloadAndDrainsBeforeNewV2CashPlay()
    {
        var player = await Player(emulator.Database(), "v1-bonus");
        await RestoreFrozenV1Bank(player);
        Assert.False((await player.Guard.GetSnapshotAsync()).ContainsField("freeSpinMathModelId"));
        var state = Assert.IsType<SlotStateResponse>(Assert.IsType<OkObjectResult>(await player.State()).Value);
        Assert.Equal(6, state.FreeSpinsRemaining);
        Assert.Equal(4, state.FreeSpinWagerPoints);
        Assert.Equal(6, state.BonusRound!.TotalSpins);
        Assert.Equal(V1, state.BonusRound.MathModelId);
        for (var remaining = 5; remaining >= 0; remaining--)
        {
            await ReadyForNextRequest(player);
            var free = Success(await player.Spin(new(GameId, 4, true, false)));
            Assert.Equal(V1, free.PaytableId);
            Assert.True(free.IsFreeSpin);
            Assert.Equal(remaining, free.FreeSpinsRemaining);
            if (remaining > 0) Assert.Equal(6, free.BonusRound!.TotalSpins);
            await AssertSavedAudit(player.Database, free, derived: true);
        }
        await ReadyForNextRequest(player);
        var cash = Success(await player.Spin(new(GameId, 2, false, false)));
        Assert.Equal(V2, cash.PaytableId);
        Assert.False(cash.IsFreeSpin);
    }

    [Theory]
    [InlineData(V1, 6)]
    [InlineData(V2, 10)]
    public async Task RetriggeredFreeSpinsKeepTheAwardingPoolsMathModel(string model, int awardedSpins)
    {
        var player = await Player(emulator.Database(), "retrigger-" + model[^2..], scatter: true);
        await SeedBank(player, 4, 1, model);
        var response = Success(await player.Spin(new(GameId, 4, true, false)));
        Assert.Equal(model, response.PaytableId);
        Assert.Equal(awardedSpins, response.FreeSpinsRemaining);
        Assert.Equal(awardedSpins, response.BonusRound!.TotalSpins);
        Assert.Equal(model, (await player.Guard.GetSnapshotAsync()).GetValue<string>("freeSpinMathModelId"));
        Assert.Equal(4, response.FreeSpinWagerPoints);
    }

    [Fact]
    public async Task SameAdmissionAndOutcomeSettleOnceAndTamperedModelCannotSettle()
    {
        var player = await Player(emulator.Database(), "idempotent");
        var admission = await player.Store.BeginSlotSpinAsync(player.UserId, GameId, 2, 25, false, false, 0, DateTime.UtcNow, TimeSpan.Zero, default);
        var result = player.Engine.SpinAdmitted(GameId, admission, player.UserId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => player.Store.RecordSlotSpinAsync(player.UserId, result,
            admission with { MathModelId = V1 }, DateTime.UtcNow, default));
        Assert.Empty((await player.Database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        var first = await player.Store.RecordSlotSpinAsync(player.UserId, result, admission, DateTime.UtcNow, default);
        var second = await player.Store.RecordSlotSpinAsync(player.UserId, result, admission, DateTime.UtcNow, default);
        Assert.Equal(first.SlotsCreditsBalance, second.SlotsCreditsBalance);
        AssertPayoutEqual(first.Payout, second.Payout);
        Assert.Single((await player.Database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Equal(2, (await player.Database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task SettledAdmissionRejectsAnAlteredPayoutForTheSameSpinWithoutFinancialWrites()
    {
        var player = await Player(emulator.Database(), "retry-altered-payout");
        var admission = await CashAdmission(player, 2);
        var result = player.Engine.SpinAdmitted(GameId, admission, player.UserId);
        await player.Store.RecordSlotSpinAsync(player.UserId, result, admission, DateTime.UtcNow, default);
        var versions = await FinancialVersions(player.Database);
        var changed = result with { Payout = result.Payout with { TotalPoints = result.Payout.TotalPoints + 1 } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => player.Store.RecordSlotSpinAsync(player.UserId,
            changed, admission, DateTime.UtcNow, default));
        Assert.Equal(versions, await FinancialVersions(player.Database));
    }

    [Fact]
    public async Task OriginalRetryAfterLaterCashPlayReturnsItsOriginalWalletSnapshotWithoutFinancialWrites()
    {
        var player = await Player(emulator.Database(), "retry-after-cash");
        var admission = await CashAdmission(player, 2);
        var result = player.Engine.SpinAdmitted(GameId, admission, player.UserId);
        var first = await player.Store.RecordSlotSpinAsync(player.UserId, result, admission, DateTime.UtcNow, default);
        await ReadyForNextRequest(player);
        var later = Success(await player.Spin(new(GameId, 4, false, false)));
        Assert.NotEqual(first.SlotsCreditsBalance, later.SlotsCreditsBalance);
        var versions = await FinancialVersions(player.Database);
        var retry = await player.Store.RecordSlotSpinAsync(player.UserId, result, admission, DateTime.UtcNow, default);
        Assert.Equal(first.SlotsCreditsBalance, retry.SlotsCreditsBalance);
        Assert.Equal(first.FreeSpinsRemaining, retry.FreeSpinsRemaining);
        AssertPayoutEqual(first.Payout, retry.Payout);
        Assert.Equal(versions, await FinancialVersions(player.Database));
        Assert.Equal(later.SlotsCreditsBalance, (await WalletCents(player)) / 100m);
    }

    [Fact]
    public async Task OriginalTriggerRetryAfterLaterBonusPlayReturnsItsOriginalQueueAndAwardSnapshot()
    {
        var player = await Player(emulator.Database(), "retry-after-bonus", scatter: true);
        var admission = await CashAdmission(player, 4);
        var result = player.Engine.SpinAdmitted(GameId, admission, player.UserId);
        var first = await player.Store.RecordSlotSpinAsync(player.UserId, result, admission, DateTime.UtcNow, default);
        Assert.Equal(10, first.FreeSpinsRemaining);
        Assert.Empty(first.QueuedBonusRounds);
        Assert.Single(first.AwardedBonusRounds);
        await ReadyForNextRequest(player);
        var later = Success(await player.Spin(new(GameId, 4, true, false)));
        Assert.Equal(9, later.FreeSpinsRemaining);
        Assert.Single(later.QueuedBonusRounds);
        var versions = await FinancialVersions(player.Database);
        var retry = await player.Store.RecordSlotSpinAsync(player.UserId, result, admission, DateTime.UtcNow, default);
        Assert.Equal(first.SlotsCreditsBalance, retry.SlotsCreditsBalance);
        Assert.Equal(first.FreeSpinsRemaining, retry.FreeSpinsRemaining);
        Assert.Equal(first.BonusRound, retry.BonusRound);
        Assert.Equal(first.QueuedBonusRounds, retry.QueuedBonusRounds);
        Assert.Equal(first.AwardedBonusRounds, retry.AwardedBonusRounds);
        Assert.Equal(versions, await FinancialVersions(player.Database));
    }

    [Theory]
    [InlineData("wagerPoints", 3.5)]
    [InlineData("remainingSpins", 1.5)]
    [InlineData("totalSpins", 9.5)]
    public async Task FractionalStoredRoundFieldsFailBeforeAnyCreditIsConsumed(string field, double fractional)
    {
        var player = await Player(emulator.Database(), "fractional-" + field);
        await SeedBank(player, 3, 2, V2);
        var rounds = (await player.Guard.GetSnapshotAsync()).GetValue<List<Dictionary<string, object>>>("midnightBonusRounds");
        rounds[0][field] = fractional;
        await player.Guard.UpdateAsync("midnightBonusRounds", rounds);
        var versions = await FinancialVersions(player.Database);
        Assert.Equal(500, Assert.IsType<ObjectResult>(await player.Spin(new(GameId, 3, true, false))).StatusCode);
        Assert.Equal(versions, await FinancialVersions(player.Database));
        Assert.Equal(2, (await FreeGames(player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Empty((await player.Database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StoredRoundModeCannotBeUpgradedByChangingAdmissionOrEngineAudit(bool changeAdmission)
    {
        var player = await Player(emulator.Database(), changeAdmission ? "changed-admission-mode" : "changed-audit-mode");
        await SeedBank(player, 3, 1, V2);
        var admission = await player.Store.BeginSlotSpinAsync(player.UserId, GameId, 3, 25, true, false, 0,
            DateTime.UtcNow, TimeSpan.Zero, default);
        Assert.Null(admission.BonusRound!.FeatureMode);
        var changed = changeAdmission ? admission with { FreeSpinFeatureMode = "rows-paw" } : admission;
        var result = player.Engine.SpinAdmitted(GameId, changed, player.UserId);
        if (!changeAdmission)
            result = result with { EngineAudit = result.EngineAudit! with { FreeSpinFeatureMode = "rows-paw" } };
        var versions = await FinancialVersions(player.Database);
        await Assert.ThrowsAsync<InvalidOperationException>(() => player.Store.RecordSlotSpinAsync(player.UserId,
            result, changed, DateTime.UtcNow, default));
        Assert.Equal(versions, await FinancialVersions(player.Database));
        Assert.Empty((await player.Database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task RetryingAFreeSpinSettlementCannotAppendItsTriggeredRoundTwice()
    {
        var player = await Player(emulator.Database(), "retry-trigger", scatter: true);
        await SeedBank(player, 3, 1, V2);
        var admission = await player.Store.BeginSlotSpinAsync(player.UserId, GameId, 3, 25, true, false, 0,
            DateTime.UtcNow, TimeSpan.Zero, default);
        var result = player.Engine.SpinAdmitted(GameId, admission, player.UserId);
        var first = await player.Store.RecordSlotSpinAsync(player.UserId, result, admission, DateTime.UtcNow, default);
        var second = await player.Store.RecordSlotSpinAsync(player.UserId, result, admission, DateTime.UtcNow, default);
        Assert.Equal(10, first.FreeSpinsRemaining);
        Assert.Equal(10, second.FreeSpinsRemaining);
        Assert.Equal(first.BonusRound, second.BonusRound);
        Assert.Equal(10, (await FreeGames(player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Single((await player.Guard.GetSnapshotAsync()).GetValue<List<Dictionary<string, object>>>("midnightBonusRounds"));
        Assert.Single((await player.Database.Collection("balanceTransactions").WhereEqualTo("type", "free-game-use").GetSnapshotAsync()).Documents);
        Assert.Single((await player.Database.Collection("balanceTransactions").WhereEqualTo("type", "free-game-award").GetSnapshotAsync()).Documents);
        Assert.Single((await player.Database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task RequestAbortedAfterAdmissionStillPersistsOneCompletePaidSettlement()
    {
        using var aborted = new CancellationTokenSource();
        var player = await Player(emulator.Database(), "aborted-request", onGenerate: aborted.Cancel);
        var response = Success(await player.Spin(new(GameId, 2, false, false), aborted.Token));
        Assert.True(aborted.IsCancellationRequested);
        Assert.Equal(100_000 - 50 + response.Payout.TotalPoints * 25, await WalletCents(player));
        var admission = Assert.Single((await player.Database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
        Assert.Equal("settled", admission.GetValue<string>("status"));
        Assert.Single((await player.Database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        await AssertSavedAudit(player.Database, response, derived: false);
    }

    [Fact]
    public async Task InterruptedBeforeGenerationLeavesDurableAdmissionAndConsumptionReceiptsForReconciliation()
    {
        var player = await Player(emulator.Database(), "admission-only");
        await SeedBank(player, 3, 2, V2);
        var cost = player.Engine.GetSpecialBoostCost(GameId);
        await SpecialPoints(player).SetAsync(new Dictionary<string, object> { ["available"] = cost + 5L });
        var admission = await player.Store.BeginSlotSpinAsync(player.UserId, GameId, 3, 25, true, true, cost,
            DateTime.UtcNow, TimeSpan.FromMilliseconds(500), default);
        // Deliberately do not generate or settle an outcome. This is the durable
        // evidence available if the process dies at this exact boundary.
        var receipt = await player.Database.Collection("slotSpinAdmissions").Document(admission.AdmissionId.ToString("N")).GetSnapshotAsync();
        Assert.Equal("admitted", receipt.GetValue<string>("status"));
        Assert.Equal(2, receipt.GetValue<long>("freeSpinsBefore"));
        Assert.Equal(cost + 5, receipt.GetValue<long>("specialPointsBefore"));
        Assert.Equal(V2, receipt.GetValue<string>("mathModelId"));
        foreach (var field in new[] { "freeGameUseTransactionId", "specialPointUseTransactionId" })
            Assert.True((await player.Database.Collection("balanceTransactions").Document(receipt.GetValue<string>(field)).GetSnapshotAsync()).Exists);
        Assert.Equal(1, (await FreeGames(player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(5, (await SpecialPoints(player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(100_000, await WalletCents(player));
        Assert.Empty((await player.Database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Equal(admission.AdmissionId.ToString("N"), (await player.Guard.GetSnapshotAsync()).GetValue<string>("activeAdmissionId"));
        var blocked = await player.Store.BeginSlotSpinAsync(player.UserId, GameId, 3, 25, true, true, cost,
            DateTime.UtcNow, TimeSpan.FromMilliseconds(500), default);
        Assert.NotNull(blocked.CooldownRemaining);
        Assert.Equal(1, (await FreeGames(player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(5, (await SpecialPoints(player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(2, (await player.Database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task ExpiredInterruptedAdmissionRestoresFreeSpinAndBoostOnceAndRejectsItsLateOutcome()
    {
        var player = await Player(emulator.Database(), "expired-admission");
        await SeedBank(player, 3, 2, V2);
        var cost = player.Engine.GetSpecialBoostCost(GameId);
        await SpecialPoints(player).SetAsync(new Dictionary<string, object> { ["available"] = cost + 5L });
        var old = await player.Store.BeginSlotSpinAsync(player.UserId, GameId, 3, 25, true, true, cost,
            DateTime.UtcNow, TimeSpan.FromMilliseconds(500), default);
        var lateResult = player.Engine.SpinAdmitted(GameId, old, player.UserId);
        var next = await player.Store.BeginSlotSpinAsync(player.UserId, GameId, 3, 25, true, true, cost,
            old.StartedAtUtc.AddSeconds(31), TimeSpan.FromMilliseconds(500), default);
        Assert.Null(next.CooldownRemaining);
        var admissions = player.Database.Collection("slotSpinAdmissions");
        Assert.Equal("recovered", (await admissions.Document(old.AdmissionId.ToString("N")).GetSnapshotAsync()).GetValue<string>("status"));
        var nextReceipt = await admissions.Document(next.AdmissionId.ToString("N")).GetSnapshotAsync();
        Assert.Equal(2, nextReceipt.GetValue<long>("freeSpinsBefore"));
        Assert.Equal(cost + 5, nextReceipt.GetValue<long>("specialPointsBefore"));
        Assert.Equal(1, (await FreeGames(player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(5, (await SpecialPoints(player).GetSnapshotAsync()).GetValue<long>("available"));
        var ledger = player.Database.Collection("balanceTransactions");
        var freeRefund = await ledger.Document(old.AdmissionId.ToString("N") + "-free-game-refund").GetSnapshotAsync();
        var boostRefund = await ledger.Document(old.AdmissionId.ToString("N") + "-special-boost-refund").GetSnapshotAsync();
        Assert.Equal(1, freeRefund.GetValue<long>("amount"));
        Assert.Equal(2, freeRefund.GetValue<long>("balanceAfter"));
        Assert.Equal(cost, boostRefund.GetValue<long>("amount"));
        Assert.Equal(cost + 5, boostRefund.GetValue<long>("balanceAfter"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => player.Store.RecordSlotSpinAsync(player.UserId,
            lateResult, old, DateTime.UtcNow, default));
        var blocked = await player.Store.BeginSlotSpinAsync(player.UserId, GameId, 3, 25, true, true, cost,
            next.StartedAtUtc.AddMilliseconds(1), TimeSpan.FromMilliseconds(500), default);
        Assert.NotNull(blocked.CooldownRemaining);
        Assert.Single((await ledger.WhereEqualTo("type", "free-game-refund").GetSnapshotAsync()).Documents);
        Assert.Single((await ledger.WhereEqualTo("type", "special-boost-refund").GetSnapshotAsync()).Documents);
        Assert.Empty((await player.Database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Equal(100_000, await WalletCents(player));
    }

    [Fact]
    public async Task ChangedPaidFreeSpinMetadataIsDetectedDuringFrozenReplay()
    {
        var player = await Player(emulator.Database(), "tampered-metadata");
        await SeedBank(player, 3, 1, V2);
        var response = Success(await player.Spin(new(GameId, 3, true, false)));
        var detail = await Audit(player.Database, response);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(detail.Summary, detail.PayloadJson!));
        var changed = JsonNode.Parse(detail.PayloadJson!)!;
        changed["data"]!["isFreeSpin"] = false;
        Assert.Contains(SlotEvidenceReplayAudit.Inspect(detail.Summary, changed.ToJsonString()), finding => finding.Severity == "error");
    }

    [Fact]
    public async Task ActualCooldownRejectsASecondRequestWithoutDuplicatingACharge()
    {
        var player = await Player(emulator.Database(), "cooldown");
        Success(await player.Spin(new(GameId, 2, false, false)));
        var before = await WalletCents(player);
        await player.Guard.UpdateAsync("lastSpinStartedAt", Timestamp.FromDateTime(DateTime.UtcNow));
        Assert.Equal(429, Assert.IsType<ObjectResult>(await player.Spin(new(GameId, 2, false, false))).StatusCode);
        Assert.Equal(before, await WalletCents(player));
        Assert.Single((await player.Database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
    }

    private static OptionsSlotsDefinitionProvider Provider()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        return new(Options.Create(document.RootElement.GetProperty("Slots").Deserialize<SlotsOptions>(Json)!));
    }

    private static async Task<PaidPlayer> Player(FirestoreDb database, string userId, bool scatter = false,
        long initialRand = 1000, IRandomIndexSource? featureRandom = null, Action? onGenerate = null, bool boosted = false,
        int[]? stopsOverride = null)
    {
        var provider = Provider();
        var store = new FirestoreAccountStore(database);
        Assert.NotNull((await store.CreateAsync(userId, userId, userId.ToUpperInvariant(), userId + "@example.test", "unused-local-test", "active", default)).Value);
        await database.Collection("userBalances").Document(userId + "_slotsCredits").UpdateAsync(new Dictionary<string, object>
        { ["available"] = initialRand, ["availableFractionalCents"] = 0L });
        var token = Hash("local-midnight-" + userId);
        await store.CreateSessionAsync(Hash(token), userId, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), null, default);
        var accessor = new HttpContextAccessor();
        var accountService = new AccountService(store, null!, null!, accessor);
        var game = provider.GetGame(GameId)!;
        var reels = provider.GetReelSet(game.Math.ReelSetId)!;
        if (boosted) reels = SpecialPointBonus.CreateBoostedReelSet(game, reels);
        var stops = stopsOverride ?? reels.Reels.Select((strip, index) => strip.IndexOf(scatter && index < 3 ? "FREE" : "2")).ToArray();
        Assert.All(stops, stop => Assert.True(stop >= 0));
        var engine = new SpinService(provider, new CryptoReelGenerator(new FixedStops(stops, onGenerate)), new CombinationEvaluator(), new PayoutCalculator(), featureRandom ?? new QuietFeatures());
        return new(database, userId, token, accessor, store, engine,
            new SlotsController(engine, accountService, NullLogger<SlotsController>.Instance));
    }

    private sealed record PaidPlayer(FirestoreDb Database, string UserId, string Token, HttpContextAccessor Accessor,
        FirestoreAccountStore Store, SpinService Engine, SlotsController Controller)
    {
        public DocumentReference Guard => Database.Collection("slotSpinGuards").Document(UserId + "_" + Hash(GameId));
        private void NewRequest()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Cookie = AccountSessionCookie.Name + "=" + Token;
            Accessor.HttpContext = context;
            Controller.ControllerContext = new ControllerContext { HttpContext = context };
        }
        public Task<ActionResult> Spin(SpinRequest request, CancellationToken cancellationToken = default)
        { NewRequest(); return Controller.Spin(request, cancellationToken); }
        public Task<ActionResult> State() { NewRequest(); return Controller.State(GameId, default); }
    }

    private static SpinResult Success(ActionResult result) => Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(result).Value);
    private static void AssertPayoutEqual(SpinPayout expected, SpinPayout actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected, Json), JsonSerializer.Serialize(actual, Json));
    private static Task<SlotSpinAdmission> CashAdmission(PaidPlayer player, long stake) =>
        player.Store.BeginSlotSpinAsync(player.UserId, GameId, stake, 25, false, false, 0,
            DateTime.UtcNow, TimeSpan.Zero, default);
    private static async Task<string> FinancialVersions(FirestoreDb database)
    {
        var versions = new List<object>();
        foreach (var collection in new[] { "userBalances", "slotSpinGuards", "slotSpinAdmissions", "slotSpinResults", "balanceTransactions",
            RoundAuditDocuments.Summaries, RoundAuditDocuments.Evidence })
        foreach (var document in (await database.Collection(collection).GetSnapshotAsync()).Documents.OrderBy(document => document.Id, StringComparer.Ordinal))
            versions.Add(new { Path = document.Reference.Path, Revision = document.UpdateTime.ToString() });
        return JsonSerializer.Serialize(versions, Json);
    }
    private static int[] FindLosingStops()
    {
        var provider = Provider();
        var engine = new SpinService(provider, new CryptoReelGenerator(new SeededStops(9827)),
            new CombinationEvaluator(), new PayoutCalculator(), new QuietFeatures());
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var result = engine.Spin(GameId, 2, "paid-loss-window-search", false, persistedPityMisses: 0);
            if (result.Payout.TotalPoints == 0 && result.FreeSpinsAwarded == 0)
                return result.ReelStops.ToArray();
        }
        throw new InvalidOperationException("No deterministic losing window was found in the bounded real-engine search.");
    }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static DocumentReference FreeGames(PaidPlayer player) => player.Database.Collection("userBalances").Document(player.UserId + "_freeGames:" + GameId);
    private static DocumentReference SpecialPoints(PaidPlayer player) => player.Database.Collection("userBalances").Document(player.UserId + "_specialPoints:" + GameId);
    private static Dictionary<string, object> Modes(long sync) => new() { ["sync"] = sync, ["rows"] = 0L, ["paw"] = 0L, ["rand"] = 0L };
    private static async Task<long> WalletCents(PaidPlayer player)
    {
        var snapshot = await player.Database.Collection("userBalances").Document(player.UserId + "_slotsCredits").GetSnapshotAsync();
        return snapshot.GetValue<long>("available") * 100 + snapshot.GetValue<long>("availableFractionalCents");
    }
    private static Task ReadyForNextRequest(PaidPlayer player) => player.Guard.UpdateAsync("lastSpinStartedAt", Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(-1)));
    private static async Task SeedBank(PaidPlayer player, long carry, long count, string model)
    {
        await FreeGames(player).SetAsync(new Dictionary<string, object> { ["available"] = count });
        await player.Guard.SetAsync(new Dictionary<string, object>
        {
            ["userId"] = player.UserId, ["freeSpinWagerPoints"] = carry, ["freeSpinWagerCents"] = carry * 25,
            ["freeSpinFeatureMode"] = string.Empty, ["freeSpinMathModelId"] = model,
            ["sealCounts"] = Modes(0), ["sealWagerCents"] = Modes(0),
            ["midnightBonusRounds"] = new[] { new Dictionary<string, object>
            {
                ["id"] = player.UserId + ":fixture-round", ["totalSpins"] = model == V1 ? count : 10L,
                ["remainingSpins"] = count, ["wagerPoints"] = carry,
                ["featureMode"] = string.Empty, ["mathModelId"] = model
            } }
        });
    }
    private static async Task RestoreFrozenV1Bank(PaidPlayer player)
    {
        using var state = JsonDocument.Parse(File.ReadAllText(FixturePath("v1-pending-bonus-state.json")));
        foreach (var collection in state.RootElement.GetProperty("documents").EnumerateObject()
            .Where(collection => collection.Name is "userBalances" or "slotSpinGuards"))
        foreach (var document in collection.Value.EnumerateObject().Where(document => document.Name.StartsWith(player.UserId + "_", StringComparison.Ordinal)))
            await player.Database.Collection(collection.Name).Document(document.Name).SetAsync(ToMap(document.Value));
        await ReadyForNextRequest(player);
    }
    private static Dictionary<string, object> ToMap(JsonElement value) => value.EnumerateObject().ToDictionary(pair => pair.Name,
        pair => pair.Name is "updatedAt" or "lastSpinStartedAt"
            ? (object)Timestamp.FromDateTime(pair.Value.GetDateTime().ToUniversalTime()) : ToFirestore(pair.Value));
    private static object ToFirestore(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => ToMap(value), JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number => value.TryGetInt64(out var integer) ? (object)integer : value.GetDouble(),
        JsonValueKind.True => true, JsonValueKind.False => false,
        JsonValueKind.Array => value.EnumerateArray().Select(ToFirestore).ToArray(), _ => null!
    };
    private static string FixturePath(string name, [CallerFilePath] string sourcePath = "") =>
        Path.Combine(Path.GetDirectoryName(sourcePath)!, "__fixtures__", "midnight-vault-paid-v1", name);

    private static async Task<GameRoundAuditDetail> Audit(FirestoreDb database, SpinResult response)
    {
        var summary = Assert.Single((await database.Collection(RoundAuditDocuments.Summaries)
            .WhereEqualTo("roundId", response.SpinId.ToString("N")).GetSnapshotAsync()).Documents);
        var audits = new FirestoreGameRoundAuditStore(database, Options.Create(new AdminOperationsOptions
        { CursorSigningKey = "midnight-local-paid-evidence-only-signing-key" }));
        return (await audits.ReadAsync(summary.Id, default))!;
    }
    private static async Task AssertSavedAudit(FirestoreDb database, SpinResult response, bool derived)
    {
        var detail = await Audit(database, response);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(detail.Summary, detail.PayloadJson!));
        using var payload = JsonDocument.Parse(detail.PayloadJson!);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(derived, data.GetProperty("engineAudit").GetProperty("isDerivedFreeSpin").GetBoolean());
        Assert.Equal(derived, data.GetProperty("isFreeSpin").GetBoolean());
    }
    private sealed class FixedStops(int[] stops, Action? onGenerate) : IRandomIndexSource
    {
        private int next;
        public int Next(int maximumExclusive)
        {
            onGenerate?.Invoke();
            var value = stops[next++ % stops.Length];
            Assert.InRange(value, 0, maximumExclusive - 1);
            return value;
        }
    }
    private sealed class QuietFeatures : IRandomIndexSource
    { public int Next(int maximumExclusive) => maximumExclusive == 100 ? 47 : 0; }
    private sealed class SeededStops(int seed) : IRandomIndexSource
    { private readonly Random random = new(seed); public int Next(int maximumExclusive) => random.Next(maximumExclusive); }
    private sealed class OneSyncSeal : IRandomIndexSource
    {
        private int hundredRoll;
        public int Next(int maximumExclusive) => maximumExclusive switch
        { 100 => ++hundredRoll == 2 ? 0 : 47, 25 => 1, _ => 0 };
    }
}
