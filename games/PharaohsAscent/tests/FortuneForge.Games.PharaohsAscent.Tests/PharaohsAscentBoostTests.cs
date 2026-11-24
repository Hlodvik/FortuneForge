using FortuneForge.Games.PharaohsAscent.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.PharaohsAscent.Tests;

public sealed class PharaohsAscentBoostTests
{
    private const string Account = "pharaoh-funded-boost-tests";
    private static readonly int Cost = PharaohsAscentEngine.CreateGame().SpecialPoints!.ActivationCost;

    [Fact]
    public void CreditsEarnedAtMinimumWagerCannotBoostAMaximumWager()
    {
        var state = State(new(0, new Dictionary<long, int> { [2] = Cost }));
        var error = Assert.Throws<PharaohsAscentInsufficientSpecialPointsException>(() =>
            PharaohsAscentRoundPolicy.Prepare(Account, new("pharaoh-low-to-high-boost", 300, false, true),
                state, DateTime.UtcNow, null, new NoDraws()));
        Assert.Equal(0, error.Available);
        Assert.Equal(Cost, state.SpecialPoints);
    }

    [Fact]
    public void CreditsBoostTheirOwnFundedWagerAndReplayAfterStorageRestore()
    {
        var state = State(new(0, new Dictionary<long, int> { [2] = Cost, [300] = Cost }));
        state = PharaohsAscentRoundStateCodec.Storage.Decode(PharaohsAscentRoundStateCodec.Storage.Write(state), 0);
        var round = PharaohsAscentRoundPolicy.Prepare(Account, new("pharaoh-own-wager-boost", 2, false, true),
            state, DateTime.UtcNow, null, new RandomDraws(19));
        PharaohsAscentRoundPolicy.Validate(round);
        var funding = Assert.IsType<PharaohsAscentBoostFunding>(SealedTombState.Parse(round.Progress.After.NativeStateJson).BoostFunding);
        Assert.Equal(Cost, funding.PointsByWager[300]);
        Assert.Equal(round.Outcome.SpecialPointsAwarded, funding.PointsByWager.GetValueOrDefault(2));
        Assert.Equal(Cost + round.Outcome.SpecialPointsAwarded, round.Progress.After.SpecialPoints);
    }

    [Fact]
    public void HistoricalGlobalBalanceIsConsumedFirstAndCannotRegenerate()
    {
        var legacy = SealedTombState.Empty;
        var first = PharaohsAscentBoosts.AfterSpin(legacy, 2, Cost + 3, true, Cost - 3);
        Assert.Equal(3, first.LegacyPoints);
        Assert.Equal(Cost - 3, first.PointsByWager[2]);
        var migrated = legacy with { BoostFunding = first };
        Assert.Equal(3, PharaohsAscentBoosts.BeforeSpin(migrated, 300, Cost));
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentBoosts.AfterSpin(migrated, 300, Cost, true, 0));
        var second = PharaohsAscentBoosts.AfterSpin(migrated, 2, Cost, true, 2);
        Assert.Equal(0, second.LegacyPoints);
        Assert.Equal(2, second.PointsByWager[2]);
        Assert.Equal(0, PharaohsAscentBoosts.BeforeSpin(migrated with { BoostFunding = second }, 300, 2));
    }

    [Fact]
    public void HistoricalV2KeepsItsOriginalGlobalBoostContract()
    {
        var before = Empty() with { SpecialPoints = Cost };
        var started = DateTime.UtcNow;
        var engine = PharaohsAscentSpinEvidence.Record(new RandomDraws(83), Account, 300, true,
            before.Energy, null, before.Pity, false, PharaohsAscentEngine.PreviousTombEngineVersion);
        var progress = PharaohsAscentRoundPolicy.Advance(before, engine.Outcome, false);
        var round = new PharaohsAscentPreparedRound(Guid.NewGuid(), Account, new("pharaoh-legacy-boost", 300, false, true),
            before, engine, progress, PharaohsAscentRoundPolicy.OutcomeDigest(engine.Outcome), 7_500,
            progress.SettledPayout.TotalPoints * 25, false, started, null);
        PharaohsAscentRoundPolicy.Validate(round);
        Assert.Null(SealedTombState.Parse(progress.After.NativeStateJson).BoostFunding);
        Assert.Equal(engine.Outcome.SpecialPointsAwarded, progress.After.SpecialPoints);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(12)]
    [InlineData(300)]
    public void FixedStakeNativeFlowHasExactlyTheSameBoostAvailabilityAndPointBalance(long wager)
    {
        var state = Empty();
        var engine = new PharaohsAscentEngine(new RandomDraws(107));
        var boostsUsed = 0;
        for (var spin = 0; spin < 1_000; spin++)
        {
            var free = state.Carry is not null;
            Assert.Equal(wager, state.Carry?.WagerPoints ?? wager);
            var available = PharaohsAscentRoundPolicy.BoostPointsAvailable(state, wager);
            Assert.Equal(state.SpecialPoints, available);
            var boost = available >= Cost;
            if (boost) boostsUsed++;
            var outcome = engine.Spin(wager, Account, boost, PharaohsAscentRoundPolicy.EnergyBeforeSpin(state, wager), state.Carry?.Mode,
                PharaohsAscentRoundPolicy.PityBeforeSpin(state, wager), free);
            var conventionalBalance = checked(state.SpecialPoints - (boost ? Cost : 0) + outcome.SpecialPointsAwarded);
            state = PharaohsAscentRoundPolicy.Advance(state, outcome, false).After;
            Assert.Equal(conventionalBalance, state.SpecialPoints);
            Assert.Equal(state.SpecialPoints, PharaohsAscentBoosts.Total(SealedTombState.Parse(state.NativeStateJson).BoostFunding!));
        }
        Assert.True(boostsUsed > 0);
    }

    [Fact]
    public void MalformedPoolsOrAggregateDisagreementAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentBoosts.Validate(new(-1, new Dictionary<long, int>())));
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentBoosts.Validate(new(0, new Dictionary<long, int> { [1] = 8 })));
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentBoosts.Validate(new(0, new Dictionary<long, int> { [2_001] = 8 })));
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentBoosts.Validate(new(0, new Dictionary<long, int> { [2] = -1 })));
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentBoosts.Validate(new(int.MaxValue, new Dictionary<long, int> { [2] = 1 })));
        var funding = new PharaohsAscentBoostFunding(0, new Dictionary<long, int> { [2] = Cost });
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentBoosts.BeforeSpin(SealedTombState.Empty with { BoostFunding = funding }, 2, Cost + 1));
    }

    private static ClassicSlotRoundState State(PharaohsAscentBoostFunding funding) => Empty() with
    {
        SpecialPoints = PharaohsAscentBoosts.Total(funding),
        NativeStateJson = SealedTombState.Serialize(SealedTombState.Empty with { BoostFunding = funding }),
    };
    private static ClassicSlotRoundState Empty() => new(0, 0, null, 0, 0, 0,
        new Dictionary<string, long>(), new Dictionary<string, long>());
    private sealed class NoDraws : IGameRandomSource
    {
        public int Next(int maximumExclusive) => throw new InvalidOperationException("An unfunded boost must be rejected before drawing.");
    }
    private sealed class RandomDraws(int seed) : IGameRandomSource
    {
        private readonly Random random = new(seed);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
