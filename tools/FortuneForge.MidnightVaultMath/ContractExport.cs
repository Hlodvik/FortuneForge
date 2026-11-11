using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Models;

internal static class ContractExport
{
    public static object Create(ISlotsDefinitionProvider provider, SlotSpecialRoundProfile profile, Dictionary<string, string> hashes)
    {
        var game = provider.GetGame(Definitions.GameId)!;
        var symbols = provider.GetSymbolSet(game.Symbols.SymbolSetId)!;
        var reels = provider.GetReelSet(game.Math.ReelSetId)!;
        var paytable = provider.GetPaytable(game.Math.PaytableId)!;
        var baseIds = reels.Reels.SelectMany(reel => reel).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var active = baseIds.Append("BANANA").Concat(profile.UsesCollections ? new[] { "SEAL_SYNC", "SEAL_ROWS", "SEAL_PAW", "SEAL_RAND" } : [])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (active.Length != 15 || !active.Order().SequenceEqual(symbols.Symbols.Select(symbol => symbol.Id).Order()))
            throw new InvalidOperationException("Expected exactly fifteen defined and reachable Midnight symbols.");
        return new
        {
            schemaVersion = "midnight-vault-effective-contract-v1", generatedUtc = DateTime.UtcNow, gameId = game.Id,
            engineVersion = FortuneForge.Games.MidnightVault.MidnightVaultEngine.Version,
            symbolSetId = symbols.Id, reelSetId = reels.Id, paytableId = paytable.Id,
            activeSymbolIds = active, baseReelSymbolIds = baseIds, definedSymbolIds = symbols.Symbols.Select(symbol => symbol.Id),
            artworkLabels = new Dictionary<string, string>
            {
                ["2"] = "Q", ["3"] = "J", ["4"] = "K", ["5"] = "A", ["6"] = "Overflowing chips",
                ["7"] = "Ruby ring", ["8"] = "Cash briefcase", ["ACE"] = "Front vault Wild",
                ["POWER"] = "Loose diamond", ["BANANA"] = "Smooth 777", ["FREE"] = "Faberge egg",
                ["SEAL_SYNC"] = "Ruby", ["SEAL_ROWS"] = "Sapphire", ["SEAL_PAW"] = "Amber", ["SEAL_RAND"] = "Emerald"
            },
            layout = game.Layout, matching = game.Matching, symbolRules = game.Symbols, targets = game.Math.Targets,
            payoutMultiplier = game.Math.PayoutMultiplier, paylinePayoutSteps = game.Math.PaylinePayoutSteps,
            fiveMatchPityMissLimit = game.Math.FiveMatchPityMissLimit, paylines = game.Paylines,
            paytableRules = paytable.Rules, wagering = game.Wagering, profile,
            versionedModels = new[] { MidnightVaultSlotDefinitions.LegacyPaytableId,
                MidnightVaultSlotDefinitions.PreviousPaytableId, MidnightVaultSlotDefinitions.PaytableId }.Select(modelId =>
            {
                var modelGame = MidnightVaultSlotDefinitions.CreateGameForPaytable(game, modelId);
                var modelReels = provider.GetReelSet(modelGame.Math.ReelSetId)!;
                return new { mathModelId = modelId, payoutMultiplier = modelGame.Math.PayoutMultiplier,
                    reelSetId = modelReels.Id, scatterAwardedSpins = modelGame.FreeGames!.AwardedSpins,
                    collectionAwardedSpins = modelId == MidnightVaultSlotDefinitions.LegacyPaytableId ?
                        MidnightVaultSlotDefinitions.LegacyCollectionAwardedSpins : MidnightVaultSlotDefinitions.BonusRoundSpins,
                    paytableRules = provider.GetPaytable(modelId)!.Rules, reelStrips = modelReels.Reels };
            }),
            features = new { scatter = game.FreeGames, specialPoints = game.SpecialPoints, energy = game.Energy,
                collection = new { target = profile.CollectionTarget, awardedSpins = profile.CollectionAwardedSpins, mode = profile.CollectionFeatureMode },
                bonusRounds = new { helper = nameof(MidnightVaultBonusRounds), ordering = "FIFO; existing rounds, then newly earned scatter, then sync/rows/paw/rand tracks",
                    lockedTerms = new[] { "wagerPoints", "featureMode", "mathModelId" },
                    weightedCollectionWager = "RoundAwayFromZero(exact sum of the 30 contributing gem wagers /30); overflow gems retain their own current wager",
                    derivedWager = new { minimumPoints = 2, maximumPoints = 2000, stepPoints = 1, requiresCheckedBonusAdmission = true },
                    legacy = new { paytableId = MidnightVaultSlotDefinitions.LegacyPaytableId, multiplier = 1.09m,
                        scatterSpins = MidnightVaultSlotDefinitions.LegacyScatterAwardedSpins, collectionSpins = MidnightVaultSlotDefinitions.LegacyCollectionAwardedSpins,
                        paytableRules = provider.GetPaytable(MidnightVaultSlotDefinitions.LegacyPaytableId)!.Rules } },
                bananaPatterns = new { symbolId = "BANANA", rawMultiplier = 3, geometries = new[] { "vertical", "horizontal", "diagonal-up", "diagonal-down" }, duplicateGeometryPaysOnce = true },
                freeRows = new { baseRows = game.Layout.VisibleRows, extraRowsMode = "rows", extraRows = 2 },
                pawWithoutDirectTokens = "paw injects two ACE wilds; it does not display PAW/RAND token symbols" },
            rounding = new { match = "Round(wagerPoints * (rawMultiplier + fullMatchPaylineStep) * payoutMultiplier, MidpointRounding.AwayFromZero), individually per paid match before totals",
                banana = "Round(wagerPoints * 3 * payoutMultiplier, MidpointRounding.AwayFromZero), per distinct three-symbol geometry",
                money = "PointValueInCents is applied after point payout; calibration measures point RTP without wallet transactions" },
            reelCounts = reels.Reels.Select((strip, index) => new { reel = index + 1, length = strip.Count,
                symbols = strip.GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count()) }),
            reelStrips = reels.Reels,
            examplePayouts = new long[] { 2, 6, 20, 100, 2000 }.Select(wager => new { wagerPoints = wager,
                rules = paytable.Rules.Select(rule => new { rule.SymbolId, rule.MatchLength, rawMultiplier = rule.Multiplier,
                    payline1Points = (long)Math.Round(wager * (rule.Multiplier + (rule.MatchLength == game.Layout.ReelCount ? game.Math.PaylinePayoutSteps[0] : 0)) * game.Math.PayoutMultiplier, MidpointRounding.AwayFromZero) }) }),
            sourceHashes = hashes
        };
    }
}
