using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.PiratesFortune.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.PiratesFortune.Tests;

public sealed class PiratesRoundStoreTests
{
    private const string Account = "pirates-native-player";
    private const string GameId = PiratesFortuneEngine.GameId;
    private const long Wager = 100;

    [PiratesStoreEmulatorFact]
    public async Task CompositeGemGrantsAndDirectCollectorPayoutResumeTheOriginalPaidSpin()
    {
        var database = Connect();
        var wallet = new Wallet { LoseNextReply = true, ReceiptOffset = TimeSpan.FromSeconds(-5) };
        var time = new RoundClock();
        await SeedTwoFullTracks(database);
        await LegacyBalance(database, "energy").CreateAsync(LegacyFields("energy", 67));
        var operation = new PiratesRoundRequest("pirates-first-paid-collection", Wager, false);
        var service = Service(database, wallet, GemDraws(), time);
        using var signed = GameSecurityContext.Push(new("pirates-original-request", Account, "private-pirates-session", "203.0.113.19",
            DateTime.UtcNow, "POST", PiratesPaidRouteContract.SpinPath, PiratesPaidRouteContract.AuditGameId,
            null, operation.OperationId, null, null, "sanitized", "direct"));

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, operation, true, default));
        var pending = await service.ReadStateAsync(Account, default);
        Assert.Equal(operation, pending.PendingOperation);
        Assert.Equal(67, pending.EnergyBalance);
        Assert.Equal(24, pending.SealCollections.Single(track => track.SealId == "sync").Count);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        var conflict = await Assert.ThrowsAsync<PiratesRoundConflictException>(() => service.SpinAsync(Account,
            operation with { OperationId = "pirates-would-be-second" }, true, default));
        Assert.Equal(operation, conflict.PendingRequest);
        await Assert.ThrowsAsync<PiratesRoundConflictException>(() =>
            service.SpinAsync(Account, operation with { WagerPoints = Wager + 2 }, true, default));

        var restored = Service(database, wallet, new NoDraws(), time);
        var result = await restored.SpinAsync(Account, operation, false, default);
        var round = Assert.IsType<PiratesPreparedRound>(result.Round);
        Assert.Equal(Wager * 25, round.WagerCents);
        Assert.Equal(33, round.Outcome.CollectorPoints);
        Assert.Equal(round.Outcome.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.Equal(round.PayoutCents, wallet.Last!.PayoutCents);
        Assert.Equal(wallet.Last.OperationId, round.Outcome.SpinId.ToString("N"));
        Assert.Equal(67, round.Outcome.Audit.EnergyBalanceBefore);
        Assert.Equal(0, result.Features!.EnergyBalance);
        Assert.Equal(67, (await LegacyBalance(database, "energy").GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(20, result.Features.FreeSpinsRemaining);
        Assert.Equal(101, result.Features.FreeSpinWagerPoints);
        Assert.Equal("sync-rows", result.Features.FreeSpinFeatureMode);
        Assert.Equal(20, round.Progress.CollectionFreeSpinsAwarded);
        Assert.Equal(101, round.Progress.CollectionFreeSpinWagerPoints);
        Assert.Equal(new PiratesGemGrant("sync", 25, 2525 * 25), round.Progress.GemAwards[0]);
        Assert.Equal(new PiratesGemGrant("rows", 25, 2550 * 25), round.Progress.GemAwards[1]);
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        Assert.Equal(result.Wallet, (await restored.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var raw = Assert.Single((await database.Collection(PiratesRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.DoesNotContain("private-pirates-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);

        time.Advance();
        var bonus = await Service(database, wallet, QuietCompositeDraws(), time).SpinAsync(Account,
            new("pirates-first-odd-native-spin", 101, true), true, default);
        Assert.Equal(101, bonus.Round!.Outcome.WagerPoints);
        Assert.Equal("sync-rows", bonus.Round.Outcome.Audit.FeatureMode);
        Assert.Equal(19, bonus.Features!.FreeSpinsRemaining);
        Assert.Equal(0, bonus.ChargedWagerCents);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
    }

    [PiratesStoreEmulatorFact]
    public async Task ScatterAndTwoGemGrantsAwardSevenPlusTenPlusTenSpins()
    {
        var database = Connect();
        var wallet = new Wallet();
        await SeedTwoFullTracks(database);
        var service = Service(database, wallet, GemDraws(scatter: true));
        var result = await service.SpinAsync(Account, new("pirates-scatter-and-composite", Wager, false), true, default);
        Assert.Equal(7, result.Round!.Outcome.FreeSpinsAwarded);
        Assert.Equal(20, result.Round.Progress.CollectionFreeSpinsAwarded);
        Assert.Equal(27, result.Features!.FreeSpinsRemaining);
        Assert.Equal(101, result.Features.FreeSpinWagerPoints);
        Assert.Equal("sync-rows", result.Features.FreeSpinFeatureMode);
        var response = PiratesSpinResponse.From(result);
        Assert.Equal(27, response.FreeSpinsAwarded);
        Assert.Equal(20, response.CollectionFreeSpinsAwarded);
    }

    [PiratesStoreEmulatorFact]
    public async Task IncompleteHistoricalWeightKeepsRawOnePointWitnessAndAwardsAtLegalMinimum()
    {
        var database = Connect();
        var wallet = new Wallet();
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["sealCounts"] = new Dictionary<string, object> { ["sync"] = 24L }
        });
        const long minimum = 2;
        var result = await Service(database, wallet, GemDraws()).SpinAsync(Account,
            new("pirates-incomplete-historical-weights", minimum, false), true, default);
        Assert.Equal(minimum, PiratesFortuneEngine.CreateGame().Wagering.MinimumWagerPoints);
        var proof = result.Round!.Progress;
        var award = Assert.Single(proof.GemAwards);
        Assert.Equal(new PiratesGemGrant("sync", 25, minimum * 25), award);
        Assert.Equal(1, proof.CollectionFreeSpinWagerPoints);
        Assert.Equal("sync", proof.AwardedFeatureMode);
        Assert.Equal(minimum, result.Features!.FreeSpinWagerPoints);
        Assert.Equal(10, result.Features.FreeSpinsRemaining);
    }

    [PiratesStoreEmulatorFact]
    public async Task PreviouslySavedOnePointWagerIsVisibleButCannotConsumeHistoricalCredits()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2 };
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 25L, ["freeSpinFeatureMode"] = "rows",
        });
        var service = Service(database, wallet, new NoDraws());
        var before = await service.ReadStateAsync(Account, default);
        Assert.Equal(1, before.FreeSpinWagerPoints);
        Assert.Equal(2, before.FreeSpinsRemaining);
        var error = await Assert.ThrowsAsync<PiratesLegacyFreeWagerUnplayableException>(() => service.SpinAsync(Account,
            new("pirates-old-under-minimum", 1, true), true, default));
        Assert.Equal(1, error.WagerPoints);
        Assert.Equal(2, wallet.LegacyRemaining);
        Assert.Equal(0, wallet.Calls);
        Assert.False((await Guard(database).GetSnapshotAsync()).ContainsField(PiratesRoundStateCodec.OwnerField));
    }

    [PiratesStoreEmulatorFact]
    public async Task AwardedCompositeBankContinuesThroughTheLastHistoricalCreditAndThenNative()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2 };
        var time = new RoundClock();
        await SeedTwoFullTracks(database, includeOldLock: true);
        var awarded = await Service(database, wallet, GemDraws(), time).SpinAsync(Account,
            new("pirates-merge-legacy-native", Wager, true), true, default);
        Assert.Equal(21, awarded.Features!.FreeSpinsRemaining);
        Assert.Equal(101, awarded.Features.FreeSpinWagerPoints);
        Assert.Equal("sync-rows", awarded.Features.FreeSpinFeatureMode);
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);

        time.Advance();
        var next = Service(database, wallet, QuietCompositeDraws(), time);
        await Assert.ThrowsAsync<PiratesFreeWagerLockedException>(() => next.SpinAsync(Account,
            new("pirates-old-lock-rejected", Wager, true), true, default));
        var legacy = await next.SpinAsync(Account, new("pirates-coalesced-last-legacy", 101, true), true, default);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.Equal(20, legacy.Features!.FreeSpinsRemaining);

        time.Advance();
        var native = await Service(database, wallet, QuietCompositeDraws(), time).SpinAsync(Account,
            new("pirates-coalesced-first-native", 101, true), true, default);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
        Assert.Equal(19, native.Features!.FreeSpinsRemaining);
    }

    [PiratesStoreEmulatorFact]
    public async Task InitialLegacyCountConflictKeepsImportedBankRecoverable()
    {
        var database = Connect();
        var wallet = new Wallet { ChangeBeforeSettle = 1 };
        var service = Service(database, wallet, GemDraws());
        var operation = new PiratesRoundRequest("pirates-stale-legacy-cas-001", Wager, false);
        var declined = await service.SpinAsync(Account, operation, true, default);
        Assert.True(declined.Wallet.LegacyFreeGamesCountMismatch);
        Assert.Null(declined.Round);
        Assert.Null(declined.Features);
        Assert.Equal(0, wallet.Approvals);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.False((await Guard(database).GetSnapshotAsync()).ContainsField(PiratesRoundStateCodec.OwnerField));
        var state = await service.ReadStateAsync(Account, default);
        Assert.Equal(1, state.FreeSpinsRemaining);
        Assert.Null(state.FreeSpinWagerPoints);
        Assert.Equal(declined.Wallet, (await service.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(1, wallet.Calls);
    }

    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"pirates-native-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static PiratesRoundService Service(FirestoreDb database, Wallet wallet, IGameRandomSource random, TimeProvider? time = null) =>
        new(new PiratesRoundStore(database, random, time ?? TimeProvider.System), wallet, wallet);

    private static DocumentReference Guard(FirestoreDb database) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Account, GameId));

    private static Task SeedTwoFullTracks(FirestoreDb database, bool includeOldLock = false)
    {
        var fields = new Dictionary<string, object>
        {
            ["sealCounts"] = new Dictionary<string, object> { ["sync"] = 24L, ["rows"] = 24L },
            ["sealWagerCents"] = new Dictionary<string, object> { ["sync"] = 2_425L * 25, ["rows"] = 2_450L * 25 }
        };
        if (includeOldLock) fields["freeSpinWagerCents"] = Wager * 25;
        return Guard(database).CreateAsync(fields);
    }

    private static string Currency(string name) => $"{name}:{GameId}";
    private static DocumentReference LegacyBalance(FirestoreDb database, string name) =>
        database.Collection("userBalances").Document($"{Account}_{Currency(name)}");
    private static Dictionary<string, object> LegacyFields(string name, long available) =>
        new() { ["userId"] = Account, ["currencyId"] = Currency(name), ["available"] = available };

    private static IEnumerable<string> Window(IReadOnlyList<string> strip, int stop, int rows = 4) =>
        Enumerable.Range(0, rows).Select(offset => strip[(stop + offset) % strip.Count]);

    private static Draws GemDraws(bool scatter = false)
    {
        var strips = PiratesFortuneEngine.CreateReelSet().Reels;
        var stops = strips.Select((strip, reel) => scatter && reel < 3 ? strip.IndexOf("FREE") :
            Enumerable.Range(0, strip.Count).First(stop => Window(strip, stop).All(symbol => symbol != "FREE"))).ToArray();
        var freeCount = stops.Select((stop, reel) => Window(strips[reel], stop).Count(symbol => symbol == "FREE")).Sum();
        // One value token, one paw, no banana, then two gems on the first two tracks.
        return new Draws([.. stops, .. Enumerable.Repeat(0, freeCount), 91, 0, 0, 776, 0, 0, 0, 0, 0, 0, 0, 0, 1]);
    }

    private static Draws QuietCompositeDraws()
    {
        var strips = PiratesFortuneEngine.CreateReelSet().Reels;
        var stops = strips.Select(strip => Enumerable.Range(0, strip.Count).First(stop =>
            Window(strip, stop, rows: 6).All(symbol => symbol != "FREE")));
        // Sync source/destination, no value tokens, paw, bananas, or gems.
        return new Draws([.. stops, 0, 0, 0, 776, 13, 0, 99]);
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

public sealed class PiratesStoreEmulatorFactAttribute : FactAttribute
{
    public PiratesStoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for Pirates' Fortune paid round persistence checks.";
    }
}
