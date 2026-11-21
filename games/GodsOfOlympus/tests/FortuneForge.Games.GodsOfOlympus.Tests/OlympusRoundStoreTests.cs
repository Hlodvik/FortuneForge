using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.GodsOfOlympus.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.GodsOfOlympus.Tests;

public sealed class OlympusRoundStoreTests
{
    private const string Account = "olympus-native-player";
    private const string GameId = GodsOfOlympusEngine.GameId;

    [OlympusStoreEmulatorFact]
    public async Task PaidV4TumblesRetainClassicProgressAndReplayAnInterruptedWalletCommit()
    {
        var database = Connect();
        var wallet = new Wallet { LoseNextReply = true };
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["consecutiveFiveMisses"] = 7L,
            ["sealCounts"] = new Dictionary<string, long> { ["sync"] = 7L },
            ["sealWagerCents"] = new Dictionary<string, long> { ["sync"] = 350L }
        });
        await SeedBalance(database, "specialPoints", 9);
        await SeedBalance(database, "energy", 43);
        var operation = new OlympusRoundRequest("olympus-paid-tumble-001", 6, false);
        var service = Service(database, wallet, TumbleDraws());
        using var signed = GameSecurityContext.Push(new("olympus-request-1", Account, "private-olympus-session",
            "203.0.113.19", DateTime.UtcNow, "POST", OlympusPaidRouteContract.SpinPath, OlympusPaidRouteContract.AuditGameId,
            null, operation.OperationId, null, null, "sanitized", "direct"));

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, operation, true, default));
        var state = await service.ReadStateAsync(Account, default);
        Assert.Equal(operation, state.PendingOperation);
        Assert.Equal(9, state.SpecialPointsBalance);
        Assert.Equal(43, state.EnergyBalance);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);

        var restored = Service(database, wallet, new NoDraws());
        var conflict = await Assert.ThrowsAsync<OlympusRoundConflictException>(() => restored.SpinAsync(Account,
            operation with { OperationId = "olympus-next-tumble-001" }, true, default));
        Assert.Equal(operation, conflict.PendingRequest);
        await Assert.ThrowsAsync<OlympusRoundConflictException>(() => restored.SpinAsync(Account,
            operation with { WagerPoints = 8 }, true, default));

        var result = await restored.SpinAsync(Account, operation, false, default);
        var round = Assert.IsType<OlympusPreparedRound>(result.Round);
        var tumble = Assert.IsType<GodsOfOlympusTumbleSpinOutcome>(round.Engine.Tumble);
        Assert.Null(round.Engine.Classic);
        Assert.Equal("olympus-tumble-demo-v4", tumble.EngineVersion);
        Assert.Equal(6, tumble.Outcome.InitialReels.Count);
        Assert.All(tumble.Outcome.InitialReels, reel => Assert.Equal(5, reel.Count));
        Assert.Equal(2, tumble.Outcome.Steps.Count);
        Assert.Equal(18, tumble.Outcome.BaseTotalPoints);
        Assert.Equal(2, tumble.Outcome.Multiplier);
        Assert.Equal(36, tumble.Outcome.TotalPoints);
        Assert.Contains(200_000, tumble.DrawIndexes);
        Assert.True(round.Before.Matches(round.Progress.After));
        Assert.Equal(7, round.Progress.After.Pity);
        Assert.Equal(7, result.Features!.SealCollections.Single(track => track.SealId == "sync").Count);
        Assert.Equal(2, result.Features.SealCollections.Single(track => track.SealId == "sync").AverageWagerPoints);
        Assert.Equal(43, result.Features.EnergyBalance);
        Assert.Equal(9, result.Features.SpecialPointsBalance);
        Assert.Equal(150, result.ChargedWagerCents);
        Assert.Equal(900, wallet.Last!.PayoutCents);
        Assert.Equal(tumble.SpinId.ToString("N"), wallet.Last.OperationId);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        Assert.Equal(result.Wallet, (await restored.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var raw = Assert.Single((await database.Collection(OlympusRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.DoesNotContain("private-olympus-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);
        Assert.Equal(GameId, (await Guard(database).GetSnapshotAsync()).GetValue<string>(OlympusRoundStateCodec.OwnerField));
    }

    [OlympusStoreEmulatorFact]
    public async Task ScatterAndCollectionCreditsCoalesceInOneBankAndSpendTheLastLegacyCreditFirst()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2 };
        var time = new RoundClock();
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 75L,
            ["sealCounts"] = new Dictionary<string, long> { ["sync"] = 27L },
            ["sealWagerCents"] = new Dictionary<string, long> { ["sync"] = 137L * 25 }
        });
        var service = Service(database, wallet, ClassicAwardDraws(), time);
        await Assert.ThrowsAsync<OlympusFreeSpinsRequiredException>(() => service.SpinAsync(Account,
            new("olympus-cash-blocked-001", 4, false), true, default));
        await Assert.ThrowsAsync<OlympusFreeWagerLockedException>(() => service.SpinAsync(Account,
            new("olympus-wrong-legacy-wager", 4, true), true, default));
        Assert.Equal(0, wallet.Calls);

        var bonus = await service.SpinAsync(Account, new("olympus-overlap-award-001", 3, true), true, default);
        var granted = Assert.IsType<OlympusPreparedRound>(bonus.Round);
        var classic = Assert.IsType<FortuneForge.Games.Slots.Runtime.ClassicSlotSpinOutcome>(granted.Engine.Classic);
        Assert.Null(granted.Engine.Tumble);
        Assert.Equal(GodsOfOlympusEngine.ClassicEngineVersion, classic.Audit.EngineVersion);
        Assert.Equal(6, classic.FreeSpinsAwarded);
        Assert.Equal(6, granted.Progress.CollectionFreeSpinsAwarded);
        Assert.Equal(5, granted.Progress.CollectionFreeSpinWagerPoints);
        Assert.Equal(13, bonus.Features!.FreeSpinsRemaining);
        Assert.Equal(5, bonus.Features.FreeSpinWagerPoints);
        Assert.Equal("paw-rand", bonus.Features.FreeSpinFeatureMode);
        Assert.Equal(0, bonus.Features.SealCollections.Single(track => track.SealId == "sync").Count);
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.Equal(0, bonus.ChargedWagerCents);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(2, wallet.Last.ExpectedLegacyFreeGamesRemaining);

        time.Advance();
        var lastLegacy = await Service(database, wallet, new HighDraws(), time).SpinAsync(Account,
            new("olympus-last-legacy-001", 5, true), true, default);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.Equal(12, lastLegacy.Features!.FreeSpinsRemaining);
        Assert.Equal("paw-rand", lastLegacy.Round!.Engine.Classic!.Audit.FeatureMode);

        time.Advance();
        var native = await Service(database, wallet, new HighDraws(), time).SpinAsync(Account,
            new("olympus-first-native-001", 5, true), true, default);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
        Assert.Equal(11, native.Features!.FreeSpinsRemaining);
        Assert.Equal(0, native.ChargedWagerCents);
    }

    [OlympusStoreEmulatorFact]
    public async Task ClassicBoostUsesImportedPointsAndClearsDormantEnergyWhileCashRejectsTheBoost()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 1 };
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 75L, ["freeSpinFeatureMode"] = "paw-rand"
        });
        await SeedBalance(database, "specialPoints", 9);
        await SeedBalance(database, "energy", 43);
        var service = Service(database, wallet, new HighDraws());
        await Assert.ThrowsAsync<ArgumentException>(() => service.SpinAsync(Account,
            new("olympus-no-cash-boost-001", 4, false, true), true, default));
        Assert.Equal(0, wallet.Calls);

        var result = await service.SpinAsync(Account, new("olympus-classic-boost-001", 3, true, true), true, default);
        var classic = result.Round!.Engine.Classic!;
        Assert.True(classic.SpecialBoostApplied);
        Assert.EndsWith("-power-boost", classic.ReelSetId, StringComparison.Ordinal);
        Assert.Equal(43, classic.Audit.EnergyBalanceBefore);
        Assert.Equal(0, result.Features!.EnergyBalance);
        Assert.Equal(1 + classic.SpecialPointsAwarded, result.Features.SpecialPointsBalance);
        Assert.Equal(0, result.Features.FreeSpinsRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Null(result.Wallet.WagerTransactionId);
    }

    [OlympusStoreEmulatorFact]
    public async Task ANewLegacyCreditDeclinesTheFrozenCashTumbleWithoutRevealingIt()
    {
        var database = Connect();
        var wallet = new Wallet { ChangeBeforeSettle = 1 };
        var operation = new OlympusRoundRequest("olympus-cash-cas-decline", 6, false);
        var service = Service(database, wallet, TumbleDraws());
        var declined = await service.SpinAsync(Account, operation, true, default);
        Assert.True(declined.Wallet.LegacyFreeGamesCountMismatch);
        Assert.Null(declined.Features);
        Assert.Null(declined.Round);
        Assert.Equal(0, wallet.Approvals);
        Assert.False((await Guard(database).GetSnapshotAsync()).ContainsField(OlympusRoundStateCodec.OwnerField));
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var refreshed = await service.ReadStateAsync(Account, default);
        Assert.Equal(1, refreshed.FreeSpinsRemaining);
        Assert.Null(refreshed.FreeSpinWagerPoints);
        Assert.Equal(declined.Wallet, (await Service(database, wallet, new NoDraws()).SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(1, wallet.Calls);
    }

    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"olympus-native-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static OlympusRoundService Service(FirestoreDb database, Wallet wallet, IGameRandomSource random, TimeProvider? time = null) =>
        new(new OlympusRoundStore(database, random, time ?? TimeProvider.System), wallet, wallet);

    private static DocumentReference Guard(FirestoreDb database) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Account, GameId));

    private static Task SeedBalance(FirestoreDb database, string name, long value)
    {
        var currency = $"{name}:{GameId}";
        return database.Collection("userBalances").Document($"{Account}_{currency}").CreateAsync(
            new Dictionary<string, object> { ["userId"] = Account, ["currencyId"] = currency, ["available"] = value });
    }

    private static Draws TumbleDraws()
    {
        int[] other = [0, 34, 51, 77, 85, 91, 96];
        var indexes = Enumerable.Repeat(66, 8).Concat(Enumerable.Repeat(17, 7))
            .Concat(Enumerable.Range(0, 15).Select(index => other[index % other.Length]))
            .Append(17)
            .Concat(Enumerable.Range(0, 15).Select(index => other[index % other.Length]))
            .Select(index => index * 2_000).ToArray();
        indexes[29] = 200_000; // One ×2 coin in an otherwise unchanged two-step board.
        return new Draws(indexes);
    }

    private static Draws ClassicAwardDraws()
    {
        var strips = GodsOfOlympusEngine.CreateReelSet().Reels.Select(strip => strip.Where(symbol => symbol != "BOLT").ToList()).ToArray();
        var rows = GodsOfOlympusEngine.CreateGame().Layout.VisibleRows;
        IEnumerable<string> Window(IReadOnlyList<string> strip, int stop) =>
            Enumerable.Range(0, rows).Select(offset => strip[(stop + offset) % strip.Count]);
        var stops = strips.Select((strip, reel) => reel < GodsOfOlympusEngine.Profile.ScatterRequiredSymbols
            ? strip.IndexOf("FREE") : Enumerable.Range(0, strip.Count).First(stop => !Window(strip, stop).Contains("FREE"))).ToArray();
        var scatters = stops.Select((stop, reel) => Window(strips[reel], stop).Count(symbol => symbol == "FREE")).Sum();
        // Retain all scatters, suppress bananas, and inject one Sync seal.
        return new Draws([.. stops, .. Enumerable.Repeat(0, scatters), 0, 0, 1, 0, 0]);
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

public sealed class OlympusStoreEmulatorFactAttribute : FactAttribute
{
    public OlympusStoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for Gods of Olympus paid round persistence checks.";
    }
}
