using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.JadeDragon.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.JadeDragon.Tests;

public sealed class JadeRoundStoreTests
{
    private const string Account = "jade-native-player";
    private const string GameId = JadeDragonEngine.GameId;
    private const long Wager = 40;

    [JadeRoundEmulatorFact]
    public async Task PaidCascadeUsesCentPayoutAndRecoversAnAlreadyCommittedWallet()
    {
        var database = Connect();
        var wallet = new Wallet { LoseNextReply = true, ReceiptOffset = TimeSpan.FromSeconds(-5) };
        var request = new JadeRoundRequest("jade-cascade-payout-001", Wager, false);
        var service = Service(database, wallet, WinningCascade());
        using var signed = GameSecurityContext.Push(new("jade-original-request", Account, "private-jade-session", "203.0.113.12",
            DateTime.UtcNow, "POST", JadePaidRouteContract.SpinPath, JadePaidRouteContract.AuditGameId, null,
            request.OperationId, null, null, "sanitized", "direct"));

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, request, true, default));
        var held = await service.ReadStateAsync(Account, default);
        Assert.Equal(request, held.PendingOperation);
        Assert.Equal(wallet.Balance / 100m, held.SlotsCreditsBalance);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        var conflict = await Assert.ThrowsAsync<JadeRoundConflictException>(() =>
            service.SpinAsync(Account, request with { OperationId = "jade-cascade-other-002" }, true, default));
        Assert.Equal(request, conflict.PendingRequest);
        await Assert.ThrowsAsync<JadeRoundConflictException>(() =>
            service.SpinAsync(Account, request with { WagerPoints = Wager + 2 }, true, default));

        var retry = Service(database, wallet, new NoDraws());
        var result = await retry.SpinAsync(Account, request, false, default);
        var spin = Assert.IsType<JadePreparedRound>(result.Round);
        Assert.Equal(JadeDragonCascadeRules.Version, spin.EngineVersion);
        Assert.Equal(Wager * 25, spin.WagerCents);
        Assert.Equal(spin.SettledPayout.TotalPoints, spin.PayoutCents);
        Assert.True(spin.PayoutCents > 0);
        Assert.Equal(spin.PayoutCents, wallet.Last!.PayoutCents);
        Assert.Equal(2, wallet.Calls);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(result.Wallet, (await retry.SpinAsync(Account, request, false, default)).Wallet);
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var raw = Assert.Single((await database.Collection(JadeRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.DoesNotContain("private-jade-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);
    }

    [JadeRoundEmulatorFact]
    public async Task CascadeAwardsNativeFreeGamesAndChargesNeitherCashNorHistoricalUnitsForThem()
    {
        var database = Connect();
        var wallet = new Wallet();
        var time = new RoundClock();
        var service = Service(database, wallet, Pearls(), time);
        var paid = await service.SpinAsync(Account, new("jade-cascade-pearls-001", Wager, false), true, default);
        Assert.Equal(JadeDragonCascadeRules.BaseFreeSpins, paid.Features!.FreeSpinsRemaining);
        Assert.Equal(Wager, paid.Features.FreeSpinWagerPoints);
        Assert.Equal(0, wallet.LegacyRemaining);
        time.Advance();
        var free = Service(database, wallet, PlainCascade(), time);
        var wrongWager = await Assert.ThrowsAsync<JadeFreeWagerLockedException>(() => free.SpinAsync(Account,
            new("jade-free-wrong-wager", Wager + 2, true), true, default));
        Assert.Equal(Wager, wrongWager.WagerPoints);
        await Assert.ThrowsAsync<JadeFreeSpinsRequiredException>(() => free.SpinAsync(Account,
            new("jade-free-mandatory", Wager, false), true, default));
        Assert.Equal(1, wallet.Calls);

        var round = await free.SpinAsync(Account, new("jade-cascade-native-free", Wager, true), true, default);
        Assert.Equal(JadeDragonCascadeRules.Version, round.Round!.EngineVersion);
        Assert.Equal(JadeDragonCascadeRules.BaseFreeSpins - 1, round.Features!.FreeSpinsRemaining);
        Assert.Equal(0, round.ChargedWagerCents);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
    }

    [JadeRoundEmulatorFact]
    public async Task PlatformEraCascadeCarryKeepsV2MathAndLocalizesItsRetriggerAwards()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2 };
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = Wager * 25, ["freeSpinMathModelId"] = JadeDragonCascadeRules.Version
        });
        var service = Service(database, wallet, Pearls());
        var result = await service.SpinAsync(Account, new("jade-v2-legacy-retrigger", Wager, true), true, default);
        Assert.Equal(JadeDragonCascadeRules.Version, result.Round!.EngineVersion);
        Assert.Equal(0, result.ChargedWagerCents);
        Assert.Equal(JadeDragonCascadeRules.RetriggerFreeSpins + 1, result.Features!.FreeSpinsRemaining);
        Assert.Equal(JadeDragonCascadeRules.RetriggerFreeSpins, result.Round.Progress.After.NativeRemaining);
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.Equal(2, wallet.Last!.ExpectedLegacyFreeGamesRemaining);
        Assert.True(wallet.Last.SpendLegacyFreeGame);
    }

    [JadeRoundEmulatorFact]
    public async Task LegacyCollectionCarryKeepsItsHigherWagerBoostCostAndQuarterRandPayout()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 1, LoseNextReply = true };
        const long oldWager = 550;
        const int oldPoints = 12;
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = oldWager * 25, ["freeSpinFeatureMode"] = JadeDragonCollections.CollectionFeatureMode,
            ["consecutiveFiveMisses"] = 4L,
            ["sealCounts"] = new Dictionary<string, object> { ["track1"] = JadeDragonCollections.CollectionTarget },
            ["sealWagerCents"] = new Dictionary<string, object> { ["track1"] = oldWager * 25 * JadeDragonCollections.CollectionTarget }
        });
        var special = LegacyBalance(database, "specialPoints");
        await special.CreateAsync(LegacyBalanceFields("specialPoints", oldPoints));
        await LegacyBalance(database, "energy").CreateAsync(LegacyBalanceFields("energy", 97));

        var service = Service(database, wallet, new Draws([], 73));
        var request = new JadeRoundRequest("jade-v1-carried-boosted-001", oldWager, true, true);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, request, true, default));
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.Equal(oldWager, (await service.ReadStateAsync(Account, default)).FreeSpinWagerPoints);

        var retry = Service(database, wallet, new NoDraws());
        var result = await retry.SpinAsync(Account, request, false, default);
        var outcome = result.Round!.Outcome.Legacy!;
        Assert.Equal(JadeDragonEngine.Version, result.Round.EngineVersion);
        Assert.Null(result.Round.Outcome.Cascade);
        Assert.NotEmpty(result.Round.Outcome.LegacyDrawIndexes!);
        Assert.All(outcome.Reels, reel => Assert.Equal(6, reel.Count));
        Assert.Equal(oldWager, outcome.WagerPoints);
        Assert.True(outcome.SpecialBoostApplied);
        Assert.Equal(0, result.ChargedWagerCents);
        Assert.Equal(outcome.Payout.TotalPoints * 25, result.Round.PayoutCents);
        Assert.Equal(97, result.Round.Outcome.LegacyAudit!.EnergyBalanceBefore);
        Assert.Equal(0, result.Features!.EnergyBalance);
        Assert.Equal(oldPoints - new JadeDragonEngine().Definition.SpecialPoints!.ActivationCost + outcome.SpecialPointsAwarded,
            result.Features.SpecialPointsBalance);
        Assert.Equal(JadeDragonCollections.AwardedSpins, result.Round.Progress.CollectionFreeSpinsAwarded);
        Assert.Equal(JadeDragonCollections.AwardedSpins + outcome.FreeSpinsAwarded, result.Round.Progress.After.NativeRemaining);
        Assert.Equal(JadeDragonEngine.Version, result.Round.Progress.After.Carry!.EngineVersion);
        Assert.Equal(oldWager, result.Features.FreeSpinWagerPoints);
        Assert.Equal(oldPoints, (await special.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
    }

    [JadeRoundEmulatorFact]
    public async Task LegacyCountChangedDuringFirstImportDoesNotEraseOldCarryMetadata()
    {
        var database = Connect();
        var wallet = new Wallet { ChangeBeforeSettle = 1 };
        var guard = Guard(database);
        await guard.CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = Wager * 25, ["freeSpinMathModelId"] = JadeDragonCascadeRules.Version
        });
        var service = Service(database, wallet, PlainCascade());
        var request = new JadeRoundRequest("jade-stale-legacy-wallet-001", Wager, false);
        var refused = await service.SpinAsync(Account, request, true, default);
        Assert.True(refused.Wallet.LegacyFreeGamesCountMismatch);
        Assert.Null(refused.Features);
        Assert.Null(refused.Round);
        Assert.Equal(0, wallet.Approvals);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        var restored = await guard.GetSnapshotAsync();
        Assert.False(restored.ContainsField(JadeRoundStateCodec.OwnerField));
        Assert.Equal(Wager * 25, restored.GetValue<long>("freeSpinWagerCents"));
        var state = await service.ReadStateAsync(Account, default);
        Assert.Equal(1, state.FreeSpinsRemaining);
        Assert.Equal(Wager, state.FreeSpinWagerPoints);
        Assert.Equal(refused.Wallet, (await service.SpinAsync(Account, request, false, default)).Wallet);
        Assert.Equal(1, wallet.Calls);
    }

    [JadeRoundEmulatorFact]
    public async Task ExpiredLegacyAdmissionAndInvalidCascadeTermsCannotSpendOrDraw()
    {
        var database = Connect();
        var wallet = new Wallet();
        var service = Service(database, wallet, new NoDraws());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SpinAsync(Account,
            new("jade-invalid-high-wager", 402, false), true, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SpinAsync(Account,
            new("jade-invalid-v2-boost", Wager, false, true), true, default));

        var oldAdmission = Guid.NewGuid().ToString("N");
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            [SlotSpinGuard.ActiveAdmissionField] = oldAdmission,
            [SlotSpinGuard.LastSpinStartedField] = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(-1))
        });
        await Assert.ThrowsAsync<JadeRoundConflictException>(() => service.SpinAsync(Account,
            new("jade-old-admission-blocked", Wager, false), true, default));
        Assert.Equal(oldAdmission, (await Guard(database).GetSnapshotAsync()).GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
        Assert.Equal(0, wallet.Calls);
    }

    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"jade-native-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static JadeRoundService Service(FirestoreDb database, Wallet wallet, IGameRandomSource random, TimeProvider? time = null) =>
        new(new JadeRoundStore(database, random, time ?? TimeProvider.System), wallet, wallet);

    private static DocumentReference Guard(FirestoreDb database) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Account, GameId));

    private static string Currency(string name) => $"{name}:{GameId}";
    private static DocumentReference LegacyBalance(FirestoreDb database, string name) =>
        database.Collection("userBalances").Document($"{Account}_{Currency(name)}");

    private static Dictionary<string, object> LegacyBalanceFields(string name, long available) =>
        new() { ["userId"] = Account, ["currencyId"] = Currency(name), ["available"] = available };

    private static Draws PlainCascade() => CascadeFrom(
        ["dragon", "dragon", "dragon", "dragon", "phoenix", "phoenix", "phoenix", "phoenix",
        "lotus", "lotus", "lotus", "lotus", "jade", "jade", "jade", "jade", "coin", "coin", "coin", "coin"]);

    private static Draws WinningCascade() => CascadeFrom(
        ["dragon", "lotus", "lotus", "lotus", "dragon", "phoenix", "phoenix", "phoenix",
        "dragon", "jade", "jade", "jade", "coin", "coin", "coin", "coin", "bamboo", "bamboo", "bamboo", "bamboo",
        "knot", "coin", "bamboo"]);

    private static Draws Pearls() => CascadeFrom(
        ["pearl", "dragon", "dragon", "dragon", "pearl", "phoenix", "phoenix", "phoenix",
        "pearl", "lotus", "lotus", "lotus", "jade", "jade", "jade", "jade", "coin", "coin", "coin", "coin"]);

    private static Draws CascadeFrom(string[] symbols)
    {
        var pool = JadeDragonCascadeEngine.CreateReelSet().Reels[0];
        return new(symbols.Select(symbol => pool.IndexOf(symbol)).ToArray());
    }

    private sealed class Draws(int[] draws, int seed = 73) : IGameRandomSource
    {
        private int cursor;
        private readonly Random fallback = new(seed);
        public int Next(int maximumExclusive) => cursor < draws.Length ? draws[cursor++] : fallback.Next(maximumExclusive);
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

        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken cancellationToken)
        {
            Last = request;
            Calls++;
            if (receipts.TryGetValue(request.OperationId, out var existing))
            {
                Assert.Equal(existing.Terms, request);
                return Task.FromResult(existing.Receipt with { WasAlreadySettled = true });
            }
            if (ChangeBeforeSettle is { } next) LegacyRemaining = next;
            var before = Balance;
            var mismatch = request.ExpectedLegacyFreeGamesRemaining != LegacyRemaining;
            var declined = mismatch || before < request.WagerCents;
            if (!declined)
            {
                Balance = before - request.WagerCents + request.PayoutCents;
                if (request.SpendLegacyFreeGame) LegacyRemaining--;
                Approvals++;
            }
            var receipt = new GameWalletSettlementReceipt(
                GameWalletSettlementIdentity.CreateId(GameId, accountId, request.OperationId),
                !declined && request.WagerCents > 0 ? $"{request.OperationId}-wager" : null,
                !declined && request.PayoutCents > 0 ? $"{request.OperationId}-payout" : null,
                before, declined ? before : before - request.WagerCents, Balance, DateTimeOffset.UtcNow + ReceiptOffset, false,
                declined && !mismatch, false, !declined && request.SpendLegacyFreeGame ? $"{request.OperationId}-free-game-use" : null,
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

public sealed class JadeRoundEmulatorFactAttribute : FactAttribute
{
    public JadeRoundEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for Jade Dragon paid round persistence checks.";
    }
}
