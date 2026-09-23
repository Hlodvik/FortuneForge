using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Dice;
using FortuneForge.Games.LiarsDice;

namespace FortuneForge.Games.Tests;

public sealed class LiarsDiceEngineTests
{
    [Fact]
    public void TruthfulBidMakesChallengerLose()
    {
        var state = StartRound([4, 4], [2, 4]);
        var bid = LiarsDiceEngine.Apply(
            state,
            new PlaceLiarsDiceBid("alice", new LiarsDiceBid(2, Die(4))));

        var challenge = LiarsDiceEngine.Apply(bid.State, new ChallengeLiarsDiceBid("bob"));

        Assert.Equal(LiarsDiceRoundPhase.Resolved, challenge.State.Phase);
        Assert.Equal("bob", challenge.Outcome?.LoserId);
        Assert.Equal(3, challenge.Outcome?.MatchingDice);
    }

    [Fact]
    public void FalseBidMakesBidderLose()
    {
        var state = StartRound([6], [2]);
        var bid = LiarsDiceEngine.Apply(
            state,
            new PlaceLiarsDiceBid("alice", new LiarsDiceBid(2, Die(6))));

        var challenge = LiarsDiceEngine.Apply(bid.State, new ChallengeLiarsDiceBid("bob"));

        Assert.Equal("alice", challenge.Outcome?.LoserId);
    }

    [Fact]
    public void SpotOnCallWinsOnlyWhenBidIsExact()
    {
        var exact = LiarsDiceEngine.Apply(
            LiarsDiceEngine.Apply(StartRound([4, 4], [2, 4]), new PlaceLiarsDiceBid("alice", new LiarsDiceBid(3, Die(4)))).State,
            new SpotOnLiarsDiceBid("bob"));
        var inexact = LiarsDiceEngine.Apply(
            LiarsDiceEngine.Apply(StartRound([4, 4], [2, 4]), new PlaceLiarsDiceBid("alice", new LiarsDiceBid(2, Die(4)))).State,
            new SpotOnLiarsDiceBid("bob"));

        Assert.Equal("alice", exact.Outcome?.LoserId);
        Assert.True(exact.Outcome?.IsSpotOn);
        Assert.Equal("bob", inexact.Outcome?.LoserId);
    }

    [Fact]
    public void BidMustIncreaseAndFollowTurnOrder()
    {
        var state = StartRound([3], [4]);
        var bid = LiarsDiceEngine.Apply(
            state,
            new PlaceLiarsDiceBid("alice", new LiarsDiceBid(1, Die(3))));

        Assert.Throws<LiarsDiceRuleException>(() => LiarsDiceEngine.Apply(
            bid.State,
            new PlaceLiarsDiceBid("alice", new LiarsDiceBid(2, Die(3)))));
        Assert.Throws<LiarsDiceRuleException>(() => LiarsDiceEngine.Apply(
            bid.State,
            new PlaceLiarsDiceBid("bob", new LiarsDiceBid(1, Die(2)))));
    }

    [Fact]
    public void RoundCopiesHandsAndRequiresEveryPlayer()
    {
        var alice = new List<DieValue> { Die(1) };
        var state = LiarsDiceEngine.StartRound(
            ["alice", "bob"],
            new Dictionary<string, IReadOnlyCollection<DieValue>>
            {
                ["alice"] = alice,
                ["bob"] = [Die(2)],
            });

        alice.Add(Die(6));

        Assert.Single(state.Hands["alice"]);
        Assert.Throws<ArgumentException>(() => LiarsDiceEngine.StartRound(
            ["alice", "bob"],
            new Dictionary<string, IReadOnlyCollection<DieValue>> { ["alice"] = [Die(1)] }));
        Assert.Throws<InvalidOperationException>(() => LiarsDiceEngine.StartRound(
            ["alice", "bob"],
            new Dictionary<string, IReadOnlyCollection<DieValue>>
            {
                ["alice"] = [default],
                ["bob"] = [Die(2)],
            }));
    }

    [Fact]
    public void DescriptorDeclaresDiceSkeletonContract()
    {
        var descriptor = LiarsDiceModule.Descriptor;

        Assert.Equal("liars-dice", descriptor.Id);
        Assert.Equal("0.1.1", descriptor.PackageVersion);
        Assert.Equal(GameCategory.Dice, descriptor.Category);
        Assert.True(descriptor.Capabilities.HasFlag(GameCapability.Multiplayer));
        Assert.True(descriptor.Capabilities.HasFlag(GameCapability.History));
    }

    private static LiarsDiceRoundState StartRound(int[] alice, int[] bob) =>
        LiarsDiceEngine.StartRound(
            ["alice", "bob"],
            new Dictionary<string, IReadOnlyCollection<DieValue>>
            {
                ["alice"] = alice.Select(Die).ToArray(),
                ["bob"] = bob.Select(Die).ToArray(),
            });

    private static DieValue Die(int value) => new(value);
}
