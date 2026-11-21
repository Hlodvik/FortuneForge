using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.NeonNights.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.NeonNights.Tests;

public sealed class NeonPrimaryRoundTests
{
    private const string Player = "neon-primary-player";
    private const string GameId = NeonArcadeSlotDefinitions.GameId;
    private const long Wager = 150;

    [PrimaryFirestoreFact]
    public async Task InterruptedStandardRoundResumesExactlyAndPublishesOnce()
    {
        var db = Connect();
        var wallet = new Wallet { LoseNext = true };
        var service = Service(db, wallet, BaseStops());
        var request = new NeonRoundRequest("same-operation-001", Wager, false);
        using var signedRequest = GameSecurityContext.Push(new("paid-001", Player, "private-session", "203.0.113.11",
            DateTime.UtcNow, "POST", NeonPrimaryRouteContract.SpinPath, "neon-nights", null, request.OperationId, null,
            null, "sanitized", "direct"));

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Player, request, true, default));
        var held = await service.ReadStateAsync(Player, default);
        Assert.Equal(request, held.PendingOperation);
        Assert.Equal(wallet.Balance / 100m, held.SlotsCreditsBalance);
        Assert.Empty((await db.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        var conflict = await Assert.ThrowsAsync<NeonPrimaryRoundConflictException>(() =>
            service.SpinAsync(Player, request with { OperationId = "different-operation-001" }, true, default));
        Assert.Equal(request, conflict.PendingRequest);

        // A fresh replica can recover using only the operation, without a wallet-state read or RNG draw.
        var restored = Service(db, wallet, []);
        var result = await restored.SpinAsync(Player, request, false, default);
        var paid = Assert.IsType<NeonSpinReply>(result.Spin);
        Assert.Equal(wallet.Last!.OperationId, paid.SpinId.ToString("N"));
        Assert.Equal(Wager, paid.ChargedWagerCents);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
        Assert.False(wallet.Last.SpendLegacyFreeGame);
        Assert.Equal(1, wallet.Commits);
        Assert.Equal(2, wallet.SettleCalls);
        Assert.Null((await restored.ReadStateAsync(Player, default)).PendingOperation);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(await restored.SpinAsync(Player, request, false, default)));
        Assert.Equal(2, wallet.SettleCalls);
        await Assert.ThrowsAsync<NeonPrimaryRoundConflictException>(() =>
            restored.SpinAsync(Player, request with { WagerPoints = Wager + 50 }, true, default));

        Assert.Single((await db.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var raw = Assert.Single((await db.Collection(NeonPrimaryRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.False(raw.ContainsField("requestId"));
        Assert.DoesNotContain("private-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);
    }

    [PrimaryFirestoreFact]
    public async Task NativeCarGrantsAndNitroConsumeOnlyTheSelectedPaidPlay()
    {
        var db = Connect();
        var wallet = new Wallet();
        var random = CarStops(nitro: true);
        var service = Service(db, wallet, random);
        Assert.True(await service.GrantAsync(Player, "grant-native-car-001", Wager, true, false, default));
        Assert.True(await service.GrantAsync(Player, "grant-native-car-001", Wager, true, false, default));
        Assert.False(await service.GrantAsync(Player, "grant-native-disco-001", Wager, false, true, default));
        var request = new NeonRoundRequest("native-car-nitro-001", Wager + 100, true);
        var pending = await service.SpinAsync(Player, request, true, default);
        var offer = Assert.IsType<NeonNitroChoiceOffer>(pending.PendingNeonNitroChoice);
        Assert.Equal(NeonNitroChoice.SpinSource, offer.SourceKind);
        Assert.Equal(Wager, offer.WagerPoints);
        Assert.Equal(NeonArcadeSlotDefinitions.SpecialPlaySpins - 1, offer.FreeSpinsRemaining);
        Assert.Equal(1, wallet.SettleCalls);
        Assert.Equal(GameWalletSettlementPhase.Reserve, wallet.Last!.Phase);
        Assert.Equal(Wager, wallet.Last.WagerCents);
        Assert.Equal(wallet.Balance, offer.WalletBalanceAfterWagerCents);
        var holdDigest = wallet.Last.OutcomeSha256;

        // Spending available balance elsewhere after the wager debit cannot cancel a revealed spin.
        wallet.Balance = 0;
        wallet.LoseNext = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SelectNitroAsync(Player, offer.SpinId, 2, default));
        Assert.Equal(2, (await service.ReadStateAsync(Player, default)).PendingNeonNitroChoice!.SelectedReel);
        await Assert.ThrowsAsync<NeonPrimaryNitroConflictException>(() => service.SelectNitroAsync(Player, offer.SpinId, 1, default));
        await Assert.ThrowsAsync<NeonPrimaryNitroConflictException>(() => service.SelectNitroAsync("another-player", offer.SpinId, 2, default));
        var restored = Service(db, wallet, []);
        var chosen = await restored.SelectNitroAsync(Player, offer.SpinId, 2, default);
        Assert.Equal(2, chosen.Spin!.NeonNitroWild!.ExpandedReel);
        Assert.Equal(Wager, chosen.Spin.ChargedWagerCents);
        Assert.Equal(NeonArcadeSlotDefinitions.SpecialPlaySpins - 1, chosen.Spin.FreeSpinsRemaining);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(GameWalletSettlementPhase.CompleteReservation, wallet.Last.Phase);
        Assert.Equal(holdDigest, wallet.Last.ReservationSha256);
        Assert.Equal(0, wallet.Last.WagerCents);
        Assert.Equal(chosen.Spin.Payout.TotalPoints, wallet.Balance);
        Assert.Equal(0, wallet.LegacyCount);
        Assert.Equal(2, wallet.Commits);
        var state = await restored.ReadStateAsync(Player, default);
        Assert.Equal(chosen.Spin.FreeSpinsRemaining, state.FreeSpinsRemaining);
        Assert.Null(state.PendingNeonNitroChoice);
        Assert.Null(state.PendingOperation);
        Assert.Equal(chosen.Wallet, (await restored.SpinAsync(Player, request, false, default)).Wallet);
    }

    [PrimaryFirestoreFact]
    public async Task CommittedReservationReplyLossHidesBoardUntilTheSameWagerIsConfirmed()
    {
        var db = Connect();
        var wallet = new Wallet { LoseNext = true, LegacyCount = 1 };
        var fields = new Dictionary<string, object> { ["freeSpinWagerCents"] = Wager, ["freeSpinFeatureMode"] = NeonArcadeBonus.PaidCarFeatureMode };
        NeonCarRoundState.Start("old-car-round", NeonArcadeBonus.PaidCarFeatureMode).WriteGuard(fields);
        await Guard(db).CreateAsync(fields);
        var service = Service(db, wallet, CarStops(nitro: true));
        var request = new NeonRoundRequest("nitro-reserve-disconnect", Wager, true);

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Player, request, true, default));
        var concealed = await service.ReadStateAsync(Player, default);
        Assert.Null(concealed.PendingNeonNitroChoice);
        Assert.Equal(request, concealed.PendingOperation);
        Assert.Equal(0, wallet.LegacyCount);
        Assert.Equal(1_000_000 - Wager, wallet.Balance);

        var restored = Service(db, wallet, []);
        var held = await restored.SpinAsync(Player, request, false, default);
        var offer = Assert.IsType<NeonNitroChoiceOffer>(held.PendingNeonNitroChoice);
        Assert.Equal(wallet.Last!.OperationId, offer.SpinId.ToString("N"));
        Assert.Equal(0, offer.FreeSpinsRemaining);
        Assert.Equal(1, wallet.Commits);
        Assert.Equal(2, wallet.SettleCalls);
        var result = await restored.SelectNitroAsync(Player, offer.SpinId, 2, default);
        Assert.Equal(0, result.Spin!.FreeSpinsRemaining);
        Assert.Equal(2, wallet.Commits);
        Assert.Null((await restored.ReadStateAsync(Player, default)).PendingOperation);
    }

    [PrimaryFirestoreFact]
    public async Task DeclinedReservationNeverPublishesTheConcealedBoard()
    {
        var db = Connect();
        var wallet = new Wallet { Balance = 0 };
        var service = Service(db, wallet, CarStops(nitro: true));
        Assert.True(await service.GrantAsync(Player, "grant-nitro-decline", Wager, true, false, default));
        var request = new NeonRoundRequest("declined-hidden-nitro", Wager, true);
        var declined = await service.SpinAsync(Player, request, true, default);
        Assert.True(declined.Wallet!.InsufficientFunds);
        Assert.Equal(GameWalletSettlementPhase.Reserve, wallet.Last!.Phase);
        Assert.Null(declined.PendingNeonNitroChoice);
        Assert.Null(declined.Spin);
        var state = await service.ReadStateAsync(Player, default);
        Assert.Null(state.PendingNeonNitroChoice);
        Assert.Null(state.PendingOperation);
        Assert.Equal(NeonArcadeSlotDefinitions.SpecialPlaySpins, state.FreeSpinsRemaining);
        Assert.Empty((await db.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Equal(declined, await service.SpinAsync(Player, request, false, default));
        Assert.Equal(1, wallet.SettleCalls);
    }

    [PrimaryFirestoreFact]
    public async Task LegacyCarChargesLockedWagerAndCasDebitsOneOriginalUnit()
    {
        var db = Connect();
        var wallet = new Wallet { LegacyCount = 2 };
        var fields = new Dictionary<string, object> { ["freeSpinWagerCents"] = Wager, ["freeSpinFeatureMode"] = NeonArcadeBonus.PaidCarFeatureMode };
        NeonCarRoundState.Start("old-car-round", NeonArcadeBonus.PaidCarFeatureMode).WriteGuard(fields);
        await Guard(db).CreateAsync(fields);
        var service = Service(db, wallet, CarStops(nitro: false));
        var result = await service.SpinAsync(Player, new("legacy-car-operation", Wager + 50, true), true, default);
        var paid = Assert.IsType<NeonSpinReply>(result.Spin);
        Assert.Equal(Wager, paid.WagerPoints);
        Assert.Equal(Wager, paid.ChargedWagerCents);
        Assert.Equal(2, wallet.Last!.ExpectedLegacyFreeGamesRemaining);
        Assert.True(wallet.Last.SpendLegacyFreeGame);
        Assert.Equal(1, wallet.LegacyCount);
        Assert.Equal(1, paid.FreeSpinsRemaining);
        Assert.Equal($"{paid.SpinId:N}-free-game-use", result.Wallet!.LegacyFreeGameTransactionId);
    }

    [PrimaryFirestoreFact]
    public async Task LegacyCarRetriggerStoresTheNewPlaysInNeonAndExposesBothBanks()
    {
        var db = Connect();
        var wallet = new Wallet { LegacyCount = 2 };
        var fields = new Dictionary<string, object> { ["freeSpinWagerCents"] = Wager, ["freeSpinFeatureMode"] = NeonArcadeBonus.PaidCarFeatureMode };
        NeonCarRoundState.Start("old-car-round", NeonArcadeBonus.PaidCarFeatureMode).WriteGuard(fields);
        await Guard(db).CreateAsync(fields);
        var service = Service(db, wallet, CarStops(nitro: false, triggerCar: true));
        var result = await service.SpinAsync(Player, new("legacy-car-retrigger", Wager, true), true, default);

        var paid = Assert.IsType<NeonSpinReply>(result.Spin);
        Assert.Equal(NeonArcadeSlotDefinitions.SpecialPlaySpins, paid.FreeSpinsAwarded);
        Assert.Equal(NeonArcadeSlotDefinitions.SpecialPlaySpins + 1, paid.FreeSpinsRemaining);
        Assert.Equal(1, wallet.LegacyCount);
        var state = await service.ReadStateAsync(Player, default);
        Assert.Equal(paid.FreeSpinsRemaining, state.FreeSpinsRemaining);
        Assert.Equal(Wager, state.FreeSpinWagerPoints);
        Assert.Equal(NeonArcadeBonus.PaidCarFeatureMode, state.FreeSpinFeatureMode);
        Assert.Equal(NeonArcadeSlotDefinitions.SpecialPlaySpins,
            (await Guard(db).GetSnapshotAsync()).GetValue<long>("neonNativeCarSpinsRemaining"));
    }

    [PrimaryFirestoreFact]
    public async Task StaleLegacyCountDeclinesBeforeMoneyAndWithoutPublishing()
    {
        var db = Connect();
        var wallet = new Wallet { ChangeCountBeforeSettle = 1 };
        var service = Service(db, wallet, BaseStops());
        var result = await service.SpinAsync(Player, new("stale-wallet-count", Wager, false), true, default);
        Assert.True(result.Wallet!.LegacyFreeGamesCountMismatch);
        Assert.Null(result.Spin);
        Assert.Equal(1_000_000, wallet.Balance);
        Assert.Equal(0, wallet.Commits);
        Assert.Empty((await db.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        var guard = await Guard(db).GetSnapshotAsync();
        Assert.Equal(string.Empty, guard.GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
        Assert.False(guard.ContainsField(SlotSpinGuard.PreparedAdmissionField));
    }

    [PrimaryFirestoreFact]
    public async Task DiscoUsesLockedWagerAndAdvancesExactlyOnce()
    {
        var db = Connect();
        var wallet = new Wallet();
        var discoStops = NeonDiscoDefinitions.CreateReelSet().Reels.Select(reel => reel.IndexOf("2")).ToArray();
        var service = Service(db, wallet, discoStops);
        Assert.True(await service.GrantAsync(Player, "grant-disco-operation", Wager, false, true, default));
        await Assert.ThrowsAsync<NeonDiscoWagerLockedException>(() => service.SpinAsync(Player,
            new("wrong-disco-wager", Wager + 50, false), true, default));
        var request = new NeonRoundRequest("correct-disco-wager", Wager, false);
        var result = await service.SpinAsync(Player, request, true, default);
        Assert.NotNull(result.Spin!.NeonDiscoCascade);
        Assert.Equal(NeonArcadeSlotDefinitions.DiscoPlaySpins - 1, result.Spin.NeonDiscoSpinsRemaining);
        Assert.Equal(Wager, result.Spin.ChargedWagerCents);
        Assert.Equal(result.Wallet, (await service.SpinAsync(Player, request, false, default)).Wallet);
        Assert.Equal(1, wallet.SettleCalls);
    }

    [PrimaryFirestoreFact]
    public async Task OnlyExactOldSpinNitroCanRequestLegacyFallbackAndHeldLegacyBlocksNewSpins()
    {
        var db = Connect();
        var wallet = new Wallet();
        var pending = new NeonNightsEngine(new Stops(CarStops(nitro: true))).Spin(
            Wager, Player, false, 0, NeonArcadeBonus.PaidCarFeatureMode, 0, true);
        var offer = NeonNitroChoice.CreateSpinOffer(pending.SpinId, Wager, 0, pending.NeonNitroWild!);
        var admission = Guid.NewGuid().ToString("N");
        await Guard(db).CreateAsync(new Dictionary<string, object>
        {
            [SlotSpinGuard.ActiveAdmissionField] = admission,
            [SlotSpinGuard.LastSpinStartedField] = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-1)),
            ["pendingNeonNitroChoiceJson"] = JsonSerializer.Serialize(offer), ["pendingNeonNitroAdmissionId"] = admission,
        });
        var admissionRecord = db.Collection("slotSpinAdmissions").Document(admission);
        await admissionRecord.CreateAsync(new Dictionary<string, object>
        {
            ["userId"] = Player, ["gameId"] = GameId, ["status"] = "admitted",
            ["pendingNeonNitroSpinId"] = pending.SpinId.ToString("N"),
            ["pendingNeonNitroOutcomeJson"] = JsonSerializer.Serialize(pending), ["pendingNeonNitroAdmissionJson"] = "{}"
        });
        var service = Service(db, wallet, []);
        await Assert.ThrowsAsync<NeonPrimaryRoundConflictException>(() =>
            service.SpinAsync(Player, new("new-while-legacy-held", Wager, false), true, default));
        await Assert.ThrowsAsync<NeonLegacyNitroChoiceException>(() => service.SelectNitroAsync(Player, pending.SpinId, 2, default));
        await Assert.ThrowsAsync<NeonPrimaryNitroConflictException>(() => service.SelectNitroAsync(Player, Guid.NewGuid(), 2, default));
        await Assert.ThrowsAsync<NeonPrimaryNitroConflictException>(() => service.SelectNitroAsync("wrong-player", pending.SpinId, 2, default));
        Assert.Equal(0, wallet.SettleCalls);
    }

    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"neon-primary-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static NeonPrimaryRoundService Service(FirestoreDb db, Wallet wallet, int[] stops) =>
        new(new NeonPrimaryRoundStore(db, new Stops(stops), TimeProvider.System), wallet, wallet);

    private static DocumentReference Guard(FirestoreDb db) =>
        db.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Player, GameId));

    private static int[] BaseStops() => PickStops(NeonArcadeSlotDefinitions.CreateReelSet(), 4, false);
    private static int[] CarStops(bool nitro, bool triggerCar = false) =>
        PickStops(NeonArcadeSlotDefinitions.CreateCarReelSet(), 7, nitro, triggerCar);

    private static int[] PickStops(ReelSetDefinition reels, int rows, bool nitro, bool triggerCar = false) => reels.Reels.Select((raw, reel) =>
    {
        var strip = raw.Where(symbol => symbol != "BOLT").ToArray();
        return Enumerable.Range(0, strip.Length).First(stop =>
        {
            var view = Enumerable.Range(0, rows).Select(offset => strip[(stop + offset) % strip.Length]).ToArray();
            return view.Contains("6") == (triggerCar && reel == 0) && !view.Contains("POWER") &&
                view.Count(symbol => symbol == "NITRO") == (nitro && reel >= 3 ? 1 : 0);
        });
    }).ToArray();

    private sealed class Stops(int[] indexes) : IGameRandomSource
    {
        private int position;
        private readonly Random fallback = new(73);
        public int Next(int maximumExclusive) => position < indexes.Length ? indexes[position++] : fallback.Next(maximumExclusive);
    }

    private sealed class Wallet : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly Dictionary<(string OperationId, GameWalletSettlementPhase Phase),
            (GameWalletSettlementRequest Terms, GameWalletSettlementReceipt Receipt)> receipts = [];
        public long LegacyCount { get; set; }
        public long Balance { get; set; } = 1_000_000;
        public int Commits { get; private set; }
        public int SettleCalls { get; private set; }
        public bool LoseNext { get; set; }
        public long? ChangeCountBeforeSettle { get; set; }
        public GameWalletSettlementRequest? Last { get; private set; }

        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult(new GameWalletAccountState(LegacyCount, Balance));

        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken cancellationToken)
        {
            Last = request;
            SettleCalls++;
            var key = (request.OperationId, request.Phase);
            if (receipts.TryGetValue(key, out var old))
            {
                Assert.Equal(old.Terms, request);
                return Task.FromResult(old.Receipt with { WasAlreadySettled = true });
            }
            if (ChangeCountBeforeSettle is { } nextCount) LegacyCount = nextCount;
            var before = Balance;
            var id = GameWalletSettlementIdentity.CreateId(GameId, accountId, request.OperationId);
            if (request.Phase == GameWalletSettlementPhase.CompleteReservation)
            {
                var held = receipts[(request.OperationId, GameWalletSettlementPhase.Reserve)];
                Assert.Equal(held.Terms.OutcomeSha256, request.ReservationSha256);
                Assert.False(held.Receipt.InsufficientFunds || held.Receipt.LegacyFreeGamesCountMismatch);
                Assert.Equal(0, request.WagerCents);
                Assert.False(request.SpendLegacyFreeGame);
                id = GameWalletSettlementIdentity.CreateCompletionId(GameId, accountId, request.OperationId);
            }
            var mismatch = request.ExpectedLegacyFreeGamesRemaining is { } expected && expected != LegacyCount;
            var insufficientFunds = !mismatch && request.WagerCents > before;
            var declined = mismatch || insufficientFunds;
            if (!declined)
            {
                Balance = before - request.WagerCents + request.PayoutCents;
                if (request.SpendLegacyFreeGame) LegacyCount--;
                Commits++;
            }
            var receipt = new GameWalletSettlementReceipt(id,
                !declined && request.WagerCents > 0 ? $"{request.OperationId}-wager" : null,
                !declined && request.PayoutCents > 0 ? $"{request.OperationId}-payout" : null, before,
                declined ? before : before - request.WagerCents, Balance, DateTimeOffset.UtcNow, false,
                insufficientFunds, false, !declined && request.SpendLegacyFreeGame ? $"{request.OperationId}-free-game-use" : null,
                LegacyCount, mismatch);
            receipts.Add(key, (request, receipt));
            if (LoseNext)
            {
                LoseNext = false;
                throw new GameWalletUnavailableException();
            }
            return Task.FromResult(receipt);
        }
    }
}

public sealed class PrimaryFirestoreFactAttribute : FactAttribute
{
    public PrimaryFirestoreFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for Neon paid round persistence checks.";
    }
}
