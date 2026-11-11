using System.Text.Json;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class ArcaneLightningCollectorTests
{
    private const string GameId = SlotSpecialRoundProfiles.ArcaneArchivesGameId;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DemoSpinShowsEachValueOnceAndNeverUsesTheOldEnergyMeter(int lightningCount)
    {
        var spin = CreateSpin(lightningCount);
        var auditStore = new CaptureDemoAudit();
        var controller = new DemoSlotsController(spin, auditStore, NullLogger<DemoSlotsController>.Instance);
        var synced = lightningCount == 2;
        var request = new DemoSpinRequest(GameId, 10, synced, synced ? 1 : 0, synced ? 10 : null, 100,
            FreeSpinFeatureMode: synced ? "sync" : null);
        var response = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(
            await controller.Spin(request, CancellationToken.None)).Value);

        Assert.Equal(lightningCount, response.Reels.SelectMany(reel => reel).Count(symbol => symbol == "BOLT"));
        Assert.Equal(0, response.EnergyAwarded);
        Assert.Equal(0, response.EnergyBalance);
        Assert.False(response.EnergyMultiplierApplied);
        Assert.Equal(1, response.SealsAwarded["SEAL_RAND"]);
        Assert.Empty(SlotRoundAudit.Check(auditStore.Entry!.EngineResult, response.Payout));

        if (lightningCount == 0)
        {
            Assert.Null(response.LightningCollection);
            Assert.DoesNotContain(response.Payout.Paylines, line => line.PaylineId == ArcaneLightningCollector.PaylineId);
            return;
        }

        var collection = AssertCollection(response, [5L, 10L, 20L]);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(collection.TotalPoints, json.RootElement.GetProperty("lightningCollection").GetProperty("totalPoints").GetInt64());
    }

    [Fact]
    public void AdmittedCarrySpinPaysRoundedFaceValuesAtItsLockedOddWager()
    {
        var spin = CreateSpin(lightningCount: 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => spin.ValidateRequest(GameId, 3));
        spin.ValidateRequest(GameId, 3, useFreeSpin: true);
        var admission = new SlotSpinAdmission(3, 0, true, 0, false, 0, null, 100, null)
        {
            AdmissionId = Guid.NewGuid()
        };
        var result = spin.SpinAdmitted(GameId, admission, "carry-fixture");

        Assert.True(result.IsFreeSpin);
        Assert.True(result.EngineAudit?.IsDerivedFreeSpin);
        Assert.Equal(100, result.EngineAudit?.EnergyBalanceBefore);
        Assert.Equal(0, result.EnergyAwarded);
        AssertCollection(result, [2L, 3L, 6L]);
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
    }

    [Fact]
    public void LightningAloneDoesNotCollectLegacyCashOrOtherSeals()
    {
        Assert.Null(ArcaneLightningCollector.Calculate([["BOLT", "PAW", "RAND_5", "SEAL_ROWS"]], 10));
    }

    private static ArcaneLightningCollection AssertCollection(SpinResult result, long[] tiers)
    {
        var collection = Assert.IsType<ArcaneLightningCollection>(result.LightningCollection);
        Assert.Equal("BOLT", result.Reels[collection.Collector.Reel][collection.Collector.Row]);
        Assert.Equal(tiers, collection.Cells.Select(cell => cell.AmountPoints).Distinct().Order().ToArray());
        var visibleValues = result.Reels.SelectMany((reel, reelIndex) => reel.Select((symbol, row) => (symbol, reelIndex, row)))
            .Where(cell => cell.symbol is "7" or "POWER" or "SEAL_RAND")
            .Select(cell => new ArcaneConstellationValue(cell.reelIndex, cell.row,
                tiers[cell.symbol == "7" ? 0 : cell.symbol == "POWER" ? 1 : 2]));
        Assert.Equal(visibleValues.ToArray(), collection.Cells.ToArray());
        Assert.Equal(collection.Cells.Sum(cell => cell.AmountPoints), collection.TotalPoints);
        Assert.Equal(collection.TotalPoints, Assert.Single(result.Payout.Paylines,
            line => line.PaylineId == ArcaneLightningCollector.PaylineId).AmountPoints);
        Assert.Equal(result.Payout.Paylines.Sum(line => line.AmountPoints), result.Payout.TotalPoints);
        return collection;
    }

    private static SpinService CreateSpin(int lightningCount)
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        var options = settings.RootElement.GetProperty("Slots")
            .Deserialize<SlotsOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var provider = new OptionsSlotsDefinitionProvider(Options.Create(options));
        var game = provider.GetGame(GameId)!;
        var strips = provider.GetReelSet(game.Math.ReelSetId)!.Reels;
        string[] symbols = ["7", "POWER", lightningCount > 0 ? "BOLT" : "2", "3", "4"];
        var stops = strips.Select((strip, reel) => Enumerable.Range(0, strip.Count).First(stop =>
        {
            var visible = Enumerable.Range(0, game.Layout.VisibleRows).Select(row => strip[(stop + row) % strip.Count]).ToArray();
            return !visible.Contains("FREE") && visible.Contains(symbols[reel]) &&
                visible.Contains("BOLT") == (reel == 2 && lightningCount > 0);
        })).ToArray();
        // Skip legacy mana, paws and wands. Inject one Fortune seal into a common cell.
        int[] featureRolls = [0, 1, 1, 0, 0, 1, 0, 3];
        int[] packageRolls = lightningCount == 2
            ? [.. stops, 2, 1, .. featureRolls]
            : [.. stops, .. featureRolls];
        return new SpinService(provider, new CryptoReelGenerator(new ScriptedRandom(stops)), new CombinationEvaluator(),
            new PayoutCalculator(), new ScriptedRandom(packageRolls));
    }

    private sealed class ScriptedRandom(IEnumerable<int> values) : IRandomIndexSource
    {
        private readonly Queue<int> _values = new(values);

        public int Next(int maximumExclusive)
        {
            var value = _values.Dequeue();
            Assert.InRange(value, 0, maximumExclusive - 1);
            return value;
        }
    }

    private sealed class CaptureDemoAudit : IDemoSlotAuditStore
    {
        public DemoSlotAuditEntry? Entry { get; private set; }

        public Task RecordAsync(DemoSlotAuditEntry entry, CancellationToken cancellationToken)
        {
            Entry = entry;
            return Task.CompletedTask;
        }
    }
}
