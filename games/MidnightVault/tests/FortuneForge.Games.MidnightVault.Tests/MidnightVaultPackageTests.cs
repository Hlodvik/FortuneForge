using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.MidnightVault.Tests;

public sealed class MidnightVaultPackageTests
{
    [Fact]
    public void PackageOwnsEveryCurrentAndHistoricalDefinition()
    {
        var current = MidnightVaultSlotDefinitions.CreateGame();
        var previous = MidnightVaultSlotDefinitions.CreateGameForPaytable(MidnightVaultSlotDefinitions.PreviousPaytableId);
        var legacy = MidnightVaultSlotDefinitions.CreateGameForPaytable(MidnightVaultSlotDefinitions.LegacyPaytableId);
        Assert.Equal("dragon-hoard-v1", current.Id);
        Assert.Equal(MidnightVaultBaseReelSet.CurrentId, current.Math.ReelSetId);
        Assert.Equal(MidnightVaultBaseReelSet.Id, previous.Math.ReelSetId);
        Assert.Equal(1.09m, legacy.Math.PayoutMultiplier);
        Assert.Equal(6, legacy.FreeGames!.AwardedSpins);
        Assert.Equal(15, MidnightVaultSlotDefinitions.CreateSymbolSet().Symbols.Count);
        Assert.DoesNotContain(typeof(MidnightVaultEngine).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name!.StartsWith("FortuneForge.Server", StringComparison.Ordinal));
    }

    [Fact]
    public void EngineAndReplayUseOnlyOwnedRules()
    {
        var reels = MidnightVaultBaseReelSet.CreateCurrent();
        var stops = reels.Reels.Select(reel => reel.IndexOf("8")).ToArray();
        var result = new MidnightVaultEngine(new Draws(stops, 6103))
            .Spin(100, "package-proof", persistedPityMisses: 0);
        Assert.Equal(MidnightVaultEngine.Version, result.EngineAudit.EngineVersion);
        Assert.Equal(MidnightVaultSlotDefinitions.PaytableId, result.PaytableId);
        Assert.Contains(result.Payout.Paylines, line => line.PaylineId == 1 &&
            line.Matches.Any(match => match.Match.SymbolId == "8" && match.Match.MatchLength == 5));
        Assert.Empty(MidnightVaultOutcomeReplay.Check(result, result.Payout));
        Assert.NotEmpty(MidnightVaultOutcomeReplay.Check(result,
            result.Payout with { TotalPoints = result.Payout.TotalPoints + 1 }));
    }

    [Fact]
    public void WeightedCollectionsKeepOddWagersAndFifoTerms()
    {
        var first = MidnightVaultBonusRounds.Advance([], new Dictionary<string, long>(),
            new Dictionary<string, long>(), new Dictionary<string, int> { ["SEAL_SYNC"] = 15 },
            2, 0, null, MidnightVaultSlotDefinitions.PaytableId, Guid.NewGuid());
        var second = MidnightVaultBonusRounds.Advance(first.Rounds, first.CollectionCounts,
            first.CollectionWagerPoints, new Dictionary<string, int> { ["SEAL_SYNC"] = 15 },
            4, 0, null, MidnightVaultSlotDefinitions.PaytableId, Guid.NewGuid());
        var round = Assert.Single(second.AwardedRounds);
        Assert.Equal(3, round.WagerPoints);
        Assert.Equal(MidnightVaultSlotDefinitions.CollectionFeatureMode, round.FeatureMode);
        Assert.Equal(9, MidnightVaultBonusRounds.Consume(second.Rounds)[0].RemainingSpins);
        MidnightVaultBonusPolicy.ValidateDerivedWager(3);
        Assert.Throws<ArgumentOutOfRangeException>(() => MidnightVaultBonusPolicy.ValidateCashWager(3));
    }

    private sealed class Draws(int[] stops, int seed) : IGameRandomSource
    {
        private int cursor;
        private readonly Random random = new(seed);
        public int Next(int maximumExclusive) => cursor < stops.Length
            ? stops[cursor++] : random.Next(maximumExclusive);
    }
}
