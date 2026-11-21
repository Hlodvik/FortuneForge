using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.ArcaneArchives.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Slots.Games;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

[Collection(MidnightVaultPaidEmulatorCollection.Name)]
public sealed class ArcanePreparedSpinTests(MidnightVaultPaidEmulatorFixture emulator)
{
    private const string UserId = "arcane-prepared-test";
    private const string GameId = ArcaneArchivesEngine.GameId;
    private const string OperationId = "test-arcane-operation-1";
    private const string OtherOperationId = "test-arcane-operation-2";
    private const long WagerPoints = 12;
    private const decimal PointValueInCents = 25m;

    [Fact]
    public async Task PendingCashRoundSurvivesTimeoutWalletDropAndRestartWithoutChangingOutcome()
    {
        var database = emulator.Database();
        var store = await CreatePlayer(database);
        var wallet = database.Collection("userBalances").Document($"{UserId}_slotsCredits");
        var originalRequest = new GameRequestObservation("first-request", UserId, null, null, DateTime.UtcNow,
            "POST", "/api/slots/spin", GameId, null, OperationId, null, null, "empty", "direct");
        await Assert.ThrowsAsync<SlotPreparedStartPausedException>(() =>
            Begin(store, DateTime.UtcNow, OperationId, Resolve, allowNewPrepared: false));
        Assert.Empty((await database.Collection("slotPreparedOperations").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
        var resolverCalls = 0;
        SlotSpinAdmission first;
        using (GameSecurityContext.Push(originalRequest))
        {
            first = await Begin(store, DateTime.UtcNow, OperationId, (admission, cancellationToken) =>
            {
                resolverCalls++;
                return Resolve(admission, cancellationToken);
            });
        }
        var outcome = Assert.IsType<SpinResult>(first.ResolvedOutcome);
        var admissionRef = database.Collection("slotSpinAdmissions").Document(first.AdmissionId.ToString("N"));
        var afterTimeout = DateTime.UtcNow.AddMinutes(2);
        var restarted = new FirestoreAccountStore(database);

        foreach (var competingKey in new string?[] { null, OtherOperationId })
        {
            var conflict = await Assert.ThrowsAsync<SlotPreparedOperationConflictException>(() =>
                Begin(restarted, afterTimeout, competingKey, Resolve, allowNewPrepared: false));
            Assert.Equal(new SlotPreparedOperationRequest(OperationId, WagerPoints, false, false), conflict.PendingOperation);
        }
        var changedWager = await Assert.ThrowsAsync<SlotPreparedOperationConflictException>(() =>
            Begin(restarted, afterTimeout, OperationId, Resolve, wagerPoints: WagerPoints + 4));
        Assert.Null(changedWager.PendingOperation);
        await Assert.ThrowsAsync<ArcaneTomeConflictException>(() =>
            new ArcaneFeatureStore(database).SelectTomePageAsync(UserId, "herbalism", false, afterTimeout, default));

        var restored = await Begin(restarted, afterTimeout, OperationId, allowNewPrepared: false);
        var restoredOutcome = Assert.IsType<SpinResult>(restored.ResolvedOutcome);
        Assert.Equal(1, resolverCalls);
        Assert.Equal(first.AdmissionId, restored.AdmissionId);
        Assert.Equal(first.StartedAtUtc, restored.StartedAtUtc);
        Assert.Equal(originalRequest, restored.PreparedRequestObservation);
        Assert.Equal(JsonSerializer.Serialize(outcome), JsonSerializer.Serialize(restoredOutcome));
        Assert.Equal(JsonSerializer.Serialize(outcome.EngineAudit), JsonSerializer.Serialize(restoredOutcome.EngineAudit));

        var tampered = await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.RecordSlotSpinAsync(UserId,
            restoredOutcome with { SpinId = Guid.NewGuid() }, restored, afterTimeout, default));
        Assert.Equal("The slot result differs from the prepared outcome.", tampered.Message);
        await wallet.UpdateAsync("available", 0L);
        await Assert.ThrowsAsync<InsufficientSlotCreditsException>(() =>
            restarted.RecordSlotSpinAsync(UserId, restoredOutcome, restored, afterTimeout, default));
        Assert.Equal("admitted", (await admissionRef.GetSnapshotAsync()).GetValue<string>("status"));
        Assert.Empty((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);

        await wallet.UpdateAsync("available", 20L);
        var settled = await restarted.RecordSlotSpinAsync(UserId, restoredOutcome, restored, afterTimeout, default);
        var replay = await Begin(restarted, afterTimeout.AddMinutes(1), OperationId, allowNewPrepared: false);
        var replaySettlement = await restarted.RecordSlotSpinAsync(UserId, Assert.IsType<SpinResult>(replay.ResolvedOutcome),
            replay, afterTimeout.AddMinutes(1), default);

        Assert.Equal(JsonSerializer.Serialize(settled), JsonSerializer.Serialize(replaySettlement));
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);
        Assert.Equal(1, resolverCalls);
    }

    [Fact]
    public async Task APreparedFreeSpinConsumesEntitlementOnceAndLeavesPermanentKeyAfterRetention()
    {
        var database = emulator.Database();
        var store = await CreatePlayer(database);
        var entitlement = database.Collection("userBalances").Document($"{UserId}_freeGames:{GameId}");
        await entitlement.SetAsync(new Dictionary<string, object> { ["available"] = 1L, ["version"] = 1L });
        var first = await Begin(store, DateTime.UtcNow, OperationId, Resolve, useFreeSpin: true);
        var restarted = new FirestoreAccountStore(database);
        var afterTimeout = DateTime.UtcNow.AddMinutes(2);
        var restored = await Begin(restarted, afterTimeout, OperationId, useFreeSpin: true);

        Assert.Equal(first.AdmissionId, restored.AdmissionId);
        Assert.Equal(0L, (await entitlement.GetSnapshotAsync()).GetValue<long>("available"));
        var debits = await database.Collection("balanceTransactions").WhereEqualTo("type", "free-game-use").GetSnapshotAsync();
        Assert.Single(debits.Documents);

        await restarted.RecordSlotSpinAsync(UserId, Assert.IsType<SpinResult>(restored.ResolvedOutcome), restored, afterTimeout, default);
        await database.Collection("slotSpinAdmissions").Document(restored.AdmissionId.ToString("N")).DeleteAsync();
        var expired = await Assert.ThrowsAsync<SlotPreparedOperationConflictException>(() =>
            Begin(restarted, afterTimeout.AddMinutes(1), OperationId, Resolve, useFreeSpin: true));

        Assert.Null(expired.PendingOperation);
        Assert.Empty((await database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("slotPreparedOperations").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
    }

    private static async Task<FirestoreAccountStore> CreatePlayer(FirestoreDb database)
    {
        var store = new FirestoreAccountStore(database);
        Assert.NotNull((await store.CreateAsync(UserId, UserId, UserId.ToUpperInvariant(),
            $"{UserId}@example.test", "local-test-password", "active", default)).Value);
        await database.Collection("userBalances").Document($"{UserId}_slotsCredits").UpdateAsync("available", 20L);
        return store;
    }

    private static Task<SlotSpinAdmission> Begin(FirestoreAccountStore store, DateTime time, string? operationId,
        Func<SlotSpinAdmission, CancellationToken, Task<SpinResult>>? resolve = null, long wagerPoints = WagerPoints,
        bool useFreeSpin = false, bool allowNewPrepared = true) =>
        store.BeginSlotSpinAsync(UserId, GameId, wagerPoints, PointValueInCents, useFreeSpin, false, 0,
            time, TimeSpan.Zero, default, resolve, operationId, allowNewPrepared);

    private static Task<SpinResult> Resolve(SlotSpinAdmission admission, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var potion = ArcanePotions.ForSpin(admission.ArcanePotionsBefore, admission.IsFreeSpin);
        var engine = new ArcaneArchivesEngine(new SeededRandom());
        var outcome = StandaloneSlotPackageRegistry.MapOutcome(engine.SpinTome(admission.WagerPoints, UserId,
            admission.EnergyBalance, admission.PityMissesBefore, admission.IsFreeSpin, admission.ArcaneTomeBefore?.ActivePage is not null,
            potion), potion);
        return Task.FromResult(outcome with { ChargedWagerCents = admission.ChargedWagerCents });
    }

    private sealed class SeededRandom : IGameRandomSource
    {
        private readonly Random random = new(20261120);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
