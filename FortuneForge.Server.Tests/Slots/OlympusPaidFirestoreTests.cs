using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

[Collection(MidnightVaultPaidEmulatorCollection.Name)]
public sealed class OlympusPaidFirestoreTests(MidnightVaultPaidEmulatorFixture emulator)
{
    private const string GameId = SlotSpecialRoundProfiles.GodsOfOlympusGameId;
    private const string UserId = "local-olympus-player";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoWinningTumblesSettleExactlyOnceAndReplayUsingTheAdmittedVersion(bool admittedBeforeUpgrade)
    {
        var player = await CreatePlayer();
        await player.Guard.SetAsync(new Dictionary<string, object>
        {
            ["userId"] = UserId,
            ["consecutiveFiveMisses"] = 7L,
            ["sealCounts"] = new Dictionary<string, long> { ["sync"] = 7L },
            ["sealWagerCents"] = new Dictionary<string, long> { ["sync"] = 350L }
        });
        var admission = await player.Store.BeginSlotSpinAsync(UserId, GameId, 6, 25, false, false, 0,
            DateTime.UtcNow, TimeSpan.Zero, default);
        Assert.Equal(OlympusTumbleDemo.EngineVersion, admission.MathModelId);
        Assert.Equal(150L, admission.ChargedWagerCents);
        if (admittedBeforeUpgrade)
        {
            // Simulate an accepted v2 spin left outstanding at deployment.
            admission = admission with { MathModelId = OlympusTumbleDemo.EngineVersionV2 };
            await player.Database.Collection("slotSpinAdmissions").Document(admission.AdmissionId.ToString("N"))
                .UpdateAsync("mathModelId", OlympusTumbleDemo.EngineVersionV2);
        }

        // Eight "6" symbols win first, then a single "3" refill joins seven survivors.
        var otherSymbols = new[] { 0, 34, 51, 77, 85, 91, 96 };
        var indexes = Enumerable.Repeat(66, 8).Concat(Enumerable.Repeat(17, 7))
            .Concat(Enumerable.Range(0, 15).Select(i => otherSymbols[i % otherSymbols.Length]))
            .Append(17)
            .Concat(Enumerable.Range(0, 15).Select(i => otherSymbols[i % otherSymbols.Length]))
            .ToArray();
        var engine = Engine(new ScriptedDraws(indexes));
        var spin = engine.SpinAdmitted(GameId, admission, UserId);
        Assert.Equal(admission.MathModelId, spin.EngineAudit!.EngineVersion);
        Assert.Equal(admission.MathModelId, spin.ReelSetId);
        Assert.Equal(admission.MathModelId, spin.PaytableId);
        Assert.False(spin.IsFreeSpin);
        var firstAward = admittedBeforeUpgrade ? 18L : 15L;
        var expectedPoints = firstAward + 3;
        Assert.Collection(spin.OlympusTumble!.Steps,
            first => Assert.Equal(firstAward, Assert.Single(first.Awards).AmountPoints),
            second => Assert.Equal(3L, Assert.Single(second.Awards).AmountPoints));
        Assert.Empty(spin.Payout.Paylines);
        Assert.Equal(expectedPoints, spin.Payout.TotalPoints);

        var differentVersion = admittedBeforeUpgrade ? OlympusTumbleDemo.EngineVersion : OlympusTumbleDemo.EngineVersionV2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => player.Store.RecordSlotSpinAsync(
            UserId, spin, admission with { MathModelId = differentVersion }, DateTime.UtcNow, default));

        var settlement = await player.Store.RecordSlotSpinAsync(UserId, spin, admission, DateTime.UtcNow, default);
        var expectedPayout = expectedPoints * 0.25m;
        var expectedBalance = 1000m - 1.50m + expectedPayout;
        Assert.Equal(expectedBalance, settlement.SlotsCreditsBalance);
        var ledger = player.Database.Collection("balanceTransactions");
        var spinId = spin.SpinId.ToString("N");
        var wager = await ledger.Document(spinId + "-wager").GetSnapshotAsync();
        var payout = await ledger.Document(spinId + "-payout").GetSnapshotAsync();
        Assert.Equal(-1.50m, (decimal)wager.GetValue<double>("amount"));
        Assert.Equal(expectedPayout, (decimal)payout.GetValue<double>("amount"));
        Assert.Equal(expectedBalance, (decimal)payout.GetValue<double>("balanceAfter"));
        var guard = await player.Guard.GetSnapshotAsync();
        Assert.Equal(7L, guard.GetValue<long>("consecutiveFiveMisses"));
        Assert.Equal(7L, guard.GetValue<Dictionary<string, long>>("sealCounts")["sync"]);
        Assert.Equal(350L, guard.GetValue<Dictionary<string, long>>("sealWagerCents")["sync"]);

        var walletBeforeRetry = await player.Wallet.GetSnapshotAsync();
        var replay = await player.Store.RecordSlotSpinAsync(UserId, spin, admission, DateTime.UtcNow, default);
        Assert.Equal(settlement.SlotsCreditsBalance, replay.SlotsCreditsBalance);
        Assert.Equal(walletBeforeRetry.UpdateTime, (await player.Wallet.GetSnapshotAsync()).UpdateTime);
        Assert.Equal(2, (await ledger.GetSnapshotAsync()).Count);

        var evidence = await ReadAudit(player.Database, spin);
        Assert.Equal(admission.MathModelId, evidence.GetProperty("admittedMathModelId").GetString());
        var tumble = evidence.GetProperty("engineAudit").GetProperty("olympusTumble");
        Assert.False(tumble.GetProperty("showcase").GetBoolean());
        Assert.Equal(indexes, tumble.GetProperty("drawIndexes").Deserialize<int[]>(Json));
    }

    [Fact]
    public async Task OwedLegacySpinsBlockCashAndKeepTheirOddLockedWagerAndFeatureMode()
    {
        var player = await CreatePlayer();
        await player.Guard.SetAsync(new Dictionary<string, object>
        {
            ["userId"] = UserId,
            ["freeSpinWagerCents"] = 75L,
            ["freeSpinFeatureMode"] = "paw-rand"
        });
        var bank = player.Database.Collection("userBalances").Document(UserId + "_freeGames:" + GameId);
        await bank.SetAsync(new Dictionary<string, object> { ["available"] = 2L, ["version"] = 0L });
        var accessor = new HttpContextAccessor();
        var accountService = new AccountService(player.Store, null!, null!, accessor);
        var controller = new SlotsController(Engine(new QuietDraws()), accountService, NullLogger<SlotsController>.Instance);
        Task<ActionResult> Spin(long wagerPoints, bool useFreeSpin)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Cookie = AccountSessionCookie.Name + "=" + player.Token;
            accessor.HttpContext = context;
            controller.ControllerContext = new ControllerContext { HttpContext = context };
            return controller.Spin(new(GameId, wagerPoints, useFreeSpin, false), default);
        }

        var cash = Assert.IsAssignableFrom<ObjectResult>(await Spin(2, false));
        Assert.Equal(StatusCodes.Status409Conflict, cash.StatusCode);
        var error = JsonSerializer.SerializeToElement(cash.Value, Json);
        Assert.Equal("free-spins-required", error.GetProperty("code").GetString());
        Assert.Equal(2L, (await bank.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Empty((await player.Database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
        Assert.Empty((await player.Database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);

        var result = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(await Spin(3, true)).Value);
        Assert.True(result.IsFreeSpin);
        Assert.Null(result.OlympusTumble);
        Assert.Equal(3L, result.WagerPoints);
        Assert.Equal(1L, (await bank.GetSnapshotAsync()).GetValue<long>("available"));
        var guard = await player.Guard.GetSnapshotAsync();
        Assert.Equal(75L, guard.GetValue<long>("freeSpinWagerCents"));
        Assert.Equal("paw-rand", guard.GetValue<string>("freeSpinFeatureMode"));
        var wager = await player.Database.Collection("balanceTransactions")
            .Document(result.SpinId.ToString("N") + "-wager").GetSnapshotAsync();
        Assert.False(wager.Exists);

        var evidence = await ReadAudit(player.Database, result);
        Assert.True(evidence.GetProperty("isFreeSpin").GetBoolean());
        Assert.Equal("paw-rand", evidence.GetProperty("activeFreeSpinFeatureMode").GetString());
        Assert.Equal(SpinService.AuditEngineVersion, evidence.GetProperty("engineAudit").GetProperty("engineVersion").GetString());
    }

    private async Task<PaidPlayer> CreatePlayer()
    {
        var database = emulator.Database();
        var store = new FirestoreAccountStore(database);
        Assert.NotNull((await store.CreateAsync(UserId, UserId, UserId.ToUpperInvariant(), UserId + "@example.test",
            "unused-local-test", "active", default)).Value);
        var player = new PaidPlayer(database, store, Hash(UserId));
        await player.Wallet.UpdateAsync(new Dictionary<string, object> { ["available"] = 1000L, ["availableFractionalCents"] = 0L });
        await store.CreateSessionAsync(Hash(player.Token), UserId, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), null, default);
        return player;
    }

    private static SpinService Engine(IRandomIndexSource draws)
    {
        using var appSettings = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        var provider = new OptionsSlotsDefinitionProvider(Options.Create(appSettings.RootElement.GetProperty("Slots")
            .Deserialize<SlotsOptions>(Json)!));
        var game = provider.GetGame(GameId)!;
        var strips = provider.GetReelSet(game.Math.ReelSetId)!.Reels
            .Select(strip => CryptoReelGenerator.GetPlayableStrip(game, strip));
        var stops = strips.Select(strip => Enumerable.Range(0, strip.Count).First(stop =>
            Enumerable.Range(0, game.Layout.VisibleRows).All(row => strip[(stop + row) % strip.Count] != "FREE"))).ToArray();
        return new SpinService(provider, new CryptoReelGenerator(new ScriptedDraws(stops)),
            new CombinationEvaluator(), new PayoutCalculator(), draws);
    }

    private static async Task<JsonElement> ReadAudit(FirestoreDb database, SpinResult result)
    {
        var summary = Assert.Single((await database.Collection(RoundAuditDocuments.Summaries)
            .WhereEqualTo("roundId", result.SpinId.ToString("N")).GetSnapshotAsync()).Documents);
        var store = new FirestoreGameRoundAuditStore(database, Options.Create(new AdminOperationsOptions
        { CursorSigningKey = "olympus-local-paid-evidence-only-signing-key" }));
        var detail = Assert.IsType<GameRoundAuditDetail>(await store.ReadAsync(summary.Id, default));
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(detail.Summary, detail.PayloadJson!));
        using var payload = JsonDocument.Parse(detail.PayloadJson!);
        return payload.RootElement.GetProperty("data").Clone();
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record PaidPlayer(FirestoreDb Database, FirestoreAccountStore Store, string Token)
    {
        public DocumentReference Guard => Database.Collection("slotSpinGuards").Document(UserId + "_" + Hash(GameId));
        public DocumentReference Wallet => Database.Collection("userBalances").Document(UserId + "_slotsCredits");
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

    private sealed class QuietDraws : IRandomIndexSource
    {
        public int Next(int maximumExclusive) => maximumExclusive == 100 ? 47 : 0;
    }
}
