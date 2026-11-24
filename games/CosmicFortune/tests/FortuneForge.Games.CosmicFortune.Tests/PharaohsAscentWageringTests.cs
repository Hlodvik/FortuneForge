using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.CosmicFortune.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Xunit;

namespace FortuneForge.Games.CosmicFortune.Tests;

public sealed class PharaohsAscentWageringTests
{
    private const string Account = "pharaoh-wager-cap-unit";

    [Fact]
    public void CurrentPaidTermsUseTheGameOwnedHalfRandMinimumThreeRandDefaultAndSeventyFiveRandMaximum()
    {
        var terms = PharaohsAscentWagering.Current;
        Assert.Equal(25, terms.PointValueInCents);
        Assert.Equal(2, terms.MinimumWagerPoints);
        Assert.Equal(12, terms.DefaultWagerPoints);
        Assert.Equal(300, terms.MaximumWagerPoints);
        Assert.Equal(2, terms.WagerIncrementPoints);
        foreach (var wager in new long[] { 2, 12, 300 }) PharaohsAscentWagering.ValidateNewPaidWager(wager);
        Assert.Equal(2_000, CosmicFortuneEngine.CreateGame().Wagering.MaximumWagerPoints);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(302)]
    [InlineData(2_000)]
    public void NewPaidAdmissionsRejectOutOfRangeOrOffStepBetsBeforeDrawing(long wager)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CosmicFortuneRoundPolicy.Prepare(Account,
            new("pharaoh-invalid-paid-wager", wager, false), Empty(), DateTime.UtcNow, null, new NoDraws()));
    }

    [Fact]
    public void ANewMaximumBetChargesExactlySeventyFiveRand()
    {
        var round = CosmicFortuneRoundPolicy.Prepare(Account, new("pharaoh-current-maximum", 300, false),
            Empty(), DateTime.UtcNow, null, new FirstDraws());
        Assert.Equal(7_500, round.WagerCents);
        CosmicFortuneRoundPolicy.Validate(round);
    }

    [Fact]
    public void HistoricalAboveCapPreparedPaidRoundsKeepTheirFrozenReplayAndSettlement()
    {
        var before = Empty();
        var started = DateTime.UtcNow;
        var engine = CosmicFortuneSpinEvidence.Record(new FirstDraws(), Account, 2_000, false, 0, null, 0, false,
            CosmicFortuneEngine.PreviousTombEngineVersion);
        var outcome = engine.Outcome;
        var progress = CosmicFortuneRoundPolicy.Advance(before, outcome, false);
        var round = new CosmicFortunePreparedRound(Guid.NewGuid(), Account, new("pharaoh-old-paid-operation", 2_000, false),
            before, engine, progress, CosmicFortuneRoundPolicy.OutcomeDigest(outcome), 50_000,
            CosmicFortuneRoundPolicy.ToCents(progress.SettledPayout.TotalPoints, outcome.PointValueInCents), false, started, null);
        CosmicFortuneRoundPolicy.Validate(round);
        var id = outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(CosmicFortuneEngine.GameId, Account, id),
            id + "-wager", round.PayoutCents > 0 ? id + "-payout" : null, 1_000_000, 950_000,
            950_000 + round.PayoutCents, DateTimeOffset.UtcNow, false, false, LegacyFreeGamesRemaining: 0);
        var settled = new CosmicFortuneRoundResult(CosmicFortuneRoundStateReply.From(progress.After), receipt)
        { Round = round, ChargedWagerCents = round.WagerCents };
        var report = CosmicFortuneSpinReport.Capture(round, settled, DateTime.UtcNow);
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        Assert.Empty(CosmicFortuneSpinEvidence.InspectNative(report.Round.Audit.Summary, proof.RootElement.GetProperty("data")));
        Assert.Equal(50_000, CosmicFortuneSpinResponse.From(settled).ChargedWagerCents);
    }

    [Fact]
    public void HistoricalFundedCarryAboveTheCurrentCashCapRemainsPlayable()
    {
        var before = Empty() with { NativeRemaining = 2, Carry = new(2, 2_000, null) };
        var saved = CosmicFortuneRoundStateCodec.Storage.Write(before);
        Assert.Equal(before.Carry, CosmicFortuneRoundStateCodec.Storage.Decode(saved, 0).Carry);
        var round = CosmicFortuneRoundPolicy.Prepare(Account, new("pharaoh-old-funded-carry", 2_000, true),
            before, DateTime.UtcNow, null, new FirstDraws());
        Assert.Equal(0, round.WagerCents);
        Assert.Equal(2_000, round.Outcome.WagerPoints);
        CosmicFortuneRoundPolicy.Validate(round);
    }

    [Fact]
    public void MixedHistoricalAndCurrentSigilsRetainTheirFundedValueAboveTheCashCap()
    {
        var before = SealedTombState.Empty with { Sigils = 5, SigilWagerPointTotal = 10_000 };
        var funded = SealedTombRules.Accrue(before, Guid.NewGuid(), 300, false, 2);
        Assert.Equal(1_717, Assert.Single(funded.Tickets).FundedWagerPoints);
        var entered = SealedTombRules.Act(funded, new("enter", funded.Revision), new FirstDraws()).After;
        var collected = SealedTombRules.Act(entered, new("collect", entered.Revision), new NoDraws());
        Assert.Equal(8_585, collected.PayoutPoints);
        Assert.Empty(collected.After.Tickets);
    }

    private static ClassicSlotRoundState Empty() => new(0, 0, null, 0, 0, 0,
        new Dictionary<string, long>(), new Dictionary<string, long>());
    private sealed class FirstDraws : IGameRandomSource { public int Next(int maximumExclusive) => 0; }
    private sealed class NoDraws : IGameRandomSource { public int Next(int maximumExclusive) => throw new InvalidOperationException("A rejected wager cannot draw an outcome."); }
}
