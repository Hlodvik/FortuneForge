using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.NordicLegends.Server;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;
using Xunit;
using static FortuneForge.Games.NordicLegends.Tests.NordicLegendsTestSupport;

namespace FortuneForge.Games.NordicLegends.Tests;

public sealed class NordicLegendsRoundStoreTests
{
    [NordicLegendsEmulatorFact]
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
        var request = new NordicLegendsRoundRequest("nordic-legends-lost-response-001", 6, false);
        using var observed = GameSecurityContext.Push(new("nordic-legends-request-1", Account, "private-nordic-legends-session",
            "203.0.113.43", DateTime.UtcNow, "POST", NordicLegendsPaidRouteContract.SpinPath, NordicLegendsPaidRouteContract.AuditGameId,
            null, request.OperationId, null, null, "sanitized", "direct"));
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(database, wallet).SpinAsync(Account, request, true, default));
        var restored = Service(database, wallet, new NoDraws());
        var pending = await restored.ReadStateAsync(Account, default);
        Assert.Equal(request, pending.PendingOperation);
        Assert.Equal(106, pending.EnergyBalance);
        Assert.Empty(pending.SealCollections);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        var conflict = await Assert.ThrowsAsync<NordicLegendsRoundConflictException>(() => restored.SpinAsync(Account,
            request with { OperationId = "nordic-legends-competing-request" }, true, default));
        Assert.Equal(request, conflict.PendingRequest);
        await Assert.ThrowsAsync<NordicLegendsRoundConflictException>(() => restored.SpinAsync(Account,
            request with { WagerPoints = 8 }, true, default));

        var recovered = await restored.SpinAsync(Account, request, false, default);
        var round = Assert.IsType<NordicLegendsPreparedRound>(recovered.Round);
        Assert.Equal(0, recovered.Features!.EnergyBalance);
        Assert.False(NordicLegendsSpinResponse.From(recovered).EnergyMultiplierApplied);
        Assert.Equal(0, recovered.Features.FreeSpinsRemaining);
        Assert.Null(recovered.Features.FreeSpinWagerPoints);
        Assert.Null(recovered.Features.FreeSpinFeatureMode);
        Assert.Equal(150, recovered.ChargedWagerCents);
        Assert.Equal(round.Outcome.SpinId.ToString("N"), wallet.Last!.OperationId);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        var exact = await restored.SpinAsync(Account, request, false, default);
        Assert.Equal(JsonSerializer.Serialize(NordicLegendsSpinResponse.From(recovered)), JsonSerializer.Serialize(NordicLegendsSpinResponse.From(exact)));
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var raw = Assert.Single((await database.Collection(NordicLegendsRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.DoesNotContain("private-nordic-legends-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);
        Assert.Equal(NordicLegendsEngine.GameId, (await Guard(database).GetSnapshotAsync()).GetValue<string>(NordicLegendsRoundStateCodec.OwnerField));
    }

    [NordicLegendsEmulatorFact]
    public async Task PaidRoundCooldownAndPauseDoNotAffectExactRetryOrTheNextFreeRound()
    {
        var database = Connect();
        var wallet = new Wallet();
        var clock = new RoundClock();
        var service = Service(database, wallet, clock: clock);
        var first = new NordicLegendsRoundRequest("nordic-legends-first-cash-round", 6, false);
        await Assert.ThrowsAsync<NordicLegendsRoundPausedException>(() => service.SpinAsync(Account, first, false, default));
        Assert.Equal(0, wallet.Calls);
        var result = await service.SpinAsync(Account, first, true, default);
        Assert.NotNull(result.Features);
        Assert.Equal(1, wallet.Approvals);
        var next = first with { OperationId = "nordic-legends-second-cash-round" };
        await Assert.ThrowsAsync<NordicLegendsRoundCooldownException>(() => service.SpinAsync(Account, next, true, default));
        await Assert.ThrowsAsync<NordicLegendsRoundConflictException>(() => service.SpinAsync(Account, first with { UseSpecialBoost = true }, true, default));
        Assert.Equal(result.Wallet, (await service.SpinAsync(Account, first, false, default)).Wallet);
        Assert.Equal(1, wallet.Calls);
        clock.Advance();
        Assert.NotNull((await service.SpinAsync(Account, next, true, default)).Features);
        Assert.Equal(2, wallet.Approvals);
    }

    [NordicLegendsEmulatorFact]
    public async Task LegacyAndNativeFreeBanksResumeAtExactOddWagerWithoutCashDebit()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2 };
        var clock = new RoundClock();
        await Guard(database).CreateAsync(NordicLegendsRoundStateCodec.Storage.Write(Empty() with
        { LegacyRemaining = 2, NativeRemaining = 2, Carry = new(4, 3, "sync-paw") }));
        var service = Service(database, wallet, clock: clock);
        await Assert.ThrowsAsync<NordicLegendsFreeSpinsRequiredException>(() => service.SpinAsync(Account,
            new("nordic-legends-legacy-block-cash", 6, false), true, default));
        await Assert.ThrowsAsync<NordicLegendsFreeWagerLockedException>(() => service.SpinAsync(Account,
            new("nordic-legends-legacy-wrong-wager", 6, true), true, default));
        Assert.Equal(0, wallet.Calls);
        var first = await service.SpinAsync(Account, new("nordic-legends-first-legacy-free", 3, true), true, default);
        Assert.Equal(3, first.Features!.FreeSpinsRemaining);
        Assert.Equal(3, first.Features.FreeSpinWagerPoints);
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.WagerCents);
        clock.Advance();
        var second = await service.SpinAsync(Account, new("nordic-legends-second-legacy-free", 3, true), true, default);
        Assert.Equal(2, second.Features!.FreeSpinsRemaining);
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        clock.Advance();
        var native = await service.SpinAsync(Account, new("nordic-legends-first-native-free", 3, true), true, default);
        Assert.Equal(1, native.Features!.FreeSpinsRemaining);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
        Assert.Equal(0, native.ChargedWagerCents);
        Assert.Equal(3, native.Round!.Outcome.WagerPoints);
        Assert.Equal("sync-paw", native.Round.Outcome.Audit.FeatureMode);
    }

    [NordicLegendsEmulatorFact]
    public async Task LegacyCountRaceDeclinesAndPreservesUnpinnedBankForRefresh()
    {
        var database = Connect();
        var wallet = new Wallet { ChangeBeforeSettle = 2 };
        var clock = new RoundClock();
        var request = new NordicLegendsRoundRequest("nordic-legends-stale-legacy-count", 6, false);
        var service = Service(database, wallet, clock: clock);
        var declined = await service.SpinAsync(Account, request, true, default);
        Assert.True(declined.Wallet.LegacyFreeGamesCountMismatch);
        Assert.Null(declined.Round);
        Assert.Null(declined.Features);
        Assert.Equal(0, wallet.Approvals);
        Assert.False((await Guard(database).GetSnapshotAsync()).ContainsField(NordicLegendsRoundStateCodec.OwnerField));
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Equal(declined.Wallet, (await Service(database, wallet, new NoDraws()).SpinAsync(Account, request, false, default)).Wallet);
        var refreshed = await service.ReadStateAsync(Account, default);
        Assert.Equal(2, refreshed.FreeSpinsRemaining);
        Assert.Null(refreshed.FreeSpinWagerPoints);
        clock.Advance();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SpinAsync(Account,
            new("nordic-legends-unpinned-odd-free", 3, true), true, default));
        var pinned = await service.SpinAsync(Account, new("nordic-legends-pin-unpriced-free", 6, true), true, default);
        Assert.Equal(6, pinned.Features!.FreeSpinWagerPoints);
        Assert.Equal(1, wallet.LegacyRemaining);
    }

    [NordicLegendsEmulatorFact]
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
        var request = new NordicLegendsRoundRequest("nordic-legends-insufficient-money", 6, false);
        var declined = await service.SpinAsync(Account, request, true, default);
        Assert.True(declined.Wallet.InsufficientFunds);
        Assert.Null(declined.Features);
        Assert.Empty((await service.ReadStateAsync(Account, default)).SealCollections);
        Assert.Equal(declined.Wallet, (await Service(database, wallet, new NoDraws()).SpinAsync(Account, request, false, default)).Wallet);
        Assert.Equal(1, wallet.Calls);
        Assert.Equal(0, wallet.Approvals);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
    }

    [NordicLegendsEmulatorFact]
    public async Task LegacyAdmissionAlreadyInFlightCannotBeClaimedByNewWorker()
    {
        var database = Connect();
        var wallet = new Wallet();
        await Guard(database).CreateAsync(new Dictionary<string, object> { [SlotSpinGuard.ActiveAdmissionField] = Guid.NewGuid().ToString("N") });
        await Assert.ThrowsAsync<FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException>(() => Service(database, wallet, new NoDraws()).SpinAsync(Account,
            new("nordic-legends-existing-admission", 6, false), true, default));
        Assert.Equal(0, wallet.Calls);
        Assert.Empty((await database.Collection(NordicLegendsRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
    }

    [NordicLegendsEmulatorFact]
    public async Task PausedLegacyAdmissionRemainsDiscoverableForHostReconciliation()
    {
        var database = Connect();
        var wallet = new Wallet();
        await Guard(database).CreateAsync(new Dictionary<string, object> { [SlotSpinGuard.ActiveAdmissionField] = Guid.NewGuid().ToString("N") });
        await Assert.ThrowsAsync<FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException>(() => Service(database, wallet, new NoDraws()).SpinAsync(Account,
            new("nordic-legends-existing-admission", 6, false), false, default));
        Assert.Equal(0, wallet.Calls);
        Assert.Empty((await database.Collection(NordicLegendsRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
    }
}
