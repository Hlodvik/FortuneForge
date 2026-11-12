using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.JadeDragon;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

[Collection(MidnightVaultPaidEmulatorCollection.Name)]
public sealed class JadeDragonCascadeWalletTests(MidnightVaultPaidEmulatorFixture emulator)
{
    private const string GameId = JadeDragonEngine.GameId;
    private const string UserId = "local-jade-wallet-player";
    private const long WagerPoints = 20;
    private const decimal PointValueInCents = JadeDragonCascadeRules.WagerPointValueInCents;

    [Fact]
    public async Task CascadeCreditsWholeCentsAndFreeSpinsExactlyOnce()
    {
        var player = await CreatePlayer();
        var admission = await Admit(player.Store, WagerPoints, useFreeSpin: false);
        Assert.Equal(JadeDragonCascadeRules.Version, admission.MathModelId);
        Assert.Equal(500L, admission.ChargedWagerCents);

        // Two dragon ways clear, leaving three pearls on the final board.
        var draws = new ScriptedDraws([
            0, 0, 96, 96,
            0, 82, 96, 35,
            0, 82, 50, 66,
            50, 82, 21, 35,
            50, 66, 35, 21,
            21, 50, 66, 35
        ]);
        var engine = new SpinService(new OptionsSlotsDefinitionProvider(), new CryptoReelGenerator(draws),
            new CombinationEvaluator(), new PayoutCalculator(), draws);
        var result = engine.SpinAdmitted(GameId, admission, UserId);
        Assert.Equal(2, Assert.Single(Assert.Single(result.JadeDragonCascade!.Steps).Awards).Ways);
        Assert.Equal(PointValueInCents, result.PointValueInCents);
        Assert.Equal(1, result.PayoutPointValueInCents);
        Assert.Equal(47L, result.Payout.TotalPoints);
        Assert.Equal(10, result.FreeSpinsAwarded);

        var settlement = await player.Store.RecordSlotSpinAsync(UserId, result, admission, DateTime.UtcNow, default);
        Assert.Equal(995.47m, settlement.SlotsCreditsBalance);
        Assert.Equal(47L, settlement.Payout.TotalPoints);
        Assert.Equal(10, settlement.FreeSpinsRemaining);

        var ledger = player.Database.Collection("balanceTransactions");
        var spinId = result.SpinId.ToString("N");
        var wager = await ledger.Document(spinId + "-wager").GetSnapshotAsync();
        var payout = await ledger.Document(spinId + "-payout").GetSnapshotAsync();
        Assert.Equal(-5m, (decimal)wager.GetValue<double>("amount"));
        Assert.Equal(0.47m, (decimal)payout.GetValue<double>("amount"));
        Assert.Equal(995.47m, (decimal)payout.GetValue<double>("balanceAfter"));

        var reloaded = new FirestoreAccountStore(player.Database);
        var state = await reloaded.GetSlotStateAsync(UserId, GameId, PointValueInCents, default);
        Assert.Equal(10, state.FreeSpinsRemaining);
        Assert.Equal(WagerPoints, state.FreeSpinWagerPoints);
        Assert.Null(state.FreeSpinFeatureMode);
        Assert.Equal(JadeDragonCascadeRules.Version,
            (await player.Guard.GetSnapshotAsync()).GetValue<string>("freeSpinMathModelId"));

        // Consume a free spin before retrying so the original receipt must come
        // from the frozen settlement, rather than the current entitlement count.
        var freeSpin = await Admit(reloaded, 40, useFreeSpin: true);
        Assert.Equal(JadeDragonCascadeRules.Version, freeSpin.MathModelId);
        Assert.Equal(WagerPoints, freeSpin.WagerPoints);
        Assert.Equal(0L, freeSpin.ChargedWagerCents);
        Assert.Equal(9, freeSpin.FreeSpinsRemaining);
        var walletBeforeRetry = await player.Wallet.GetSnapshotAsync();
        Assert.Equal(995L, walletBeforeRetry.GetValue<long>("available"));
        Assert.Equal(47L, walletBeforeRetry.GetValue<long>("availableFractionalCents"));
        var ledgerCount = (await ledger.GetSnapshotAsync()).Count;

        var retry = await reloaded.RecordSlotSpinAsync(UserId, result, admission, DateTime.UtcNow, default);
        Assert.Equal(settlement.SlotsCreditsBalance, retry.SlotsCreditsBalance);
        Assert.Equal(settlement.Payout.TotalPoints, retry.Payout.TotalPoints);
        Assert.Equal(settlement.FreeSpinsRemaining, retry.FreeSpinsRemaining);
        Assert.Equal(walletBeforeRetry.UpdateTime, (await player.Wallet.GetSnapshotAsync()).UpdateTime);
        Assert.Equal(ledgerCount, (await ledger.GetSnapshotAsync()).Count);
        Assert.Equal(9, (await reloaded.GetSlotStateAsync(UserId, GameId, PointValueInCents, default)).FreeSpinsRemaining);
    }

    [Fact]
    public async Task SignedJadeWorkerOutcomeSettlesThroughTheSameWallet()
    {
        var player = await CreatePlayer();
        var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["GameWorker:SigningKey"] = signingKey;
        await using var worker = builder.Build();
        worker.UseGameWorkerAuthentication(GameId);
        var draws = new ScriptedDraws([
            0, 0, 96, 96, 0, 82, 96, 35, 0, 82, 50, 66, 50, 82, 21, 35, 50, 66, 35, 21,
            21, 50, 66, 35
        ]);
        var engine = new JadeDragonCascadeEngine(draws);
        worker.MapGameAction(PaidSlotOutcomeProtocol.Route, (PaidSlotOutcomeRequest request) =>
        {
            Assert.Equal(UserId, request.PlayerId);
            Assert.Equal(JadeDragonCascadeRules.Version, request.MathModelId);
            return PaidSlotOutcomeResponse.Create(request, JadeDragonCascadeRules.Version,
                engine.Spin(request.WagerPoints, request.IsFreeSpin));
        }, request => request.PlayerId);
        await worker.StartAsync();
        using var http = new HttpClient();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"GameWorkers:{GameId}:Url"] = Assert.Single(worker.Urls),
            [$"GameWorkers:{GameId}:SigningKey"] = signingKey
        }).Build();
        var client = new GameWorkerClient(http, configuration, worker.Environment,
            TimeProvider.System, NullLogger<GameWorkerClient>.Instance);
        var admission = await player.Store.BeginSlotSpinAsync(UserId, GameId, WagerPoints, PointValueInCents,
            false, false, 0, DateTime.UtcNow, TimeSpan.Zero, default,
            (pending, ct) => PaidSlotWorker.ResolveAsync(client, GameId, UserId, pending, ct));
        Assert.NotNull(admission.ResolvedOutcome);
        var settled = await player.Store.RecordSlotSpinAsync(UserId, admission.ResolvedOutcome, admission, DateTime.UtcNow, default);
        Assert.Equal(995.47m, settled.SlotsCreditsBalance);
        Assert.Equal(10, settled.FreeSpinsRemaining);
    }

    [Fact]
    public async Task FailedWorkerDoesNotConsumeAnAwardedSpinOrCreateAnAdmission()
    {
        var player = await CreatePlayer();
        await player.Guard.SetAsync(new Dictionary<string, object>
        {
            ["userId"] = UserId,
            ["freeSpinWagerCents"] = 500L,
            ["freeSpinMathModelId"] = JadeDragonCascadeRules.Version
        });
        await player.FreeGames.SetAsync(new Dictionary<string, object> { ["available"] = 2L, ["version"] = 0L });
        var ledger = player.Database.Collection("balanceTransactions");
        var ledgerCount = (await ledger.GetSnapshotAsync()).Count;
        var wallet = await player.Wallet.GetSnapshotAsync();

        await Assert.ThrowsAsync<GameWorkerUnavailableException>(() => player.Store.BeginSlotSpinAsync(
            UserId, GameId, WagerPoints, PointValueInCents, useFreeSpin: true, useSpecialBoost: false,
            specialBoostCost: 0, startedAtUtc: DateTime.UtcNow, cooldown: TimeSpan.Zero, cancellationToken: default,
            resolveOutcome: (_, _) => throw new GameWorkerUnavailableException(GameId)));

        Assert.Equal(2L, (await player.FreeGames.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(wallet.UpdateTime, (await player.Wallet.GetSnapshotAsync()).UpdateTime);
        Assert.False((await player.Guard.GetSnapshotAsync()).ContainsField("activeAdmissionId"));
        Assert.Empty((await player.Database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
        Assert.Equal(ledgerCount, (await ledger.GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task LegacyFreeSpinsKeepTheirWagerAndFeatureWithoutACascadeModel()
    {
        var player = await CreatePlayer();
        await player.Guard.SetAsync(new Dictionary<string, object>
        {
            ["userId"] = UserId,
            ["freeSpinWagerCents"] = 12_500L,
            ["freeSpinFeatureMode"] = JadeDragonCollections.CollectionFeatureMode
        });
        await player.FreeGames.SetAsync(new Dictionary<string, object> { ["available"] = 2L, ["version"] = 0L });

        var admission = await Admit(player.Store, WagerPoints, useFreeSpin: true);
        Assert.True(admission.IsFreeSpin);
        Assert.Null(admission.MathModelId);
        Assert.Equal(500L, admission.WagerPoints);
        Assert.Equal(0L, admission.ChargedWagerCents);
        Assert.Equal(JadeDragonCollections.CollectionFeatureMode, admission.FreeSpinFeatureMode);
        Assert.Equal(1, admission.FreeSpinsRemaining);
    }

    private static Task<SlotSpinAdmission> Admit(FirestoreAccountStore store, long wagerPoints, bool useFreeSpin) =>
        store.BeginSlotSpinAsync(UserId, GameId, wagerPoints, PointValueInCents, useFreeSpin,
            useSpecialBoost: false, specialBoostCost: 0, startedAtUtc: DateTime.UtcNow,
            cooldown: TimeSpan.Zero, cancellationToken: default);

    private async Task<PaidPlayer> CreatePlayer()
    {
        var database = emulator.Database();
        var store = new FirestoreAccountStore(database);
        Assert.NotNull((await store.CreateAsync(UserId, UserId, UserId.ToUpperInvariant(),
            UserId + "@example.test", "unused-local-test", "active", default)).Value);
        var player = new PaidPlayer(database, store);
        await player.Wallet.UpdateAsync(new Dictionary<string, object>
        {
            ["available"] = 1_000L,
            ["availableFractionalCents"] = 0L
        });
        return player;
    }

    private sealed record PaidPlayer(FirestoreDb Database, FirestoreAccountStore Store)
    {
        public DocumentReference Guard => Database.Collection("slotSpinGuards").Document(UserId + "_" +
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(GameId))));
        public DocumentReference Wallet => Database.Collection("userBalances").Document(UserId + "_slotsCredits");
        public DocumentReference FreeGames => Database.Collection("userBalances").Document(UserId + "_freeGames:" + GameId);
    }

    private sealed class ScriptedDraws(int[] indexes) : IRandomIndexSource
    {
        private int next;

        public int Next(int maximumExclusive)
        {
            var index = indexes[next++];
            Assert.InRange(index, 0, maximumExclusive - 1);
            return index;
        }
    }
}
