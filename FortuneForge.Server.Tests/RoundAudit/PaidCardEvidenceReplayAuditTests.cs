using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.AndarBahar;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;
using FortuneForge.Games.DragonTiger;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.HiLo;
using FortuneForge.Games.HouseHoldem;
using FortuneForge.Games.VideoPoker;
using FortuneForge.Server.Cards.Baccarat;
using FortuneForge.Server.Cards.CasinoWar;
using FortuneForge.Server.Cards.VideoPoker;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.RoundAudit;

public sealed class PaidCardEvidenceReplayAuditTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void HiLo_saved_history_replays_and_tampered_choice_is_flagged()
    {
        var round = HiLoEngine.Start(Guid.NewGuid(), 1m, new(CardRank.Five, CardSuit.Clubs));
        round = HiLoEngine.Guess(round, HiLoDirection.Higher, new(CardRank.Six, CardSuit.Diamonds));
        round = HiLoEngine.CashOut(round);
        var saved = Capture("hi-lo", round.Id.ToString("N"), 100, checked((long)(round.CurrentReturn * 100)),
            round, "hi-lo-revealed-cards-and-choices-v1");
        Check(saved, payload => payload["data"]!["history"]![1]!["direction"] = (int)HiLoDirection.Lower);
    }

    [Fact]
    public void CasinoWar_saved_deck_and_tie_decision_replay_and_tampered_return_is_flagged()
    {
        var first = new PlayingCard(CardRank.Ace, CardSuit.Clubs);
        var second = new PlayingCard(CardRank.Ace, CardSuit.Diamonds);
        var deck = new[] { first, second }.Concat(StandardDeck.Create().Where(card => card != first && card != second))
            .Take(10).ToArray();
        var round = CasinoWarRoundEngine.Start(deck, 1m);
        Assert.Equal(CasinoWarRoundPhase.AwaitingTieDecision, round.Phase);
        var tie = CasinoWarTieBetPaytable.Settle(round.Opening, 1m);
        round = CasinoWarRoundEngine.Decide(round, CasinoWarTieDecision.GoToWar);
        var saved = Capture("casino-war", "war-round", 300,
            checked((long)((round.Settlement!.TotalReturn + tie.TotalReturn) * 100)),
            new CasinoWarStoreRound("war-round", "alice", round, tie, deck), "casino-war-cards-and-decision-v1");
        Check(saved, payload => payload["data"]!["round"]!["settlement"]!["totalReturn"] = 999m);
    }

    [Fact]
    public void VideoPoker_saved_holds_and_hands_replay_and_tampered_award_is_flagged()
    {
        var round = VideoPokerRoundEngine.Deal(StandardDeck.Create(), 2);
        round = VideoPokerRoundEngine.Draw(round, new VideoPokerHeldCardPositions(
            [VideoPokerCardPosition.First, VideoPokerCardPosition.Third]));
        var payout = checked(round.Results.Sum(result => result.PaytableOutcome.CreditsWon) * VideoPokerMoney.CoinValueCents);
        var saved = Capture("video-poker", "video-round", 200, payout, round,
            "video-poker-deal-holds-and-draw-v1");
        Check(saved, payload => payload["data"]!["results"]![0]!["paytableOutcome"]!["creditsWon"] = 999);
    }

    [Fact]
    public void HouseHoldem_saved_actions_replay_and_tampered_settlement_is_flagged()
    {
        var round = HouseHoldemRules.StartRound(StandardDeck.Create().Select(CardCode.Format).ToArray(), 100);
        var steps = new[] { HouseHoldemActions.Play4X, "advance", "advance" };
        HouseHoldemRules.ApplyAction(round, steps[0]);
        HouseHoldemRules.Advance(round);
        HouseHoldemRules.Advance(round);
        var saved = Capture("house-holdem", "house-round", round.TotalWagerCents, round.PayoutCents,
            new { round, steps }, "house-holdem-cards-and-actions-v1");
        Check(saved, payload => payload["data"]!["round"]!["settlement"]!["antePayoutCents"] = 999L);
    }

    [Fact]
    public void Baccarat_saved_tableau_replays_and_tampered_natural_is_flagged()
    {
        static PlayingCard Card(CardRank rank) => new(rank, CardSuit.Spades);
        var round = PuntoBancoRoundDealer.Deal([Card(CardRank.Two), Card(CardRank.Three),
            Card(CardRank.Four), Card(CardRank.Five), Card(CardRank.Six), Card(CardRank.Seven)]);
        var side = BaccaratBetSide.Player;
        var stored = new BaccaratStoreRound("baccarat-round", "alice", side, round,
            PuntoBancoPaytable.Settle(side, 1m, round), PuntoBancoPaytable.SettlePair(0m, round.PlayerHand),
            PuntoBancoPaytable.SettlePair(0m, round.BankerHand));
        var saved = Capture("baccarat", stored.RoundId, 100, checked((long)(stored.CombinedTotalReturn * 100)),
            stored, "baccarat-deal-and-side-bets-v1", "baccarat-outcome-v1");
        Check(saved, payload => payload["data"]!["round"]!["endedOnNatural"] = !round.EndedOnNatural);
    }

    [Fact]
    public void DragonTiger_saved_cards_replay_and_tampered_wager_is_flagged()
    {
        var wagers = new DragonTigerWagers(100, 0, 50);
        var round = new DragonTigerRound(new(CardRank.Queen, CardSuit.Clubs), new(CardRank.Four, CardSuit.Hearts));
        var payout = DragonTigerRules.Settle(wagers, round).Sum(settlement => settlement.TotalReturnCents);
        var saved = Capture("dragon-tiger", "dragon-round", wagers.TotalCents, payout,
            new DragonTigerStoredRound("dragon-round", "alice", wagers, round, 2, 414),
            "dragon-tiger-cards-and-wagers-v1", "dragon-tiger-outcome-v1");
        Check(saved, payload => payload["data"]!["wagers"]!["dragonCents"] = 150L);
    }

    [Fact]
    public void AndarBahar_saved_deal_sequence_replays_and_tampered_side_is_flagged()
    {
        var round = AndarBaharRules.Deal(StandardDeck.Create());
        const long stake = 100;
        var payout = AndarBaharRules.Settle(AndarBaharSide.Andar, stake, round).TotalReturnCents;
        var saved = Capture("andar-bahar", "andar-round", stake, payout,
            new AndarBaharStoredRound("andar-round", "alice", "test-idempotency-key", AndarBaharSide.Andar,
                stake, round, 1000, 1000 + payout, new DateTimeOffset(Now)),
            "andar-bahar-deal-sequence-v1", "andar-bahar-outcome-v1");
        Check(saved, payload => payload["data"]!["round"]!["deals"]![0]!["side"] = (int)AndarBaharSide.Bahar);
    }

    private static RoundAuditCapture Capture(string game, string round, long wager, long payout, object data,
        string evidenceKind, string version = "server-settlement-receipt-v1") =>
        SinglePlayerRoundRecord.Create(game, round, "alice", wager, payout, Now, Now,
            "source", round, data, evidenceKind, true, version).Audit;

    private static void Check(RoundAuditCapture saved, Action<JsonNode> tamper)
    {
        Assert.True(PaidCardEvidenceReplayAudit.Supports(saved.Summary));
        var clean = PaidCardEvidenceReplayAudit.Inspect(saved.Summary, saved.PayloadJson);
        Assert.True(clean.Count == 0,
            $"{saved.Summary.GameId}: {string.Join(", ", clean.Select(finding => finding.Code))}");
        var changed = JsonNode.Parse(saved.PayloadJson)!;
        tamper(changed);
        var findings = PaidCardEvidenceReplayAudit.Inspect(saved.Summary, changed.ToJsonString());
        Assert.Contains(findings, finding => finding.Severity == "error");
    }
}
