using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.NeonNights;
using FortuneForge.Games.NeonNights.Server;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

[Collection(MidnightVaultPaidEmulatorCollection.Name)]
public sealed class NeonCarRespinWorkerTests(MidnightVaultPaidEmulatorFixture emulator)
{
    private const string Player = "neon-car-worker-test";
    private const string GameId = NeonArcadeSlotDefinitions.GameId;

    [Fact]
    public async Task RespinAndNitroChoiceSurviveLostWalletResponseWithoutAnotherDrawOrCredit()
    {
        var database = emulator.Database();
        var source = await SeedAsync(database);
        var wallet = new RecordedWallet { LoseFirstReceipt = true };
        var store = new NeonCarRespinStore(database, wallet, wallet, new Draws([7]), TimeProvider.System);

        NeonCarRespinResponse pending;
        using (GameSecurityContext.Push(Observation("original-car-respin", source.Original.SpinId)))
            pending = await store.RespinAsync(Player, source.Original.SpinId, 4, default);
        Assert.Equal(7, pending.StopIndex);
        Assert.Equal(4, pending.PendingNeonNitroChoice?.ReelIndex);
        Assert.Equal(3, pending.PendingNeonNitroChoice?.FreeSpinsRemaining);
        Assert.Empty(wallet.Requests);
        var guard = await source.Guard.GetSnapshotAsync();
        var operationId = guard.GetValue<string>(NeonCarRespinStore.PendingAdjustmentField);
        Assert.Equal(operationId, guard.GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
        Assert.Equal(operationId, guard.GetValue<string>(SlotSpinGuard.PreparedAdmissionField));
        Assert.NotEqual(source.Original.SpinId.ToString("N"), operationId);
        var primary = new NeonPrimaryRoundService(new NeonPrimaryRoundStore(database, new Draws([]), TimeProvider.System),
            wallet, wallet);
        await Assert.ThrowsAsync<NeonPrimaryRoundConflictException>(() =>
            primary.SpinAsync(Player, new("neon-spin-during-adjustment", 300, true), allowStart: true, default));
        Assert.Empty(wallet.Requests);

        store = new NeonCarRespinStore(database, wallet, wallet, new Draws([]), TimeProvider.System);
        await store.DismissAsync(Player, source.Original.SpinId, default);
        Assert.Equal(operationId, (await source.Guard.GetSnapshotAsync()).GetValue<string>(NeonCarRespinStore.PendingAdjustmentField));
        Assert.Equal(pending.StopIndex, (await store.RespinAsync(Player, source.Original.SpinId, 4, default)).StopIndex);
        await Assert.ThrowsAsync<NeonCarActionConflictException>(() => store.RespinAsync(Player, source.Original.SpinId, 3, default));
        using (GameSecurityContext.Push(Observation("original-nitro-choice", source.Original.SpinId)))
            await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.ChooseNitroAsync(Player, source.Original.SpinId, 2, default));
        Assert.False((await source.Receipt.GetSnapshotAsync()).Exists);
        await Assert.ThrowsAsync<NeonCarActionConflictException>(() => store.ChooseNitroAsync(Player, source.Original.SpinId, 1, default));

        var completed = await store.ChooseNitroAsync(Player, source.Original.SpinId, 2, default);
        Assert.False(completed.AlreadyApplied);
        Assert.Equal(2, completed.NeonNitroWild?.ExpandedReel);
        Assert.Equal(210, completed.ImprovementPoints);
        Assert.Equal(102.10m, completed.SlotsCreditsBalance);
        Assert.Equal(2, wallet.Requests.Count);
        Assert.Equal(wallet.Requests[0], wallet.Requests[1]);
        Assert.Equal(operationId, wallet.Requests[0].OperationId);
        Assert.Equal(0, wallet.Requests[0].WagerCents);
        Assert.False(wallet.Requests[0].SpendLegacyFreeGame);
        Assert.Equal(1, wallet.AppliedCount);

        wallet.AddOtherGameCredit(500);
        var repeated = await store.ChooseNitroAsync(Player, source.Original.SpinId, 2, default);
        Assert.True(repeated.AlreadyApplied);
        Assert.Equal(107.10m, repeated.SlotsCreditsBalance);
        Assert.Equal(completed.StopIndex, repeated.StopIndex);
        Assert.Equal(completed.RoundWinPoints, repeated.RoundWinPoints);
        Assert.Equal(2, wallet.Requests.Count);
        guard = await source.Guard.GetSnapshotAsync();
        Assert.False(guard.ContainsField(NeonCarRespinStore.PendingAdjustmentField));
        Assert.False(guard.ContainsField(SlotSpinGuard.PreparedAdmissionField));
        Assert.Equal(NeonCarRespinStore.WorkerOwner, guard.GetValue<string>(NeonCarRespinStore.WorkerOwnerField));

        var saved = await source.Receipt.GetSnapshotAsync();
        Assert.Equal(2L, saved.GetValue<long>("neonNitroChosenReel"));
        Assert.Equal(wallet.Requests[0].OutcomeSha256, saved.GetValue<string>("outcomeSha256"));
        var stats = await database.Collection("userSlotStatistics").Document(Player).GetSnapshotAsync();
        Assert.Equal(2.10, stats.GetValue<double>("creditsWon"));
        Assert.False(stats.ContainsField("spinsPlayed"));
        var analytics = Assert.Single((await database.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Equal(NeonCarRespinReport.ReceiptCollection, analytics.GetValue<string>("sourceCollection"));
        Assert.Equal(210L, analytics.GetValue<long>("payoutCents"));
        var savedStage = await database.Collection("neonCarRespinAdjustments").Document(operationId).GetSnapshotAsync();
        var savedJson = savedStage.GetValue<string>("frozenJson");
        Assert.DoesNotContain("original-car-respin", savedJson);
        Assert.DoesNotContain("original-nitro-choice", savedJson);
        var evidence = Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        Assert.Equal("original-car-respin", evidence.GetValue<string>("requestId"));
        var journal = Assert.Single((await database.Collection(GameSecurityEventDocuments.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Equal("original-nitro-choice", journal.GetValue<string>("requestId"));
    }

    [Fact]
    public async Task ExistingPendingCarNitroIsAdoptedAndUsesTheLegacyReceiptKey()
    {
        var database = emulator.Database();
        var source = await SeedAsync(database, legacyRoundReceipt: true);
        var pending = new NeonCarRespin(new Draws([7])).Resolve(source.Original, 4);
        var offer = NeonNitroChoice.CreateCarRespinOffer(source.Original, pending, 1, 0);
        await source.Guard.UpdateAsync(new Dictionary<string, object>
        {
            ["pendingNeonNitroChoiceJson"] = JsonSerializer.Serialize(offer),
            ["pendingNeonNitroCarRespinJson"] = JsonSerializer.Serialize(pending)
        });

        var wallet = new RecordedWallet();
        var store = new NeonCarRespinStore(database, wallet, wallet, new Draws([]), TimeProvider.System);
        var completed = await store.ChooseNitroAsync(Player, source.Original.SpinId, 2, default);
        Assert.Equal(pending.StopIndex, completed.StopIndex);
        Assert.Equal(210, completed.ImprovementPoints);
        Assert.Equal(source.RoundId, source.Receipt.Id);
        Assert.Equal(source.RoundId, (await source.Receipt.GetSnapshotAsync()).GetValue<string>("roundId"));
        Assert.Single(wallet.Requests);
    }

    [Fact]
    public async Task HistoricalReceiptRetryUsesCurrentBalanceAndDismissRemovesOnlyTheAvailableOffer()
    {
        var database = emulator.Database();
        var source = await SeedAsync(database, legacyRoundReceipt: true);
        var pending = new NeonCarRespin(new Draws([7])).Resolve(source.Original, 4);
        var original = NeonCarRespin.ResolveChoice(source.Original, pending, 2);
        await source.Receipt.CreateAsync(new Dictionary<string, object>
        {
            ["userId"] = Player, ["sourceSpinId"] = source.Original.SpinId.ToString("N"), ["roundId"] = source.RoundId,
            ["reelIndex"] = 4L, ["outcomeJson"] = JsonSerializer.Serialize(original),
            ["sourceSha256"] = GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(source.Original)),
            ["improvementPoints"] = 210L, ["improvementCents"] = 210L, ["neonNitroChosenReel"] = 2L,
            ["createdAt"] = Timestamp.GetCurrentTimestamp()
        });

        var wallet = new RecordedWallet();
        var store = new NeonCarRespinStore(database, wallet, wallet, new Draws([]), TimeProvider.System);
        var repeated = await store.ChooseNitroAsync(Player, source.Original.SpinId, 2, default);
        Assert.True(repeated.AlreadyApplied);
        Assert.Equal(100m, repeated.SlotsCreditsBalance);
        await Assert.ThrowsAsync<NeonCarActionConflictException>(() => store.ChooseNitroAsync(Player, source.Original.SpinId, 3, default));
        await Assert.ThrowsAsync<NeonCarActionConflictException>(() => store.RespinAsync("other-account", source.Original.SpinId, 4, default));
        Assert.Empty(wallet.Requests);

        var next = await SeedAsync(database);
        await store.DismissAsync(Player, source.Original.SpinId, default);
        Assert.Equal(next.Original.SpinId.ToString("N"), (await next.Guard.GetSnapshotAsync()).GetValue<string>("neonCarRespinSourceSpinId"));
        await store.DismissAsync(Player, next.Original.SpinId, default);
        await store.DismissAsync(Player, next.Original.SpinId, default);
        await Assert.ThrowsAsync<NeonCarActionConflictException>(() => store.RespinAsync(Player, next.Original.SpinId, 4, default));
        Assert.Empty(wallet.Requests);
    }

    private static async Task<(NeonSpinOutcome Original, string RoundId, DocumentReference Guard, DocumentReference Receipt)> SeedAsync(
        FirestoreDb database, bool legacyRoundReceipt = false)
    {
        var original = new FortuneForge.Games.NeonNights.NeonNightsEngine(new FixtureDefinitions(), new Draws([0, 0, 0, 0, 0]))
            .Spin(100, Player, false, 0, NeonArcadeBonus.PaidCarFeatureMode, 0, true);
        var spinId = original.SpinId.ToString("N");
        var roundId = Guid.NewGuid().ToString("N") + "-historical-car";
        var car = NeonCarRoundState.Start(roundId, NeonArcadeBonus.PaidCarFeatureMode)
            .Advance(original.SpinId, original, null, 0);
        Assert.NotNull(car.AvailableOffer());
        var guardRef = database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Player, GameId));
        var guard = new Dictionary<string, object>
        {
            ["neonNativeCarSpinsRemaining"] = legacyRoundReceipt ? 0L : 2L,
            ["neonNativeCarWagerPoints"] = 100L,
            ["neonNativeCarFeatureMode"] = NeonArcadeBonus.PaidCarFeatureMode,
            ["freeSpinWagerCents"] = 100L,
            ["freeSpinFeatureMode"] = NeonArcadeBonus.PaidCarFeatureMode
        };
        car.WriteGuard(guard);
        await guardRef.SetAsync(guard);
        var result = new Dictionary<string, object>
        {
            ["gameId"] = GameId, ["userId"] = Player, ["createdAt"] = Timestamp.GetCurrentTimestamp()
        };
        car.WriteSpinResult(result, original);
        if (legacyRoundReceipt) result.Remove("neonCarRespinReceiptId");
        await database.Collection("slotSpinResults").Document(spinId).CreateAsync(result);
        return (original, roundId, guardRef,
            database.Collection(NeonCarRespinReport.ReceiptCollection).Document(legacyRoundReceipt ? roundId : spinId));
    }

    private static GameRequestObservation Observation(string requestId, Guid spinId) => new(requestId, Player, null,
        "192.0.2.123", DateTime.UtcNow, "POST", "/api/slots/neon/car-respin", NeonCarRespinEndpoints.AuditGameId,
        spinId.ToString("N"), null, null, null, "empty", "direct");

    private sealed class FixtureDefinitions : ISlotDefinitionSource
    {
        private readonly NeonDefinitionSource definitions = new();
        public GameDefinition? GetGame(string id) => definitions.GetGame(id);
        public SymbolSetDefinition? GetSymbolSet(string id) => definitions.GetSymbolSet(id);
        public PaytableDefinition? GetPaytable(string id) => definitions.GetPaytable(id);
        public ReelSetDefinition? GetReelSet(string id) => id != NeonArcadeSlotDefinitions.CarReelSetId
            ? definitions.GetReelSet(id) : new()
            {
                Id = id, SymbolSetId = NeonArcadeSlotDefinitions.CarSymbolSetId,
                Reels = [
                    ["7", "6", "6", "6", "6", "6", "6"], ["7", "6", "6", "6", "6", "6", "6"],
                    ["2", "6", "6", "6", "6", "6", "6"], ["NITRO", "6", "6", "6", "6", "6", "6"],
                    ["5", "6", "6", "6", "6", "6", "6", "NITRO", "6", "6", "6", "6", "6", "6"]
                ]
            };
    }

    private sealed class Draws(int[] stops) : IGameRandomSource
    {
        private int next;
        public int Next(int exclusiveUpperBound) => next < stops.Length ? stops[next++] : throw new InvalidOperationException("Unexpected redraw.");
    }

    private sealed class RecordedWallet : IGameWalletClient, IGameWalletAccountReader
    {
        private long balance = 10_000;
        private GameWalletSettlementReceipt? saved;
        public bool LoseFirstReceipt { get; init; }
        public List<GameWalletSettlementRequest> Requests { get; } = [];
        public int AppliedCount { get; private set; }

        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken) => Task.FromResult(new GameWalletAccountState(1, balance));
        public void AddOtherGameCredit(long cents) => balance += cents;

        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Assert.Equal(Player, accountId);
            if (saved is not null)
            {
                Assert.Equal(Requests[0], request);
                return Task.FromResult(saved with { WasAlreadySettled = true });
            }

            var before = balance;
            balance += request.PayoutCents;
            AppliedCount++;
            saved = new(GameWalletSettlementIdentity.CreateId(GameId, accountId, request.OperationId), null,
                request.PayoutCents > 0 ? request.OperationId + "-payout" : null, before, before, balance,
                DateTimeOffset.UtcNow, false, false);
            if (LoseFirstReceipt) throw new GameWalletUnavailableException();
            return Task.FromResult(saved);
        }
    }
}
