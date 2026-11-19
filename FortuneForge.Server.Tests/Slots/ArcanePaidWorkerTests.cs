// Edited: 2026-11-18T18:15:15-06:00 | Computer: Vinnabok
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Spins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

[Collection(MidnightVaultPaidEmulatorCollection.Name)]
public sealed class ArcanePaidWorkerTests(MidnightVaultPaidEmulatorFixture emulator)
{
    private const string UserId = "local-arcane-worker-test";
    private const string GameId = ArcaneArchivesEngine.GameId;
    private const long WagerPoints = 12;
    private const decimal PointValueInCents = 25;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignedWorkerHonorsTheAdmittedTomeAndPotionOrLeavesPriorStateIntact(bool legacyWorker)
    {
        var database = emulator.Database();
        var store = new FirestoreAccountStore(database);
        Assert.NotNull((await store.CreateAsync(UserId, UserId, UserId.ToUpperInvariant(),
            UserId + "@example.test", "local-test-password", "active", default)).Value);
        var wallet = database.Collection("userBalances").Document(UserId + "_slotsCredits");
        await wallet.UpdateAsync("available", 1_000L);
        var walletBefore = await wallet.GetSnapshotAsync();

        await store.SelectArcaneTomePageAsync(UserId, "herbalism", false, DateTime.UtcNow, default);
        var guard = Assert.Single((await database.Collection("slotSpinGuards").WhereEqualTo("userId", UserId)
            .GetSnapshotAsync()).Documents);
        var tome = ArcaneTome.SelectPage(
            ArcaneTome.CompleteSpin(ArcaneTome.Empty, [[ArcaneTome.GrimoireSymbolId]], false).State,
            "herbalism", true);
        var brewed = ArcanePotions.CompleteSpin(ArcanePotions.Empty,
            Enumerable.Repeat("PHOENIX_FEATHER", ArcanePotions.Majority).ToArray(), false);
        var potion = Assert.Single(brewed.Bottles);
        await guard.Reference.UpdateAsync(new Dictionary<string, object>
        {
            ["arcaneTomeJson"] = JsonSerializer.Serialize(tome),
            ["arcanePotionsJson"] = JsonSerializer.Serialize(ArcanePotions.Use(brewed, potion.Id))
        });

        var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["GameWorker:SigningKey"] = signingKey;
        await using var worker = builder.Build();
        worker.UseGameWorkerAuthentication(GameId);
        var resolver = new ArcanePaidSlotOutcomes(new SeededRandom());
        PaidArcaneSpinOptions? received = null;
        worker.MapGameAction(PaidSlotOutcomeProtocol.Route, (PaidSlotOutcomeRequest request) =>
        {
            received = request.Arcane;
            return resolver.Resolve(legacyWorker ? request with { Arcane = null } : request);
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
        Task<SlotSpinAdmission> Admit() => store.BeginSlotSpinAsync(UserId, GameId, WagerPoints,
            PointValueInCents, false, false, 0, DateTime.UtcNow, TimeSpan.Zero, default,
            (pending, ct) => PaidSlotWorker.ResolveAsync(client, GameId, UserId, pending, ct));

        if (legacyWorker)
        {
            await Assert.ThrowsAsync<GameWorkerUnavailableException>(Admit);
            Assert.Equal(walletBefore.UpdateTime, (await wallet.GetSnapshotAsync()).UpdateTime);
            var unchanged = await store.GetSlotStateAsync(UserId, GameId, PointValueInCents, default);
            Assert.Equal(ArcaneTome.BoostedSpinCount, unchanged.ArcaneTome!.BoostedSpinsRemaining);
            Assert.Equal(ArcanePotions.Duration, unchanged.ArcanePotions!.Active!.SpinsRemaining);
            Assert.Empty((await database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
        }
        else
        {
            var admission = await Admit();
            var resolved = Assert.IsType<SpinResult>(admission.ResolvedOutcome);
            Assert.Equal(potion, resolved.EngineAudit!.ArcanePotionApplied);
            Assert.True(ArcaneArchivesEngine.IsBoostedReelSet(resolved.ReelSetId));
            Assert.DoesNotContain(ArcaneTome.GrimoireSymbolId, resolved.EngineAudit.ReelSet.Reels.SelectMany(strip => strip));
            Assert.False(ArcaneArchivesEngine.PotionReelsMatch(resolved.EngineAudit.ReelSet, null));
            Assert.Empty(SlotRoundAudit.Check(resolved, resolved.Payout, isPaid: true));

            var settled = await store.RecordSlotSpinAsync(UserId, resolved, admission, DateTime.UtcNow, default);
            Assert.Equal(ArcaneTome.BoostedSpinCount - 1, settled.ArcaneTome!.BoostedSpinsRemaining);
            Assert.Equal(ArcanePotions.Duration - 1, settled.ArcanePotions!.Active!.SpinsRemaining);
        }

        Assert.Equal(new PaidArcaneSpinOptions(ArcaneArchivesEngine.EngineVersion, true,
            new PaidArcanePotion(potion.Id, potion.IngredientId, potion.Potency)), received);
    }

    [Fact]
    public void UnversionedHostRequestStillReceivesTheLiveLightningModel()
    {
        var request = new PaidSlotOutcomeRequest(Guid.NewGuid(), GameId, UserId, WagerPoints,
            false, false, 0, null, 0, null);
        var legacy = new ArcanePaidSlotOutcomes(new SeededRandom());
        var response = Enumerable.Range(0, 1000).Select(_ => legacy.Resolve(request))
            .First(response => response.Outcome.GetProperty("payout").GetProperty("paylines").EnumerateArray()
                .Any(line => line.GetProperty("paylineId").GetInt32() == ArcaneLightningCollector.PaylineId));
        Assert.Equal(PaidSlotOutcomeProtocol.ClassicSlotKind, response.OutcomeKind);
        Assert.Equal(request.AdmissionId, response.AdmissionId);
        var outcome = response.Outcome.Deserialize<ClassicSlotSpinOutcome>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(outcome);
        Assert.Equal("arcane-lightning-collector-v1", outcome.Audit.EngineVersion);
        Assert.Equal("arcane-archives-lightning-reels-v1", outcome.ReelSetId);
        var lightning = ArcaneLightningCollector.Calculate(outcome.Reels, WagerPoints);
        Assert.NotNull(lightning);
        var line = Assert.Single(outcome.Payout.Paylines, line => line.PaylineId == ArcaneLightningCollector.PaylineId);
        Assert.Equal(lightning.TotalPoints, line.AmountPoints);
    }

    private sealed class SeededRandom : IGameRandomSource
    {
        private readonly Random random = new(20261118);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
