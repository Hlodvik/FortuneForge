using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using FortuneForge.Games.Wukong.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.Wukong.Tests;

public sealed class WukongRoundStoreTests
{
    private const string Account = "wukong-native-player";
    private const long Wager = 100;

    [WukongStoreEmulatorFact]
    public async Task PaidEnergyCompletionCreatesOddNativeQuestAndRecoversExactly()
    {
        var database = Connect();
        var wallet = new Wallet { LoseNextReply = true, ReceiptOffset = TimeSpan.FromSeconds(-5) };
        var time = new RoundClock();
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["sealCounts"] = new Dictionary<string, object> { ["sync"] = 39L },
            ["sealWagerCents"] = new Dictionary<string, object> { ["sync"] = 3_940L * 25 },
        });
        await LegacyBalance(database, "energy").CreateAsync(LegacyFields("energy", 100));
        var operation = new WukongRoundRequest("wukong-original-paid-energy", Wager, false);
        var service = Service(database, wallet, WinningDraws(), time);
        using var signed = GameSecurityContext.Push(new("wukong-first", Account, "private-wukong-session", "203.0.113.16",
            DateTime.UtcNow, "POST", WukongPaidRouteContract.SpinPath, WukongPaidRouteContract.AuditGameId,
            null, operation.OperationId, null, null, "sanitized", "direct"));

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, operation, true, default));
        Assert.Equal(operation, (await service.ReadStateAsync(Account, default)).PendingOperation);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        var conflict = await Assert.ThrowsAsync<WukongRoundConflictException>(() => service.SpinAsync(Account,
            operation with { OperationId = "wukong-not-a-second-charge" }, true, default));
        Assert.Equal(operation, conflict.PendingRequest);
        await Assert.ThrowsAsync<WukongRoundConflictException>(() =>
            service.SpinAsync(Account, operation with { WagerPoints = Wager + 2 }, true, default));

        var restored = Service(database, wallet, new NoDraws(), time);
        var result = await restored.SpinAsync(Account, operation, false, default);
        var round = Assert.IsType<WukongPreparedRound>(result.Round);
        Assert.Equal(Wager * 25, round.WagerCents);
        var expected = round.Outcome.Payout.Paylines.SelectMany(line => line.Matches)
            .Sum(match => checked((long)Math.Round(match.AmountPoints * 1.5m, MidpointRounding.AwayFromZero)));
        Assert.True(expected > 0);
        Assert.Equal(expected, round.Progress.SettledPayout.TotalPoints);
        Assert.Equal(expected * 25, round.PayoutCents);
        Assert.Equal(round.PayoutCents, wallet.Last!.PayoutCents);
        Assert.True(round.Progress.EnergySettlement.MultiplierApplied);
        Assert.Equal(0, result.Features!.EnergyBalance);
        Assert.Equal(10, result.Features.FreeSpinsRemaining);
        Assert.Equal(101, result.Features.FreeSpinWagerPoints);
        Assert.Equal("sync", result.Features.FreeSpinFeatureMode);
        Assert.Equal(10, round.Progress.After.NativeRemaining);
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        Assert.Equal(result.Wallet, (await restored.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var raw = Assert.Single((await database.Collection(WukongRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.DoesNotContain("private-wukong-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);
        Assert.True((await Guard(database).GetSnapshotAsync()).ContainsField(WukongRoundStateCodec.OwnerField));
        Assert.False((await database.Collection(SlotSpinGuard.Collection)
            .Document(SlotSpinGuard.DocumentId(Account, WukongEngine.GameId)).GetSnapshotAsync()).Exists);

        time.Advance();
        var bonus = await Service(database, wallet, new Draws([]), time).SpinAsync(Account,
            new("wukong-first-odd-native-quest", 101, true), true, default);
        Assert.Equal(101, bonus.Round!.Outcome.WagerPoints);
        Assert.Equal("sync", bonus.Round.Outcome.Audit.FeatureMode);
        Assert.Equal(9, bonus.Features!.FreeSpinsRemaining);
        Assert.Equal(0, bonus.ChargedWagerCents);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
    }

    [WukongStoreEmulatorFact]
    public async Task SavedOddLegacyLockAndBoostSpendStayOnTheSameLegacyBank()
    {
        var database = Connect();
        const long oldWager = 31;
        const int oldSpecial = 12;
        var wallet = new Wallet { LegacyRemaining = 2, LoseNextReply = true };
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = oldWager * 25, ["freeSpinFeatureMode"] = "rows",
            ["consecutiveFiveMisses"] = 2L,
        });
        var points = LegacyBalance(database, "specialPoints");
        await points.CreateAsync(LegacyFields("specialPoints", oldSpecial));

        var service = Service(database, wallet, new Draws([]));
        var request = new WukongRoundRequest("wukong-odd-legacy-locks", oldWager, true, true);
        await Assert.ThrowsAsync<WukongFreeWagerLockedException>(() => service.SpinAsync(Account,
            request with { OperationId = "wukong-old-wager-mismatch", WagerPoints = 100 }, true, default));
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, request, true, default));
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.Equal(oldSpecial, (await service.ReadStateAsync(Account, default)).SpecialPointsBalance);
        var result = await Service(database, wallet, new NoDraws()).SpinAsync(Account, request, false, default);
        var payout = result.Round!.Outcome;
        Assert.True(payout.SpecialBoostApplied);
        Assert.All(payout.Reels, reel => Assert.Equal(6, reel.Count));
        Assert.Equal("rows", payout.Audit.FeatureMode);
        Assert.Equal(oldWager, payout.WagerPoints);
        Assert.Equal(0, result.ChargedWagerCents);
        Assert.Equal(1, result.Features!.FreeSpinsRemaining);
        Assert.Equal(oldSpecial - WukongEngine.CreateGame().SpecialPoints!.ActivationCost + payout.SpecialPointsAwarded,
            result.Features.SpecialPointsBalance);
        Assert.Equal(2, wallet.Last!.ExpectedLegacyFreeGamesRemaining);
        Assert.True(wallet.Last.SpendLegacyFreeGame);
        Assert.Equal(oldSpecial, (await points.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(1, wallet.Approvals);
    }

    [WukongStoreEmulatorFact]
    public async Task LegacyCreditWithoutLockAcceptsFirstLegalEvenWagerAndPinsIt()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 3 };
        var service = Service(database, wallet, new Draws([]));
        var imported = await service.ReadStateAsync(Account, default);
        Assert.Equal(3, imported.FreeSpinsRemaining);
        Assert.Null(imported.FreeSpinWagerPoints);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SpinAsync(Account,
            new("wukong-unpinned-odd-rejected", 101, true), true, default));
        Assert.Equal(0, wallet.Calls);

        var result = await service.SpinAsync(Account, new("wukong-unpinned-new-lock", Wager, true), true, default);
        Assert.Equal(2, result.Features!.FreeSpinsRemaining);
        Assert.Equal(Wager, result.Features.FreeSpinWagerPoints);
        Assert.Equal(2, wallet.LegacyRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(Wager, (await service.ReadStateAsync(Account, default)).FreeSpinWagerPoints);
    }

    [WukongStoreEmulatorFact]
    public async Task NewSealAwardCoalescesTheLegacyAndNativeBanksAtTheNewOddWager()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2 };
        var time = new RoundClock();
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = Wager * 25,
            ["sealCounts"] = new Dictionary<string, object> { ["paw"] = 39L },
            ["sealWagerCents"] = new Dictionary<string, object> { ["paw"] = 3_940L * 25 },
        });
        await LegacyBalance(database, "energy").CreateAsync(LegacyFields("energy", 100));

        var awarded = await Service(database, wallet, WinningDraws(), time).SpinAsync(Account,
            new("wukong-merge-legacy-native", Wager, true), true, default);
        Assert.Equal(11, awarded.Features!.FreeSpinsRemaining);
        Assert.Equal(101, awarded.Features.FreeSpinWagerPoints);
        Assert.Equal("paw", awarded.Features.FreeSpinFeatureMode);
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.Equal(10, awarded.Round!.Progress.After.NativeRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);

        time.Advance();
        var lastLegacy = await Service(database, wallet, new Draws([]), time).SpinAsync(Account,
            new("wukong-coalesced-last-legacy", 101, true), true, default);
        Assert.Equal("paw", lastLegacy.Round!.Outcome.Audit.FeatureMode);
        Assert.Equal(10, lastLegacy.Features!.FreeSpinsRemaining);
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);

        time.Advance();
        var firstNative = await Service(database, wallet, new Draws([]), time).SpinAsync(Account,
            new("wukong-coalesced-first-native", 101, true), true, default);
        Assert.Equal("paw", firstNative.Round!.Outcome.Audit.FeatureMode);
        Assert.Equal(9, firstNative.Features!.FreeSpinsRemaining);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
    }

    [WukongStoreEmulatorFact]
    public async Task WalletCountMismatchAtFirstAdoptionLeavesTheImportedCreditRecoverable()
    {
        var database = Connect();
        var wallet = new Wallet { ChangeBeforeSettle = 1 };
        var service = Service(database, wallet, new Draws([]));
        var operation = new WukongRoundRequest("wukong-initial-count-mismatch", Wager, false);
        var result = await service.SpinAsync(Account, operation, true, default);
        Assert.True(result.Wallet.LegacyFreeGamesCountMismatch);
        Assert.Null(result.Round);
        Assert.Null(result.Features);
        Assert.Equal(0, wallet.Approvals);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.False((await Guard(database).GetSnapshotAsync()).ContainsField(WukongRoundStateCodec.OwnerField));
        var available = await service.ReadStateAsync(Account, default);
        Assert.Equal(1, available.FreeSpinsRemaining);
        Assert.Null(available.FreeSpinWagerPoints);
        Assert.Equal(result.Wallet, (await service.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(1, wallet.Calls);
    }

    [WukongStoreEmulatorFact]
    public async Task UnresolvedHistoricalLeaseAndUntrustedOddCashCannotDrawOrSpend()
    {
        var database = Connect();
        var wallet = new Wallet();
        var service = Service(database, wallet, new NoDraws());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SpinAsync(Account,
            new("wukong-odd-cash-unsupported", 101, false), true, default));
        await Assert.ThrowsAsync<WukongInsufficientSpecialPointsException>(() => service.SpinAsync(Account,
            new("wukong-unfunded-special-boost", Wager, false, true), true, default));
        await Assert.ThrowsAsync<WukongRoundPausedException>(() => service.SpinAsync(Account,
            new("wukong-pause-new-round", Wager, false), false, default));

        var oldAdmission = Guid.NewGuid().ToString("N");
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            [SlotSpinGuard.ActiveAdmissionField] = oldAdmission,
            [SlotSpinGuard.LastSpinStartedField] = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-1))
        });
        var blocked = new WukongRoundRequest("wukong-cannot-bypass-old-lease", Wager, false);
        await Assert.ThrowsAsync<FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException>(
            () => service.SpinAsync(Account, blocked, true, default));
        await Assert.ThrowsAsync<FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException>(
            () => service.SpinAsync(Account, blocked, false, default));
        Assert.Equal(oldAdmission, (await Guard(database).GetSnapshotAsync()).GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
        Assert.Equal(0, wallet.Calls);
    }

    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"wukong-native-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static WukongRoundService Service(FirestoreDb database, Wallet wallet, IGameRandomSource random, TimeProvider? time = null) =>
        new(new WukongRoundStore(database, random, time ?? TimeProvider.System), wallet, wallet);

    private static DocumentReference Guard(FirestoreDb database) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Account, WukongEngine.LegacyStorageGameId));

    private static string Currency(string name) => $"{name}:{WukongEngine.LegacyStorageGameId}";
    private static DocumentReference LegacyBalance(FirestoreDb database, string name) =>
        database.Collection("userBalances").Document($"{Account}_{Currency(name)}");
    private static Dictionary<string, object> LegacyFields(string name, long available) =>
        new() { ["userId"] = Account, ["currencyId"] = Currency(name), ["available"] = available };

    private static Draws WinningDraws()
    {
        var stops = WukongEngine.CreateReelSet().Reels.Select(strip => strip.IndexOf("7")).ToArray();
        return new Draws([.. stops, 0, 776, 13, 0, 99]);
    }

    private sealed class Draws(int[] indexes) : IGameRandomSource
    {
        private int cursor;
        private readonly Random fallback = new(974);
        public int Next(int maximumExclusive) => cursor < indexes.Length ? indexes[cursor++] : fallback.Next(maximumExclusive);
    }

    private sealed class NoDraws : IGameRandomSource
    {
        public int Next(int maximumExclusive) => throw new InvalidOperationException("A frozen or invalid operation cannot draw new reels.");
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
            var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(WukongEngine.GameId, accountId, request.OperationId),
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

public sealed class WukongStoreEmulatorFactAttribute : FactAttribute
{
    public WukongStoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for Wukong paid round persistence checks.";
    }
}
