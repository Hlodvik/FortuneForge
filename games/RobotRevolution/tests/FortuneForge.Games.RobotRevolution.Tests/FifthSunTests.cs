using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.RobotRevolution.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.RobotRevolution.Tests;

public sealed class FifthSunTests
{
    [Fact]
    public void NewVocabularyAndWagerRulesAreOwnedByFifthSun()
    {
        var game = FifthSunDefinitions.Create(RobotRevolutionEngine.HistoricalFifthSunProfile).CreateGame();
        var symbols = FifthSunDefinitions.Create(RobotRevolutionEngine.HistoricalFifthSunProfile).CreateSymbolSet().Symbols.Select(symbol => symbol.Id).ToArray();
        Assert.Equal(12, symbols.Length);
        Assert.Equal(5, symbols.Count(symbol => symbol.StartsWith("GLYPH_", StringComparison.Ordinal)));
        Assert.DoesNotContain(symbols, symbol => new[] { "2", "3", "4", "5", "6", "7", "POWER", "FREE", "ACE", "BANANA" }.Contains(symbol));
        Assert.Equal(23, game.Paylines.Count);
        Assert.Equal("SUNSTONE", game.SpecialPoints!.SymbolId);
        Assert.Equal("ECLIPSE", game.FreeGames!.SymbolId);
        Assert.Equal(new[] { 3, 5 }, game.Symbols.WildSubstitutionMatchLengths);
        var table = FifthSunDefinitions.Create(RobotRevolutionEngine.HistoricalFifthSunProfile).CreatePaytable().Rules;
        Assert.Equal(8, table.Single(rule => rule.SymbolId == "SUNSTONE" && rule.MatchLength == 3).Multiplier);
        Assert.Equal(32, table.Max(rule => rule.Multiplier));
        foreach (var wager in new long[] { 12, 14, 100, 300 }) RobotRevolutionEngine.ValidateWager(wager, false);
        foreach (var wager in new long[] { 0, 2, 10, 13, 302, 2000 })
            Assert.ThrowsAny<ArgumentException>(() => RobotRevolutionEngine.ValidateWager(wager, false));
        foreach (var wager in new long[] { 2, 3, 2000 }) RobotRevolutionEngine.ValidateWager(wager, true);
    }

    [Theory]
    [InlineData("rows", RobotRevolutionEngine.LegacyEngineVersion, 1.126)]
    [InlineData(RobotRevolutionEngine.SunstoneFeatureMode, RobotRevolutionEngine.SunstoneEngineVersion, 0.874)]
    [InlineData(RobotRevolutionEngine.EclipseFeatureMode, RobotRevolutionEngine.HistoricalFifthSunEngineVersion, 1.14)]
    public void BonusMarkerOrderAndHistoricalWagersRetainTheirExactMath(string mode, string version, double scale)
    {
        var wager = version == RobotRevolutionEngine.HistoricalFifthSunEngineVersion ? 12 : 3;
        var before = Empty() with { LegacyRemaining = 2, Carry = new(2, wager, mode) };
        var stored = RobotRevolutionRoundStateCodec.Storage.Write(before);
        // A mode's ordering is the version marker; serialization must not normalize it.
        Assert.Equal(mode, RobotRevolutionRoundStateCodec.Storage.Decode(stored, 2).Carry!.Mode);
        var round = RobotRevolutionRoundPolicy.Prepare("fifth-sun-bonus", new("bonus-proof", wager, true),
            before, DateTime.UtcNow, null, new StableRandom(70));
        Assert.Equal(version, round.Outcome.Audit.EngineVersion);
        Assert.Equal((decimal)scale, round.Outcome.Audit.EffectiveGame.Math.PayoutMultiplier);
        Assert.Equal(6, round.Outcome.Reels[0].Count);
        Assert.Equal(mode, round.Progress.After.Carry!.Mode);
        Assert.Equal(wager, round.Progress.After.Carry.WagerPoints);
        Assert.Equal(0, round.WagerCents);
        RobotRevolutionRoundPolicy.Validate(round);
    }

    [Theory]
    [InlineData(RobotRevolutionEngine.HistoricalFifthSunEngineVersion, 12)]
    [InlineData(RobotRevolutionEngine.SunstoneEngineVersion, 2)]
    [InlineData(RobotRevolutionEngine.LegacyEngineVersion, 2)]
    public void AllThreePreparedVersionsReplayAndRejectAlteredFeatureProof(string version, long wager)
    {
        const string account = "fifth-sun-proof";
        var before = Empty();
        var started = DateTime.UtcNow;
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(new StableRandom(7629), source =>
            new RobotRevolutionEngine(source, version).Spin(wager, account, persistedPityMisses: 0));
        var progress = RobotRevolutionRoundPolicy.Advance(before, outcome, false);
        var round = new RobotRevolutionPreparedRound(Guid.NewGuid(), account, new("fifth-sun-prepared-proof", wager, false), before,
            new(outcome, draws), progress, RobotRevolutionRoundPolicy.OutcomeDigest(outcome), wager * 25,
            RobotRevolutionRoundPolicy.ToCents(outcome.Payout.TotalPoints, 25), false, started, null);
        RobotRevolutionRoundPolicy.Validate(round);
        var id = outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(RobotRevolutionEngine.GameId, account, id),
            $"{id}-wager", round.PayoutCents > 0 ? $"{id}-payout" : null,
            50_000, 50_000 - round.WagerCents, 50_000 - round.WagerCents + round.PayoutCents,
            DateTimeOffset.UtcNow, false, false, LegacyFreeGamesRemaining: 0);
        var settled = new RobotRevolutionRoundResult(RobotRevolutionRoundStateReply.From(progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        { Round = round, ChargedWagerCents = round.WagerCents };
        var report = RobotRevolutionSpinReport.Capture(round, settled, outcome.Audit.GeneratedAtUtc);
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(RobotRevolutionSpinEvidence.InspectNative(report.Round.Audit.Summary, data));
        var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        if (version == RobotRevolutionEngine.LegacyEngineVersion)
        {
            Assert.False(data.GetProperty("result").TryGetProperty("spinFeature", out _));
            changed["result"]!["spinFeature"] = JsonSerializer.SerializeToNode(SunstoneEclipse.Resolve(outcome.Reels, wager, false), RoundAuditCapture.Json);
        }
        else
        {
            Assert.Equal(version == RobotRevolutionEngine.HistoricalFifthSunEngineVersion ? "fifth-sun" : "sunstone-eclipse",
                data.GetProperty("result").GetProperty("spinFeature").GetProperty("kind").GetString());
            changed["result"]!["spinFeature"]!["totalAwardPoints"] = 99999;
        }
        Assert.Single(RobotRevolutionSpinEvidence.InspectNative(report.Round.Audit.Summary, JsonSerializer.SerializeToElement(changed)));
    }

    [Fact]
    public void NewWheelUsesTheActualNewSymbolsAndWholePointAwardsAtEveryAllowedBet()
    {
        foreach (var wager in Enumerable.Range(6, 145).Select(value => value * 2L))
        foreach (var free in new[] { false, true })
        {
            var board = Enumerable.Range(0, 5).Select(_ => (IReadOnlyList<string>)Enumerable.Repeat("SUNSTONE", free ? 6 : 4).ToArray()).ToArray();
            var feature = FifthSunWheel.Resolve(board, wager, free);
            Assert.Equal(2, feature.ContractVersion);
            Assert.Equal("fifth-sun", feature.Kind);
            Assert.Equal(new[] { "JAGUAR", "EAGLE", "SERPENT" }, feature.Rings.Select(ring => ring.SymbolId));
            Assert.True(feature.EclipseTriggered);
            Assert.Equal(wager * (free ? 40 : 20), feature.TotalAwardPoints);
        }
        var empty = Enumerable.Range(0, 5).Select(_ => Enumerable.Repeat("GLYPH_FLAME", 4).ToArray()).ToArray();
        var ring = FifthSunWheel.Resolve(empty, 12, false).Rings[0];
        foreach (var position in ring.Path.Take(3)) empty[position.Reel][position.Row] = "JAGUAR";
        Assert.Equal(6, FifthSunWheel.Resolve(empty, 12, false).TotalAwardPoints);
    }

    [Fact]
    public void ExportCurrentNativeGuideDefinition()
    {
        var game = FifthSunDefinitions.Create(RobotRevolutionEngine.HistoricalFifthSunProfile).CreateGame();
        WriteArtifact("native-definition.json", new { gameId = game.Id, profile = RobotRevolutionEngine.Profile,
            layout = game.Layout, symbols = game.Symbols, wagering = game.Wagering, math = game.Math,
            specialPoints = game.SpecialPoints, paylines = game.Paylines, paytable = FifthSunDefinitions.Create(RobotRevolutionEngine.HistoricalFifthSunProfile).CreatePaytable().Rules });
    }

    internal static ClassicSlotRoundState Empty() => new(0, 0, null, 0, 0, 0,
        new Dictionary<string, long>(), new Dictionary<string, long>());
    internal static void WriteArtifact(string name, object value)
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../artifacts/fifth-sun"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value,
            new JsonSerializerOptions(RoundAuditCapture.Json) { WriteIndented = true }));
    }
    internal sealed class StableRandom(int seed) : IGameRandomSource
    {
        private readonly Random random = new(seed);
        public int Next(int exclusiveMaximum) => random.Next(exclusiveMaximum);
    }
}
