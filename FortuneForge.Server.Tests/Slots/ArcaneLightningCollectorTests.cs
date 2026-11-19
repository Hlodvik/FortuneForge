// Edited: 2026-11-18T16:55:38-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:46:41-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:26:40-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T14:49:30-06:00 | Computer: Vinnabok
using ArcaneArchivesEngine = FortuneForge.Games.ArcaneArchives.ArcaneArchivesEngine;
using ArcaneWandCollector = FortuneForge.Games.ArcaneArchives.ArcaneWandCollector;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class ArcaneArchivesPaylineTests
{
    private static readonly string[] PayingSymbols = ["2", "3", "4", "5", "6"];
    private static readonly string[] Ingredients =
        ["MANDRAKE_ROOT", "NIGHTSHADE_BERRIES", "MOONCAP_MUSHROOM", "PHOENIX_FEATHER", "WOLFSBANE_WINE", "QUICKSILVER"];

    [Fact]
    public void CurrentReelsContainOnlyTheApprovedSymbols()
    {
        string[] roster = [.. PayingSymbols, "WAND", "POWER", "FORTUNE_RUNE", "BOLT", "ACE", .. Ingredients];
        var game = ArcaneArchivesEngine.CreateGame();
        var symbols = ArcaneArchivesEngine.CreateSymbolSet();
        var reels = ArcaneArchivesEngine.CreateReelSet();
        var paytable = ArcaneArchivesEngine.CreatePaytable();

        Assert.Equal(5, game.Layout.ReelCount);
        Assert.Equal(4, game.Layout.VisibleRows);
        Assert.Equal(roster.Order(), symbols.Symbols.Select(symbol => symbol.Id).Order());
        Assert.Equal(roster.Order(), reels.Reels.SelectMany(strip => strip).Distinct().Order());
        Assert.Equal(PayingSymbols, game.Symbols.PaylineSymbolIds);
        Assert.Equal(PayingSymbols, paytable.Rules.Select(rule => rule.SymbolId).Distinct().Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryReelCanLandEachIngredientWithoutMovingTheSpecialSymbols(bool tomeActive)
    {
        var baseline = tomeActive ? ArcaneArchivesEngine.CreateTomeReelSet() : ArcaneArchivesEngine.CreateReelSet();
        var rows = ArcaneArchivesEngine.CreateGame().Layout.VisibleRows;
        var reelCount = baseline.Reels.Count;
        var seenByReel = Enumerable.Range(0, reelCount).Select(_ => new HashSet<string>()).ToArray();
        int[] ingredientWindowsByRole = [52, 28, 8, 0, 0];
        string[] specialSymbols = ["WAND", "POWER", "FORTUNE_RUNE", "BOLT", "ACE"];
        for (var shift = 0; shift < reelCount; shift++)
        {
            var id = shift == 0 ? baseline.Id : $"{baseline.Id}-shift-{shift}";
            var reelSet = ArcaneArchivesEngine.FindReelSet(id);
            Assert.NotNull(reelSet);
            Assert.Equal(tomeActive, ArcaneArchivesEngine.IsBoostedReelSet(id));
            for (var reel = 0; reel < reelCount; reel++)
            {
                var strip = reelSet.Reels[reel];
                var stops = Enumerable.Range(0, strip.Count).ToArray();
                Assert.Equal(baseline.Reels[reel].Count, strip.Count);
                foreach (var symbol in specialSymbols)
                    Assert.Equal(stops.Where(stop => baseline.Reels[reel][stop] == symbol),
                        stops.Where(stop => strip[stop] == symbol));
                if (tomeActive)
                    Assert.DoesNotContain("ACE", strip);

                var windows = stops.Select(stop =>
                    Enumerable.Range(0, rows).Select(row => strip[(stop + row) % strip.Count]).ToArray()).ToArray();
                Assert.All(windows, window => Assert.InRange(window.Count(Ingredients.Contains), 0, 1));
                Assert.Equal(ingredientWindowsByRole[(reelCount + reel - shift) % reelCount],
                    windows.Count(window => window.Any(Ingredients.Contains)));
                seenByReel[reel].UnionWith(strip.Where(Ingredients.Contains));
            }
        }
        Assert.All(seenByReel, seen => Assert.Equal(Ingredients.Order(), seen.Order()));
    }

    [Fact]
    public void CurrentModelProducesNaturalPaylineAndWandAwardsAndReplays()
    {
        var random = new SeededRandom();
        var service = new SpinService(new OptionsSlotsDefinitionProvider(), new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random);
        var game = ArcaneArchivesEngine.CreateGame();
        Assert.Empty(game.Symbols.WildSymbolId);
        Assert.Null(game.FreeGames);
        Assert.Null(game.SpecialPoints);
        Assert.Null(game.Energy);

        var seen = new HashSet<string>();
        var reelSetIds = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            var result = service.Spin(game.Id, 12, "arcane-paylines", false);
            Assert.Equal(ArcaneArchivesEngine.EngineVersion, result.EngineAudit?.EngineVersion);
            Assert.Equal(0, result.FreeSpinsAwarded);
            Assert.Equal(0, result.SpecialPointsAwarded);
            Assert.Equal(0, result.BananaBonusPoints);
            Assert.Equal(0, result.MoneyGrabPoints);
            Assert.Empty(result.SealsAwarded);
            Assert.Null(result.LightningCollection);
            var ordinaryLines = result.Payout.Paylines.Where(line => line.PaylineId != ArcaneWandCollector.PaylineId);
            Assert.All(ordinaryLines.SelectMany(line => line.Matches), match =>
            {
                Assert.Contains(match.Match.SymbolId, PayingSymbols);
                Assert.Empty(match.Match.WildPositions);
            });
            Assert.InRange(result.Reels.SelectMany(reel => reel).Count(Ingredients.Contains), 0, 3);
            Assert.True(ArcaneWandCollector.SameCollection(
                ArcaneWandCollector.Calculate(result.Reels, result.WagerPoints), result.ArcaneWandCollection));
            Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
            Assert.Equal(result.ReelSetId, result.EngineAudit?.BaseGame.Math.ReelSetId);
            Assert.Equal(result.ReelSetId, result.EngineAudit?.EffectiveGame.Math.ReelSetId);
            reelSetIds.Add(result.ReelSetId);
            seen.UnionWith(result.Reels.SelectMany(reel => reel));
        }
        Assert.Equal(game.Layout.ReelCount, reelSetIds.Count);
        Assert.Contains("WAND", seen);
        Assert.Contains("FORTUNE_RUNE", seen);
        Assert.Contains("BOLT", seen);
    }

    [Fact]
    public void OneWandPaysEveryVisibleConstellationExactlyOnceAtTheUnscaledWagerMultiple()
    {
        var collection = ArcaneWandCollector.Calculate([
            ["POWER", "WAND", "BOLT", "WAND"],
            ["2", "FORTUNE_RUNE", "3", "BOLT"]
        ], wagerPoints: 12);

        Assert.NotNull(collection);
        Assert.Equal((0, 1), (collection.Wand.Reel, collection.Wand.Row));
        Assert.Equal(new long[] { 24, 48, 120, 48 }, collection.Cells.Select(cell => cell.AmountPoints));
        Assert.Equal(new long[] { 2, 4, 10, 4 }, collection.Cells.Select(cell => cell.Multiplier));
        Assert.Equal(240, collection.TotalPoints);
        var line = ArcaneWandCollector.CreatePayout(collection);
        Assert.Equal(903, line.PaylineId);
        Assert.Equal(240, line.AmountPoints);
        Assert.Equal(20, Assert.Single(line.Matches).Multiplier);
        Assert.Equal(24, ArcaneWandCollector.Calculate([["POWER"], ["WAND"]], 12)?.TotalPoints);
        Assert.Null(ArcaneWandCollector.Calculate([["BOLT", "POWER", "FORTUNE_RUNE"]], 12));
        Assert.Null(ArcaneWandCollector.Calculate([["WAND", "2", "MANDRAKE_ROOT"]], 12));
    }

    [Fact]
    public void AuditRejectsATamperedConstellationPayment()
    {
        var random = new SeededRandom();
        var service = new SpinService(new OptionsSlotsDefinitionProvider(), new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random);
        var spin = Enumerable.Range(0, 200).Select(_ => service.Spin(ArcaneArchivesEngine.GameId,
            12, "arcane-wand-audit", false)).First(outcome => outcome.ArcaneWandCollection is not null);

        Assert.Empty(SlotRoundAudit.Check(spin, spin.Payout));
        var collection = spin.ArcaneWandCollection!;
        var corrupt = spin with { ArcaneWandCollection = collection with { TotalPoints = collection.TotalPoints + 1 } };
        Assert.Contains(SlotRoundAudit.Check(corrupt, corrupt.Payout), finding => finding.Code == "slot-arcane-wand-mismatch");
    }

    [Fact]
    public void LegacyAwardedSpinKeepsAnOddCarryWagerWithoutExtendingTheFeature()
    {
        var random = new SeededRandom();
        var service = new SpinService(new OptionsSlotsDefinitionProvider(), new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random);
        var gameId = ArcaneArchivesEngine.GameId;
        Assert.Throws<ArgumentOutOfRangeException>(() => service.ValidateRequest(gameId, 3));
        service.ValidateRequest(gameId, 3, useFreeSpin: true);

        var result = service.Spin(gameId, 3, "arcane-carry", false, freeSpinFeatureMode: "sync", isFreeSpin: true);

        Assert.True(result.IsFreeSpin);
        Assert.True(result.EngineAudit?.IsDerivedFreeSpin);
        Assert.Equal(3, result.WagerPoints);
        Assert.Equal(0, result.FreeSpinsAwarded);
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
    }

    [Fact]
    public void HistoricalLightningAloneDoesNotCollectLegacyCashOrOtherSeals()
    {
        Assert.Null(ArcaneLightningCollector.Calculate([["BOLT", "PAW", "RAND_5", "SEAL_ROWS"]], 10));
    }

    private sealed class SeededRandom : IRandomIndexSource
    {
        private readonly Random random = new(2026);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
