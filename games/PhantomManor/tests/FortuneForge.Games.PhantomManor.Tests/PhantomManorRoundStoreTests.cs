using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.PhantomManor.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.PhantomManor.Tests;

public sealed class PhantomManorRoundStoreTests
{
    private const string Account = "phantom-manor-native-player";
    private const string GameId = PhantomManorEngine.GameId;

    [PhantomManorStoreEmulatorFact]
    public async Task PaidScatterResumesExactlyWithDormantEnergyAndSeals()
    {
        var database = Connect();
        var wallet = new Wallet { LoseNextReply = true };
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["sealCounts"] = new Dictionary<string, long> { ["rows"] = 12 },
            ["sealWagerCents"] = new Dictionary<string, long> { ["rows"] = 12 * 10 * 25 },
            ["consecutiveFiveMisses"] = 5L
        });
        await SeedBalance(database, "energy", 100);
        var operation = new PhantomManorRoundRequest("phantom-manor-paid-collector-001", 10, false);
        var service = Service(database, wallet, ScatterAndValueDraws());
        using var signed = GameSecurityContext.Push(new("phantom-manor-request-1", Account, "private-phantom-manor-session",
            "203.0.113.19", DateTime.UtcNow, "POST", PhantomManorPaidRouteContract.SpinPath, PhantomManorPaidRouteContract.AuditGameId,
            null, operation.OperationId, null, null, "sanitized", "direct"));

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, operation, true, default));
        var pending = await service.ReadStateAsync(Account, default);
        Assert.Equal(operation, pending.PendingOperation);
        Assert.Equal(100, pending.EnergyBalance);
        Assert.Empty(pending.SealCollections);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);

        var restored = Service(database, wallet, new NoDraws());
        var conflict = await Assert.ThrowsAsync<PhantomManorRoundConflictException>(() => restored.SpinAsync(Account,
            operation with { OperationId = "phantom-manor-unrelated-cash" }, true, default));
        Assert.Equal(operation, conflict.PendingRequest);
        await Assert.ThrowsAsync<PhantomManorRoundConflictException>(() => restored.SpinAsync(Account,
            operation with { WagerPoints = 12 }, true, default));

        var result = await restored.SpinAsync(Account, operation, false, default);
        var round = Assert.IsType<PhantomManorPreparedRound>(result.Round);
        Assert.Equal(23, round.Outcome.Audit.EffectiveGame.Paylines.Count);
        Assert.Equal(1.139m, round.Outcome.Audit.EffectiveGame.Math.PayoutMultiplier);
        Assert.Equal(100, round.Outcome.Audit.EnergyBalanceBefore);
        Assert.Equal(5, round.Outcome.Audit.PityMissesBefore);
        Assert.Equal(0, round.Outcome.CollectorPoints);
        Assert.DoesNotContain(round.Outcome.Payout.Paylines, line => line.PaylineId == 901);
        Assert.Equal(5, round.Outcome.FreeSpinsAwarded);
        Assert.Empty(round.Outcome.CollectionsAwarded);
        Assert.Equal(0, result.Features!.EnergyBalance);
        Assert.Empty(result.Features.SealCollections);
        Assert.Equal(12, round.Progress.After.SealCounts["rows"]);
        Assert.Equal(120, round.Progress.After.SealWagerPoints["rows"]);
        Assert.Equal(5, result.Features.FreeSpinsRemaining);
        Assert.Equal("paw", result.Features.FreeSpinFeatureMode);
        Assert.Equal(10, result.Features.FreeSpinWagerPoints);
        Assert.Equal(250, result.ChargedWagerCents);
        Assert.Equal(round.Outcome.Payout.TotalPoints * 25, wallet.Last!.PayoutCents);
        Assert.Equal(round.Outcome.SpinId.ToString("N"), wallet.Last.OperationId);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        Assert.Equal(result.Wallet, (await restored.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(2, wallet.Calls);
        var history = Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Equal(round.Outcome.SpinId.ToString("N"), history.Id);
        Assert.Equal(GameId, history.GetValue<string>("gameId"));
        Assert.Equal((double)(round.PayoutCents / 100m), history.GetValue<double>("wonSlotsCredits"));
        Assert.Equal((double)(round.WagerCents / 100m), history.GetValue<double>("wageredSlotsCredits"));
        Assert.Equal(round.OutcomeSha256, history.GetValue<string>("engineOutcomeSha256"));
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var raw = Assert.Single((await database.Collection(PhantomManorRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.DoesNotContain("private-phantom-manor-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);
        Assert.Equal(GameId, (await Guard(database).GetSnapshotAsync()).GetValue<string>(PhantomManorRoundStateCodec.OwnerField));
    }

    [PhantomManorStoreEmulatorFact]
    public async Task AFreeRetriggerKeepsTheOddSavedWagerAndUsesAuthoredPawModeBeforeNative()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2 };
        var time = new RoundClock();
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 75L, ["freeSpinFeatureMode"] = "sync"
        });
        var service = Service(database, wallet, ScatterAndValueDraws(sync: true), time);
        await Assert.ThrowsAsync<PhantomManorFreeSpinsRequiredException>(() => service.SpinAsync(Account,
            new("phantom-manor-cash-blocked", 6, false), true, default));
        await Assert.ThrowsAsync<PhantomManorFreeWagerLockedException>(() => service.SpinAsync(Account,
            new("phantom-manor-wrong-carry", 4, true), true, default));
        Assert.Equal(0, wallet.Calls);

        var first = await service.SpinAsync(Account, new("phantom-manor-legacy-retrigger", 3, true), true, default);
        Assert.Equal(5, first.Round!.Outcome.FreeSpinsAwarded);
        Assert.Equal(6, first.Features!.FreeSpinsRemaining);
        Assert.Equal(3, first.Features.FreeSpinWagerPoints);
        Assert.Equal("paw", first.Features.FreeSpinFeatureMode);
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, first.ChargedWagerCents);

        time.Advance();
        var lastLegacy = await Service(database, wallet, new HighDraws(), time).SpinAsync(Account,
            new("phantom-manor-last-legacy", 3, true), true, default);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.Equal(5, lastLegacy.Features!.FreeSpinsRemaining);
        Assert.Equal("paw", lastLegacy.Round!.Outcome.Audit.FeatureMode);

        time.Advance();
        var native = await Service(database, wallet, new HighDraws(), time).SpinAsync(Account,
            new("phantom-manor-first-native", 3, true), true, default);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
        Assert.Equal(4, native.Features!.FreeSpinsRemaining);
        Assert.Null(native.Wallet.WagerTransactionId);
    }

    [PhantomManorStoreEmulatorFact]
    public async Task BoostConsumesTheExactGamePointsAndLeavesTheOldScopedBalanceArchived()
    {
        var database = Connect();
        var wallet = new Wallet();
        var service = Service(database, wallet, new HighDraws());
        await SeedBalance(database, "specialPoints", 9);
        var result = await service.SpinAsync(Account, new("phantom-manor-boosted-paid", 6, false, true), true, default);
        var outcome = result.Round!.Outcome;
        Assert.True(outcome.SpecialBoostApplied);
        Assert.EndsWith("-power-boost", outcome.ReelSetId, StringComparison.Ordinal);
        Assert.Equal(1 + outcome.SpecialPointsAwarded, result.Features!.SpecialPointsBalance);
        Assert.Equal(9, (await LegacyBalance(database, "specialPoints").GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(150, result.ChargedWagerCents);
    }

    [PhantomManorStoreEmulatorFact]
    public async Task LostLegacyFreeGameReplyRecoversAfterTheSignedWalletCountHasAlreadyDecreased()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2, LoseNextReply = true };
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 75L,
            ["freeSpinFeatureMode"] = "paw"
        });
        await SeedBalance(database, "energy", 96);
        var operation = new PhantomManorRoundRequest("phantom-free-lost-reply-001", 3, true);
        var service = Service(database, wallet, ScatterAndValueDraws(sync: true));

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, operation, true, default));
        Assert.Equal(1, wallet.LegacyRemaining);
        var restarted = Service(database, wallet, new NoDraws());
        var pending = await restarted.ReadStateAsync(Account, default);
        Assert.Equal(operation, pending.PendingOperation);
        Assert.Equal(2, pending.FreeSpinsRemaining);
        Assert.Equal(96, pending.EnergyBalance);

        var result = await restarted.SpinAsync(Account, operation, false, default);
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        Assert.Equal(0, result.ChargedWagerCents);
        Assert.Equal(6, result.Features!.FreeSpinsRemaining);
        Assert.Equal("paw", result.Features.FreeSpinFeatureMode);
        Assert.Equal(0, result.Features.EnergyBalance);
        Assert.Equal(result.Wallet, (await restarted.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
    }

    [PhantomManorStoreEmulatorFact]
    public async Task LegacyCountChangeDeclinesTheExactCashRoundAndOnlyEvenWagersCanPinAnUnlockedBank()
    {
        var database = Connect();
        await SeedBalance(database, "energy", 99);
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["sealCounts"] = new Dictionary<string, long> { ["paw"] = 20 },
            ["sealWagerCents"] = new Dictionary<string, long> { ["paw"] = 200 * 25 }
        });
        var wallet = new Wallet { ChangeBeforeSettle = 2 };
        var time = new RoundClock();
        var operation = new PhantomManorRoundRequest("phantom-manor-stale-cash-001", 6, false);
        var service = Service(database, wallet, ScatterAndValueDraws(), time);
        var declined = await service.SpinAsync(Account, operation, true, default);
        Assert.True(declined.Wallet.LegacyFreeGamesCountMismatch);
        Assert.Null(declined.Round);
        Assert.Null(declined.Features);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.False((await Guard(database).GetSnapshotAsync()).ContainsField(PhantomManorRoundStateCodec.OwnerField));
        var refreshed = await service.ReadStateAsync(Account, default);
        Assert.Equal(2, refreshed.FreeSpinsRemaining);
        Assert.Equal(99, refreshed.EnergyBalance);
        Assert.Equal(99, (await LegacyBalance(database, "energy").GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(20, (await Guard(database).GetSnapshotAsync()).GetValue<Dictionary<string, long>>("sealCounts")["paw"]);
        Assert.Null(refreshed.FreeSpinWagerPoints);
        Assert.Equal(declined.Wallet, (await Service(database, wallet, new NoDraws()).SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(1, wallet.Calls);

        time.Advance();
        var unpinned = Service(database, wallet, new HighDraws(), time);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => unpinned.SpinAsync(Account,
            new("phantom-manor-unpinned-odd", 5, true), true, default));
        var pinned = await unpinned.SpinAsync(Account, new("phantom-manor-pin-six-001", 6, true), true, default);
        Assert.Equal(6, pinned.Features!.FreeSpinWagerPoints);
        Assert.Equal(1, pinned.Features.FreeSpinsRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
    }

    [PhantomManorStoreEmulatorFact]
    public async Task AnUnplayableOnePointCarryRemainsVisibleWithoutDebitingOrClaimingTheGame()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2 };
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 25L,
            ["freeSpinFeatureMode"] = "paw"
        });
        var service = Service(database, wallet, new NoDraws());

        var state = await service.ReadStateAsync(Account, default);
        Assert.Equal(1, state.FreeSpinWagerPoints);
        Assert.Equal("paw", state.FreeSpinFeatureMode);
        var error = await Assert.ThrowsAsync<PhantomManorLegacyFreeWagerUnplayableException>(() =>
            service.SpinAsync(Account, new("phantom-one-point-carry-001", 1, true), true, default));
        Assert.Equal(1, error.WagerPoints);
        Assert.Equal(0, wallet.Calls);
        Assert.Equal(2, wallet.LegacyRemaining);
        Assert.False((await Guard(database).GetSnapshotAsync()).ContainsField(PhantomManorRoundStateCodec.OwnerField));
    }

    [PhantomManorStoreEmulatorFact]
    public async Task LegacyAdmissionAlreadyInFlightCannotBeClaimedByNewWorker()
    {
        var database = Connect();
        var wallet = new Wallet();
        var oldAdmission = Guid.NewGuid().ToString("N");
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            [SlotSpinGuard.ActiveAdmissionField] = oldAdmission
        });
        await Assert.ThrowsAsync<FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException>(() =>
            Service(database, wallet, new NoDraws()).SpinAsync(Account,
                new("phantom-manor-old-host-admission", 6, false), true, default));
        var guard = await Guard(database).GetSnapshotAsync();
        Assert.Equal(oldAdmission, guard.GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
        Assert.False(guard.ContainsField(PhantomManorRoundStateCodec.OwnerField));
        Assert.Equal(0, wallet.Calls);
        Assert.Empty((await database.Collection(PhantomManorRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
    }

    [PhantomManorStoreEmulatorFact]
    public async Task PausedLegacyAdmissionRemainsDiscoverableForHostReconciliation()
    {
        var database = Connect();
        var wallet = new Wallet();
        var oldAdmission = Guid.NewGuid().ToString("N");
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            [SlotSpinGuard.ActiveAdmissionField] = oldAdmission
        });
        await Assert.ThrowsAsync<FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException>(() =>
            Service(database, wallet, new NoDraws()).SpinAsync(Account,
                new("phantom-manor-old-host-admission", 6, false), false, default));
        var guard = await Guard(database).GetSnapshotAsync();
        Assert.Equal(oldAdmission, guard.GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
        Assert.False(guard.ContainsField(PhantomManorRoundStateCodec.OwnerField));
        Assert.Equal(0, wallet.Calls);
        Assert.Empty((await database.Collection(PhantomManorRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
    }

    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"phantom-manor-native-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static PhantomManorRoundService Service(FirestoreDb database, Wallet wallet, IGameRandomSource random, TimeProvider? time = null) =>
        new(new PhantomManorRoundStore(database, random, time ?? TimeProvider.System), wallet, wallet);

    private static DocumentReference Guard(FirestoreDb database) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Account, GameId));

    private static DocumentReference LegacyBalance(FirestoreDb database, string name) =>
        database.Collection("userBalances").Document($"{Account}_{name}:{GameId}");

    private static Task SeedBalance(FirestoreDb database, string name, long value) => LegacyBalance(database, name).CreateAsync(
        new Dictionary<string, object> { ["userId"] = Account, ["currencyId"] = $"{name}:{GameId}", ["available"] = value });

    private static Draws ScatterAndValueDraws(bool sync = false)
    {
        var strips = PhantomManorEngine.CreateReelSet().Reels.Select(strip => strip.Where(symbol => symbol != "BOLT").ToList()).ToArray();
        var rows = PhantomManorEngine.CreateGame().Layout.VisibleRows;
        IEnumerable<string> Window(IReadOnlyList<string> strip, int stop) =>
            Enumerable.Range(0, rows).Select(offset => strip[(stop + offset) % strip.Count]);
        var stops = strips.Select((strip, reel) => reel < 3 ? strip.IndexOf("FREE") :
            Enumerable.Range(0, strip.Count).First(stop => !Window(strip, stop).Contains("FREE"))).ToArray();
        var scatters = stops.Select((stop, reel) => Window(strips[reel], stop).Count(symbol => symbol == "FREE")).Sum();
        int[] optionalSync = sync ? [0, 0] : [];
        return new Draws([.. stops, .. Enumerable.Repeat(0, scatters), .. optionalSync, 0]);
    }

    private sealed class Draws(int[] indexes) : IGameRandomSource
    {
        private int cursor;
        public int Next(int maximumExclusive)
        {
            Assert.InRange(cursor, 0, indexes.Length - 1);
            var draw = indexes[cursor++];
            Assert.InRange(draw, 0, maximumExclusive - 1);
            return draw;
        }
    }

    private sealed class HighDraws : IGameRandomSource
    {
        public int Next(int maximumExclusive) => maximumExclusive - 1;
    }

    private sealed class NoDraws : IGameRandomSource
    {
        public int Next(int maximumExclusive) => throw new InvalidOperationException("A restored spin cannot draw new reels.");
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
        private long balance = 100_000;
        public long LegacyRemaining { get; set; }
        public int Approvals { get; private set; }
        public int Calls { get; private set; }
        public bool LoseNextReply { get; set; }
        public long? ChangeBeforeSettle { get; init; }
        public GameWalletSettlementRequest? Last { get; private set; }

        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult(new GameWalletAccountState(LegacyRemaining, balance));

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
            var mismatch = request.ExpectedLegacyFreeGamesRemaining != LegacyRemaining;
            var startingBalance = balance;
            if (!mismatch)
            {
                balance = startingBalance - request.WagerCents + request.PayoutCents;
                if (request.SpendLegacyFreeGame) LegacyRemaining--;
                Approvals++;
            }
            var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(GameId, accountId, request.OperationId),
                !mismatch && request.WagerCents > 0 ? $"{request.OperationId}-wager" : null,
                !mismatch && request.PayoutCents > 0 ? $"{request.OperationId}-payout" : null,
                startingBalance, mismatch ? startingBalance : startingBalance - request.WagerCents, balance, DateTimeOffset.UtcNow,
                false, false, false, !mismatch && request.SpendLegacyFreeGame ? $"{request.OperationId}-free-game-use" : null,
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

public sealed class PhantomManorStoreEmulatorFactAttribute : FactAttribute
{
    public PhantomManorStoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for Phantom Manor paid round persistence checks.";
    }
}
