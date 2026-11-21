using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.ReelRiches.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.ReelRiches.Tests;

public sealed class ReelRichesRoundStoreTests
{
    private const string Account = "reel-riches-native-player";
    private const string GameId = ReelRichesEngine.GameId;

    [ReelRichesStoreEmulatorFact]
    public async Task PaidPearlAndEnergyUseExactPerMatchPayoutAndRetryWithoutADuplicateDebit()
    {
        var database = Connect();
        var wallet = new Wallet { LoseNextReply = true };
        await SeedNearCollection(database, wagerPoints: 194);
        await SeedBalance(database, "energy", 100);
        var operation = new ReelRichesRoundRequest("reel-riches-paid-pearl-001", 6, false);
        var service = Service(database, wallet, ScatterAndPearlDraws());
        using var signed = GameSecurityContext.Push(new("reel-riches-request-1", Account, "private-riches-session",
            "203.0.113.19", DateTime.UtcNow, "POST", ReelRichesPaidRouteContract.SpinPath, ReelRichesPaidRouteContract.AuditGameId,
            null, operation.OperationId, null, null, "sanitized", "direct"));

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, operation, true, default));
        var pending = await service.ReadStateAsync(Account, default);
        Assert.Equal(operation, pending.PendingOperation);
        Assert.Equal(100, pending.EnergyBalance);
        Assert.Equal(39, pending.SealCollections.Single(track => track.SealId == "rows").Count);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);

        var restored = Service(database, wallet, new NoDraws());
        var conflict = await Assert.ThrowsAsync<ReelRichesRoundConflictException>(() => restored.SpinAsync(Account,
            operation with { OperationId = "reel-riches-another-spin" }, true, default));
        Assert.Equal(operation, conflict.PendingRequest);
        await Assert.ThrowsAsync<ReelRichesRoundConflictException>(() => restored.SpinAsync(Account,
            operation with { WagerPoints = 8 }, true, default));

        var result = await restored.SpinAsync(Account, operation, false, default);
        var round = Assert.IsType<ReelRichesPreparedRound>(result.Round);
        Assert.Equal(19, round.Outcome.Audit.EffectiveGame.Paylines.Count);
        Assert.Equal(3, round.Outcome.CollectorPoints);
        Assert.Equal(3, Assert.Single(round.Outcome.Payout.Paylines.Single(line => line.PaylineId == 901).Matches).AmountPoints);
        Assert.Equal(5, Assert.Single(round.Progress.SettledPayout.Paylines.Single(line => line.PaylineId == 901).Matches).AmountPoints);
        Assert.Equal(1.5m, round.Progress.EnergySettlement.PayoutMultiplier);
        Assert.Equal(100, round.Progress.EnergySettlement.MeterBalanceBeforeReset);
        Assert.Equal(0, result.Features!.EnergyBalance);
        Assert.Empty(round.Outcome.CollectionsAwarded);
        Assert.Equal(10, round.Progress.CollectionFreeSpinsAwarded);
        Assert.Equal(5, round.Outcome.FreeSpinsAwarded);
        Assert.Equal(0, result.Features.SealCollections.Single(track => track.SealId == "rows").Count);
        Assert.Equal(2, result.Features.SealCollections.Single(track => track.SealId == "sync").Count);
        Assert.Equal(15, result.Features.FreeSpinsRemaining);
        Assert.Equal(5, result.Features.FreeSpinWagerPoints);
        Assert.Equal("sync-rows-rand", result.Features.FreeSpinFeatureMode);
        Assert.Equal(150, result.ChargedWagerCents);
        Assert.Equal(round.Progress.SettledPayout.TotalPoints * 25, wallet.Last!.PayoutCents);
        Assert.Equal(round.Outcome.SpinId.ToString("N"), wallet.Last.OperationId);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        Assert.Equal(result.Wallet, (await restored.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var raw = Assert.Single((await database.Collection(ReelRichesRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.DoesNotContain("private-riches-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);
        Assert.Equal(GameId, (await Guard(database).GetSnapshotAsync()).GetValue<string>(ReelRichesRoundStateCodec.OwnerField));
    }

    [ReelRichesStoreEmulatorFact]
    public async Task FreeCreditsBlockCashAndSpendLegacyBeforeNewAwardsAtTheSavedOddWager()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2 };
        var time = new RoundClock();
        await SeedNearCollection(database, wagerPoints: 194, lockedWager: 6);
        await SeedBalance(database, "energy", 100);
        var service = Service(database, wallet, ScatterAndPearlDraws(), time);
        await Assert.ThrowsAsync<ReelRichesFreeSpinsRequiredException>(() => service.SpinAsync(Account,
            new("reel-riches-cash-blocked", 6, false), true, default));
        await Assert.ThrowsAsync<ReelRichesFreeWagerLockedException>(() => service.SpinAsync(Account,
            new("reel-riches-mismatched-carry", 8, true), true, default));
        Assert.Equal(0, wallet.Calls);

        var first = await service.SpinAsync(Account, new("reel-riches-legacy-award", 6, true), true, default);
        Assert.Equal(16, first.Features!.FreeSpinsRemaining);
        Assert.Equal(5, first.Features.FreeSpinWagerPoints);
        Assert.Equal("sync-rows-rand", first.Features.FreeSpinFeatureMode);
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, first.ChargedWagerCents);

        time.Advance();
        var lastLegacy = await Service(database, wallet, new HighDraws(), time).SpinAsync(Account,
            new("reel-riches-last-legacy", 5, true), true, default);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.Equal(15, lastLegacy.Features!.FreeSpinsRemaining);
        Assert.Equal(5, lastLegacy.Round!.Outcome.WagerPoints);
        Assert.Equal("sync-rows-rand", lastLegacy.Round.Outcome.Audit.FeatureMode);
        Assert.All(lastLegacy.Round.Outcome.Reels, reel => Assert.Equal(6, reel.Count));

        time.Advance();
        var native = await Service(database, wallet, new HighDraws(), time).SpinAsync(Account,
            new("reel-riches-first-native", 5, true), true, default);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
        Assert.Equal(14, native.Features!.FreeSpinsRemaining);
        Assert.Equal(0, native.ChargedWagerCents);
    }

    [ReelRichesStoreEmulatorFact]
    public async Task HistoricalOnePointLockRemainsVisibleWhileANewRawOnePointAwardUsesTwo()
    {
        var legacyDatabase = Connect();
        var legacyWallet = new Wallet { LegacyRemaining = 1 };
        await Guard(legacyDatabase).CreateAsync(new Dictionary<string, object> { ["freeSpinWagerCents"] = 25L });
        var service = Service(legacyDatabase, legacyWallet, new NoDraws());
        Assert.Equal(1, (await service.ReadStateAsync(Account, default)).FreeSpinWagerPoints);
        var hold = await Assert.ThrowsAsync<ReelRichesLegacyFreeWagerUnplayableException>(() => service.SpinAsync(Account,
            new("reel-riches-saved-one-point", 1, true), true, default));
        Assert.Equal(1, hold.WagerPoints);
        Assert.Equal(0, legacyWallet.Calls);
        Assert.Equal(1, legacyWallet.LegacyRemaining);

        var database = Connect();
        await SeedNearCollection(database, wagerPoints: 15);
        await SeedBalance(database, "energy", 100);
        var settled = await Service(database, new Wallet(), ScatterAndPearlDraws()).SpinAsync(Account,
            new("reel-riches-new-one-point", 6, false), true, default);
        Assert.Equal(1, settled.Round!.Progress.CollectionFreeSpinWagerPoints);
        Assert.Equal(2, settled.Features!.FreeSpinWagerPoints);
        Assert.Equal(15, settled.Features.FreeSpinsRemaining);
    }

    [ReelRichesStoreEmulatorFact]
    public async Task AChangedLegacyCountDeclinesTheCashSpinAndKeepsTheUnpinnedBankRecoverable()
    {
        var database = Connect();
        var wallet = new Wallet { ChangeBeforeSettle = 2 };
        var time = new RoundClock();
        var operation = new ReelRichesRoundRequest("reel-riches-stale-cash-001", 6, false);
        var service = Service(database, wallet, ScatterAndPearlDraws(), time);
        var declined = await service.SpinAsync(Account, operation, true, default);
        Assert.True(declined.Wallet.LegacyFreeGamesCountMismatch);
        Assert.Null(declined.Features);
        Assert.Null(declined.Round);
        Assert.Equal(0, wallet.Approvals);
        Assert.False((await Guard(database).GetSnapshotAsync()).ContainsField(ReelRichesRoundStateCodec.OwnerField));
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var refreshed = await service.ReadStateAsync(Account, default);
        Assert.Equal(2, refreshed.FreeSpinsRemaining);
        Assert.Null(refreshed.FreeSpinWagerPoints);
        Assert.Equal(declined.Wallet, (await Service(database, wallet, new NoDraws()).SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(1, wallet.Calls);

        time.Advance();
        var unpinned = Service(database, wallet, new HighDraws(), time);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => unpinned.SpinAsync(Account,
            new("reel-riches-unpinned-odd", 5, true), true, default));
        var pinned = await unpinned.SpinAsync(Account, new("reel-riches-pin-6-wager", 6, true), true, default);
        Assert.Equal(6, pinned.Features!.FreeSpinWagerPoints);
        Assert.Equal(1, pinned.Features.FreeSpinsRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
    }

    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"riches-native-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static ReelRichesRoundService Service(FirestoreDb database, Wallet wallet, IGameRandomSource random, TimeProvider? time = null) =>
        new(new ReelRichesRoundStore(database, random, time ?? TimeProvider.System), wallet, wallet);

    private static DocumentReference Guard(FirestoreDb database) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Account, GameId));

    private static Task SeedBalance(FirestoreDb database, string name, long value)
    {
        var currency = $"{name}:{GameId}";
        return database.Collection("userBalances").Document($"{Account}_{currency}").CreateAsync(
            new Dictionary<string, object> { ["userId"] = Account, ["currencyId"] = currency, ["available"] = value });
    }

    private static Task SeedNearCollection(FirestoreDb database, long wagerPoints, long? lockedWager = null)
    {
        var fields = new Dictionary<string, object>
        {
            ["sealCounts"] = new Dictionary<string, long> { ["rows"] = 39, ["sync"] = 2 },
            ["sealWagerCents"] = new Dictionary<string, long> { ["rows"] = wagerPoints * 25, ["sync"] = 12 * 25 }
        };
        if (lockedWager is { } wager) fields["freeSpinWagerCents"] = wager * 25;
        return Guard(database).CreateAsync(fields);
    }

    private static Draws ScatterAndPearlDraws()
    {
        var strips = ReelRichesEngine.CreateReelSet().Reels;
        var rows = ReelRichesEngine.CreateGame().Layout.VisibleRows;
        IEnumerable<string> Window(IReadOnlyList<string> strip, int stop) =>
            Enumerable.Range(0, rows).Select(offset => strip[(stop + offset) % strip.Count]);
        var stops = strips.Select((strip, reel) => reel < 3 ? strip.IndexOf("FREE") :
            Enumerable.Range(0, strip.Count).First(stop => !Window(strip, stop).Contains("FREE"))).ToArray();
        var scatters = stops.Select((stop, reel) => Window(strips[reel], stop).Count(symbol => symbol == "FREE")).Sum();
        // Retain every scatter, inject one half-wager pearl and collector, then skip bananas and seals.
        return new Draws([.. stops, .. Enumerable.Repeat(0, scatters), 30, 0, 0, 776, 0, 0, 0, 99]);
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

public sealed class ReelRichesStoreEmulatorFactAttribute : FactAttribute
{
    public ReelRichesStoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for Reel Riches paid round persistence checks.";
    }
}
