using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class MidnightVaultDefinitionTests
{
    private const string V1 = "midnight-vault-paytable-v1";
    private const string V2 = "midnight-vault-paytable-v2";
    private const string V3 = "midnight-vault-paytable-v3";
    private static readonly (string Symbol, long Three, long Five)[] CurrentRates =
    [
        ("2", 1, 2), ("3", 1, 1), ("4", 1, 4), ("5", 1, 4), ("6", 1, 4),
        ("7", 2, 6), ("8", 2, 8), ("ACE", 3, 10), ("POWER", 1, 2)
    ];
    [Fact]
    public void DedicatedDefinitions_ExposeFifteenSymbolsAndSevenOrdinaryPayingIdentities()
    {
        var provider = Provider(ReadOptions());
        var game = Assert.IsType<GameDefinition>(provider.GetGame(SlotSpecialRoundProfiles.DragonHoardGameId));
        var symbols = Assert.IsType<SymbolSetDefinition>(provider.GetSymbolSet(game.Symbols.SymbolSetId));
        var paytable = Assert.IsType<PaytableDefinition>(provider.GetPaytable(game.Math.PaytableId));
        var reels = Assert.IsType<ReelSetDefinition>(provider.GetReelSet(game.Math.ReelSetId));

        Assert.Equal(MidnightVaultSlotDefinitions.SymbolSetId, symbols.Id);
        Assert.Equal(symbols.Id, paytable.SymbolSetId);
        Assert.Equal(symbols.Id, reels.SymbolSetId);
        Assert.Equal(MidnightVaultSlotDefinitions.PaytableId, paytable.Id);
        Assert.Equal(MidnightVaultBaseReelSet.CurrentId, reels.Id);
        Assert.Equal(new[]
        {
            "2", "3", "4", "5", "6", "7", "8", "ACE", "FREE", "POWER", "BANANA",
            "SEAL_SYNC", "SEAL_ROWS", "SEAL_PAW", "SEAL_RAND"
        }.Order(StringComparer.Ordinal), symbols.Symbols.Select(symbol => symbol.Id).Order(StringComparer.Ordinal));
        Assert.Equal(15, symbols.Symbols.Select(symbol => symbol.Id).Distinct(StringComparer.Ordinal).Count());

        var pairs = Enumerable.Range(2, 7).Select(value =>
        {
            var id = value.ToString();
            return (
                paytable.Rules.Single(rule => rule.SymbolId == id && rule.MatchLength == 3).Multiplier,
                paytable.Rules.Single(rule => rule.SymbolId == id && rule.MatchLength == 5).Multiplier);
        }).ToArray();
        Assert.Equal(CurrentRates.Take(7).Select(rate => (rate.Three, rate.Five)), pairs);
        Assert.All(pairs, pair => { Assert.True(pair.Item1 > 0); Assert.True(pair.Item2 > 0); });
        Assert.Equal(7, paytable.Rules.Where(rule => int.TryParse(rule.SymbolId, out var id) && id is >= 2 and <= 8)
            .Select(rule => rule.SymbolId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Rules(CurrentRules()), Rules(paytable.Rules));
        Assert.Equal(1m, game.Math.PayoutMultiplier);
        Assert.Equal(MidnightVaultSlotDefinitions.MinimumWagerPoints, game.Wagering.MinimumWagerPoints);
        Assert.Equal(MidnightVaultSlotDefinitions.MaximumWagerPoints, game.Wagering.MaximumWagerPoints);
        Assert.Equal(new[] { 3, 5 }, game.Symbols.NativeWildMatchLengths);
        Assert.Equal(new[] { 5 }, game.Symbols.WildSubstitutionMatchLengths);
        Assert.Equal("ACE", game.Symbols.WildSymbolId);
        Assert.Equal("POWER", game.SpecialPoints?.SymbolId);
        Assert.Equal(new[] { "2", "3", "4" }, game.SpecialPoints!.CommonSymbolIds);
        Assert.Null(game.Energy);
        Assert.Equal(3, game.FreeGames?.RequiredSymbols);
        Assert.Equal(MidnightVaultSlotDefinitions.BonusRoundSpins, game.FreeGames?.AwardedSpins);
        Assert.True(SlotSpecialRoundProfiles.TryGet(game.Id, out var profile));
        Assert.False(profile.UsesEnergy);
        Assert.False(profile.UsesDirectValueTokens);
        Assert.Equal(30, profile.CollectionTarget);
        Assert.Equal(MidnightVaultSlotDefinitions.BonusRoundSpins, profile.CollectionAwardedSpins);
        Assert.Equal("rows-paw", profile.CollectionFeatureMode);
    }

    [Theory]
    [InlineData(null, 4)]
    [InlineData("rows-paw", 6)]
    public void ActualSpinPipeline_GeneratesAndPaysTheNewSymbolOnBaseAndEnhancedReels(string? mode, int rows)
    {
        var provider = Provider(ReadOptions());
        var game = provider.GetGame(SlotSpecialRoundProfiles.DragonHoardGameId)!;
        var reels = provider.GetReelSet(game.Math.ReelSetId)!;
        var stops = reels.Reels.Select(reel => reel.IndexOf("8")).ToArray();
        Assert.All(stops, stop => Assert.True(stop >= 0));
        var spin = new SpinService(provider,
            new CryptoReelGenerator(new FixedStopsRandom(stops)),
            new CombinationEvaluator(), new PayoutCalculator(), new QuietFeatureRandom());

        var result = spin.Spin(game.Id, 100, "midnight-definition-test", false, freeSpinFeatureMode: mode);

        Assert.Equal(MidnightVaultSlotDefinitions.SymbolSetId, result.SymbolSetId);
        Assert.Equal(MidnightVaultSlotDefinitions.PaytableId, result.PaytableId);
        Assert.Equal(MidnightVaultBaseReelSet.CurrentId, result.ReelSetId);
        Assert.All(result.Reels, reel =>
        {
            Assert.Equal(rows, reel.Count);
            Assert.Equal("8", reel[0]);
            Assert.DoesNotContain("BOLT", reel);
            Assert.DoesNotContain("PAW", reel);
            Assert.DoesNotContain(reel, symbol => symbol.StartsWith("RAND_", StringComparison.Ordinal));
        });
        var award = Assert.Single(result.Payout.Paylines, payout => payout.PaylineId == 1);
        var match = Assert.Single(award.Matches);
        Assert.Equal("8", match.Match.SymbolId);
        Assert.Equal(5, match.Match.MatchLength);
        Assert.Equal(8, match.Multiplier);
        Assert.Equal((long)Math.Round(100 * 8 * game.Math.PayoutMultiplier, MidpointRounding.AwayFromZero), award.AmountPoints);
        Assert.Equal(0, result.EnergyAwarded);
        Assert.Equal(0, result.MoneyGrabPoints);
        var audit = Assert.IsType<SlotSpinEngineAudit>(result.EngineAudit);
        Assert.Equal(rows, audit.EffectiveGame.Layout.VisibleRows);
        Assert.Equal(15, audit.SymbolSet.Symbols.Count);
        var persistedAudit = JsonSerializer.Deserialize<SlotSpinEngineAudit>(JsonSerializer.Serialize(audit))!;
        Assert.Empty(SlotRoundAudit.Check(result with { EngineAudit = persistedAudit }, result.Payout));
    }

    [Theory]
    [InlineData("8,8,8,2,3", 3, 2)]
    [InlineData("8,8,8,8,3", 4, 2)]
    [InlineData("8,8,8,8,8", 5, 8)]
    [InlineData("8,ACE,8,8,8", 5, 8)]
    [InlineData("8,ACE,8,2,3", 0, 0)]
    public void NewSymbol_UsesExistingNativeRunFallbackAndFiveOnlyWildSubstitution(
        string lineText, int matchLength, long multiplier)
    {
        var provider = Provider(ReadOptions());
        var game = provider.GetGame(SlotSpecialRoundProfiles.DragonHoardGameId)!;
        var line = lineText.Split(',');
        IReadOnlyList<IReadOnlyList<string>> grid = line.Select(symbol =>
            (IReadOnlyList<string>)new[] { symbol, "FREE", "FREE", "FREE" }).ToArray();
        var evaluations = new CombinationEvaluator().Evaluate(grid, game, provider.GetSymbolSet(game.Symbols.SymbolSetId)!);
        var payout = new PayoutCalculator().Calculate(evaluations, game, provider.GetPaytable(game.Math.PaytableId)!, 100);
        var award = payout.Paylines.SingleOrDefault(value => value.PaylineId == 1);

        if (matchLength == 0)
        {
            Assert.Null(award);
            return;
        }
        Assert.NotNull(award);
        var match = Assert.Single(award.Matches);
        Assert.Equal("8", match.Match.SymbolId);
        Assert.Equal(matchLength, match.Match.MatchLength);
        Assert.Equal(multiplier, match.Multiplier);
        Assert.Equal((long)Math.Round(100 * multiplier * game.Math.PayoutMultiplier, MidpointRounding.AwayFromZero), award.AmountPoints);
    }

    [Fact]
    public void DedicatedDefinitions_DoNotMutateConfiguredInputsOrOtherGameContracts()
    {
        var options = ReadOptions();
        var before = JsonSerializer.Serialize(options);
        var provider = Provider(options);
        var midnight = provider.GetGame(SlotSpecialRoundProfiles.DragonHoardGameId)!;
        var reels = provider.GetReelSet(midnight.Math.ReelSetId)!;
        var paytable = provider.GetPaytable(midnight.Math.PaytableId)!;
        var legacyPaytable = provider.GetPaytable(MidnightVaultSlotDefinitions.LegacyPaytableId)!;

        MidnightVaultHistoricalFixtures.AssertHistoricalReels(provider.GetReelSet(MidnightVaultBaseReelSet.Id)!);
        Assert.Equal(Rules(MidnightVaultHistoricalFixtures.V1Audit().Paytable.Rules), Rules(legacyPaytable.Rules));
        Assert.Equal(Rules(MidnightVaultHistoricalFixtures.V2Rules()), Rules(provider.GetPaytable(V2)!.Rules));
        Assert.Equal(Rules(CurrentRules()), Rules(paytable.Rules));
        foreach (var profile in SlotSpecialRoundProfiles.All.Where(profile => profile.GameId != midnight.Id))
        {
            var other = provider.GetGame(profile.GameId)!;
            Assert.NotEqual(midnight.Symbols.SymbolSetId, other.Symbols.SymbolSetId);
            Assert.NotEqual(midnight.Math.ReelSetId, other.Math.ReelSetId);
            Assert.NotEqual(midnight.Math.PaytableId, other.Math.PaytableId);
        }

        reels.Reels[0].Clear();
        paytable.Rules.Clear();
        midnight.Symbols.NativeWildMatchLengths.Clear();
        Assert.Equal(before, JsonSerializer.Serialize(options));
        Assert.NotEmpty(provider.GetReelSet(midnight.Math.ReelSetId)!.Reels[0]);
        Assert.NotEmpty(provider.GetPaytable(midnight.Math.PaytableId)!.Rules);
        Assert.Equal(new[] { 3, 5 }, provider.GetGame(midnight.Id)!.Symbols.NativeWildMatchLengths);
    }

    [Fact]
    public void CurrentReels_ReduceCombinedRankFrequencyAndPreserveSpecialStopPositions()
    {
        var provider = Provider(ReadOptions());
        var game = provider.GetGame(SlotSpecialRoundProfiles.DragonHoardGameId)!;
        var reels = provider.GetReelSet(game.Math.ReelSetId)!;
        var letters = new HashSet<string>(["3", "2", "4", "5"], StringComparer.Ordinal);
        int[][] expectedRanks = [[7, 7, 6, 6], [6, 6, 7, 7], [6, 7, 7, 6], [7, 6, 6, 7], [7, 7, 6, 6]];
        string[] rankIds = ["2", "3", "4", "5"];

        var historical = provider.GetReelSet(MidnightVaultBaseReelSet.Id)!;
        MidnightVaultHistoricalFixtures.AssertHistoricalReels(historical);
        for (var reel = 0; reel < reels.Reels.Count; reel++)
        {
            var strip = reels.Reels[reel];
            var counts = strip.GroupBy(symbol => symbol).ToDictionary(group => group.Key, group => group.Count());
            Assert.Equal(64, strip.Count);
            for (var rank = 0; rank < rankIds.Length; rank++) Assert.Equal(expectedRanks[reel][rank], counts[rankIds[rank]]);
            Assert.Equal(26, strip.Count(letters.Contains));
            Assert.True(strip.Count(letters.Contains) < historical.Reels[reel].Count(letters.Contains));
            Assert.Equal(10, counts["6"]);
            Assert.Equal(10, counts["7"]);
            Assert.Equal(9, counts["8"]);
            Assert.Equal(5, counts["POWER"]);
            Assert.Equal(3, counts["FREE"]);
            Assert.Equal(1, counts["ACE"]);
            for (var stop = 0; stop < strip.Count; stop++)
            {
                if (historical.Reels[reel][stop] is "FREE" or "POWER" or "ACE")
                    Assert.Equal(historical.Reels[reel][stop], strip[stop]);
                var next = (stop + 1) % strip.Count;
                if (strip[stop] == strip[next])
                {
                    Assert.Equal(historical.Reels[reel][stop], strip[stop]);
                    Assert.Equal(historical.Reels[reel][next], strip[next]);
                }
                foreach (var rows in new[] { 4, 6 })
                    Assert.Contains(Enumerable.Range(0, rows).Select(row => strip[(stop + row) % strip.Count]), symbol => !letters.Contains(symbol));
            }
        }
    }

    [Fact]
    public void CurrentIntegerPayouts_AreExactlyProportionalAcrossEveryTrustedIntegerWager()
    {
        var provider = Provider(ReadOptions());
        var game = provider.GetGame(SlotSpecialRoundProfiles.DragonHoardGameId)!;
        var paytable = provider.GetPaytable(game.Math.PaytableId)!;
        var calculator = new PayoutCalculator();
        foreach (var rule in paytable.Rules)
        {
            var lengths = rule.MatchLength == 3 && rule.SymbolId != "ACE" ? new[] { 3, 4 } : new[] { rule.MatchLength };
            foreach (var length in lengths)
            {
                var match = new SymbolMatch(1, rule.SymbolId, length,
                    Enumerable.Range(0, length).Select(reel => new GridPosition(reel, 0)).ToArray(), []);
                PaylineEvaluation[] evaluations = [new(1, [new MatchCandidate([match])])];
                for (var wager = MidnightVaultSlotDefinitions.MinimumWagerPoints;
                     wager <= MidnightVaultSlotDefinitions.MaximumWagerPoints; wager++)
                {
                    var payout = calculator.Calculate(evaluations, game, paytable, wager);
                    Assert.Equal(checked(wager * rule.Multiplier), payout.TotalPoints);
                }
            }
        }
    }

    [Fact]
    public void GameVersionSelection_PinsScalarsAndReelsWithoutChangingActiveV3()
    {
        var provider = Provider(ReadOptions());
        var active = provider.GetGame(SlotSpecialRoundProfiles.DragonHoardGameId)!;
        var before = JsonSerializer.Serialize(active);
        var legacy = MidnightVaultSlotDefinitions.CreateGameForPaytable(active, MidnightVaultSlotDefinitions.LegacyPaytableId);

        Assert.Equal(active.Id, legacy.Id);
        Assert.Equal(MidnightVaultSlotDefinitions.LegacyPaytableId, legacy.Math.PaytableId);
        Assert.Equal(1.09m, legacy.Math.PayoutMultiplier);
        Assert.Equal(MidnightVaultBaseReelSet.Id, legacy.Math.ReelSetId);
        Assert.Equal(6, legacy.FreeGames?.AwardedSpins);
        var previous = MidnightVaultSlotDefinitions.CreateGameForPaytable(active, V2);
        Assert.Equal(V2, previous.Math.PaytableId);
        Assert.Equal(MidnightVaultBaseReelSet.Id, previous.Math.ReelSetId);
        Assert.Equal(1m, previous.Math.PayoutMultiplier);
        Assert.Equal(10, previous.FreeGames?.AwardedSpins);
        var restoredCurrent = MidnightVaultSlotDefinitions.CreateGameForPaytable(legacy, MidnightVaultSlotDefinitions.PaytableId);
        Assert.Equal(active.Math.PayoutMultiplier, restoredCurrent.Math.PayoutMultiplier);
        var expectedSettings = JsonNode.Parse(before)!;
        var restoredSettings = JsonSerializer.SerializeToNode(restoredCurrent)!;
        expectedSettings["Math"]!.AsObject().Remove("PayoutMultiplier");
        restoredSettings["Math"]!.AsObject().Remove("PayoutMultiplier");
        Assert.Equal(expectedSettings.ToJsonString(), restoredSettings.ToJsonString());
        Assert.Equal(1m, restoredCurrent.Math.PayoutMultiplier);
        Assert.Equal(V3, restoredCurrent.Math.PaytableId);
        Assert.Equal(MidnightVaultBaseReelSet.CurrentId, restoredCurrent.Math.ReelSetId);
        Assert.Equal(10, restoredCurrent.FreeGames?.AwardedSpins);
        Assert.NotSame(active.Layout, legacy.Layout);
        Assert.NotSame(active.Symbols, legacy.Symbols);
        Assert.NotSame(active.Matching, legacy.Matching);
        Assert.NotSame(active.Math.Targets, legacy.Math.Targets);
        Assert.NotSame(active.Wagering, legacy.Wagering);
        Assert.NotSame(active.FreeGames, legacy.FreeGames);
        Assert.NotSame(active.SpecialPoints, legacy.SpecialPoints);
        legacy.Paylines[0].Clear();
        legacy.Symbols.NativeWildMatchLengths.Clear();
        legacy.Math.PaylinePayoutSteps.Clear();
        legacy.SpecialPoints!.CommonSymbolIds.Clear();
        Assert.Equal(before, JsonSerializer.Serialize(active));
        Assert.Equal(before, JsonSerializer.Serialize(provider.GetGame(active.Id)));
        Assert.Throws<ArgumentException>(() => MidnightVaultSlotDefinitions.CreateGameForPaytable(active, "midnight-vault-paytable-v4"));
        Assert.Throws<ArgumentException>(() => MidnightVaultSlotDefinitions.CreateGameForPaytable(
            provider.GetGame(SlotSpecialRoundProfiles.CosmicFortuneGameId)!, MidnightVaultSlotDefinitions.LegacyPaytableId));
    }

    [Fact]
    public void FrozenLegacyV1Evidence_ReplaysAfterTheActiveGameSwitchesToV3()
    {
        var provider = Provider(ReadOptions());
        var active = provider.GetGame(SlotSpecialRoundProfiles.DragonHoardGameId)!;
        var legacy = MidnightVaultSlotDefinitions.CreateGameForPaytable(active, MidnightVaultSlotDefinitions.LegacyPaytableId);
        var reels = provider.GetReelSet(legacy.Math.ReelSetId)!;
        var engine = new SpinService(new FixedGameProvider(provider, legacy),
            new CryptoReelGenerator(new FixedStopsRandom(reels.Reels.Select(reel => reel.IndexOf("8")))),
            new CombinationEvaluator(), new PayoutCalculator(), new QuietFeatureRandom());

        var oldResult = engine.Spin(legacy.Id, 100, "midnight-legacy-evidence-test", false);
        var frozen = JsonSerializer.Deserialize<SlotSpinEngineAudit>(JsonSerializer.Serialize(oldResult.EngineAudit))!;
        var nativeEight = Assert.Single(oldResult.Payout.Paylines, line => line.PaylineId == 1);
        Assert.Equal(1526L, nativeEight.AmountPoints);
        Assert.Equal(MidnightVaultSlotDefinitions.LegacyPaytableId, oldResult.PaytableId);
        Assert.Equal(1.09m, frozen.BaseGame.Math.PayoutMultiplier);
        Assert.Equal(MidnightVaultSlotDefinitions.PaytableId, provider.GetGame(active.Id)!.Math.PaytableId);
        Assert.Equal(1m, provider.GetGame(active.Id)!.Math.PayoutMultiplier);
        Assert.Empty(SlotRoundAudit.Check(oldResult with { EngineAudit = frozen }, oldResult.Payout));
    }

    [Theory]
    [InlineData(V1, 1.09, 6)]
    [InlineData(V2, 1.0, 10)]
    public void HistoricalModelsKeepExactFrozenStripOrderAndNativeRates(string model, double scalar, int spins)
    {
        var provider = Provider(ReadOptions());
        var game = MidnightVaultSlotDefinitions.CreateGameForPaytable(provider.GetGame(SlotSpecialRoundProfiles.DragonHoardGameId)!, model);
        Assert.Equal((decimal)scalar, game.Math.PayoutMultiplier);
        Assert.Equal(spins, game.FreeGames!.AwardedSpins);
        MidnightVaultHistoricalFixtures.AssertHistoricalReels(provider.GetReelSet(game.Math.ReelSetId)!);
        var expected = model == V1 ? MidnightVaultHistoricalFixtures.V1Audit().Paytable.Rules : MidnightVaultHistoricalFixtures.V2Rules();
        Assert.Equal(Rules(expected), Rules(provider.GetPaytable(model)!.Rules));
    }

    [Theory]
    [InlineData(V1)]
    [InlineData(V2)]
    [InlineData(V3)]
    public void CapturedModelKeepsItsPublishedGameAndProfileHitTargets(string model)
    {
        var result = VersionedSpin(model, null, "8");
        var audit = result.EngineAudit!;
        Assert.Equal(model == V3 ? .21m : .30m, audit.BaseGame.Math.Targets.HitRate);
        Assert.Equal(model == V3 ? .21m : .30m, audit.EffectiveGame.Math.Targets.HitRate);
        if (model == V3) Assert.Equal(.21m, audit.FeatureProfile!.TargetHitRate);
        else Assert.Null(audit.FeatureProfile!.TargetHitRate);
        Assert.True(SlotSpecialRoundProfiles.TryGet(result.GameId, out var activeProfile));
        Assert.Equal(.21m, activeProfile.TargetHitRate);
    }

    [Theory]
    [InlineData(V1, null, 6)]
    [InlineData(V1, "rows-paw", 6)]
    [InlineData(V2, "rows-paw", 10)]
    [InlineData(V3, "rows-paw", 10)]
    public void ScatterKeepsItsResolvedModelsSpinAwardAndReplays(string model, string? mode, int spins)
    {
        var result = VersionedSpin(model, mode, "FREE");
        Assert.Equal(spins, result.FreeSpinsAwarded);
        Assert.Equal(spins, result.EngineAudit!.EffectiveGame.FreeGames!.AwardedSpins);
        Assert.All(result.Reels, reel => Assert.Equal(mode is null ? 4 : 6, reel.Count));
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
    }

    [Fact]
    public void NonMidnightRowsStillAppliesItsExistingProfileFreeGameConfiguration()
    {
        var provider = Provider(ReadOptions());
        var wukong = provider.GetGame(SlotSpecialRoundProfiles.WukongGameId)!;
        var node = JsonSerializer.SerializeToNode(wukong)!;
        node["FreeGames"] = JsonSerializer.SerializeToNode(new GameFreeGamesDefinition
        { SymbolId = "FREE", RequiredSymbols = 3, AwardedSpins = 99, VisibleFrequencyDivisor = 1 });
        var configured = node.Deserialize<GameDefinition>()!;
        var reels = provider.GetReelSet(configured.Math.ReelSetId)!;
        var result = new SpinService(new FixedGameProvider(provider, configured),
            new CryptoReelGenerator(new FixedStopsRandom(reels.Reels.Select(reel => reel.IndexOf("2")))),
            new CombinationEvaluator(), new PayoutCalculator(), new QuietFeatureRandom())
            .Spin(configured.Id, 100, "non-midnight-rows-profile-proof", false, freeSpinFeatureMode: "rows");
        Assert.Equal(99, result.EngineAudit!.BaseGame.FreeGames!.AwardedSpins);
        Assert.Equal(0, result.EngineAudit.EffectiveGame.FreeGames!.AwardedSpins);
        Assert.Equal(0, result.FreeSpinsAwarded);
        Assert.All(result.Reels, reel => Assert.Equal(6, reel.Count));
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
    }

    [Theory]
    [InlineData(V2)]
    [InlineData(V3)]
    public void IntegerModelsRejectWrongFrozenScalarDuringReplay(string model)
    {
        var result = VersionedSpin(model, null, "8");
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
        var node = JsonSerializer.SerializeToNode(result.EngineAudit!.BaseGame)!;
        node["Math"]!["PayoutMultiplier"] = .99m;
        var wrong = node.Deserialize<GameDefinition>()!;
        var altered = result with { EngineAudit = result.EngineAudit with { BaseGame = wrong } };
        Assert.Contains(SlotRoundAudit.Check(altered, altered.Payout), finding => finding.Code == "slot-model-mismatch");
    }

    private static SpinResult VersionedSpin(string model, string? mode, string stopSymbol)
    {
        var provider = Provider(ReadOptions());
        var game = MidnightVaultSlotDefinitions.CreateGameForPaytable(provider.GetGame(SlotSpecialRoundProfiles.DragonHoardGameId)!, model);
        var reels = provider.GetReelSet(game.Math.ReelSetId)!;
        return new SpinService(new FixedGameProvider(provider, game),
            new CryptoReelGenerator(new FixedStopsRandom(reels.Reels.Select(reel => reel.IndexOf(stopSymbol)))),
            new CombinationEvaluator(), new PayoutCalculator(), new QuietFeatureRandom())
            .Spin(game.Id, 100, "midnight-version-proof", false, freeSpinFeatureMode: mode);
    }

    private static IEnumerable<(string, int, long)> Rules(IEnumerable<PayoutRule> rules) => rules
        .OrderBy(rule => rule.SymbolId, StringComparer.Ordinal).ThenBy(rule => rule.MatchLength)
        .Select(rule => (rule.SymbolId, rule.MatchLength, rule.Multiplier));

    private static IEnumerable<PayoutRule> CurrentRules() => CurrentRates.SelectMany(rate => new[]
    {
        new PayoutRule { SymbolId = rate.Symbol, MatchLength = 3, Multiplier = rate.Three },
        new PayoutRule { SymbolId = rate.Symbol, MatchLength = 5, Multiplier = rate.Five }
    });

    private static OptionsSlotsDefinitionProvider Provider(SlotsOptions options) => new(Options.Create(options));

    private static SlotsOptions ReadOptions()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        return document.RootElement.GetProperty("Slots").Deserialize<SlotsOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Slot options are missing.");
    }

    private sealed class FixedStopsRandom(IEnumerable<int> stops) : IRandomIndexSource
    {
        private readonly Queue<int> _stops = new(stops);
        public int Next(int maximumExclusive)
        {
            var stop = _stops.Dequeue();
            Assert.InRange(stop, 0, maximumExclusive - 1);
            return stop;
        }
    }

    private sealed class QuietFeatureRandom : IRandomIndexSource
    {
        public int Next(int maximumExclusive) => maximumExclusive == 100 ? 47 : 0;
    }

    private sealed class FixedGameProvider(ISlotsDefinitionProvider source, GameDefinition selected) : ISlotsDefinitionProvider
    {
        public GameDefinition? GetGame(string id) => id == selected.Id ? selected : source.GetGame(id);
        public SymbolSetDefinition? GetSymbolSet(string id) => source.GetSymbolSet(id);
        public ReelSetDefinition? GetReelSet(string id) => source.GetReelSet(id);
        public PaytableDefinition? GetPaytable(string id) => source.GetPaytable(id);
    }
}
