using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.MidnightVault.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.MidnightVault.Tests;

public sealed class MidnightVaultRoundStoreTests
{
    private const string Account = "midnight-native-player";
    private const string GameId = MidnightVaultEngine.GameId;
    private const long Wager = 100;

    [MidnightVaultStoreEmulatorFact]
    public async Task PaidSpinCommitsCurrentModelOnceAndRecoversTheOriginalAfterWalletReplyLoss()
    {
        var database = Connect();
        var wallet = new Wallet { LoseNextReply = true, ReceiptOffset = TimeSpan.FromSeconds(-5) };
        var operation = new MidnightVaultRoundRequest("midnight-original-paid-001", Wager, false);
        var stops = MidnightVaultEngine.CreateReelSet().Reels.Select(strip => strip.IndexOf("8")).ToArray();
        var service = Service(database, wallet, new Draws(stops));
        using var signed = GameSecurityContext.Push(new("midnight-first", Account, "private-midnight-session", "203.0.113.15",
            DateTime.UtcNow, "POST", MidnightVaultPaidRouteContract.SpinPath, MidnightVaultPaidRouteContract.AuditGameId,
            null, operation.OperationId, null, null, "sanitized", "direct"));

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, operation, true, default));
        Assert.Equal(operation, (await service.ReadStateAsync(Account, default)).PendingOperation);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        var conflict = await Assert.ThrowsAsync<MidnightVaultRoundConflictException>(() => service.SpinAsync(Account,
            operation with { OperationId = "midnight-would-be-second" }, true, default));
        Assert.Equal(operation, conflict.PendingRequest);
        await Assert.ThrowsAsync<MidnightVaultRoundConflictException>(() =>
            service.SpinAsync(Account, operation with { WagerPoints = Wager + 2 }, true, default));

        var restored = Service(database, wallet, new NoDraws());
        var result = await restored.SpinAsync(Account, operation, false, default);
        var round = Assert.IsType<MidnightVaultPreparedRound>(result.Round);
        Assert.Equal(MidnightVaultSlotDefinitions.PaytableId, round.Outcome.PaytableId);
        Assert.Equal(Wager * 25, round.WagerCents);
        Assert.Equal(round.Outcome.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.True(round.PayoutCents > 0);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
        Assert.Equal(round.PayoutCents, wallet.Last.PayoutCents);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        Assert.Equal(result.Wallet, (await restored.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var raw = Assert.Single((await database.Collection(MidnightVaultRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.DoesNotContain("private-midnight-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);
    }

    [MidnightVaultStoreEmulatorFact]
    public async Task LegacyModelsWagersAndModesRemainInFifoAndNewAwardsAppendAsNative()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 4, LoseNextReply = true };
        var time = new RoundClock();
        const int originalSpecial = 12;
        const int originalEnergy = 70;
        var first = new MidnightVaultBonusRound("legacy-original-v1", 6, 1, 31, "rows", MidnightVaultSlotDefinitions.LegacyPaytableId);
        var second = new MidnightVaultBonusRound("older-v2-next", 10, 2, 47, MidnightVaultSlotDefinitions.CollectionFeatureMode,
            MidnightVaultSlotDefinitions.PreviousPaytableId);
        var third = new MidnightVaultBonusRound("v3-existing-tail", 10, 1, 62, null, MidnightVaultSlotDefinitions.PaytableId);
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["midnightBonusRounds"] = new[] { Saved(first), Saved(second), Saved(third) },
            ["sealCounts"] = new Dictionary<string, object> { ["sync"] = 29L },
            ["sealWagerCents"] = new Dictionary<string, object> { ["sync"] = 29L * first.WagerPoints * 25 }
        });
        await LegacyBalance(database, "specialPoints").CreateAsync(LegacyFields("specialPoints", originalSpecial));
        await LegacyBalance(database, "energy").CreateAsync(LegacyFields("energy", originalEnergy));

        var service = Service(database, wallet, new SyncSealDraws(), time);
        var old = new MidnightVaultRoundRequest("midnight-old-fifo-head-001", first.WagerPoints, true, true);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, old, true, default));
        Assert.Equal(3, wallet.LegacyRemaining);
        Assert.Equal(first, (await service.ReadStateAsync(Account, default)).BonusRound);

        var retry = Service(database, wallet, new NoDraws(), time);
        var played = await retry.SpinAsync(Account, old, false, default);
        var round = played.Round!;
        Assert.Equal(first.MathModelId, round.Outcome.PaytableId);
        Assert.Equal(first.FeatureMode, round.Outcome.EngineAudit.FreeSpinFeatureMode);
        Assert.All(round.Outcome.Reels, reel => Assert.Equal(6, reel.Count));
        Assert.True(round.Outcome.SpecialBoostApplied);
        Assert.Equal(originalEnergy, round.Outcome.EngineAudit.EnergyBalanceBefore);
        Assert.Equal(0, played.Features!.EnergyBalance);
        Assert.Equal(originalSpecial - round.Outcome.EngineAudit.BaseGame.SpecialPoints!.ActivationCost +
            round.Outcome.SpecialPointsAwarded, played.Features.SpecialPointsBalance);
        Assert.Equal(originalSpecial, (await LegacyBalance(database, "specialPoints").GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(second, played.Features.BonusRound);
        Assert.Equal(2, played.Features.FreeSpinsRemaining);
        Assert.Equal(third, played.Features.QueuedBonusRounds[0]);
        var awarded = round.Progress.BonusEvidence.AwardedRounds;
        Assert.NotEmpty(awarded);
        Assert.Equal(awarded, played.Features.QueuedBonusRounds.Skip(1));
        Assert.All(awarded, item => Assert.Equal(first.MathModelId, item.MathModelId));
        Assert.Equal(3, round.Progress.After.LegacyRemaining);
        Assert.Equal(4, wallet.Last!.ExpectedLegacyFreeGamesRemaining);
        Assert.True(wallet.Last.SpendLegacyFreeGame);
        Assert.Equal(0, round.WagerCents);
        Assert.Equal(1, wallet.Approvals);

        time.Advance();
        var nextService = Service(database, wallet, new Draws([]), time);
        var locked = await Assert.ThrowsAsync<MidnightVaultFreeWagerLockedException>(() => nextService.SpinAsync(Account,
            new("midnight-wrong-carry-wager", first.WagerPoints, true), true, default));
        Assert.Equal(second.WagerPoints, locked.WagerPoints);
        var next = await nextService.SpinAsync(Account, new("midnight-next-v2-bonus-001", second.WagerPoints, true), true, default);
        Assert.Equal(second.MathModelId, next.Round!.Outcome.PaytableId);
        Assert.Equal(second.FeatureMode, next.Round.Outcome.EngineAudit.FreeSpinFeatureMode);
        Assert.Equal(second with { RemainingSpins = 1 }, next.Features!.BonusRound);
        Assert.Equal(2, wallet.LegacyRemaining);

        time.Advance();
        var lastV2 = await Service(database, wallet, new Draws([]), time).SpinAsync(Account,
            new("midnight-last-v2-bonus-002", second.WagerPoints, true), true, default);
        Assert.Equal(third, lastV2.Features!.BonusRound);
        time.Advance();
        var lastLegacy = await Service(database, wallet, new Draws([]), time).SpinAsync(Account,
            new("midnight-last-old-bonus-003", third.WagerPoints, true), true, default);
        Assert.Equal(awarded[0], lastLegacy.Features!.BonusRound);
        Assert.Equal(0, wallet.LegacyRemaining);

        time.Advance();
        var firstNative = await Service(database, wallet, new Draws([]), time).SpinAsync(Account,
            new("midnight-first-new-bonus-004", awarded[0].WagerPoints, true), true, default);
        Assert.Equal(awarded[0].MathModelId, firstNative.Round!.Outcome.PaytableId);
        Assert.Equal(awarded[0].FeatureMode, firstNative.Round.Outcome.EngineAudit.FreeSpinFeatureMode);
        Assert.Equal(awarded[0] with { RemainingSpins = awarded[0].RemainingSpins - 1 }, firstNative.Features!.BonusRound);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
    }

    [MidnightVaultStoreEmulatorFact]
    public async Task NativeScatterRoundConsumesItsOwnUnitsWithoutSpendingLegacyWallet()
    {
        var database = Connect();
        var wallet = new Wallet();
        var time = new RoundClock();
        var strips = MidnightVaultEngine.CreateReelSet().Reels;
        var scatterStops = strips.Select((strip, reel) =>
            reel < 3 ? strip.IndexOf("FREE") : Enumerable.Range(0, strip.Count).First(stop =>
                Enumerable.Range(0, 4).All(index => strip[(stop + index) % strip.Count] != "FREE"))).ToArray();
        var freeCount = scatterStops.Select((stop, reel) => Enumerable.Range(0, 4)
            .Count(index => strips[reel][(stop + index) % strips[reel].Count] == "FREE")).Sum();
        var paid = Service(database, wallet, new Draws([.. scatterStops, .. Enumerable.Repeat(0, freeCount), 0, 99]), time);
        var result = await paid.SpinAsync(Account, new("midnight-native-award-paid", Wager, false), true, default);
        var bonus = Assert.IsType<MidnightVaultBonusRound>(result.Features!.BonusRound);
        Assert.Equal(MidnightVaultSlotDefinitions.PaytableId, bonus.MathModelId);
        Assert.Equal(MidnightVaultSlotDefinitions.BonusRoundSpins, bonus.RemainingSpins);
        Assert.Equal(0, wallet.LegacyRemaining);

        time.Advance();
        var free = Service(database, wallet, new Draws([]), time);
        var next = await free.SpinAsync(Account, new("midnight-native-free-spin", Wager, true), true, default);
        Assert.Equal(0, next.ChargedWagerCents);
        Assert.Equal(bonus with { RemainingSpins = bonus.RemainingSpins - 1 }, next.Features!.BonusRound);
        Assert.Equal(0, wallet.Last!.ExpectedLegacyFreeGamesRemaining);
        Assert.False(wallet.Last.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.LegacyRemaining);
    }

    [MidnightVaultStoreEmulatorFact]
    public async Task FirstAdoptionCountConflictKeepsOriginalFlattenedFifoCarry()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 1, ChangeBeforeSettle = 2 };
        var guard = Guard(database);
        const long oldWager = 55;
        await guard.CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = oldWager * 25,
            ["freeSpinFeatureMode"] = "paw",
        });
        var service = Service(database, wallet, new Draws([]));
        var request = new MidnightVaultRoundRequest("midnight-stale-old-carry", oldWager, true);
        var declined = await service.SpinAsync(Account, request, true, default);
        Assert.True(declined.Wallet.LegacyFreeGamesCountMismatch);
        Assert.Null(declined.Round);
        Assert.Null(declined.Features);
        Assert.Equal(0, wallet.Approvals);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        var historical = await guard.GetSnapshotAsync();
        Assert.False(historical.ContainsField(MidnightVaultRoundStateCodec.OwnerField));
        Assert.Equal(oldWager * 25, historical.GetValue<long>("freeSpinWagerCents"));
        var restored = await service.ReadStateAsync(Account, default);
        Assert.Equal(oldWager, restored.FreeSpinWagerPoints);
        Assert.Equal(2, restored.FreeSpinsRemaining);
        Assert.Equal("paw", restored.FreeSpinFeatureMode);
        Assert.Equal(MidnightVaultSlotDefinitions.LegacyPaytableId, restored.BonusRound!.MathModelId);
        Assert.Equal(declined.Wallet, (await service.SpinAsync(Account, request, false, default)).Wallet);
        Assert.Equal(1, wallet.Calls);
    }

    [MidnightVaultStoreEmulatorFact]
    public async Task UnresolvedOldLeaseAndInvalidRequestsCannotDrawOrSpend()
    {
        var database = Connect();
        var wallet = new Wallet();
        var service = Service(database, wallet, new NoDraws());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SpinAsync(Account,
            new("midnight-odd-cash-wager", 3, false), true, default));
        await Assert.ThrowsAsync<MidnightVaultInsufficientSpecialPointsException>(() => service.SpinAsync(Account,
            new("midnight-missing-power-boost", Wager, false, true), true, default));
        await Assert.ThrowsAsync<MidnightVaultFreeSpinsUnavailableException>(() => service.SpinAsync(Account,
            new("midnight-missing-free-round", Wager, true), true, default));
        await Assert.ThrowsAsync<MidnightVaultRoundPausedException>(() => service.SpinAsync(Account,
            new("midnight-paused-cash-play", Wager, false), false, default));

        var oldAdmission = Guid.NewGuid().ToString("N");
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            [SlotSpinGuard.ActiveAdmissionField] = oldAdmission,
            [SlotSpinGuard.LastSpinStartedField] = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-1))
        });
        await Assert.ThrowsAsync<MidnightVaultRoundConflictException>(() => service.SpinAsync(Account,
            new("midnight-expired-old-lease", Wager, false), true, default));
        Assert.Equal(oldAdmission, (await Guard(database).GetSnapshotAsync()).GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
        Assert.Equal(0, wallet.Calls);
    }

    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"midnight-native-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static MidnightVaultRoundService Service(FirestoreDb database, Wallet wallet, IGameRandomSource random,
        TimeProvider? time = null) =>
        new(new MidnightVaultRoundStore(database, random, time ?? TimeProvider.System), wallet, wallet);

    private static DocumentReference Guard(FirestoreDb database) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Account, GameId));

    private static string Currency(string name) => $"{name}:{GameId}";
    private static DocumentReference LegacyBalance(FirestoreDb database, string name) =>
        database.Collection("userBalances").Document($"{Account}_{Currency(name)}");
    private static Dictionary<string, object> LegacyFields(string name, long available) =>
        new() { ["userId"] = Account, ["currencyId"] = Currency(name), ["available"] = available };

    private static Dictionary<string, object> Saved(MidnightVaultBonusRound round) => new()
    {
        ["id"] = round.Id, ["totalSpins"] = (long)round.TotalSpins, ["remainingSpins"] = (long)round.RemainingSpins,
        ["wagerPoints"] = round.WagerPoints, ["featureMode"] = round.FeatureMode ?? string.Empty,
        ["mathModelId"] = round.MathModelId
    };

    private sealed class Draws(int[] indexes) : IGameRandomSource
    {
        private int cursor;
        private readonly Random fallback = new(6103);
        public int Next(int maximumExclusive) => cursor < indexes.Length ? indexes[cursor++] : fallback.Next(maximumExclusive);
    }

    private sealed class NoDraws : IGameRandomSource
    {
        public int Next(int maximumExclusive) => throw new InvalidOperationException("A frozen or invalid operation cannot draw new reels.");
    }

    private sealed class SyncSealDraws : IGameRandomSource
    {
        public int Next(int maximumExclusive) => maximumExclusive is 2 or 25 ? 1 : 0;
    }

    private sealed class RoundClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow.AddSeconds(-10);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = now.AddSeconds(1);
    }

    private sealed class Wallet : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly Dictionary<string, (GameWalletSettlementRequest Terms, GameWalletSettlementReceipt Receipt)> receipts = [];
        public long LegacyRemaining { get; set; }
        public long Balance { get; private set; } = 1_000_000;
        public int Approvals { get; private set; }
        public int Calls { get; private set; }
        public bool LoseNextReply { get; set; }
        public long? ChangeBeforeSettle { get; init; }
        public TimeSpan ReceiptOffset { get; init; }
        public GameWalletSettlementRequest? Last { get; private set; }

        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult(new GameWalletAccountState(LegacyRemaining, Balance));

        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request,
            CancellationToken cancellationToken)
        {
            Last = request;
            Calls++;
            if (receipts.TryGetValue(request.OperationId, out var exact))
            {
                Assert.Equal(exact.Terms, request);
                return Task.FromResult(exact.Receipt with { WasAlreadySettled = true });
            }
            if (ChangeBeforeSettle is { } newCount) LegacyRemaining = newCount;
            var before = Balance;
            var mismatch = request.ExpectedLegacyFreeGamesRemaining != LegacyRemaining;
            var declined = mismatch || before < request.WagerCents;
            if (!declined)
            {
                Balance = before - request.WagerCents + request.PayoutCents;
                if (request.SpendLegacyFreeGame) LegacyRemaining--;
                Approvals++;
            }
            var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(GameId, accountId, request.OperationId),
                !declined && request.WagerCents > 0 ? $"{request.OperationId}-wager" : null,
                !declined && request.PayoutCents > 0 ? $"{request.OperationId}-payout" : null,
                before, declined ? before : before - request.WagerCents, Balance, DateTimeOffset.UtcNow + ReceiptOffset,
                false, declined && !mismatch, false, !declined && request.SpendLegacyFreeGame ? $"{request.OperationId}-free-game-use" : null,
                LegacyRemaining, mismatch);
            receipts.Add(request.OperationId, (request, receipt));
            if (LoseNextReply)
            {
                LoseNextReply = false;
                throw new GameWalletUnavailableException();
            }
            return Task.FromResult(receipt);
        }
    }
}

public sealed class MidnightVaultStoreEmulatorFactAttribute : FactAttribute
{
    public MidnightVaultStoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for Midnight Vault paid round persistence checks.";
    }
}
