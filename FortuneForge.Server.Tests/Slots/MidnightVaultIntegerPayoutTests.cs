using System.Numerics;
using System.Text.Json;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.Extensions.Options;
#if !MIDNIGHT_ORACLE_ONLY
using Xunit;
#endif

// The private calibration tool links this file with MIDNIGHT_ORACLE_ONLY. The
// same independent test arithmetic is used by the focused tests and audit;
// neither assembly is referenced by production game code.
namespace FortuneForge.MidnightAudit
{
    internal static class IntegerPayoutOracle
    {
        public const string GameId = "dragon-hoard-v1";
        public static readonly long[] AdmittedWagers = Enumerable.Range(1, 1000).Select(value => value * 2L).ToArray();
        public static readonly long[] DerivedWagers = Enumerable.Range(2, 1999).Select(value => (long)value).ToArray();
        private static readonly IReadOnlyDictionary<string, (long Three, long Five)> Rates = new Dictionary<string, (long, long)>(StringComparer.Ordinal)
        {
            ["2"] = (1, 4), ["3"] = (1, 2), ["4"] = (1, 7), ["5"] = (2, 7),
            ["6"] = (2, 8), ["7"] = (3, 11), ["8"] = (4, 14), ["ACE"] = (5, 18), ["POWER"] = (2, 4)
        };

        public static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        public static void CheckDefinition(ISlotsDefinitionProvider provider)
        {
            var game = provider.GetGame(GameId)!;
            var table = provider.GetPaytable(game.Math.PaytableId)!;
            Require(game.Math.PayoutMultiplier == 1m, "Integer proof requires actual scalar 1.0.");
            Require(table.Id == "midnight-vault-paytable-v2", "Integer proof requires versioned v2 paytable.");
            Require(game.Layout.ReelCount == 5 && game.Layout.VisibleRows == 4 && game.Layout.PaylineCount == 23, "Unexpected layout.");
            Require(game.Math.PaylinePayoutSteps.All(value => value == 0), "Expected zero full-match steps.");
            Require(game.Matching.MinimumRunLength == 3, "The one-match-per-five-reel-line bound requires minimum run 3.");
            Require(game.Wagering.PointValueInCents == 25m && game.Wagering.MinimumWagerPoints == 2 &&
                game.Wagering.MaximumWagerPoints == 2000 && game.Wagering.WagerIncrementPoints == 2 && game.Wagering.AllowedWagerPoints.Count == 0,
                "The admitted point/cash menu changed.");
            Require(SlotSpecialRoundProfiles.TryGet(GameId, out var profile), "Midnight profile is missing.");
            Require(game.Energy is null &&
                !profile.UsesEnergy && !profile.UsesDirectValueTokens, "Noninteger energy/direct-token paths must remain disabled.");
            Require(game.FreeGames?.AwardedSpins == 10 && profile.ScatterAwardedSpins == 10 && profile.CollectionAwardedSpins == 10 &&
                profile.CollectionTarget == 30 && profile.CollectionFeatureMode == "rows-paw", "New round awards must remain ten spins per trigger.");
            Require(table.Rules.Count == Rates.Count * 2, "Unexpected native rule count.");
            foreach (var (id, rate) in Rates)
            {
                Require(table.Rules.Single(rule => rule.SymbolId == id && rule.MatchLength == 3).Multiplier == rate.Three, "Unexpected three-match rate for " + id);
                Require(table.Rules.Single(rule => rule.SymbolId == id && rule.MatchLength == 5).Multiplier == rate.Five, "Unexpected five-match rate for " + id);
            }
            Require(table.Rules.All(rule => rule.Multiplier > 0 && rule.Multiplier <= 18), "Positive paying support and overflow bound changed.");
        }

        // Price in integer coefficients, not by rounding a reference wager or
        // calling PayoutCalculator. Candidate ties are resolved explicitly.
        public static OraclePayout Native(IReadOnlyList<PaylineEvaluation> evaluations,
            IReadOnlyDictionary<string, (long Three, long Five)>? candidateRates = null)
        {
            var winners = new List<OracleLine>();
            for (var ordinal = 0; ordinal < evaluations.Count; ordinal++)
            {
                var evaluation = evaluations[ordinal]; OracleLine? winner = null;
                foreach (var candidate in evaluation.Candidates)
                {
                    var matches = candidate.Matches.Select(match => new OracleMatch(match, Rate(match, candidateRates)))
                        .Where(match => match.Coefficient > 0).ToArray();
                    if (matches.Length == 0) continue;
                    var line = new OracleLine(evaluation.PaylineId, ordinal, matches);
                    if (winner is null || line.Coefficient > winner.Coefficient ||
                        line.Coefficient == winner.Coefficient && StringComparer.Ordinal.Compare(line.CandidateKey, winner.CandidateKey) < 0)
                        winner = line;
                }
                if (winner is not null) winners.Add(winner);
            }
            var paid = winners.Where(line => line.Matches.Any(match => match.Match.MatchLength == 5)).ToList();
            var shorts = winners.Where(line => line.Matches.All(match => match.Match.MatchLength != 5)).ToList();
            shorts.Sort((left, right) =>
            {
                var order = right.Matches.Length.CompareTo(left.Matches.Length);
                if (order == 0) order = right.Coefficient.CompareTo(left.Coefficient);
                if (order == 0) order = StringComparer.Ordinal.Compare(left.CandidateKey, right.CandidateKey);
                return order == 0 ? left.Ordinal.CompareTo(right.Ordinal) : order;
            });
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in shorts)
            {
                var positions = line.Matches.Select(match => PositionKey(match.Match)).ToArray();
                if (positions.Any(claimed.Contains)) continue;
                paid.Add(line); foreach (var position in positions) claimed.Add(position);
            }
            return new OraclePayout(paid.ToArray());
        }

        private static long Rate(SymbolMatch match,
            IReadOnlyDictionary<string, (long Three, long Five)>? candidateRates = null) => (candidateRates ?? Rates).TryGetValue(match.SymbolId, out var pair)
            ? match.MatchLength >= 5 ? pair.Five : match.MatchLength >= 3 ? pair.Three : 0 : 0;
        private static string PositionKey(SymbolMatch match) => match.MatchLength + ":" +
            string.Join(';', match.Positions.Select(position => position.Reel + "," + position.Row));
        public static string MatchKey(int payline, SymbolMatch match, long coefficient) =>
            $"{payline}/{match.SymbolId}/{match.MatchLength}/{coefficient}/{PositionKey(match)}/wild:" +
            string.Join(';', match.WildPositions.Select(position => position.Reel + "," + position.Row));
        public static string[] EngineNativeKeys(SpinPayout payout) => payout.Paylines.Where(line => line.PaylineId < 800)
            .SelectMany(line => line.Matches.Select(match => MatchKey(line.PaylineId, match.Match, match.Multiplier))).Order(StringComparer.Ordinal).ToArray();
        public static string[] OracleKeys(OraclePayout payout) => payout.Lines
            .SelectMany(line => line.Matches.Select(match => MatchKey(line.PaylineId, match.Match, match.Coefficient))).Order(StringComparer.Ordinal).ToArray();

        // Four directions enumerate each length-three straight geometry once;
        // this is independent of the production vertical/horizontal loop order.
        public static string[] EggGeometries(IReadOnlyList<IReadOnlyList<string>> reels)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
            for (var x = 0; x < reels.Count; x++)
            for (var y = 0; y < reels[x].Count; y++)
            {
                var cells = Enumerable.Range(0, 3).Select(step => new GridPosition(x + dx * step, y + dy * step)).ToArray();
                if (cells.All(cell => cell.Reel >= 0 && cell.Reel < reels.Count && cell.Row >= 0 && cell.Row < reels[cell.Reel].Count && reels[cell.Reel][cell.Row] == "BANANA"))
                    found.Add(string.Join(';', cells.OrderBy(cell => cell.Reel).ThenBy(cell => cell.Row).Select(cell => cell.Reel + "," + cell.Row)));
            }
            return found.Order(StringComparer.Ordinal).ToArray();
        }
        public static string[] EngineEggGeometries(SpinPayout payout) => payout.Paylines.SelectMany(line => line.Matches)
            .Where(match => match.Match.SymbolId == "BANANA")
            .Select(match => string.Join(';', match.Match.Positions.OrderBy(cell => cell.Reel).ThenBy(cell => cell.Row).Select(cell => cell.Reel + "," + cell.Row)))
            .Order(StringComparer.Ordinal).ToArray();

        public static void CheckWholeSpin(SpinResult result, GameDefinition game, SymbolSetDefinition symbols)
        {
            var effective = result.EngineAudit!.EffectiveGame;
            var native = Native(new CombinationEvaluator().Evaluate(result.Reels, effective, symbols));
            var egg = EggGeometries(result.Reels);
            var coefficient = checked(native.Coefficient + egg.Length * 3L);
            Require((BigInteger)result.WagerPoints * coefficient == result.Payout.TotalPoints, "Whole-spin independent integer amount mismatch.");
            Require(OracleKeys(native).SequenceEqual(EngineNativeKeys(result.Payout)), "Whole-spin selected native geometry mismatch.");
            Require(egg.SequenceEqual(EngineEggGeometries(result.Payout)), "Whole-spin smooth-777 pattern geometry mismatch.");
            CheckAmounts(result.Payout, result.WagerPoints);
            Require(result.Payout.TotalPoints <= 600L * result.WagerPoints, "Enhanced-spin conservative bound exceeded.");
            Require(game.Energy is null && result.EnergyAwarded == 0 && result.MoneyGrabPoints == 0, "Disabled noninteger path became active.");
        }

        private static void CheckAmounts(SpinPayout payout, long wager)
        {
            foreach (var line in payout.Paylines)
            {
                foreach (var match in line.Matches)
                {
                    var coefficient = match.Match.SymbolId == "BANANA" ? 3 : Rate(match.Match);
                    Require((BigInteger)coefficient * wager == match.AmountPoints, "Independent per-match integer amount mismatch.");
                }
                Require(line.AmountPoints == line.Matches.Sum(match => match.AmountPoints), "Per-line amount mismatch.");
            }
            Require(payout.TotalPoints == payout.Paylines.Sum(line => line.AmountPoints), "Payout total mismatch.");
        }

        public static IReadOnlyList<PaylineEvaluation>[] StressCases(GameDefinition game, SymbolSetDefinition symbols)
        {
            SymbolMatch Match(string id, int length, int line = 1, int row = 0) => new(line, id, length,
                Enumerable.Range(0, length).Select(reel => new GridPosition(reel, row)).ToArray(), []);
            PaylineEvaluation Line(int line, params SymbolMatch[] alternatives) => new(line, alternatives.Select(match => new MatchCandidate([match])).ToArray());
            var cases = new List<IReadOnlyList<PaylineEvaluation>>
            {
                new[] { Line(1, Match("3", 5), Match("ACE", 3)) }, // native-wild three beats substituted J five
                new[] { Line(1, Match("3", 3), Match("2", 3)) }, // exact coefficient tie, lexical choice
                new[] { Line(1, Match("8", 5)), Line(2, Match("8", 3, 2)), Line(3, Match("8", 3, 3)) },
                new[] { Line(1, Match("4", 4)), Line(2, Match("4", 3, 2)) },
                new[] { Line(1, Match("POWER", 3)), Line(2, Match("POWER", 3, 2)), Line(3, Match("POWER", 5, 3)) },
                new[] { Line(1, Match("FREE", 3), Match("SEAL_ROWS", 5)) }
            };
            foreach (var rows in new[] { 4, 6 })
            foreach (var pattern in new[]
            {
                new[] { "3", "3", "3", "3", "3" }, new[] { "ACE", "ACE", "ACE", "3", "3" },
                new[] { "8", "8", "8", "8", "8" }, new[] { "POWER", "POWER", "POWER", "POWER", "POWER" },
                new[] { "2", "2", "2", "2", "SEAL_SYNC" }, new[] { "ACE", "ACE", "ACE", "ACE", "ACE" }
            })
            {
                var grid = Enumerable.Range(0, 5).Select(reel => (IReadOnlyList<string>)Enumerable.Repeat("SEAL_SYNC", rows).Select((id, row) => row == 0 ? pattern[reel] : id).ToArray()).ToArray();
                var effective = GameWithRows(game, rows);
                cases.Add(new CombinationEvaluator().Evaluate(grid, effective, symbols));
            }
            return cases.ToArray();
        }

        public static object AllWagers(ISlotsDefinitionProvider provider)
        {
            CheckDefinition(provider);
            var game = provider.GetGame(GameId)!; var symbols = provider.GetSymbolSet(game.Symbols.SymbolSetId)!;
            var table = provider.GetPaytable(game.Math.PaytableId)!; var calculator = new PayoutCalculator();
            var cases = StressCases(game, symbols); var checkedPrices = 0;
            var admissionEngine = new SpinService(provider, new FixedGrid(4), new CombinationEvaluator(), calculator, new QuietRandom());
            foreach (var wager in AdmittedWagers) admissionEngine.ValidateRequest(GameId, wager);
            foreach (var input in cases)
            {
                var expected = Native(input); var keys = OracleKeys(expected);
                foreach (var wager in DerivedWagers)
                {
                    var actual = calculator.Calculate(input, game, table, wager);
                    Require((BigInteger)wager * expected.Coefficient == actual.TotalPoints, "All-wager exact native amount mismatch at " + wager);
                    Require(keys.SequenceEqual(EngineNativeKeys(actual)), "All-wager candidate/dedup geometry mismatch at " + wager);
                    CheckAmounts(actual, wager);
                    checkedPrices++;
                }
            }
            foreach (var rows in new[] { 4, 6 })
            {
                var all777 = Enumerable.Range(0, 5).Select(_ => (IReadOnlyList<string>)Enumerable.Repeat("BANANA", rows).ToArray()).ToArray();
                var patterns = EggGeometries(all777).Length;
                Require(patterns == (rows == 4 ? 34 : 62), "Unexpected exhaustive straight-three pattern count.");
                var engine = new SpinService(provider, new FixedGrid(rows), new CombinationEvaluator(), calculator, new QuietRandom());
                foreach (var wager in DerivedWagers)
                {
                    var admission = new SlotSpinAdmission(wager, 0, true, 0, false, 0, null, 0, rows == 6 ? "rows-paw" : null)
                        { AdmissionId = Guid.NewGuid(), MathModelId = game.Math.PaytableId, PityMissesBefore = 0 };
                    var result = engine.SpinAdmitted(GameId, admission, "integer-pattern-proof");
                    Require((BigInteger)patterns * 3 * wager == result.Payout.TotalPoints, "All-wager smooth-777 amount mismatch.");
                    Require(EggGeometries(all777).SequenceEqual(EngineEggGeometries(result.Payout)), "All-wager smooth-777 selected geometry mismatch.");
                    CheckAmounts(result.Payout, wager);
                }
            }
            Require((BigInteger)2000 * (23 * 18 + 62 * 3) == 1_200_000, "Conservative overflow bound changed.");
            return new { admittedWagers = AdmittedWagers.Length, minimum = 2, maximum = 2000, step = 2,
                derivedBonusWagers = DerivedWagers.Length, derivedStep = 1,
                fixtureCases = cases.Length, nativeReprices = checkedPrices, selectedGeometryChecked = true,
                featureSpinReprices = DerivedWagers.Length * 2, eggPatternsBase = 34, eggPatternsEnhanced = 62,
                enhancedCoefficientBound = 600, maximumSpinPointsBound = 1_200_000, maximumSpinCentsBound = 30_000_000 };
        }

        public static object QueueArithmetic(ISlotsDefinitionProvider provider)
        {
            var v2 = MidnightVaultSlotDefinitions.PaytableId;
            var v1 = MidnightVaultSlotDefinitions.LegacyPaytableId;
            var first = new MidnightVaultBonusRound("first", 10, 1, 6, null, v2);
            var second = new MidnightVaultBonusRound("second", 10, 4, 2000, "rows-paw", v2);
            var consumed = MidnightVaultBonusRounds.Consume([first, second]);
            Require(consumed.SequenceEqual(new[] { second }), "FIFO consume altered the next round.");
            var stepped = MidnightVaultBonusRounds.Consume([second]);
            Require(stepped.Single() == (second with { RemainingSpins = 3 }), "FIFO consume altered a locked round field.");

            // 14 gems at2 +15 at4 +1 at6 =94 /30 ->3 native points.
            // The second incoming gem remains at its exact current wager6.
            var progress = MidnightVaultBonusRounds.Advance(consumed,
                new Dictionary<string, long> { ["rows"] = 29 }, new Dictionary<string, long> { ["rows"] = 88 },
                new Dictionary<string, int> { ["SEAL_ROWS"] = 2 }, 6, 10, null, v2, Guid.Parse("00000000-0000-0000-0000-000000000001"));
            Require(progress.Rounds[0] == second && progress.AwardedRounds.Count == 2, "New awards interrupted an existing round.");
            Require(progress.AwardedRounds[0].WagerPoints == 6 && progress.AwardedRounds[0].FeatureMode is null &&
                progress.AwardedRounds[1].WagerPoints == 3 && progress.AwardedRounds[1].FeatureMode == "rows-paw" &&
                progress.AwardedRounds.All(round => round.TotalSpins == 10 && round.RemainingSpins == 10 && round.MathModelId == v2),
                "Separate scatter/weighted gem round locking failed.");
            Require(progress.CollectionCounts["rows"] == 1 && progress.CollectionWagerPoints["rows"] == 6,
                "Weighted completion did not preserve the exact overflow gem wager.");

            var half = MidnightVaultBonusRounds.Advance([], new Dictionary<string, long> { ["rows"] = 29 },
                new Dictionary<string, long> { ["rows"] = 58 }, new Dictionary<string, int> { ["SEAL_ROWS"] = 1 },
                17, 0, null, v2, Guid.NewGuid());
            Require(half.AwardedRounds.Single().WagerPoints == 3, "Positive half-point average must round AwayFromZero (75/30 ->3).");
            var multiple = MidnightVaultBonusRounds.Advance([], new Dictionary<string, long> { ["rows"] = 29 },
                new Dictionary<string, long> { ["rows"] = 58 }, new Dictionary<string, int> { ["SEAL_ROWS"] = 31 },
                6, 0, null, v2, Guid.NewGuid());
            Require(multiple.AwardedRounds.Select(round => round.WagerPoints).SequenceEqual(new long[] { 2, 6 }) &&
                multiple.CollectionCounts["rows"] == 0 && multiple.CollectionWagerPoints["rows"] == 0,
                "Sequential multiple completions reused or lost weighted gems.");

            var legacy = new MidnightVaultBonusRound("migrated-bank", 37, 37, 100, "sync-paw", v1);
            MidnightVaultBonusRounds.Validate([legacy]);
            var legacyAwards = MidnightVaultBonusRounds.Advance([legacy], new Dictionary<string, long> { ["rows"] = 29 },
                new Dictionary<string, long> { ["rows"] = 2900 }, new Dictionary<string, int> { ["SEAL_ROWS"] = 1 },
                100, 6, null, v1, Guid.NewGuid());
            Require(legacyAwards.Rounds[0] == legacy && legacyAwards.AwardedRounds.Select(round => round.TotalSpins).SequenceEqual(new[] { 6, 7 }) &&
                legacyAwards.AwardedRounds.All(round => round.MathModelId == v1), "Legacy bank or legacy retrigger terms changed.");
            Reject<InvalidOperationException>(() => MidnightVaultBonusRounds.Validate([first with { TotalSpins = 11 }]));
            Reject<InvalidOperationException>(() => MidnightVaultBonusRounds.Validate([first, first]));
            Reject<InvalidOperationException>(() => MidnightVaultBonusRounds.Advance([], new Dictionary<string, long> { ["rows"] = 0 },
                new Dictionary<string, long> { ["rows"] = 1 }, new Dictionary<string, int>(), 2, 0, null, v2, Guid.NewGuid()));

            var engine = new SpinService(provider, new FixedGrid(4), new CombinationEvaluator(), new PayoutCalculator(), new QuietRandom());
            Reject<ArgumentOutOfRangeException>(() => engine.ValidateRequest(GameId, 3));
            SlotSpinAdmission Admission(long wager) => new(wager, 0, true, 0, false, 0, null, 0, null)
                { AdmissionId = Guid.NewGuid(), MathModelId = v2, PityMissesBefore = 0 };
            var odd = engine.SpinAdmitted(GameId, Admission(3), "weighted-odd-proof");
            Require(odd.IsFreeSpin && odd.EngineAudit!.IsDerivedFreeSpin && odd.Payout.TotalPoints == 306,
                "A valid odd-point weighted bonus entitlement was not priced exactly.");
            foreach (var invalid in new long[] { 0, 1, 2001 })
                Reject<ArgumentOutOfRangeException>(() => engine.SpinAdmitted(GameId, Admission(invalid), "invalid-derived-proof"));
            return new { weightedAverage = "(14*2 +15*4 +1*6)/30 ->3", overflowGems = 1, overflowWagerPoints = 6,
                weightedHalf = "75/30 ->3 AwayFromZero", queue = "Existing round, scatter round, gem round; only the consumed head changes",
                newRoundSpins = 10, legacyScatterSpins = 6, legacyCollectionSpins = 7, oddBonusWager = 3, oddBonusPayout = 306 };
        }

        private static void Reject<T>(Action operation) where T : Exception
        {
            try { operation(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected rejected input: " + typeof(T).Name);
        }

        public static GameDefinition GameWithRows(GameDefinition game, int rows) => new()
        {
            Id = game.Id, Layout = new GameLayoutDefinition { ReelCount = 5, VisibleRows = rows, PaylineCount = game.Layout.PaylineCount },
            Symbols = game.Symbols, Matching = game.Matching, Math = game.Math, Wagering = game.Wagering,
            FreeGames = game.FreeGames, SpecialPoints = game.SpecialPoints, Energy = game.Energy, Paylines = game.Paylines
        };
        internal sealed record OracleMatch(SymbolMatch Match, long Coefficient);
        internal sealed record OracleLine(int PaylineId, int Ordinal, OracleMatch[] Matches)
        {
            public long Coefficient => Matches.Sum(match => match.Coefficient);
            public string CandidateKey => string.Join('|', Matches.Select(value => $"{value.Match.SymbolId}:{value.Match.MatchLength}:{value.Match.Positions[0].Reel}"));
        }
        internal sealed record OraclePayout(OracleLine[] Lines) { public long Coefficient => Lines.Sum(line => line.Coefficient); }
        private sealed class FixedGrid(int rows) : IReelGenerator
        {
            public ReelOutcome Generate(GameDefinition game, ReelSetDefinition reelSet, SymbolSetDefinition symbolSet) =>
                new(new int[5], Enumerable.Range(0, 5).Select(_ => (IReadOnlyList<string>)Enumerable.Repeat("BANANA", rows).ToArray()).ToArray());
        }
        private sealed class QuietRandom : IRandomIndexSource { public int Next(int maximumExclusive) => maximumExclusive - 1; }
    }

    internal static class IntegerCoupledCycles
    {
        public static object Run(ISlotsDefinitionProvider provider, SlotSpecialRoundProfile profile, int cycles, int seed, bool autoBoost)
        {
            IntegerPayoutOracle.CheckDefinition(provider);
            var paths = new long[] { 2, 100, 2000 }.Select(wager => new PathState(provider, profile, wager, seed, autoBoost)).ToArray();
            long resolved = 0;
            void Resolve(bool free)
            {
                var results = paths.Select(path => path.Resolve(free)).ToArray(); resolved++;
                var first = results[0];
                var referenceNativeKeys = IntegerPayoutOracle.EngineNativeKeys(first.Payout);
                for (var index = 0; index < paths.Length; index++)
                {
                    var current = results[index];
                    IntegerPayoutOracle.Require(current.Reels.SelectMany(reel => reel).SequenceEqual(first.Reels.SelectMany(reel => reel)) && current.ReelStops.SequenceEqual(first.ReelStops), "Coupled outcomes/RNG stops diverged.");
                    IntegerPayoutOracle.Require(current.ConsecutiveFiveMisses == first.ConsecutiveFiveMisses && current.FreeSpinsAwarded == first.FreeSpinsAwarded &&
                        current.SpecialPointsAwarded == first.SpecialPointsAwarded && current.SpecialBoostApplied == first.SpecialBoostApplied &&
                        current.EngineAudit!.GenerationAttempts == first.EngineAudit!.GenerationAttempts, "Coupled pity/free/POWER state diverged.");
                    IntegerPayoutOracle.Require((BigInteger)current.Payout.TotalPoints * 2 == (BigInteger)first.Payout.TotalPoints * paths[index].Wager, "Coupled payout scaling failed.");
                    IntegerPayoutOracle.Require(referenceNativeKeys.SequenceEqual(IntegerPayoutOracle.EngineNativeKeys(current.Payout)), "Coupled selected geometry changed.");
                    IntegerPayoutOracle.Require(IntegerPayoutOracle.EngineEggGeometries(first.Payout).SequenceEqual(IntegerPayoutOracle.EngineEggGeometries(current.Payout)), "Coupled smooth-777 geometry changed.");
                    IntegerPayoutOracle.Require(paths[index].StateKey == paths[0].StateKey, "Coupled queue/mode/collection/POWER carry changed.");
                }
                IntegerPayoutOracle.CheckWholeSpin(first, paths[0].Game, paths[0].Symbols);
            }
            // Deliberately seeded liabilities exercise rows-paw and a POWER
            // spend immediately. These are coupling checks, not RTP samples.
            var initialSafety = 1000;
            while (paths[0].Pending > 0) { IntegerPayoutOracle.Require(--initialSafety > 0, "Unbounded seeded free cycle."); Resolve(true); }
            for (var paid = 0; paid < cycles; paid++)
            {
                Resolve(false); var safety = 1000;
                while (paths[0].Pending > 0) { IntegerPayoutOracle.Require(--safety > 0, "Unbounded coupled free cycle."); Resolve(true); }
            }
            return new { seed, autoBoost, paidCycles = cycles, resolvedSteps = resolved, wagers = paths.Select(path => path.Wager),
                seededFreeSpins = 1, seededSpecialPoints = 8, seededCollectionCounts = 29, exactScaling = true,
                totalPayouts = paths.Select(path => path.TotalPayout), boostedSpins = paths[0].Boosts,
                featureSpins = paths[0].FreeSpins, oracleCheckedReferenceSpins = resolved };
        }

        private sealed class PathState
        {
            private readonly SpinService engine;
            private readonly bool auto;
            private IReadOnlyList<MidnightVaultBonusRound> rounds;
            private IReadOnlyDictionary<string, long> counts;
            private IReadOnlyDictionary<string, long> wagerTotals;
            private int special = 8;
            private int pity;
            public PathState(ISlotsDefinitionProvider provider, SlotSpecialRoundProfile profile, long wager, int seed, bool auto)
            {
                this.auto = auto; Wager = wager; Game = provider.GetGame(IntegerPayoutOracle.GameId)!;
                Symbols = provider.GetSymbolSet(Game.Symbols.SymbolSetId)!;
                var random = new SeededRandom(seed);
                engine = new SpinService(provider, new CryptoReelGenerator(random), new CombinationEvaluator(), new PayoutCalculator(), random);
                rounds = [new MidnightVaultBonusRound("seeded-remaining-spin", 10, 1, wager, "rows-paw", Game.Math.PaytableId)];
                counts = new[] { "sync", "rows", "paw", "rand" }.ToDictionary(id => id, _ => 29L);
                wagerTotals = counts.ToDictionary(pair => pair.Key, pair => checked(pair.Value * wager));
            }
            public long Wager { get; }
            public GameDefinition Game { get; }
            public SymbolSetDefinition Symbols { get; }
            public int Pending => rounds.Sum(round => round.RemainingSpins);
            public long TotalPayout { get; private set; }
            public long Boosts { get; private set; }
            public long FreeSpins { get; private set; }
            public string StateKey => $"{special}/{pity}/" + string.Join('|', rounds.Select(round =>
                $"{round.TotalSpins}:{round.RemainingSpins}:{round.WagerPoints / Wager}:{round.FeatureMode}:{round.MathModelId}")) + "/" +
                string.Join('|', counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value}:{wagerTotals[pair.Key] / Wager}"));
            public SpinResult Resolve(bool free)
            {
                var boost = auto && special >= Game.SpecialPoints!.ActivationCost;
                if (boost) { special -= Game.SpecialPoints!.ActivationCost; Boosts++; }
                var head = free ? rounds[0] : null;
                if (free) rounds = MidnightVaultBonusRounds.Consume(rounds);
                var spinWager = head?.WagerPoints ?? Wager;
                var model = head?.MathModelId ?? Game.Math.PaytableId;
                var admission = new SlotSpinAdmission(spinWager, free ? 0 : checked(Wager * 25), free, Pending, boost, special, null, 0, head?.FeatureMode)
                    { AdmissionId = Guid.NewGuid(), PityMissesBefore = pity, MathModelId = model };
                var result = engine.SpinAdmitted(Game.Id, admission, "integer-coupled");
                pity = result.ConsecutiveFiveMisses;
                var carry = MidnightVaultBonusRounds.Advance(rounds, counts, wagerTotals, result.SealsAwarded, spinWager,
                    result.FreeSpinsAwarded, result.EngineAudit!.FeatureProfile!.ScatterFeatureMode, model, result.SpinId);
                rounds = carry.Rounds; counts = carry.CollectionCounts; wagerTotals = carry.CollectionWagerPoints;
                IntegerPayoutOracle.Require(counts.All(pair => wagerTotals[pair.Key] == checked(pair.Value * Wager)), "Fixed-stake collection total lost exact wager.");
                IntegerPayoutOracle.Require(rounds.All(round => round.WagerPoints == Wager), "Fixed-stake queued round lost exact wager.");
                special = checked(special + result.SpecialPointsAwarded); TotalPayout += result.Payout.TotalPoints;
                if (free) FreeSpins++;
                return result;
            }
        }
        internal sealed class SeededRandom(int seed) : IRandomIndexSource
        { private readonly Random random = new(seed); public int Next(int maximumExclusive) => random.Next(maximumExclusive); }
    }
}

#if !MIDNIGHT_ORACLE_ONLY
namespace FortuneForge.Server.Tests.Slots
{
    public sealed class MidnightVaultIntegerPayoutTests
    {
        [Fact]
        public void AllThousandAdmittedWagers_MatchIndependentIntegerAmountAndSelectedGeometry()
        {
            var provider = Provider();
            FortuneForge.MidnightAudit.IntegerPayoutOracle.AllWagers(provider);
            var random = new FortuneForge.MidnightAudit.IntegerCoupledCycles.SeededRandom(6103);
            var engine = new SpinService(provider, new CryptoReelGenerator(random), new CombinationEvaluator(), new PayoutCalculator(), random);
            foreach (var wager in FortuneForge.MidnightAudit.IntegerPayoutOracle.AdmittedWagers) engine.ValidateRequest("dragon-hoard-v1", wager);
            foreach (var wager in new long[] { 0, 1, 3, 2001 }) Assert.Throws<ArgumentOutOfRangeException>(() => engine.ValidateRequest("dragon-hoard-v1", wager));
        }

        [Fact]
        public void CandidateAndDedupOracle_HasIndependentLiteralArithmeticExpectations()
        {
            SymbolMatch Match(string id, int length, int line) => new(line, id, length, Enumerable.Range(0, length).Select(reel => new GridPosition(reel, 0)).ToArray(), []);
            var input = new[]
            {
                new PaylineEvaluation(1, [new MatchCandidate([Match("3", 5, 1)]), new MatchCandidate([Match("ACE", 3, 1)])]),
                new PaylineEvaluation(2, [new MatchCandidate([Match("POWER", 3, 2)])]),
                new PaylineEvaluation(3, [new MatchCandidate([Match("8", 5, 3)])])
            };
            var priced = FortuneForge.MidnightAudit.IntegerPayoutOracle.Native(input);
            // ACE native-three5 beats J five2. Its claimed three-cell geometry
            // then excludes POWER three2. Full cash-five14 always survives.
            Assert.Equal(19, priced.Coefficient);
            Assert.Equal(new[] { "8", "ACE" }, priced.Lines.SelectMany(line => line.Matches).Select(match => match.Match.SymbolId).Order(StringComparer.Ordinal));
            Assert.Equal(new BigInteger(38000), (BigInteger)2000 * priced.Coefficient);
        }

        [Fact]
        public void WeightedRounds_LockIndependentTermsAndAdmitOddDerivedWagersWithoutChangingCashMenu()
        {
            FortuneForge.MidnightAudit.IntegerPayoutOracle.QueueArithmetic(Provider());
        }

        [Theory]
        [InlineData(false, 20260720)]
        [InlineData(true, 20260721)]
        public void CompleteSeededCycles_CoupleLowStandardAndMaximumStakeIncludingFeatures(bool boost, int seed)
        {
            var provider = Provider();
            Assert.True(SlotSpecialRoundProfiles.TryGet("dragon-hoard-v1", out var profile));
            FortuneForge.MidnightAudit.IntegerCoupledCycles.Run(provider, profile, 500, seed, boost);
        }

        private static ISlotsDefinitionProvider Provider()
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
            return new OptionsSlotsDefinitionProvider(Options.Create(json.RootElement.GetProperty("Slots").Deserialize<SlotsOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!));
        }
    }
}
#endif
