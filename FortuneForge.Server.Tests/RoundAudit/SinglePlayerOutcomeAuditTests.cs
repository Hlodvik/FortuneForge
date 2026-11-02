using System.Collections.Immutable;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.AndarBahar;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.Baccarat;
using FortuneForge.Games.DragonTiger;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.Mines;
using FortuneForge.Games.Mines.Server;
using FortuneForge.Server.Cards.Baccarat;
using FortuneForge.Server.RoundAudit;
using Xunit;

namespace FortuneForge.Server.Tests.RoundAudit;

public sealed class SinglePlayerOutcomeAuditTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Receipt_without_a_game_outcome_auditor_remains_incomplete_even_with_a_valid_hash()
    {
        var receipt = SinglePlayerRoundRecord.Create("video-poker", "round", "alice", 100, 500,
            Now, Now, "source", "id", new { card = "AS" }, "receipt-only");
        Assert.Equal("incomplete", receipt.Audit.Summary.AuditStatus);
        Assert.Contains(receipt.Audit.Summary.Findings, finding => finding.Code == "outcome-replay-pending");
        Assert.Equal("flagged", SinglePlayerRoundRecord.Create("video-poker", "round", "alice", 100, 500,
            Now, Now, "source", "id", new { card = "AS" }, "receipt-only", false, "test-v1").Audit.Summary.AuditStatus);
    }

    [Theory]
    [InlineData(500, 1, 1, false, 1000, 2, 468)]
    [InlineData(500, 24, 1, false, 1000, 2, 11250)]
    [InlineData(500, 3, 22, false, 1000, 2, 25000)]
    [InlineData(500, 3, 0, true, 1000, 2, 0)]
    [InlineData(10000, 12, 13, false, 1000, 1, 46802700000)]
    public void Mines_independent_arithmetic_matches_known_accepted_policies(long wager, int mines, int safe, bool hit, int edge, int policy, long expected)
        => Assert.Equal(expected, MinesOutcomeAudit.ExpectedReturn(wager, mines, safe, hit, edge, policy));

    [Fact]
    public void Mines_replays_every_legal_cashout_mine_hit_and_clear_for_all_mine_counts_and_accepted_policies()
    {
        var engine = new MinesEngine();
        foreach (var (policy, edge, wager) in new[] { (1, 100, 10000L), (1, 1000, 333L), (2, 1000, 100L), (2, 1000, 333L), (2, 1000, 500L) })
            for (var mineCount = 1; mineCount <= 24; mineCount++)
            {
                var state = new MinesRoundState(wager, mineCount, Enumerable.Range(0, mineCount).ToImmutableArray(), [], MinesRoundStatus.Active, 0, edge, policy);
                for (var safe = 0; safe < 25 - mineCount; safe++)
                {
                    Check(engine.Reveal(state, 0), 2 + safe);
                    if (safe > 0) Check(engine.CashOut(state), 2 + safe);
                    state = engine.Reveal(state, mineCount + safe);
                    if (state.Status == MinesRoundStatus.Cleared) Check(state, 2 + safe);
                }
            }
        void Check(MinesRoundState state, long version)
        {
            var record = new MinesStoredRound(new string('a', 64), "alice", state, version, Now, Now.AddSeconds(version));
            Assert.True(MinesOutcomeAudit.Verify(record, state.PayoutCents));
            Assert.False(MinesOutcomeAudit.Verify(record, state.PayoutCents + 1));
            Assert.False(MinesOutcomeAudit.Verify(record with { Version = version + 1 }, state.PayoutCents));
        }
    }

    [Fact]
    public void Dragon_tiger_independently_checks_all_rank_pairs_and_half_loss_plus_tie_return()
    {
        var wagers = new DragonTigerWagers(102, 204, 306);
        foreach (var dragon in Enum.GetValues<CardRank>())
            foreach (var tiger in Enum.GetValues<CardRank>())
            {
                var round = new DragonTigerRound(new(dragon, CardSuit.Spades), new(tiger, CardSuit.Hearts));
                var returned = DragonTigerRules.Settle(wagers, round).Sum(value => value.TotalReturnCents);
                var stored = new DragonTigerStoredRound("round", "alice", wagers, round, 2, 414);
                Assert.True(DragonTigerOutcomeAudit.Verify(stored, 612, returned));
                Assert.False(DragonTigerOutcomeAudit.Verify(stored, 612, returned + 1));
                Assert.False(DragonTigerOutcomeAudit.Verify(stored, 610, returned));
            }
    }

    [Fact]
    public void Andar_bahar_checks_first_matching_card_order_and_both_distinct_payout_rates()
    {
        var deck = (from rank in Enum.GetValues<CardRank>() from suit in Enum.GetValues<CardSuit>() select new PlayingCard(rank, suit)).ToArray();
        var joker = new PlayingCard(CardRank.Ace, CardSuit.Spades);
        var nonmatches = deck.Where(card => card.Rank != joker.Rank).ToArray();
        var matches = deck.Where(card => card.Rank == joker.Rank && card != joker).ToArray();
        for (var matchAt = 1; matchAt <= 49; matchAt++)
        {
            var ordered = new[] { joker }.Concat(nonmatches.Take(matchAt - 1)).Append(matches[0])
                .Concat(nonmatches.Skip(matchAt - 1)).Concat(matches.Skip(1)).ToArray();
            var round = AndarBaharRules.Deal(ordered);
            foreach (var side in Enum.GetValues<AndarBaharSide>())
            {
                var returned = AndarBaharRules.Settle(side, 110, round).TotalReturnCents;
                var stored = new AndarBaharStoredRound("round", "alice", "key", side, 110, round, 1000, 1000 + returned, Now);
                Assert.True(AndarBaharOutcomeAudit.Verify(stored, returned));
                Assert.False(AndarBaharOutcomeAudit.Verify(stored, returned + 1));
                Assert.False(AndarBaharOutcomeAudit.Verify(stored with { BalanceAfterSettlementCents = 1000 + returned + 1 }, returned));
            }
        }
    }

    [Fact]
    public void Baccarat_independent_tableau_covers_all_initial_totals_third_card_points_and_side_bets()
    {
        for (var p = 0; p < 10; p++)
            for (var b = 0; b < 10; b++)
                for (var third = 0; third < 10; third++)
                {
                    var round = PuntoBancoRoundDealer.Deal([Card(p), Card(b), Card(0), Card(0), Card(third), Card(9)]);
                    foreach (var side in Enum.GetValues<BaccaratBetSide>())
                    {
                        var stored = new BaccaratStoreRound("round", "alice", side, round,
                            PuntoBancoPaytable.Settle(side, .25m, round), PuntoBancoPaytable.SettlePair(.5m, round.PlayerHand),
                            PuntoBancoPaytable.SettlePair(.75m, round.BankerHand));
                        var returned = (long)(stored.CombinedTotalReturn * 100);
                        Assert.True(BaccaratOutcomeAudit.Verify(stored, 150, returned));
                        Assert.False(BaccaratOutcomeAudit.Verify(stored, 150, returned + 1));
                        Assert.False(BaccaratOutcomeAudit.Verify(stored with { Round = round with { EndedOnNatural = !round.EndedOnNatural } }, 150, returned));
                    }
                }
        static PlayingCard Card(int points) => new(points == 0 ? CardRank.Ten : (CardRank)points, CardSuit.Spades);
    }
}
