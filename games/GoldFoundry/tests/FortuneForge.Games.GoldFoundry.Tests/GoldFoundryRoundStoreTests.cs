using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.GoldFoundry.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.GoldFoundry.Tests;

public sealed class GoldFoundryRoundStoreTests
{
    private const string Account = "foundry-paid-player";
    private const string GameId = GoldFoundryEngine.GameId;
    private const long Wager = 100;
    private const long WagerCents = Wager * 25;

    [GoldFoundryRoundEmulatorFact]
    public async Task ClaimedLegacyCruciblePoursAtExactCentsAndRecoversWalletReplyLoss()
    {
        var database = Connect();
        var wallet = new Wallet { LoseNextReply = true, ReceiptOffset = TimeSpan.FromSeconds(-5) };
        const long legacyWagers = 97_349;
        await SeedLegacy(database, 99, legacyWagers, 5);
        var request = new GoldFoundryRoundRequest("foundry-original-pour-operation", Wager, false);
        var first = Service(database, wallet, new Draws(Stops(charge: true)));
        using var observed = GameSecurityContext.Push(new("foundry-original-request", Account, "private-foundry-session",
            "203.0.113.13", DateTime.UtcNow, "POST", GoldFoundryPaidRouteContract.SpinPath,
            GoldFoundryPaidRouteContract.AuditGameId, null, request.OperationId, null, null, "sanitized", "direct"));

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => first.SpinAsync(Account, request, true, default));
        var pending = await first.ReadStateAsync(Account, default);
        Assert.Equal(request, pending.PendingOperation);
        Assert.Equal(99, pending.EnergyBalance);
        Assert.Equal(legacyWagers, pending.CrucibleWageredCents);
        Assert.Equal(wallet.Balance / 100m, pending.SlotsCreditsBalance);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);

        var conflict = await Assert.ThrowsAsync<GoldFoundryRoundConflictException>(() =>
            first.SpinAsync(Account, request with { OperationId = "foundry-other-pour-operation" }, false, default));
        Assert.Equal(request, conflict.PendingRequest);
        await Assert.ThrowsAsync<GoldFoundryRoundConflictException>(() =>
            first.SpinAsync(Account, request with { WagerPoints = 102 }, true, default));
        var retry = Service(database, wallet, new NoDraws());
        var result = await retry.SpinAsync(Account, request, false, default);
        var round = Assert.IsType<GoldFoundryPreparedRound>(result.Round);
        var publicSpin = GoldFoundrySpinResponse.From(result);
        Assert.True(round.Crucible.Poured);
        Assert.True(publicSpin.CruciblePoured);
        Assert.Equal(14_977, round.Crucible.PayoutCents);
        Assert.Equal(round.Crucible.PayoutCents, publicSpin.CruciblePayoutCents);
        Assert.Equal(round.Outcome.Payout.TotalPoints * 25 + round.Crucible.PayoutCents, round.PayoutCents);
        Assert.Equal(round.PayoutCents, wallet.Last!.PayoutCents);
        Assert.Equal(WagerCents, wallet.Last.WagerCents);
        Assert.Null(wallet.Last.ExpectedLegacyFreeGamesRemaining);
        Assert.False(wallet.Last.SpendLegacyFreeGame);
        Assert.Equal(1, wallet.Approvals);
        Assert.Equal(2, wallet.Calls);
        Assert.Equal(0, result.Features!.EnergyBalance);
        Assert.Equal(0, result.Features.CrucibleWageredCents);
        Assert.Equal(99, (await ArchivedEnergy(database).GetSnapshotAsync()).GetValue<long>("available"));

        var replayed = await retry.SpinAsync(Account, request, false, default);
        Assert.Equal(JsonSerializer.Serialize(publicSpin), JsonSerializer.Serialize(GoldFoundrySpinResponse.From(replayed)));
        Assert.Equal(2, wallet.Calls);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var witnesses = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents
            .ToDictionary(document => document.Id);
        Assert.Equal(2, witnesses.Count);
        var earned = witnesses[$"{round.Outcome.SpinId:N}-energy-award"];
        var spent = witnesses[$"{round.Outcome.SpinId:N}-energy-crucible-pour"];
        Assert.Equal((1L, 100L), (earned.GetValue<long>("amount"), earned.GetValue<long>("balanceAfter")));
        Assert.Equal((-100L, 0L), (spent.GetValue<long>("amount"), spent.GetValue<long>("balanceAfter")));
        Assert.All(witnesses.Values, document => Assert.Equal($"energy:{GameId}", document.GetValue<string>("currencyId")));
        var raw = Assert.Single((await database.Collection(GoldFoundryRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.DoesNotContain("private-foundry-session", raw.GetValue<string>("preparedJson"), StringComparison.Ordinal);
    }

    [GoldFoundryRoundEmulatorFact]
    public async Task TerminalWalletDeclineRetainsUnadvancedImportedMeterAndDoesNotPublishTheBoard()
    {
        var database = Connect();
        var wallet = new Wallet { Balance = 0 };
        const long legacyWagers = 5_931;
        await SeedLegacy(database, 99, legacyWagers, 7);
        var request = new GoldFoundryRoundRequest("foundry-insufficient-wallet", Wager, false);
        var service = Service(database, wallet, new Draws(Stops(charge: true)));
        var declined = await service.SpinAsync(Account, request, true, default);
        Assert.True(declined.Wallet.InsufficientFunds);
        Assert.Null(declined.Round);
        Assert.Null(declined.Features);
        Assert.Equal(0, wallet.Approvals);
        var after = await service.ReadStateAsync(Account, default);
        Assert.Equal(99, after.EnergyBalance);
        Assert.Equal(legacyWagers, after.CrucibleWageredCents);
        Assert.Null(after.PendingOperation);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        var owner = await Guard(database).GetSnapshotAsync();
        Assert.Equal(GoldFoundryRoundStateCodec.Owner, owner.GetValue<string>(GoldFoundryRoundStateCodec.OwnerField));

        wallet.Balance = 10_000;
        var stillDeclined = await Service(database, wallet, new NoDraws()).SpinAsync(Account, request, false, default);
        Assert.Equal(declined.Wallet, stillDeclined.Wallet);
        Assert.Equal(1, wallet.Calls);
    }

    [GoldFoundryRoundEmulatorFact]
    public async Task OnlyChargingPaidSpinsAddTheirWagerToTheCrucible()
    {
        var database = Connect();
        var wallet = new Wallet();
        var time = new RoundClock();
        const long beforeWagers = 3_300;
        await SeedLegacy(database, 50, beforeWagers, 3);
        var noCharge = Service(database, wallet, new Draws(Stops(charge: false)), time);
        var first = await noCharge.SpinAsync(Account, new("foundry-no-energy-outcome", Wager, false), true, default);
        Assert.Equal(0, first.Round!.Outcome.EnergyAwarded);
        Assert.Equal(50, first.Features!.EnergyBalance);
        Assert.Equal(beforeWagers, first.Features.CrucibleWageredCents);
        Assert.Equal(first.Round.Outcome.Payout.TotalPoints * 25, first.Round.PayoutCents);
        time.Advance();

        var withCharge = Service(database, wallet, new Draws(Stops(charge: true)), time);
        var second = await withCharge.SpinAsync(Account, new("foundry-qualifying-energy", Wager, false), true, default);
        Assert.True(second.Round!.Outcome.EnergyAwarded > 0);
        Assert.False(second.Round.Crucible.Poured);
        Assert.Equal(50 + second.Round.Outcome.EnergyAwarded, second.Features!.EnergyBalance);
        Assert.Equal(beforeWagers + WagerCents, second.Features.CrucibleWageredCents);
        Assert.Equal(GoldFoundryCrucibleRules.PourValueCents(beforeWagers + WagerCents), second.Features.CruciblePourValueCents);
        Assert.Equal(2, wallet.Approvals);
    }

    [GoldFoundryRoundEmulatorFact]
    public async Task OldUnresolvedAdmissionAndForeignRequestsCannotSpendOrDraw()
    {
        var database = Connect();
        var wallet = new Wallet();
        var service = Service(database, wallet, new NoDraws());
        await Assert.ThrowsAsync<ArgumentException>(() => service.SpinAsync(Account,
            new("foundry-free-mode-invalid", Wager, true), true, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SpinAsync(Account,
            new("foundry-boost-mode-invalid", Wager, false, true), true, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SpinAsync(Account,
            new("foundry-odd-wager-invalid", 3, false), true, default));
        await Assert.ThrowsAsync<GoldFoundryRoundPausedException>(() => service.SpinAsync(Account,
            new("foundry-paused-new-spin", Wager, false), false, default));

        var oldAdmission = Guid.NewGuid().ToString("N");
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            [SlotSpinGuard.ActiveAdmissionField] = oldAdmission,
            [SlotSpinGuard.LastSpinStartedField] = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(-1))
        });
        await Assert.ThrowsAsync<GoldFoundryRoundConflictException>(() => service.SpinAsync(Account,
            new("foundry-expired-old-lease", Wager, false), true, default));
        Assert.Equal(oldAdmission, (await Guard(database).GetSnapshotAsync()).GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
        Assert.Equal(0, wallet.Calls);
    }

    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"gold-foundry-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    private static GoldFoundryRoundService Service(FirestoreDb database, Wallet wallet, IGameRandomSource random,
        TimeProvider? time = null) => new(new GoldFoundryRoundStore(database, random, time ?? TimeProvider.System), wallet, wallet);

    private static DocumentReference Guard(FirestoreDb database) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Account, GameId));

    private static DocumentReference ArchivedEnergy(FirestoreDb database) =>
        database.Collection("userBalances").Document($"{Account}_energy:{GameId}");

    private static async Task SeedLegacy(FirestoreDb database, long energy, long wagered, int pity)
    {
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["crucibleWageredCents"] = wagered, ["consecutiveFiveMisses"] = (long)pity
        });
        await ArchivedEnergy(database).CreateAsync(new Dictionary<string, object>
        {
            ["userId"] = Account, ["currencyId"] = $"energy:{GameId}", ["available"] = energy
        });
    }

    private static int[] Stops(bool charge)
    {
        var strips = GoldFoundryEngine.CreateReelSet().Reels;
        var proof = GoldFoundryEngine.CreateGame().Energy!.SymbolId;
        var chargedReel = strips.FindIndex(strip => strip.Contains(proof));
        return strips.Select((strip, reel) => Enumerable.Range(0, strip.Count).First(stop =>
            Enumerable.Range(0, 4).Any(offset => strip[(stop + offset) % strip.Count] == proof) ==
                (charge && reel == chargedReel))).ToArray();
    }

    private sealed class Draws(int[] draws) : IGameRandomSource
    {
        private int cursor;
        public int Next(int maximumExclusive) => draws[cursor++];
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
        public long Balance { get; set; } = 1_000_000;
        public int Approvals { get; private set; }
        public int Calls { get; private set; }
        public bool LoseNextReply { get; set; }
        public TimeSpan ReceiptOffset { get; init; }
        public GameWalletSettlementRequest? Last { get; private set; }

        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult(new GameWalletAccountState(0, Balance));

        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request,
            CancellationToken cancellationToken)
        {
            Last = request;
            Calls++;
            if (receipts.TryGetValue(request.OperationId, out var existing))
            {
                Assert.Equal(existing.Terms, request);
                return Task.FromResult(existing.Receipt with { WasAlreadySettled = true });
            }
            var before = Balance;
            var declined = before < request.WagerCents;
            if (!declined)
            {
                Balance = before - request.WagerCents + request.PayoutCents;
                Approvals++;
            }
            var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(GameId, accountId, request.OperationId),
                !declined ? $"{request.OperationId}-wager" : null,
                !declined && request.PayoutCents > 0 ? $"{request.OperationId}-payout" : null,
                before, declined ? before : before - request.WagerCents, Balance, DateTimeOffset.UtcNow + ReceiptOffset,
                false, declined);
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

public sealed class GoldFoundryRoundEmulatorFactAttribute : FactAttribute
{
    public GoldFoundryRoundEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for Gold Foundry paid round persistence checks.";
    }
}
