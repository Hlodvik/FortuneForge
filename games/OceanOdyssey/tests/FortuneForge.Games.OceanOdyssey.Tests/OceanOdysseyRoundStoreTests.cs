using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.OceanOdyssey.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.OceanOdyssey.Tests;

public sealed class OceanOdysseyRoundStoreTests
{
    private const string Account = "ocean-odyssey-native-player";
    private const string GameId = OceanOdysseyEngine.GameId;

    [OceanOdysseyStoreEmulatorFact]
    public async Task PaidScatterAndEnergyRoundRetainsItsExactPayoutWithoutADuplicateDebit()
    {
        var database = Connect();
        var wallet = new Wallet { LoseNextReply = true };
        await SeedNearCollection(database, wagerPoints: 130);
        await SeedBalance(database, "energy", 100);
        var operation = new OceanOdysseyRoundRequest("ocean-odyssey-paid-scatter-001", 10, false);
        var service = Service(database, wallet, ScatterAndLineWinDraws());
        using var signed = GameSecurityContext.Push(new("ocean-odyssey-request-1", Account, "private-ocean-odyssey-session",
            "203.0.113.19", DateTime.UtcNow, "POST", OceanOdysseyPaidRouteContract.SpinPath, OceanOdysseyPaidRouteContract.AuditGameId,
            null, operation.OperationId, null, null, "sanitized", "direct"));

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, operation, true, default));
        var pending = await service.ReadStateAsync(Account, default);
        Assert.Equal(operation, pending.PendingOperation);
        Assert.Equal(100, pending.EnergyBalance);
        Assert.Equal(27, pending.SealCollections.Single(track => track.SealId == "rows").Count);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);

        var restored = Service(database, wallet, new NoDraws());
        var conflict = await Assert.ThrowsAsync<OceanOdysseyRoundConflictException>(() => restored.SpinAsync(Account,
            operation with { OperationId = "ocean-odyssey-another-spin" }, true, default));
        Assert.Equal(operation, conflict.PendingRequest);
        await Assert.ThrowsAsync<OceanOdysseyRoundConflictException>(() => restored.SpinAsync(Account,
            operation with { WagerPoints = 12 }, true, default));

        var result = await restored.SpinAsync(Account, operation, false, default);
        var round = Assert.IsType<OceanOdysseyPreparedRound>(result.Round);
        Assert.Equal(23, round.Outcome.Audit.EffectiveGame.Paylines.Count);
        Assert.Equal(1.071m, round.Outcome.Audit.EffectiveGame.Math.PayoutMultiplier);
        Assert.Equal(0, round.Outcome.CollectorPoints);
        Assert.DoesNotContain(round.Outcome.Payout.Paylines, line => line.PaylineId == 901);
        Assert.NotEmpty(round.Outcome.Payout.Paylines);
        var multiplied = round.Outcome.Payout.Paylines.SelectMany(line => line.Matches)
            .Sum(match => checked((long)Math.Round(match.AmountPoints * 1.5m, MidpointRounding.AwayFromZero)));
        Assert.Equal(multiplied, round.Progress.SettledPayout.TotalPoints);
        Assert.Equal(1.5m, round.Progress.EnergySettlement.PayoutMultiplier);
        Assert.Equal(100, round.Progress.EnergySettlement.MeterBalanceBeforeReset);
        Assert.Equal(0, result.Features!.EnergyBalance);
        Assert.Empty(round.Outcome.CollectionsAwarded);
        Assert.Equal(6, round.Progress.CollectionFreeSpinsAwarded);
        Assert.Equal(6, round.Outcome.FreeSpinsAwarded);
        Assert.Equal(0, result.Features.SealCollections.Single(track => track.SealId == "rows").Count);
        Assert.Equal(2, result.Features.SealCollections.Single(track => track.SealId == "sync").Count);
        Assert.Equal(12, result.Features.FreeSpinsRemaining);
        Assert.Equal(5, result.Features.FreeSpinWagerPoints);
        Assert.Equal("sync-rand", result.Features.FreeSpinFeatureMode);
        Assert.Equal(250, result.ChargedWagerCents);
        Assert.Equal(round.Progress.SettledPayout.TotalPoints * 25, wallet.Last!.PayoutCents);
        Assert.Equal(round.Outcome.SpinId.ToString("N"), wallet.Last.OperationId);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        Assert.Equal(result.Wallet, (await restored.SpinAsync(Account, operation, false, default)).Wallet);
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var raw = Assert.Single((await database.Collection(OceanOdysseyRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.DoesNotContain("private-ocean-odyssey-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);
        Assert.Equal(GameId, (await Guard(database).GetSnapshotAsync()).GetValue<string>(OceanOdysseyRoundStateCodec.OwnerField));
    }

    [OceanOdysseyStoreEmulatorFact]
    public async Task FreeCreditsBlockCashAndSpendLegacyBeforeNewAwardsAtTheSavedOddWager()
    {
        var database = Connect();
        var wallet = new Wallet { LegacyRemaining = 2 };
        var time = new RoundClock();
        await SeedNearCollection(database, wagerPoints: 130, lockedWager: 10);
        await SeedBalance(database, "energy", 100);
        var service = Service(database, wallet, ScatterAndLineWinDraws(), time);
        await Assert.ThrowsAsync<OceanOdysseyFreeSpinsRequiredException>(() => service.SpinAsync(Account,
            new("ocean-odyssey-cash-blocked", 10, false), true, default));
        await Assert.ThrowsAsync<OceanOdysseyFreeWagerLockedException>(() => service.SpinAsync(Account,
            new("ocean-odyssey-mismatched-carry", 12, true), true, default));
        Assert.Equal(0, wallet.Calls);

        var first = await service.SpinAsync(Account, new("ocean-odyssey-legacy-award", 10, true), true, default);
        Assert.Equal(13, first.Features!.FreeSpinsRemaining);
        Assert.Equal(5, first.Features.FreeSpinWagerPoints);
        Assert.Equal("sync-rand", first.Features.FreeSpinFeatureMode);
        Assert.Equal(1, wallet.LegacyRemaining);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, first.ChargedWagerCents);

        time.Advance();
        var lastLegacy = await Service(database, wallet, new HighDraws(), time).SpinAsync(Account,
            new("ocean-odyssey-last-legacy", 5, true), true, default);
        Assert.True(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.LegacyRemaining);
        Assert.Equal(12, lastLegacy.Features!.FreeSpinsRemaining);
        Assert.Equal(5, lastLegacy.Round!.Outcome.WagerPoints);
        Assert.Equal("sync-rand", lastLegacy.Round.Outcome.Audit.FeatureMode);
        Assert.All(lastLegacy.Round.Outcome.Reels, reel => Assert.Equal(4, reel.Count));

        time.Advance();
        var native = await Service(database, wallet, new HighDraws(), time).SpinAsync(Account,
            new("ocean-odyssey-first-native", 5, true), true, default);
        Assert.False(wallet.Last!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.Last.ExpectedLegacyFreeGamesRemaining);
        Assert.Equal(11, native.Features!.FreeSpinsRemaining);
        Assert.Equal(0, native.ChargedWagerCents);
    }

    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"ocean-odyssey-native-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static OceanOdysseyRoundService Service(FirestoreDb database, Wallet wallet, IGameRandomSource random, TimeProvider? time = null) =>
        new(new OceanOdysseyRoundStore(database, random, time ?? TimeProvider.System), wallet, wallet);

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
            ["sealCounts"] = new Dictionary<string, long> { ["rows"] = 27, ["sync"] = 2 },
            ["sealWagerCents"] = new Dictionary<string, long> { ["rows"] = wagerPoints * 25, ["sync"] = 12 * 25 }
        };
        if (lockedWager is { } wager) fields["freeSpinWagerCents"] = wager * 25;
        return Guard(database).CreateAsync(fields);
    }

    private static Draws ScatterAndLineWinDraws()
    {
        // Three opening 3s, exact trigger scatters, then no bananas or natural seals.
        return new Draws([30, 6, 4, 11, 0, 0, 0, 0, 0, 0, 99]);
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

public sealed class OceanOdysseyStoreEmulatorFactAttribute : FactAttribute
{
    public OceanOdysseyStoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for Ocean Odyssey paid round persistence checks.";
    }
}
