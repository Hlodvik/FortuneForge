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
    [Fact]
    public void DedicatedDefinitions_ExposeFifteenActiveSymbolsAndSevenDistinctOrdinaryPayoutPairs()
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
        Assert.Equal(MidnightVaultBaseReelSet.Id, reels.Id);
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
        Assert.Equal(7, pairs.Distinct().Count());
        Assert.Equal((4L, 14L), pairs[^1]);
        Assert.Equal((1L, 2L), pairs[1]);
        Assert.Equal((2L, 7L), pairs[3]);
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
        Assert.Equal(MidnightVaultBaseReelSet.Id, result.ReelSetId);
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
        Assert.Equal(14, match.Multiplier);
        Assert.Equal((long)Math.Round(100 * 14 * game.Math.PayoutMultiplier, MidpointRounding.AwayFromZero), award.AmountPoints);
        Assert.Equal(0, result.EnergyAwarded);
        Assert.Equal(0, result.MoneyGrabPoints);
        var audit = Assert.IsType<SlotSpinEngineAudit>(result.EngineAudit);
        Assert.Equal(rows, audit.EffectiveGame.Layout.VisibleRows);
        Assert.Equal(15, audit.SymbolSet.Symbols.Count);
        var persistedAudit = JsonSerializer.Deserialize<SlotSpinEngineAudit>(JsonSerializer.Serialize(audit))!;
        Assert.Empty(SlotRoundAudit.Check(result with { EngineAudit = persistedAudit }, result.Payout));
    }

    [Theory]
    [InlineData("8,8,8,2,3", 3, 4)]
    [InlineData("8,8,8,8,3", 4, 4)]
    [InlineData("8,8,8,8,8", 5, 14)]
    [InlineData("8,ACE,8,8,8", 5, 14)]
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
        var source = options.GameDefinitions.Single(game => game.Id == SlotSpecialRoundProfiles.WukongGameId);
        var sourceReels = options.ReelSets.Single(set => set.Id == source.Math.ReelSetId);
        var sourcePaytable = options.Paytables.Single(table => table.Id == source.Math.PaytableId);
        var midnight = provider.GetGame(SlotSpecialRoundProfiles.DragonHoardGameId)!;
        var reels = provider.GetReelSet(midnight.Math.ReelSetId)!;
        var paytable = provider.GetPaytable(midnight.Math.PaytableId)!;
        var legacyPaytable = provider.GetPaytable(MidnightVaultSlotDefinitions.LegacyPaytableId)!;

        for (var reel = 0; reel < sourceReels.Reels.Count; reel++)
        {
            var original = sourceReels.Reels[reel];
            var midnightStrip = reels.Reels[reel];
            Assert.Equal(original.Count, midnightStrip.Count);
            var addedLetters = 0;
            for (var stop = 0; stop < original.Count; stop++)
            {
                if (original[stop] == "POWER" && midnightStrip[stop] == "5")
                {
                    addedLetters++;
                    Assert.NotEqual("5", original[(stop + original.Count - 1) % original.Count]);
                    Assert.NotEqual("5", original[(stop + 1) % original.Count]);
                }
                else Assert.Equal(original[stop] == "BOLT" ? "8" : original[stop], midnightStrip[stop]);
            }
            Assert.Equal(3, addedLetters);
        }
        Assert.Equal(sourcePaytable.Rules.Select(rule => (rule.SymbolId, rule.MatchLength, rule.Multiplier)),
            legacyPaytable.Rules.Where(rule => rule.SymbolId != "8").Select(rule => (rule.SymbolId, rule.MatchLength, rule.Multiplier)));
        Assert.Equal(legacyPaytable.Rules.Select(rule => (rule.SymbolId, rule.MatchLength,
                rule.SymbolId == "5" && rule.MatchLength == 5 ? 7L : rule.Multiplier)),
            paytable.Rules.Select(rule => (rule.SymbolId, rule.MatchLength, rule.Multiplier)));
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
    public void BaseReels_MakeAllFourLetterIdentitiesMoreCommonThanEveryOtherSymbol()
    {
        var provider = Provider(ReadOptions());
        var game = provider.GetGame(SlotSpecialRoundProfiles.DragonHoardGameId)!;
        var reels = provider.GetReelSet(game.Math.ReelSetId)!;
        var letters = new HashSet<string>(["3", "2", "4", "5"], StringComparer.Ordinal);

        foreach (var strip in reels.Reels)
        {
            var counts = strip.GroupBy(symbol => symbol).ToDictionary(group => group.Key, group => group.Count());
            Assert.Equal(15, counts["3"]);
            Assert.Equal(11, counts["2"]);
            Assert.Equal(10, counts["4"]);
            Assert.Equal(9, counts["5"]);
            var mostCommonOther = counts.Where(pair => !letters.Contains(pair.Key)).Max(pair => pair.Value);
            Assert.All(letters, letter => Assert.True(counts[letter] > mostCommonOther));
        }
    }

    [Fact]
    public void IntegerV2Payouts_AreExactlyProportionalAcrossEveryTrustedIntegerWager()
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
    public void GameVersionSelection_PinsBothScalarsAndCopiesSettingsWithoutChangingActiveV2()
    {
        var provider = Provider(ReadOptions());
        var active = provider.GetGame(SlotSpecialRoundProfiles.DragonHoardGameId)!;
        var before = JsonSerializer.Serialize(active);
        var legacy = MidnightVaultSlotDefinitions.CreateGameForPaytable(active, MidnightVaultSlotDefinitions.LegacyPaytableId);

        Assert.Equal(active.Id, legacy.Id);
        Assert.Equal(MidnightVaultSlotDefinitions.LegacyPaytableId, legacy.Math.PaytableId);
        Assert.Equal(1.09m, legacy.Math.PayoutMultiplier);
        Assert.Equal(6, legacy.FreeGames?.AwardedSpins);
        var restoredCurrent = MidnightVaultSlotDefinitions.CreateGameForPaytable(legacy, MidnightVaultSlotDefinitions.PaytableId);
        Assert.Equal(active.Math.PayoutMultiplier, restoredCurrent.Math.PayoutMultiplier);
        var expectedSettings = JsonNode.Parse(before)!;
        var restoredSettings = JsonSerializer.SerializeToNode(restoredCurrent)!;
        expectedSettings["Math"]!.AsObject().Remove("PayoutMultiplier");
        restoredSettings["Math"]!.AsObject().Remove("PayoutMultiplier");
        Assert.Equal(expectedSettings.ToJsonString(), restoredSettings.ToJsonString());
        Assert.Equal(1m, restoredCurrent.Math.PayoutMultiplier);
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
        Assert.Throws<ArgumentException>(() => MidnightVaultSlotDefinitions.CreateGameForPaytable(active, "midnight-vault-paytable-v3"));
        Assert.Throws<ArgumentException>(() => MidnightVaultSlotDefinitions.CreateGameForPaytable(
            provider.GetGame(SlotSpecialRoundProfiles.CosmicFortuneGameId)!, MidnightVaultSlotDefinitions.LegacyPaytableId));
    }

    [Fact]
    public void FrozenLegacyV1Evidence_ReplaysAfterTheActiveGameSwitchesToV2()
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
