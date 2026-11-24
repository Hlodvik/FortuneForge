using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.FrontierFortune.LocalHarness;
using FortuneForge.Games.FrontierFortune.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.FrontierFortune.Tests;

public sealed class FrontierFirestoreFactAttribute : FactAttribute
{
    public FrontierFirestoreFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FRONTIER_FIRESTORE_TESTS") != "1" ||
            Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") != "127.0.0.1:8873")
            Skip = "Set FRONTIER_FIRESTORE_TESTS=1 and FIRESTORE_EMULATOR_HOST=127.0.0.1:8873 for isolated production-store tests.";
    }
}

public sealed class FrontierSettlementTests
{
    private static FirestoreDb Database() => new FirestoreDbBuilder
        { ProjectId = "demo-frontier-paid-validation", EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
    private static string Account() => "frontier-test-" + Guid.NewGuid().ToString("N");
    private static FrontierCommand Spin(string? operation = null) => new(operation ?? Guid.NewGuid().ToString("N"), "spin", 4);
    private static FrontierRoundService Service(FirestoreDb db, IGameWalletClient? wallet = null, IFrontierRoundStore? store = null) =>
        new(store ?? new FrontierRoundStore(db, TimeProvider.System), wallet ?? new FrontierHarnessWallet(db),
            new FrontierHarnessWallet(db), new FrontierEngineTests.WantedRandom(), TimeProvider.System);

    [FrontierFirestoreFact]
    public async Task SimultaneousSameCommandSettlesOneWagerAndOnePreparedOutcome()
    {
        var db = Database(); var account = Account(); var command = Spin();
        var results = await Task.WhenAll(Service(db).ExecuteAsync(account, command, true, default),
            Service(db).ExecuteAsync(account, command, true, default));
        Assert.Equal(FrontierRoundIdentity.Hash(results[0]), FrontierRoundIdentity.Hash(results[1]));
        Assert.Equal(1, results[0].Revision);
        Assert.Equal(99_900, results[0].BalanceCents);
        Assert.Single((await db.Collection("frontierValidationWalletReceipts").WhereEqualTo("accountId", account).GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection(FrontierRoundStore.RoundsCollection).WhereEqualTo("accountId", account).GetSnapshotAsync()).Documents);
    }

    [FrontierFirestoreFact]
    public async Task DifferentCommandCannotPassAnUnresolvedPreparedOperation()
    {
        var db = Database(); var account = Account();
        var gate = new BlockingWallet(new FrontierHarnessWallet(db));
        var pending = Service(db, gate).ExecuteAsync(account, Spin(), true, default);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            var error = await Assert.ThrowsAsync<FrontierConflictException>(() => Service(db).ExecuteAsync(account, Spin(), true, default));
            Assert.Equal("frontier-operation-pending", error.Code);
        }
        finally { gate.Release.TrySetResult(); }
        await pending;
    }

    [FrontierFirestoreFact]
    public async Task LostWalletResponseRecoversOriginalOutcomeAfterServiceRestartEvenWhenStartsArePaused()
    {
        var db = Database(); var account = Account(); var command = Spin();
        var transport = new LoseWalletResponse(new FrontierHarnessWallet(db));
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(db, transport).ExecuteAsync(account, command, true, default));
        var state = await Service(db).ReadAsync(account, default);
        Assert.Equal(0, state.Revision);
        Assert.Equal(command, state.PendingOperation);
        Assert.Equal(99_900, state.BalanceCents);
        var recovered = await Service(db).ExecuteAsync(account, command, false, default);
        Assert.Equal(1, recovered.Revision);
        Assert.Null(recovered.PendingOperation);
        Assert.Equal(99_900, recovered.BalanceCents);
        Assert.Equal(FrontierRoundIdentity.Hash(recovered), FrontierRoundIdentity.Hash(await Service(db).ExecuteAsync(account, command, false, default)));
    }

    [FrontierFirestoreFact]
    public async Task WalletCommittedButFinalizeFailureIsRecoverableWithoutNewCharge()
    {
        var db = Database(); var account = Account(); var command = Spin();
        var store = new LoseFinalization(new FrontierRoundStore(db, TimeProvider.System));
        await Assert.ThrowsAsync<IOException>(() => Service(db, store: store).ExecuteAsync(account, command, true, default));
        var recovered = await Service(db).ExecuteAsync(account, command, true, default);
        Assert.Equal(99_900, recovered.BalanceCents);
        Assert.Equal(1, recovered.Revision);
        Assert.Single((await db.Collection("frontierValidationWalletReceipts").WhereEqualTo("accountId", account).GetSnapshotAsync()).Documents);
    }

    [FrontierFirestoreFact]
    public async Task BountyActionsPersistExactDamageRejectStaleClicksAndBankOnceWithNoWager()
    {
        var db = Database(); var account = Account();
        var first = await Service(db).ExecuteAsync(account, Spin(), true, default);
        var bonus = first.Bonus!;
        var target = bonus.Cells.First(x => x.Kind == "bounty").Index;
        var shot = new FrontierCommand(Guid.NewGuid().ToString("N"), "shoot", BonusId: bonus.BonusId, TargetIndex: target, ExpectedRevision: first.Revision);
        var damaged = await Service(db).ExecuteAsync(account, shot, true, default);
        Assert.Equal(1, damaged.Bonus!.Cells[target].HitPoints);
        Assert.Equal(first.BalanceCents, damaged.BalanceCents);
        Assert.Equal(FrontierRoundIdentity.Hash(damaged), FrontierRoundIdentity.Hash(await Service(db).ExecuteAsync(account, shot, false, default)));
        var stale = shot with { OperationId = Guid.NewGuid().ToString("N") };
        Assert.Equal("frontier-stale-revision", (await Assert.ThrowsAsync<FrontierConflictException>(() => Service(db).ExecuteAsync(account, stale, true, default))).Code);
        var cleared = await Service(db).ExecuteAsync(account, shot with { OperationId = Guid.NewGuid().ToString("N"), ExpectedRevision = damaged.Revision }, true, default);
        Assert.Equal(80, cleared.Bonus!.CollectedPoints);
        var bank = new FrontierCommand(Guid.NewGuid().ToString("N"), "bank", BonusId: bonus.BonusId, ExpectedRevision: cleared.Revision);
        var banked = await Service(db).ExecuteAsync(account, bank, false, default);
        Assert.True(banked.Bonus!.Completed);
        Assert.Equal(101_900, banked.BalanceCents);
        Assert.Equal(80, banked.LastWinPoints);
        Assert.Equal(FrontierRoundIdentity.Hash(banked), FrontierRoundIdentity.Hash(await Service(db).ExecuteAsync(account, bank, false, default)));
        var receipts = await db.Collection("frontierValidationWalletReceipts").WhereEqualTo("accountId", account).GetSnapshotAsync();
        Assert.Equal(100, receipts.Documents.Sum(x => x.GetValue<long>("wagerCents")));
        Assert.Equal(2000, receipts.Documents.Sum(x => x.GetValue<long>("payoutCents")));
    }

    [FrontierFirestoreFact]
    public async Task OperationIdentityCannotBeReboundAndDeclineNeverCreatesBonus()
    {
        var db = Database(); var account = Account(); var command = Spin();
        var balance = db.Collection("frontierValidationWallets").Document(FrontierRoundIdentity.Key(account, "balance"));
        await balance.SetAsync(new Dictionary<string, object> { ["accountId"] = account, ["balanceCents"] = 0L });
        var declined = await Service(db).ExecuteAsync(account, command, true, default);
        Assert.False(declined.Accepted); Assert.Equal(0, declined.Revision); Assert.Null(declined.Bonus);
        await Assert.ThrowsAsync<FrontierConflictException>(() => Service(db).ExecuteAsync(account, command with { WagerPoints = 8 }, true, default));
        Assert.Equal(FrontierRoundIdentity.Hash(declined), FrontierRoundIdentity.Hash(await Service(db).ExecuteAsync(account, command, true, default)));
    }

    [FrontierFirestoreFact]
    public async Task FinalShotAutomaticallyCreditsCollectedPrizesExactlyOnce()
    {
        var db = Database(); var account = Account();
        var reply = await Service(db).ExecuteAsync(account, Spin(), true, default);
        var targets = reply.Bonus!.Cells.Where(x => x.Kind == "strongbox").Select(x => x.Index).ToArray();
        FrontierCommand? finalCommand = null;
        foreach (var target in targets.SelectMany(x => Enumerable.Repeat(x, 2)))
        {
            finalCommand = new(Guid.NewGuid().ToString("N"), "shoot", BonusId: reply.Bonus!.BonusId,
                TargetIndex: target, ExpectedRevision: reply.Revision);
            reply = await Service(db).ExecuteAsync(account, finalCommand, false, default);
        }
        Assert.True(reply.Bonus!.Completed);
        Assert.Equal(0, reply.Bonus.ShotsRemaining);
        Assert.Equal(60, reply.LastWinPoints);
        Assert.Equal(101_400, reply.BalanceCents);
        Assert.Equal(FrontierRoundIdentity.Hash(reply), FrontierRoundIdentity.Hash(await Service(db).ExecuteAsync(account, finalCommand!, false, default)));
        var bank = new FrontierCommand(Guid.NewGuid().ToString("N"), "bank", BonusId: reply.Bonus.BonusId, ExpectedRevision: reply.Revision);
        await Assert.ThrowsAsync<FrontierConflictException>(() => Service(db).ExecuteAsync(account, bank, false, default));
    }

    [FrontierFirestoreFact]
    public async Task HistoricalPendingRecordsRemainUntouchedAndNoWalletRequestIsMade()
    {
        var db = Database(); var account = Account();
        var guard = db.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(account, FrontierFortuneEngine.GameId));
        await guard.SetAsync(new Dictionary<string, object> { ["activeAdmissionId"] = "old-open-admission", ["unrelated"] = "preserved" });
        await Assert.ThrowsAsync<FrontierConflictException>(() => Service(db).ExecuteAsync(account, Spin(), true, default));
        var retained = await guard.GetSnapshotAsync();
        Assert.Equal("old-open-admission", retained.GetValue<string>("activeAdmissionId"));
        Assert.Equal("preserved", retained.GetValue<string>("unrelated"));
        Assert.Empty((await db.Collection("frontierValidationWalletReceipts").WhereEqualTo("accountId", account).GetSnapshotAsync()).Documents);
    }

    private sealed class BlockingWallet(IGameWalletClient inner) : IGameWalletClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken token)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(token); return await inner.SettleAsync(accountId, request, token); }
    }
    private sealed class LoseWalletResponse(IGameWalletClient inner) : IGameWalletClient
    {
        public async Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken token)
        { await inner.SettleAsync(accountId, request, token); throw new GameWalletUnavailableException(); }
    }
    private sealed class LoseFinalization(IFrontierRoundStore inner) : IFrontierRoundStore
    {
        public Task<FrontierReadState> ReadAsync(string a, long l, CancellationToken t) => inner.ReadAsync(a, l, t);
        public Task<FrontierStoredRound?> FindAsync(string a, FrontierCommand c, CancellationToken t) => inner.FindAsync(a, c, t);
        public Task<FrontierStoredRound> PrepareAsync(string a, FrontierCommand c, long l, Func<FrontierAccountState, FrontierPreparedRound> p, CancellationToken t) => inner.PrepareAsync(a, c, l, p, t);
        public Task<FrontierReply> FinalizeAsync(FrontierPreparedRound r, GameWalletSettlementReceipt w, CancellationToken t) => throw new IOException("Simulated process failure before state finalization.");
    }
}
