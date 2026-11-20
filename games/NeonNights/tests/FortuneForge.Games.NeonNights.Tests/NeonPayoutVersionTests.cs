using System.Text.Json;
using FortuneForge.Games.NeonNights;
using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.NeonNights.Tests;

public sealed class NeonPayoutVersionTests
{
    [Theory]
    [InlineData(null, false, false)]
    [InlineData("car-rows", true, false)]
    [InlineData(null, false, true)]
    public void Version19SnapshotsReplayTheirCapturedBaseCarAndDiscoPrices(
        string? featureMode, bool awardedPlay, bool disco)
    {
        var definitions = new Version19Definitions();
        var reelSetId = disco ? NeonDiscoDefinitions.ReelSetId
            : awardedPlay ? NeonArcadeSlotDefinitions.CarReelSetId : NeonArcadeSlotDefinitions.ReelSetId;
        var strips = definitions.GetReelSet(reelSetId)!;
        var stops = strips.Reels.Select(reel => reel.IndexOf(disco ? "2" : "3")).ToArray();
        var outcome = new NeonNightsEngine(definitions, new StopsThenSeed(stops))
            .Spin(300, "captured-version19", false, 0, featureMode, 0, awardedPlay, disco);
        var historical = outcome with
        {
            EngineAudit = outcome.EngineAudit with
            {
                EngineVersion = NeonArcadeSlotDefinitions.PreviousCombinedRtpEngineVersion
            }
        };
        if (awardedPlay)
        {
            var captured = JsonSerializer.SerializeToNode(historical.EngineAudit.EffectiveGame)!;
            captured["Math"]!["PayoutMultiplier"] = 1.23m;
            var capturedGame = captured.Deserialize<GameDefinition>()!;
            var evaluations = new NeonCombinationEvaluator().Evaluate(historical.Reels, capturedGame,
                historical.EngineAudit.SymbolSet, historical.EngineAudit.EngineVersion);
            var linePayout = new NeonPayoutCalculator().Calculate(evaluations, capturedGame,
                historical.EngineAudit.Paytable, historical.WagerPoints);
            var landed = historical.NeonNitroWild?.InitialReels ?? historical.Reels;
            var bonus = NeonArcadeBonus.Apply(linePayout, landed, isFreeSpin: true,
                engineVersion: historical.EngineAudit.EngineVersion);
            var sevens = NeonNightsEngine.CalculateTripleSevenPayouts(landed, historical.WagerPoints);
            var sevenPoints = sevens.Sum(line => line.AmountPoints);
            historical = historical with
            {
                Payout = new(bonus.Payout.TotalPoints + sevenPoints, [.. bonus.Payout.Paylines, .. sevens]),
                BananaBonusPoints = sevenPoints,
                FreeSpinsAwarded = bonus.FreeSpinsAwarded,
                NeonArcade = bonus.Evidence,
                EngineAudit = historical.EngineAudit with
                {
                    EffectiveGame = capturedGame,
                    NeonArcade = bonus.Evidence
                }
            };
        }

        Assert.Equal(0.765m, historical.EngineAudit.BaseGame.Math.PayoutMultiplier);
        Assert.True(historical.Payout.TotalPoints > 0);
        Assert.Empty(NeonOutcomeReplay.Check(historical, historical.Payout));
        Assert.NotEmpty(NeonOutcomeReplay.Check(historical,
            historical.Payout with { TotalPoints = historical.Payout.TotalPoints + 1 }));
        if (awardedPlay)
        {
            Assert.Equal(1.23m, historical.EngineAudit.EffectiveGame.Math.PayoutMultiplier);
            Assert.Equal(56, historical.EngineAudit.EffectiveGame.Paylines.Count);
        }
        if (disco)
        {
            Assert.Equal("neon-disco-symbols-v2", historical.SymbolSetId);
            Assert.Equal(0.01m, historical.EngineAudit.EffectiveGame.Math.PayoutMultiplier);
            Assert.NotNull(historical.NeonDiscoCascade);
        }
    }

    private sealed class Version19Definitions : ISlotDefinitionSource
    {
        private readonly NeonDefinitionSource definitions = new();

        public GameDefinition? GetGame(string id)
        {
            var game = definitions.GetGame(id);
            if (game is null) return null;
            var captured = JsonSerializer.SerializeToNode(game)!;
            captured["Math"]!["PayoutMultiplier"] = 0.765m;
            return captured.Deserialize<GameDefinition>()!;
        }

        public SymbolSetDefinition? GetSymbolSet(string id) => definitions.GetSymbolSet(id);
        public ReelSetDefinition? GetReelSet(string id) => definitions.GetReelSet(id);
        public PaytableDefinition? GetPaytable(string id) => definitions.GetPaytable(id);
    }

    private sealed class StopsThenSeed(int[] stops) : IGameRandomSource
    {
        private int index;
        private readonly Random random = new(73);

        public int Next(int maximumExclusive) => index < stops.Length
            ? stops[index++] : random.Next(maximumExclusive);
    }
}
