using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.RobotRevolution.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.RobotRevolution.Tests;

public sealed class RobotRevolutionRoundStoreTests
{
    private const string Account = "robot-revolution-native-player";
    private const string GameId = RobotRevolutionEngine.GameId;

    [RobotRevolutionStoreEmulatorFact]
    public async Task PaidScatterResumesExactlyWithDormantEnergyAndSeals()
    {
        var database = Connect();
        var wallet = new Wallet { LoseNextReply = true };
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["sealCounts"] = new Dictionary<string, long> { ["rows"] = 24 },
            ["sealWagerCents"] = new Dictionary<string, long> { ["rows"] = 24 * 10 * 25 },
            ["consecutiveFiveMisses"] = 5L
        });
        await SeedBalance(database, "energy", 100);
        var operation = new RobotRevolutionRoundRequest("robot-revolution-paid-collector-001", 12, false);
        var service = Service(database, wallet, ScatterDraws());
        using var signed = GameSecurityContext.Push(new("robot-revolution-request-1", Account, "private-robot-revolution-session",
            "203.0.113.19", DateTime.UtcNow, "POST", RobotRevolutionPaidRouteContract.SpinPath, RobotRevolutionPaidRouteContract.AuditGameId,
            null, operation.OperationId, null, null, "sanitized", "direct"));

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, operation, true, default));
        var pending = await service.ReadStateAsync(Account, default);
        Assert.Equal(operation, pending.PendingOperation);
        Assert.Equal(100, pending.EnergyBalance);
        Assert.Empty(pending.SealCollections);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);

        var restored = Service(database, wallet, new NoDraws());
        var conflict = await Assert.ThrowsAsync<RobotRevolutionRoundConflictException>(() => restored.SpinAsync(Account,
            operation with { OperationId = "robot-revolution-unrelated-cash" }, true, default));
        Assert.Equal(operation, conflict.PendingRequest);
        await Assert.ThrowsAsync<RobotRevolutionRoundConflictException>(() => restored.SpinAsync(Account,
            operation with { WagerPoints = 14 }, true, default));

        var result = await restored.SpinAsync(Account, operation, false, default);
        var round = Assert.IsType<RobotRevolutionPreparedRound>(result.Round);
        Assert.Equal(23, round.Outcome.Audit.EffectiveGame.Paylines.Count);
        Assert.Equal(RobotRevolutionEngine.Profile.PayoutMultiplier, round.Outcome.Audit.EffectiveGame.Math.PayoutMultiplier);
        Assert.Equal(RobotRevolutionEngine.EngineVersion, round.Outcome.Audit.EngineVersion);
        Assert.Equal(100, round.Outcome.Audit.EnergyBalanceBefore);
        Assert.Equal(5, round.Outcome.Audit.PityMissesBefore);
        Assert.Equal(0, round.Outcome.CollectorPoints);
        Assert.DoesNotContain(round.Outcome.Reels.SelectMany(reel => reel), symbol => symbol.StartsWith("RAND_", StringComparison.Ordinal));
        Assert.DoesNotContain(round.Outcome.Payout.Paylines, line => line.PaylineId == 901);
        Assert.Equal(0, round.Outcome.FreeSpinsAwarded);
        Assert.Empty(round.Outcome.CollectionsAwarded);
        Assert.Equal(0, result.Features!.EnergyBalance);
        Assert.Empty(result.Features.SealCollections);
        Assert.Equal(24, round.Progress.After.SealCounts["rows"]);
        Assert.Equal(240, round.Progress.After.SealWagerPoints["rows"]);
        Assert.Equal(0, result.Features.FreeSpinsRemaining);
        Assert.Null(result.Features.FreeSpinFeatureMode);
        Assert.Null(result.Features.FreeSpinWagerPoints);
        Assert.Equal(new FifthSunPaidMode(FifthSunTotalityDefinitions.SolarMode, 5, 12), result.Features.PaidMode);
        Assert.Equal(300, result.ChargedWagerCents);
        Assert.Equal(round.Outcome.Payout.TotalPoints * 25, wallet.Last!.PayoutCents);
        Assert.Equal(round.Outcome.SpinId.ToString("N"), wallet.Last.OperationId);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        Assert.Equal(result.Wallet, (await restored.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var raw = Assert.Single((await database.Collection(RobotRevolutionRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.DoesNotContain("private-robot-revolution-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);
        Assert.Equal(GameId, (await Guard(database).GetSnapshotAsync()).GetValue<string>(RobotRevolutionRoundStateCodec.OwnerField));
    }

    [RobotRevolutionStoreEmulatorFact]
    public async Task PaidEclipseLostReplyRestoresTheSameBoardAndConsumesOneRoundOnce()
    {
        var database = Connect();
        var wallet = new Wallet();
        var time = new RoundClock();
        var trigger = await Service(database, wallet, ScatterDraws(), time).SpinAsync(Account,
            new("totality-store-trigger", 12, false), true, default);
        Assert.Equal(5, trigger.Features!.PaidMode!.Remaining);
        time.Advance();
        wallet.LoseNextReply = true;
        var operation = new RobotRevolutionRoundRequest("totality-store-lost-paid-reply", 12, false);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(database, wallet, new HighDraws(), time)
            .SpinAsync(Account, operation, true, default));
        var restored = Service(database, wallet, new NoDraws(), time);
        var pending = await restored.ReadStateAsync(Account, default);
        Assert.Equal(5, pending.PaidMode!.Remaining);
        Assert.Equal(operation, pending.PendingOperation);
        var result = await restored.SpinAsync(Account, operation, false, default);
        Assert.Equal(300, result.ChargedWagerCents);
        Assert.Equal(4, result.Features!.PaidMode!.Remaining);
        Assert.False(result.Round!.Outcome.IsFreeSpin);
        Assert.Equal(FifthSunTotalityDefinitions.SolarMode, result.Round.Outcome.Audit.FeatureMode);
        var calls = wallet.Calls;
        var repeated = await restored.SpinAsync(Account, operation, false, default);
        Assert.Equal(result.Wallet, repeated.Wallet);
        Assert.Equal(calls, wallet.Calls);
        Assert.Equal(2, wallet.Approvals);
        Assert.Equal(4, (await restored.ReadStateAsync(Account, default)).PaidMode!.Remaining);
    }

    [RobotRevolutionStoreEmulatorFact]
    public async Task AFreeRetriggerKeepsTheOddSavedRowsWagerAndSpendsLegacyBeforeNative()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2 };
        var time = new RoundClock();
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 75L, ["freeSpinFeatureMode"] = "rows"
        });
        var service = Service(database, wallet, ScatterDraws(expandedRows: true), time);
        await Assert.ThrowsAsync<RobotRevolutionFreeSpinsRequiredException>(() => service.SpinAsync(Account,
            new("robot-revolution-cash-blocked", 6, false), true, default));
        await Assert.ThrowsAsync<RobotRevolutionFreeWagerLockedException>(() => service.SpinAsync(Account,
            new("robot-revolution-wrong-carry", 4, true), true, default));
        Assert.Equal(0, wallet.Calls);

        var first = await service.SpinAsync(Account, new("robot-revolution-legacy-retrigger", 3, true), true, default);
        Assert.Equal(7, first.Round!.Outcome.FreeSpinsAwarded);
        Assert.Equal(6, first.Round.Outcome.Audit.EffectiveGame.Layout.VisibleRows);
        Assert.Equal(8, first.Features!.FreeSpinsRemaining);
        Assert.Equal(3, first.Features.FreeSpinWagerPoints);
        Assert.Equal("rows", first.Features.FreeSpinFeatureMode);
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, first.ChargedWagerCents);

        time.Advance();
        var lastLegacy = await Service(database, wallet, new HighDraws(), time).SpinAsync(Account,
            new("robot-revolution-last-legacy", 3, true), true, default);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.Equal(7, lastLegacy.Features!.FreeSpinsRemaining);
        Assert.Equal("rows", lastLegacy.Round!.Outcome.Audit.FeatureMode);

        time.Advance();
        var native = await Service(database, wallet, new HighDraws(), time).SpinAsync(Account,
            new("robot-revolution-first-native", 3, true), true, default);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
        Assert.Equal(6, native.Features!.FreeSpinsRemaining);
        Assert.Null(native.Wallet.WagerTransactionId);
    }

    [RobotRevolutionStoreEmulatorFact]
    public async Task LostLegacyFreeGameReplyResumesAgainstTheAlreadyDecrementedWallet()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2, LoseNextReply = true };
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 75L, ["freeSpinFeatureMode"] = "rows"
        });
        var operation = new RobotRevolutionRoundRequest("robot-revolution-free-lost-reply", 3, true);
        var service = Service(database, wallet, new HighDraws());

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, operation, true, default));
        Assert.Equal(1, wallet.LegacyRemaining);
        var pending = await service.ReadStateAsync(Account, default);
        Assert.Equal(2, pending.FreeSpinsRemaining);
        Assert.Equal(operation, pending.PendingOperation);

        var restored = Service(database, wallet, new NoDraws());
        var result = await restored.SpinAsync(Account, operation, false, default);
        Assert.Equal(0, result.ChargedWagerCents);
        Assert.Equal(1, result.Features!.FreeSpinsRemaining);
        Assert.Equal(3, result.Features.FreeSpinWagerPoints);
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(2, wallet.Last.ExpectedLegacyFreeGamesRemaining);
        Assert.Equal(result.Wallet, (await restored.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
    }

    [RobotRevolutionStoreEmulatorFact]
    public async Task HistoricalInFlightAdmissionBlocksWorkerClaimWithoutCharging()
    {
        var database = Connect();
        var wallet = new Wallet();
        var oldAdmission = Guid.NewGuid().ToString("N");
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            [SlotSpinGuard.ActiveAdmissionField] = oldAdmission
        });
        var service = Service(database, wallet, new NoDraws());

        await Assert.ThrowsAsync<FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException>(() => service.SpinAsync(Account,
            new("robot-revolution-old-host-active", 6, false), true, default));
        Assert.Equal(0, wallet.Calls);
        var guard = await Guard(database).GetSnapshotAsync();
        Assert.Equal(oldAdmission, guard.GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
        Assert.False(guard.ContainsField(RobotRevolutionRoundStateCodec.OwnerField));
        Assert.Empty((await database.Collection(RobotRevolutionRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
    }

    [RobotRevolutionStoreEmulatorFact]
    public async Task PausedLegacyAdmissionRemainsDiscoverableForHostReconciliation()
    {
        var database = Connect();
        var wallet = new Wallet();
        var oldAdmission = Guid.NewGuid().ToString("N");
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            [SlotSpinGuard.ActiveAdmissionField] = oldAdmission
        });
        var service = Service(database, wallet, new NoDraws());

        await Assert.ThrowsAsync<FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException>(() => service.SpinAsync(Account,
            new("robot-revolution-old-host-active", 6, false), false, default));
        Assert.Equal(0, wallet.Calls);
        var guard = await Guard(database).GetSnapshotAsync();
        Assert.Equal(oldAdmission, guard.GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
        Assert.False(guard.ContainsField(RobotRevolutionRoundStateCodec.OwnerField));
        Assert.Empty((await database.Collection(RobotRevolutionRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
    }

    [RobotRevolutionStoreEmulatorFact]
    public async Task BoostConsumesTheExactGamePointsAndLeavesTheOldScopedBalanceArchived()
    {
        var database = Connect();
        var wallet = new Wallet();
        var service = Service(database, wallet, new HighDraws());
        await SeedBalance(database, "specialPoints", 9);
        var result = await service.SpinAsync(Account, new("robot-revolution-boosted-paid", 12, false, true), true, default);
        var outcome = result.Round!.Outcome;
        Assert.True(outcome.SpecialBoostApplied);
        Assert.EndsWith("-power-boost", outcome.ReelSetId, StringComparison.Ordinal);
        Assert.Equal(1 + outcome.SpecialPointsAwarded, result.Features!.SpecialPointsBalance);
        Assert.Equal(9, (await LegacyBalance(database, "specialPoints").GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(300, result.ChargedWagerCents);
    }

    [RobotRevolutionStoreEmulatorFact]
    public async Task LegacyCountChangeDeclinesTheExactCashRoundAndOnlyEvenWagersCanPinAnUnlockedBank()
    {
        var database = Connect();
        var wallet = new Wallet { ChangeBeforeSettle = 2 };
        var time = new RoundClock();
        var operation = new RobotRevolutionRoundRequest("robot-revolution-stale-cash-001", 12, false);
        var service = Service(database, wallet, ScatterDraws(), time);
        var declined = await service.SpinAsync(Account, operation, true, default);
        Assert.True(declined.Wallet.LegacyFreeGamesCountMismatch);
        Assert.Null(declined.Round);
        Assert.Null(declined.Features);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.False((await Guard(database).GetSnapshotAsync()).ContainsField(RobotRevolutionRoundStateCodec.OwnerField));
        var refreshed = await service.ReadStateAsync(Account, default);
        Assert.Equal(2, refreshed.FreeSpinsRemaining);
        Assert.Null(refreshed.FreeSpinWagerPoints);
        Assert.Equal(declined.Wallet, (await Service(database, wallet, new NoDraws()).SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(1, wallet.Calls);

        time.Advance();
        var unpinned = Service(database, wallet, new HighDraws(), time);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => unpinned.SpinAsync(Account,
            new("robot-revolution-unpinned-odd", 5, true), true, default));
        var pinned = await unpinned.SpinAsync(Account, new("robot-revolution-pin-twelve-001", 12, true), true, default);
        Assert.Equal(12, pinned.Features!.FreeSpinWagerPoints);
        Assert.Equal(1, pinned.Features.FreeSpinsRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
    }

    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"robot-revolution-native-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static RobotRevolutionRoundService Service(FirestoreDb database, Wallet wallet, IGameRandomSource random, TimeProvider? time = null) =>
        new(new RobotRevolutionRoundStore(database, random, time ?? TimeProvider.System), wallet, wallet);

    private static DocumentReference Guard(FirestoreDb database) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Account, GameId));

    private static DocumentReference LegacyBalance(FirestoreDb database, string name) =>
        database.Collection("userBalances").Document($"{Account}_{name}:{GameId}");

    private static Task SeedBalance(FirestoreDb database, string name, long value) => LegacyBalance(database, name).CreateAsync(
        new Dictionary<string, object> { ["userId"] = Account, ["currencyId"] = $"{name}:{GameId}", ["available"] = value });

    private static Draws ScatterDraws(bool expandedRows = false)
    {
        if (!expandedRows)
        {
            var centre = RobotRevolutionEngine.CreateReelSet().Reels[2];
            return new Draws([0, 0, centre.IndexOf(FifthSunTotalityDefinitions.SolarSymbol), 0, 0]);
        }
        var definition = expandedRows ? FortuneForge.Games.Slots.Runtime.ClassicSlotDefinitionFactory.Create(RobotRevolutionEngine.LegacyProfile).CreateReelSet()
            : RobotRevolutionEngine.CreateReelSet();
        var scatter = expandedRows ? "FREE" : "ECLIPSE";
        var strips = definition.Reels.Select(strip => strip.Where(symbol => symbol != "BOLT").ToList()).ToArray();
        var rows = RobotRevolutionEngine.CreateGame().Layout.VisibleRows + (expandedRows ? 2 : 0);
        IEnumerable<string> Window(IReadOnlyList<string> strip, int stop) =>
            Enumerable.Range(0, rows).Select(offset => strip[(stop + offset) % strip.Count]);
        var stops = strips.Select((strip, reel) => reel < 3 ? strip.IndexOf(scatter) :
            Enumerable.Range(0, strip.Count).First(stop => !Window(strip, stop).Contains(scatter))).ToArray();
        var scatters = stops.Select((stop, reel) => Window(strips[reel], stop).Count(symbol => symbol == scatter)).Sum();
        return new Draws([.. stops, .. Enumerable.Repeat(0, scatters), 0]);
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

public sealed class RobotRevolutionStoreEmulatorFactAttribute : FactAttribute
{
    public RobotRevolutionStoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for Robot Revolution paid round persistence checks.";
    }
}
