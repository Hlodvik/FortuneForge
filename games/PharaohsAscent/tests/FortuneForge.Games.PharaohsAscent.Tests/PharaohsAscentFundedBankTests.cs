using FortuneForge.Games.PharaohsAscent.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.PharaohsAscent.Tests;

public sealed class PharaohsAscentFundedBankTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewGuardianAwardCannotRepriceTheOlderScatterBatch(bool restoreFromStorage)
    {
        var empty = EmptyState();
        var state = Advance(empty, QuietOutcome(empty, 12, free: false) with { FreeSpinsAwarded = 6 });
        Assert.Equal(new ClassicSlotRoundCarry(6, 12, null), state.Carry);

        state = state with
        {
            SealCounts = new Dictionary<string, long> { ["rand"] = 23 },
            SealWagerPoints = new Dictionary<string, long> { ["rand"] = 23 * 300 },
        };
        var completingSpin = QuietOutcome(state, 12, free: true) with
        {
            CollectionsAwarded = new Dictionary<string, int> { ["SEAL_RAND"] = 1 },
        };
        var completion = PharaohsAscentRoundPolicy.Advance(state, completingSpin, spendsLegacy: false);
        Assert.Equal(7, completion.CollectionFreeSpinsAwarded);
        Assert.Equal(288, completion.CollectionFreeSpinWagerPoints);
        state = completion.After;
        Assert.Equal(new ClassicSlotRoundCarry(12, 12, null), state.Carry);

        if (restoreFromStorage)
        {
            var restored = PharaohsAscentRoundStateCodec.Storage.Decode(
                PharaohsAscentRoundStateCodec.Storage.Write(state), signedLegacyCount: 0);
            Assert.True(state.Matches(restored));
            state = restored;
        }

        for (var oldRemaining = 5; oldRemaining > 0; oldRemaining--)
        {
            Assert.Equal(new ClassicSlotRoundCarry(oldRemaining + 7, 12, null), state.Carry);
            Assert.Equal(PharaohsAscentEngine.EngineVersion, PharaohsAscentRoundPolicy.EngineVersionForSpin(state, true));
            state = Advance(state, QuietOutcome(state, 12, free: true));
        }
        Assert.Equal(new ClassicSlotRoundCarry(7, 288, "rand"), state.Carry);

        for (var guardianRemaining = 7; guardianRemaining > 0; guardianRemaining--)
        {
            Assert.Equal(new ClassicSlotRoundCarry(guardianRemaining, 288, "rand"), state.Carry);
            state = Advance(state, QuietOutcome(state, 288, free: true));
        }
        Assert.Null(state.Carry);
        Assert.Equal(0, state.NativeRemaining);
        Assert.Equal(0, state.LegacyRemaining);
    }

    [Fact]
    public void UnqueuedHistoricalCarryRetainsItsFrozenV2Progression()
    {
        var state = EmptyState() with
        {
            NativeRemaining = 6,
            Carry = new ClassicSlotRoundCarry(6, 12, null),
            SealCounts = new Dictionary<string, long> { ["rand"] = 23 },
            SealWagerPoints = new Dictionary<string, long> { ["rand"] = 23 * 300 },
        };
        Assert.Equal(PharaohsAscentEngine.PreviousTombEngineVersion,
            PharaohsAscentRoundPolicy.EngineVersionForSpin(state, true));
        var outcome = QuietOutcome(state, 12, free: true) with
        {
            CollectionsAwarded = new Dictionary<string, int> { ["SEAL_RAND"] = 1 },
        };
        Assert.Equal(PharaohsAscentEngine.PreviousTombEngineVersion, outcome.Audit.EngineVersion);
        var after = Advance(state, outcome);
        // Historical v2 used one merged bank. Its recorded behavior must still reproduce.
        Assert.Equal(new ClassicSlotRoundCarry(12, 288, "rand"), after.Carry);
        Assert.Equal(PharaohsAscentEngine.PreviousTombEngineVersion,
            PharaohsAscentRoundPolicy.EngineVersionForSpin(after, true));
    }

    private static ClassicSlotRoundState EmptyState() =>
        new(0, 0, null, 0, 0, 0, new Dictionary<string, long>(), new Dictionary<string, long>());

    private static ClassicSlotRoundState Advance(ClassicSlotRoundState before, ClassicSlotSpinOutcome outcome) =>
        PharaohsAscentRoundPolicy.Advance(before, outcome, spendsLegacy: false).After;

    private static ClassicSlotSpinOutcome QuietOutcome(ClassicSlotRoundState before, long wager, bool free)
    {
        var version = PharaohsAscentRoundPolicy.EngineVersionForSpin(before, free);
        var outcome = new PharaohsAscentEngine(new FirstIndexRandom(), version).Spin(wager, "funded-bank-unit-test",
            currentEnergyBalance: before.Energy, featureMode: free ? before.Carry?.Mode : null,
            persistedPityMisses: 0, isFreeSpin: free);
        // Isolated progression inputs keep this test independent of incidental symbol awards.
        return outcome with
        {
            FreeSpinsAwarded = 0, EnergyAwarded = 0, ConsecutiveFiveMisses = 0,
            CollectionsAwarded = new Dictionary<string, int>(), Payout = new SpinPayout(0, []),
            PatternBonusPoints = 0, CollectorPoints = 0, PawCount = 0,
        };
    }

    private sealed class FirstIndexRandom : IGameRandomSource
    {
        public int Next(int maximumExclusive) => 0;
    }
}
