using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.RainbowRealm.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.RainbowRealm.Tests;

public sealed class RainbowRoundStoreTests
{
    private const string Account = "rainbow-native-player";
    private const string GameId = RainbowRealmEngine.GameId;
    private const long Wager = 100;

    [RainbowStoreEmulatorFact]
    public async Task BasketHarvestAndOddCharmWagerRecoverAfterACommittedWalletReplyIsLost()
    {
        var database = Connect();
        var wallet = new Wallet { LoseNextReply = true, ReceiptOffset = TimeSpan.FromSeconds(-5) };
        var time = new RoundClock();
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["sealCounts"] = new Dictionary<string, object> { ["sync"] = 59L },
            ["sealWagerCents"] = new Dictionary<string, object> { ["sync"] = 5_960L * 25 },
        });
        await LegacyBalance(database, "energy").CreateAsync(LegacyFields("energy", 100));
        var operation = new RainbowRoundRequest("rainbow-original-harvest-001", Wager, false);
        var service = Service(database, wallet, HarvestDraws(), time);
        using var signed = GameSecurityContext.Push(new("rainbow-first", Account, "private-rainbow-session", "203.0.113.18",
            DateTime.UtcNow, "POST", RainbowPaidRouteContract.SpinPath, RainbowPaidRouteContract.AuditGameId,
            null, operation.OperationId, null, null, "sanitized", "direct"));

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, operation, true, default));
        var pending = await service.ReadStateAsync(Account, default);
        Assert.Equal(operation, pending.PendingOperation);
        Assert.Equal(59, pending.SealCollections.Single(track => track.SealId == "sync").Count);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        var conflict = await Assert.ThrowsAsync<RainbowRoundConflictException>(() => service.SpinAsync(Account,
            operation with { OperationId = "rainbow-would-be-second" }, true, default));
        Assert.Equal(operation, conflict.PendingRequest);
        await Assert.ThrowsAsync<RainbowRoundConflictException>(() =>
            service.SpinAsync(Account, operation with { WagerPoints = Wager + 2 }, true, default));

        var restored = Service(database, wallet, new NoDraws(), time);
        var result = await restored.SpinAsync(Account, operation, false, default);
        var round = Assert.IsType<RainbowPreparedRound>(result.Round);
        Assert.Equal(Wager * 25, round.WagerCents);
        Assert.Equal(1, round.Outcome.PawCount);
        Assert.Equal(150, round.Outcome.CollectorPoints);
        Assert.Equal(round.Outcome.Payout.Paylines.Sum(line => line.AmountPoints) + 150, round.Outcome.Payout.TotalPoints);
        Assert.Equal(round.Outcome.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.Equal(round.PayoutCents, wallet.Last!.PayoutCents);
        Assert.Equal(0, round.Outcome.Audit.EnergyBalanceBefore);
        Assert.Equal(0, result.Features!.EnergyBalance);
        Assert.Equal(100, (await LegacyBalance(database, "energy").GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(10, result.Features.FreeSpinsRemaining);
        Assert.Equal(101, result.Features.FreeSpinWagerPoints);
        Assert.Equal("sync", result.Features.FreeSpinFeatureMode);
        Assert.Equal(10, round.Progress.After.NativeRemaining);
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        Assert.Equal(result.Wallet, (await restored.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var raw = Assert.Single((await database.Collection(RainbowRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.DoesNotContain("private-rainbow-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);

        time.Advance();
        var bonus = await Service(database, wallet, QuietSyncDraws(), time).SpinAsync(Account,
            new("rainbow-first-odd-native-spin", 101, true), true, default);
        Assert.Equal(101, bonus.Round!.Outcome.WagerPoints);
        Assert.Equal("sync", bonus.Round.Outcome.Audit.FeatureMode);
        Assert.Equal(9, bonus.Features!.FreeSpinsRemaining);
        Assert.Equal(0, bonus.ChargedWagerCents);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
    }

    [RainbowStoreEmulatorFact]
    public async Task ScatterAwardKeepsTheTriggerWagerAndNoFeatureMode()
    {
        var database = Connect();
        var wallet = new Wallet();
        var time = new RoundClock();
        var strips = RainbowRealmEngine.CreateReelSet().Reels;
        var stops = strips.Select((strip, reel) => reel < 3 ? strip.IndexOf("FREE") :
            Enumerable.Range(0, strip.Count).First(stop => Window(strip, stop).All(symbol => symbol != "FREE"))).ToArray();
        var freeCount = stops.Select((stop, reel) => Window(strips[reel], stop).Count(symbol => symbol == "FREE")).Sum();
        var paid = Service(database, wallet, new Draws([.. stops, .. Enumerable.Repeat(0, freeCount), 776, 16, 0, 99]), time);
        var result = await paid.SpinAsync(Account, new("rainbow-native-scatter-paid", Wager, false), true, default);
        Assert.Equal(10, result.Round!.Outcome.FreeSpinsAwarded);
        Assert.Equal(0, result.Round.Progress.CollectionFreeSpinsAwarded);
        Assert.Equal(10, result.Features!.FreeSpinsRemaining);
        Assert.Equal(Wager, result.Features.FreeSpinWagerPoints);
        Assert.Null(result.Features.FreeSpinFeatureMode);
        Assert.Equal(0, wallet.LegacyRemaining);

        time.Advance();
        var free = Service(database, wallet, new Draws([.. stops, .. Enumerable.Repeat(0, freeCount), 776, 16, 0, 99]), time);
        var retrigger = await free.SpinAsync(Account, new("rainbow-native-scatter-retrigger", Wager, true), true, default);
        Assert.Equal(10, retrigger.Round!.Outcome.FreeSpinsAwarded);
        Assert.Equal(19, retrigger.Features!.FreeSpinsRemaining);
        Assert.Null(retrigger.Features.FreeSpinFeatureMode);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
    }

    [RainbowStoreEmulatorFact]
    public async Task OldBonusAndNewCharmAwardCoalesceAndDrainLegacyBeforeNative()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2 };
        var time = new RoundClock();
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = Wager * 25,
            ["sealCounts"] = new Dictionary<string, object> { ["sync"] = 59L },
            ["sealWagerCents"] = new Dictionary<string, object> { ["sync"] = 5_960L * 25 },
        });
        var awarded = await Service(database, wallet, HarvestDraws(), time).SpinAsync(Account,
            new("rainbow-merge-legacy-native", Wager, true), true, default);
        Assert.Equal(11, awarded.Features!.FreeSpinsRemaining);
        Assert.Equal(101, awarded.Features.FreeSpinWagerPoints);
        Assert.Equal("sync", awarded.Features.FreeSpinFeatureMode);
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.Equal(10, awarded.Round!.Progress.After.NativeRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);

        time.Advance();
        var next = Service(database, wallet, QuietSyncDraws(), time);
        await Assert.ThrowsAsync<RainbowFreeWagerLockedException>(() => next.SpinAsync(Account,
            new("rainbow-old-lock-rejected", Wager, true), true, default));
        var lastLegacy = await next.SpinAsync(Account, new("rainbow-coalesced-last-legacy", 101, true), true, default);
        Assert.Equal(10, lastLegacy.Features!.FreeSpinsRemaining);
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);

        time.Advance();
        var firstNative = await Service(database, wallet, QuietSyncDraws(), time).SpinAsync(Account,
            new("rainbow-coalesced-first-native", 101, true), true, default);
        Assert.Equal(9, firstNative.Features!.FreeSpinsRemaining);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
    }

    [RainbowStoreEmulatorFact]
    public async Task HistoricalCreditWithoutWagerPinsFirstEvenWagerAndConsumesImportedBoost()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 3 };
        const int oldSpecial = 12;
        await LegacyBalance(database, "specialPoints").CreateAsync(LegacyFields("specialPoints", oldSpecial));
        var service = Service(database, wallet, new Draws([]));
        var imported = await service.ReadStateAsync(Account, default);
        Assert.Equal(3, imported.FreeSpinsRemaining);
        Assert.Equal(oldSpecial, imported.SpecialPointsBalance);
        Assert.Null(imported.FreeSpinWagerPoints);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SpinAsync(Account,
            new("rainbow-unpinned-odd-rejected", 101, true), true, default));
        Assert.Equal(0, wallet.Calls);

        var result = await service.SpinAsync(Account, new("rainbow-unpinned-new-lock", Wager, true, true), true, default);
        Assert.Equal(2, result.Features!.FreeSpinsRemaining);
        Assert.Equal(Wager, result.Features.FreeSpinWagerPoints);
        Assert.True(result.Round!.Outcome.SpecialBoostApplied);
        Assert.Equal(oldSpecial - RainbowRealmEngine.CreateGame().SpecialPoints!.ActivationCost +
            result.Round.Outcome.SpecialPointsAwarded, result.Features.SpecialPointsBalance);
        Assert.Equal(2, wallet.LegacyRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(oldSpecial, (await LegacyBalance(database, "specialPoints").GetSnapshotAsync()).GetValue<long>("available"));
    }

    [RainbowStoreEmulatorFact]
    public async Task InitialWalletCountMismatchLeavesHistoricCarryWritableByAValidRetry()
    {
        var database = Connect();
        var wallet = new Wallet { ChangeBeforeSettle = 1 };
        var service = Service(database, wallet, new Draws([]));
        var operation = new RainbowRoundRequest("rainbow-stale-legacy-cas-001", Wager, false);
        var declined = await service.SpinAsync(Account, operation, true, default);
        Assert.True(declined.Wallet.LegacyFreeGamesCountMismatch);
        Assert.Null(declined.Round);
        Assert.Null(declined.Features);
        Assert.Equal(0, wallet.Approvals);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.False((await Guard(database).GetSnapshotAsync()).ContainsField(RainbowRoundStateCodec.OwnerField));
        var available = await service.ReadStateAsync(Account, default);
        Assert.Equal(1, available.FreeSpinsRemaining);
        Assert.Null(available.FreeSpinWagerPoints);
        Assert.Equal(declined.Wallet, (await service.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(1, wallet.Calls);
    }

    [RainbowStoreEmulatorFact]
    public async Task OldLeaseAndOddCashInputCannotDrawOrSpend()
    {
        var database = Connect();
        var wallet = new Wallet();
        var service = Service(database, wallet, new NoDraws());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SpinAsync(Account,
            new("rainbow-odd-cash-rejected", 101, false), true, default));
        await Assert.ThrowsAsync<RainbowInsufficientSpecialPointsException>(() => service.SpinAsync(Account,
            new("rainbow-unfunded-special-boost", Wager, false, true), true, default));
        await Assert.ThrowsAsync<RainbowRoundPausedException>(() => service.SpinAsync(Account,
            new("rainbow-paused-cash-play", Wager, false), false, default));

        var legacy = Guid.NewGuid().ToString("N");
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            [SlotSpinGuard.ActiveAdmissionField] = legacy,
            [SlotSpinGuard.LastSpinStartedField] = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-1))
        });
        var blocked = new RainbowRoundRequest("rainbow-held-old-admission", Wager, false);
        await Assert.ThrowsAsync<FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException>(
            () => service.SpinAsync(Account, blocked, true, default));
        await Assert.ThrowsAsync<FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException>(
            () => service.SpinAsync(Account, blocked, false, default));
        Assert.Equal(legacy, (await Guard(database).GetSnapshotAsync()).GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
        Assert.Equal(0, wallet.Calls);
    }

    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"rainbow-native-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static RainbowRoundService Service(FirestoreDb database, Wallet wallet, IGameRandomSource random, TimeProvider? time = null) =>
        new(new RainbowRoundStore(database, random, time ?? TimeProvider.System), wallet, wallet);

    private static DocumentReference Guard(FirestoreDb database) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Account, GameId));

    private static string Currency(string name) => $"{name}:{GameId}";
    private static DocumentReference LegacyBalance(FirestoreDb database, string name) =>
        database.Collection("userBalances").Document($"{Account}_{Currency(name)}");
    private static Dictionary<string, object> LegacyFields(string name, long available) =>
        new() { ["userId"] = Account, ["currencyId"] = Currency(name), ["available"] = available };

    private static IEnumerable<string> Window(IReadOnlyList<string> strip, int stop) =>
        Enumerable.Range(0, 4).Select(offset => strip[(stop + offset) % strip.Count]);

    private static Draws HarvestDraws()
    {
        var strips = RainbowRealmEngine.CreateReelSet().Reels;
        var stops = strips.Select((strip, reel) => Enumerable.Range(0, strip.Count).First(stop =>
        {
            var cells = Window(strip, stop).ToArray();
            var charms = cells.Where(symbol => symbol.StartsWith("SEAL_", StringComparison.Ordinal)).ToArray();
            return !cells.Contains("FREE") && (reel == 0 ? charms.SequenceEqual(["SEAL_SYNC"]) : charms.Length == 0);
        })).ToArray();
        return new Draws([.. stops, 776, 0, 0, 0, 99]);
    }

    private static Draws QuietSyncDraws()
    {
        var strips = RainbowRealmEngine.CreateReelSet().Reels;
        var stops = strips.Select(strip => Enumerable.Range(0, strip.Count).First(stop =>
            Window(strip, stop).All(symbol => symbol != "FREE" && !symbol.StartsWith("SEAL_", StringComparison.Ordinal))));
        return new Draws([.. stops, 0, 0, 776, 16, 0, 99]);
    }

    private sealed class Draws(int[] indexes) : IGameRandomSource
    {
        private int cursor;
        private readonly Random fallback = new(217);
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

public sealed class RainbowStoreEmulatorFactAttribute : FactAttribute
{
    public RainbowStoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for Rainbow Realm paid round persistence checks.";
    }
}
