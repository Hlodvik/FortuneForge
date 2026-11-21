using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.SamuraiFortune.Server;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;
using Xunit;
using static FortuneForge.Games.SamuraiFortune.Tests.SamuraiTestSupport;

namespace FortuneForge.Games.SamuraiFortune.Tests;

public sealed class SamuraiRoundStoreTests
{
    [SamuraiEmulatorFact]
    public async Task LostWalletReplyResumesFrozenRoundWhilePausedAndWritesHistoryOnce()
    {
        var database = Connect();
        var wallet = new Wallet { LoseNextReply = true };
        await SeedBalance(database, "energy", 106);
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["sealCounts"] = new Dictionary<string, long> { ["sync"] = 26, ["rows"] = 2 },
            ["sealWagerCents"] = new Dictionary<string, long> { ["sync"] = 1_950, ["rows"] = 300 }
        });
        var request = new SamuraiFortuneRoundRequest("samurai-lost-response-001", 6, false);
        using var observed = GameSecurityContext.Push(new("samurai-request-1", Account, "private-samurai-session",
            "203.0.113.43", DateTime.UtcNow, "POST", SamuraiFortunePaidRouteContract.SpinPath, SamuraiFortunePaidRouteContract.AuditGameId,
            null, request.OperationId, null, null, "sanitized", "direct"));
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(database, wallet).SpinAsync(Account, request, true, default));
        var restored = Service(database, wallet, new NoDraws());
        var pending = await restored.ReadStateAsync(Account, default);
        Assert.Equal(request, pending.PendingOperation);
        Assert.Equal(106, pending.EnergyBalance);
        Assert.Equal(26, pending.SealCollections.Single(track => track.SealId == "sync").Count);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        var conflict = await Assert.ThrowsAsync<SamuraiFortuneRoundConflictException>(() => restored.SpinAsync(Account,
            request with { OperationId = "samurai-competing-request" }, true, default));
        Assert.Equal(request, conflict.PendingRequest);
        await Assert.ThrowsAsync<SamuraiFortuneRoundConflictException>(() => restored.SpinAsync(Account,
            request with { WagerPoints = 8 }, true, default));

        var recovered = await restored.SpinAsync(Account, request, false, default);
        var round = Assert.IsType<SamuraiFortunePreparedRound>(recovered.Round);
        Assert.Equal(0, recovered.Features!.EnergyBalance);
        Assert.False(round.Progress.EnergySettlement.MultiplierApplied);
        Assert.Equal(7, recovered.Features.FreeSpinsRemaining);
        Assert.Equal(3, recovered.Features.FreeSpinWagerPoints);
        Assert.Equal("sync-paw", recovered.Features.FreeSpinFeatureMode);
        Assert.Equal(150, recovered.ChargedWagerCents);
        Assert.Equal(round.Outcome.SpinId.ToString("N"), wallet.Last!.OperationId);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        var exact = await restored.SpinAsync(Account, request, false, default);
        Assert.Equal(JsonSerializer.Serialize(SamuraiFortuneSpinResponse.From(recovered)), JsonSerializer.Serialize(SamuraiFortuneSpinResponse.From(exact)));
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var raw = Assert.Single((await database.Collection(SamuraiFortuneRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.DoesNotContain("private-samurai-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);
        Assert.Equal(SamuraiFortuneEngine.GameId, (await Guard(database).GetSnapshotAsync()).GetValue<string>(SamuraiFortuneRoundStateCodec.OwnerField));
    }

    [SamuraiEmulatorFact]
    public async Task PaidRoundCooldownAndPauseDoNotAffectExactRetryOrTheNextPaidRound()
    {
        var database = Connect();
        var wallet = new Wallet();
        var clock = new RoundClock();
        var service = Service(database, wallet, clock: clock);
        var first = new SamuraiFortuneRoundRequest("samurai-first-cash-round", 6, false);
        await Assert.ThrowsAsync<SamuraiFortuneRoundPausedException>(() => service.SpinAsync(Account, first, false, default));
        Assert.Equal(0, wallet.Calls);
        var result = await service.SpinAsync(Account, first, true, default);
        Assert.NotNull(result.Features);
        Assert.Equal(1, wallet.Approvals);
        var next = first with { OperationId = "samurai-second-cash-round" };
        await Assert.ThrowsAsync<SamuraiFortuneRoundCooldownException>(() => service.SpinAsync(Account, next, true, default));
        await Assert.ThrowsAsync<SamuraiFortuneRoundConflictException>(() => service.SpinAsync(Account, first with { UseSpecialBoost = true }, true, default));
        Assert.Equal(result.Wallet, (await service.SpinAsync(Account, first, false, default)).Wallet);
        Assert.Equal(1, wallet.Calls);
        clock.Advance();
        Assert.NotNull((await service.SpinAsync(Account, next, true, default)).Features);
        Assert.Equal(2, wallet.Approvals);
    }

    [SamuraiEmulatorFact]
    public async Task LegacyAndNativeFreeBanksResumeAtExactOddWagerWithoutCashDebit()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2 };
        var clock = new RoundClock();
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 75L, ["freeSpinFeatureMode"] = "sync-paw",
            ["sealCounts"] = new Dictionary<string, long> { ["sync"] = 26 },
            ["sealWagerCents"] = new Dictionary<string, long> { ["sync"] = 1_950 }
        });
        var service = Service(database, wallet, clock: clock);
        await Assert.ThrowsAsync<SamuraiFortuneFreeSpinsRequiredException>(() => service.SpinAsync(Account,
            new("samurai-legacy-block-cash", 6, false), true, default));
        await Assert.ThrowsAsync<SamuraiFortuneFreeWagerLockedException>(() => service.SpinAsync(Account,
            new("samurai-legacy-wrong-wager", 6, true), true, default));
        Assert.Equal(0, wallet.Calls);
        var first = await service.SpinAsync(Account, new("samurai-first-legacy-free", 3, true), true, default);
        Assert.Equal(8, first.Features!.FreeSpinsRemaining);
        Assert.Equal(3, first.Features.FreeSpinWagerPoints);
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.WagerCents);
        clock.Advance();
        var second = await service.SpinAsync(Account, new("samurai-second-legacy-free", 3, true), true, default);
        Assert.Equal(7, second.Features!.FreeSpinsRemaining);
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        clock.Advance();
        var native = await service.SpinAsync(Account, new("samurai-first-native-free", 3, true), true, default);
        Assert.Equal(6, native.Features!.FreeSpinsRemaining);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
        Assert.Equal(0, native.ChargedWagerCents);
        Assert.Equal(3, native.Round!.Outcome.WagerPoints);
        Assert.Equal("sync-paw", native.Round.Outcome.Audit.FeatureMode);
    }

    [SamuraiEmulatorFact]
    public async Task LegacyCountRaceDeclinesAndPreservesUnpinnedBankForRefresh()
    {
        var database = Connect();
        var wallet = new Wallet { ChangeBeforeSettle = 2 };
        var clock = new RoundClock();
        var request = new SamuraiFortuneRoundRequest("samurai-stale-legacy-count", 6, false);
        var service = Service(database, wallet, clock: clock);
        var declined = await service.SpinAsync(Account, request, true, default);
        Assert.True(declined.Wallet.LegacyFreeGamesCountMismatch);
        Assert.Null(declined.Round);
        Assert.Null(declined.Features);
        Assert.Equal(0, wallet.Approvals);
        Assert.False((await Guard(database).GetSnapshotAsync()).ContainsField(SamuraiFortuneRoundStateCodec.OwnerField));
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Equal(declined.Wallet, (await Service(database, wallet, new NoDraws()).SpinAsync(Account, request, false, default)).Wallet);
        var refreshed = await service.ReadStateAsync(Account, default);
        Assert.Equal(2, refreshed.FreeSpinsRemaining);
        Assert.Null(refreshed.FreeSpinWagerPoints);
        clock.Advance();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SpinAsync(Account,
            new("samurai-unpinned-odd-free", 3, true), true, default));
        var pinned = await service.SpinAsync(Account, new("samurai-pin-unpriced-free", 6, true), true, default);
        Assert.Equal(6, pinned.Features!.FreeSpinWagerPoints);
        Assert.Equal(1, wallet.LegacyRemaining);
    }

    [SamuraiEmulatorFact]
    public async Task DeclinedWalletDoesNotPublishAnOutcomeOrAdvanceCollections()
    {
        var database = Connect();
        var wallet = new Wallet { DeclineFunds = true };
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["sealCounts"] = new Dictionary<string, long> { ["sync"] = 26 },
            ["sealWagerCents"] = new Dictionary<string, long> { ["sync"] = 1_950 }
        });
        var service = Service(database, wallet);
        var request = new SamuraiFortuneRoundRequest("samurai-insufficient-money", 6, false);
        var declined = await service.SpinAsync(Account, request, true, default);
        Assert.True(declined.Wallet.InsufficientFunds);
        Assert.Null(declined.Features);
        Assert.Equal(26, (await service.ReadStateAsync(Account, default)).SealCollections.Single(track => track.SealId == "sync").Count);
        Assert.Equal(declined.Wallet, (await Service(database, wallet, new NoDraws()).SpinAsync(Account, request, false, default)).Wallet);
        Assert.Equal(1, wallet.Calls);
        Assert.Equal(0, wallet.Approvals);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
    }

    [SamuraiEmulatorFact]
    public async Task LegacyAdmissionAlreadyInFlightCannotBeClaimedByNewWorker()
    {
        var database = Connect();
        var wallet = new Wallet();
        await Guard(database).CreateAsync(new Dictionary<string, object> { [SlotSpinGuard.ActiveAdmissionField] = Guid.NewGuid().ToString("N") });
        await Assert.ThrowsAsync<FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException>(() => Service(database, wallet, new NoDraws()).SpinAsync(Account,
            new("samurai-existing-admission", 6, false), true, default));
        Assert.Equal(0, wallet.Calls);
        Assert.Empty((await database.Collection(SamuraiFortuneRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
    }

    [SamuraiEmulatorFact]
    public async Task PausedLegacyAdmissionRemainsDiscoverableForHostReconciliation()
    {
        var database = Connect();
        var wallet = new Wallet();
        await Guard(database).CreateAsync(new Dictionary<string, object> { [SlotSpinGuard.ActiveAdmissionField] = Guid.NewGuid().ToString("N") });
        await Assert.ThrowsAsync<FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException>(() => Service(database, wallet, new NoDraws()).SpinAsync(Account,
            new("samurai-existing-admission", 6, false), false, default));
        Assert.Equal(0, wallet.Calls);
        Assert.Empty((await database.Collection(SamuraiFortuneRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
    }
}
