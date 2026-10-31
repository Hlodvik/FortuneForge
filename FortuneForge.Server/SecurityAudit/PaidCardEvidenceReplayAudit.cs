using System.Collections.Immutable;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.AndarBahar;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.HiLo;
using FortuneForge.Games.HouseHoldem;
using FortuneForge.Games.VideoPoker;
using FortuneForge.Server.Cards.Baccarat;
using FortuneForge.Server.Cards.HouseHoldem;
using FortuneForge.Server.Cards.VideoPoker;
using FortuneForge.Server.RoundAudit;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Recomputes paid card outcomes from the private evidence saved at settlement.</summary>
internal static class PaidCardEvidenceReplayAudit
{
    internal static IReadOnlyList<RoundAuditFinding> Inspect(RoundAuditSummary summary, string payloadJson)
    {
        if (!Supports(summary)) return [];
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var data = root.GetProperty("data");
            var wager = root.GetProperty("wagerCents").GetInt64();
            var payout = root.GetProperty("payoutCents").GetInt64();
            var findings = new List<RoundAuditFinding>();
            if (root.GetProperty("netCents").GetInt64() != checked(payout - wager) ||
                summary.Participants.Count != 1 ||
                summary.Participants[0].WagerCents != wager ||
                summary.Participants[0].PayoutCents != payout ||
                summary.Participants[0].NetCents != checked(payout - wager))
                findings.Add(new("paid-card-receipt-mismatch", "error",
                    "The saved wager or return differs from its audit summary."));

            var valid = summary.GameId switch
            {
                "hi-lo" => HiLo(summary, data, wager, payout),
                "casino-war" => CasinoWar(summary, data, wager, payout),
                "video-poker" => VideoPoker(summary, data, wager, payout),
                "house-holdem" => HouseHoldem(summary, data, wager, payout),
                "baccarat" => Baccarat(summary, data, wager, payout),
                "dragon-tiger" => DragonTiger(summary, data, wager, payout),
                "andar-bahar" => AndarBahar(summary, data, wager, payout),
                _ => true
            };
            if (!valid)
                findings.Add(new("paid-card-outcome-mismatch", "error",
                    "The saved cards, actions, or payout do not replay to the recorded outcome."));
            return findings;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            ArgumentException or OverflowException or IndexOutOfRangeException or KeyNotFoundException or
            DivideByZeroException or NullReferenceException or NotSupportedException or FormatException)
        {
            return [new("paid-card-evidence-invalid", "error",
                "The saved private card evidence cannot be parsed for independent outcome review.")];
        }
    }

    internal static bool Supports(RoundAuditSummary summary) =>
        (summary.GameId, summary.EvidenceKind, summary.AuditorVersion) switch
    {
        ("hi-lo", "hi-lo-revealed-cards-and-choices-v1", "server-settlement-receipt-v1") => true,
        ("casino-war", "casino-war-cards-and-decision-v1", "server-settlement-receipt-v1") => true,
        ("video-poker", "video-poker-deal-holds-and-draw-v1", "server-settlement-receipt-v1") => true,
        ("house-holdem", "house-holdem-cards-and-actions-v1", "server-settlement-receipt-v1") => true,
        ("baccarat", "baccarat-deal-and-side-bets-v1", BaccaratOutcomeAudit.Version) => true,
        ("dragon-tiger", "dragon-tiger-cards-and-wagers-v1", DragonTigerOutcomeAudit.Version) => true,
        ("andar-bahar", "andar-bahar-deal-sequence-v1", AndarBaharOutcomeAudit.Version) => true,
        _ => false
    };

    private static bool HiLo(RoundAuditSummary summary, JsonElement data, long wager, long payout)
    {
        var round = data.Deserialize<HiLoRound>(RoundAuditCapture.Json) ?? throw new JsonException("Missing Hi-Lo round.");
        if (round.Id.ToString("N") != summary.RoundId || round.Status == HiLoRoundStatus.Active ||
            round.History.IsDefaultOrEmpty || round.History.Length > 153 ||
            round.History[0].Kind != HiLoHistoryKind.Initial ||
            round.Wager * 100 != wager || round.CurrentReturn * 100 != payout) return false;
        var rebuilt = HiLoEngine.Start(round.Id, round.Wager, round.History[0].Card);
        foreach (var entry in round.History.Skip(1))
            rebuilt = entry.Kind switch
            {
                HiLoHistoryKind.Skip => HiLoEngine.Skip(rebuilt, entry.Card),
                HiLoHistoryKind.Win or HiLoHistoryKind.Loss when entry.Direction is { } direction =>
                    HiLoEngine.Guess(rebuilt, direction, entry.Card),
                _ => throw new InvalidOperationException("Invalid Hi-Lo action history.")
            };
        if (round.Status == HiLoRoundStatus.CashedOut) rebuilt = HiLoEngine.CashOut(rebuilt);
        return EqualSaved(data, rebuilt);
    }

    private static bool CasinoWar(RoundAuditSummary summary, JsonElement data, long wager, long payout)
    {
        if (!Identity(summary, data)) return false;
        var deck = data.GetProperty("deck").Deserialize<PlayingCard[]>(RoundAuditCapture.Json)
            ?? throw new JsonException("Missing Casino War deck.");
        if (deck.Length != 10 || deck.Any(card => !ValidCard(card))) return false;
        var savedRound = data.GetProperty("round");
        var stake = savedRound.GetProperty("primaryStake").GetDecimal();
        var rebuilt = CasinoWarRoundEngine.Start(deck, stake);
        if (rebuilt.Phase == CasinoWarRoundPhase.AwaitingTieDecision)
        {
            var decision = savedRound.GetProperty("tieDecision").Deserialize<CasinoWarTieDecision?>(RoundAuditCapture.Json);
            if (decision is null) return false;
            rebuilt = CasinoWarRoundEngine.Decide(rebuilt, decision.Value);
        }
        if (!EqualSaved(savedRound, rebuilt) || rebuilt.Settlement is null) return false;
        var tie = data.GetProperty("tieSettlement").ValueKind == JsonValueKind.Null ? null :
            data.GetProperty("tieSettlement").Deserialize<CasinoWarTieBetSettlement>(RoundAuditCapture.Json);
        if (tie is not null && !EqualSaved(data.GetProperty("tieSettlement"),
                CasinoWarTieBetPaytable.Settle(rebuilt.Opening, tie.Stake))) return false;
        var extra = rebuilt.TieDecision == CasinoWarTieDecision.GoToWar ? stake : 0m;
        return (stake + (tie?.Stake ?? 0m) + extra) * 100 == wager &&
            (rebuilt.Settlement.TotalReturn + (tie?.TotalReturn ?? 0m)) * 100 == payout;
    }

    private static bool VideoPoker(RoundAuditSummary summary, JsonElement data, long wager, long payout)
    {
        var round = ReadVideoPoker(data);
        if (round.Status != VideoPokerRoundStatus.Completed || round.HandCount is not (1 or 3 or 5) ||
            round.CoinsWagered is < 1 or > 5 || round.Results.Length != round.HandCount ||
            round.Draws.Length != round.HandCount || round.RemainingDecks.Length != round.HandCount ||
            round.HeldCardPositions is null || wager != checked(round.CoinsWagered * round.HandCount * VideoPokerMoney.CoinValueCents) ||
            payout != checked(round.Results.Sum(result => result.PaytableOutcome.CreditsWon) * VideoPokerMoney.CoinValueCents))
            return false;
        var standard = StandardDeck.Create().ToHashSet();
        var initial = round.InitialDeal.Cards;
        if (initial.Length != 5 || !initial.All(standard.Contains) || initial.Distinct().Count() != 5) return false;
        var held = round.HeldCardPositions.Positions.Select(position => (int)position).ToHashSet();
        if (held.Any(position => position is < 0 or > 4)) return false;
        for (var hand = 0; hand < round.HandCount; hand++)
        {
            var draw = round.Draws[hand];
            var result = round.Results[hand];
            var final = draw.FinalHand.Cards;
            if (!draw.InitialDeal.Cards.SequenceEqual(initial) ||
                !draw.HeldCardPositions.Positions.SequenceEqual(round.HeldCardPositions.Positions) ||
                !result.FinalHand.Cards.SequenceEqual(final) ||
                held.Any(index => initial[index] != final[index]) ||
                !EqualSaved(JsonSerializer.SerializeToElement(result, RoundAuditCapture.Json),
                    new VideoPokerHandResult(draw.FinalHand, VideoPokerHandEvaluator.Evaluate(draw.FinalHand),
                        FullPayJacksOrBetterPaytable.Evaluate(draw.FinalHand, round.CoinsWagered)))) return false;
            var deck = initial.Concat(final.Where((_, index) => !held.Contains(index)))
                .Concat(round.RemainingDecks[hand]).ToArray();
            if (deck.Length != 52 || !deck.ToHashSet().SetEquals(standard)) return false;
        }
        return summary.RoundId.Length > 0 && EqualSaved(data, round);
    }

    private static bool HouseHoldem(RoundAuditSummary summary, JsonElement data, long wager, long payout)
    {
        var saved = data.GetProperty("round");
        var round = saved.Deserialize<HouseHoldemRound>(RoundAuditCapture.Json) ?? throw new JsonException("Missing House Hold'em round.");
        var steps = data.GetProperty("steps").Deserialize<string[]>(RoundAuditCapture.Json)
            ?? throw new JsonException("Missing House Hold'em actions.");
        var cards = round.PlayerCards.Concat(round.DealerCards).Concat(round.CommunityCards).ToArray();
        if (cards.Length != 9 || cards.Select(CardCode.Parse).Distinct().Count() != 9 ||
            steps.Length is < 1 or > 5) return false;
        var rebuilt = new HouseHoldemRound
        {
            PlayerCards = round.PlayerCards.ToArray(), DealerCards = round.DealerCards.ToArray(),
            CommunityCards = round.CommunityCards.ToArray(), AnteCents = round.AnteCents,
            BlindCents = round.BlindCents, TripsCents = round.TripsCents
        };
        if (round.BlindCents != round.AnteCents) return false;
        foreach (var step in steps)
        {
            if (step == HouseHoldemCommands.Advance) HouseHoldemRules.Advance(rebuilt);
            else HouseHoldemRules.ApplyAction(rebuilt, step);
        }
        return rebuilt.Settlement is not null && EqualSaved(saved, rebuilt) &&
            rebuilt.TotalWagerCents == wager && rebuilt.PayoutCents == payout;
    }

    private static bool Baccarat(RoundAuditSummary summary, JsonElement data, long wager, long payout)
    {
        var savedRound = data.GetProperty("round");
        var round = new PuntoBancoRoundResult(
            new BaccaratHand(Cards(savedRound.GetProperty("playerHand").GetProperty("cards"))),
            new BaccaratHand(Cards(savedRound.GetProperty("bankerHand").GetProperty("cards"))),
            savedRound.GetProperty("outcome").Deserialize<BaccaratRoundOutcome>(RoundAuditCapture.Json),
            savedRound.GetProperty("endedOnNatural").GetBoolean(),
            Cards(savedRound.GetProperty("consumedCards")).ToImmutableArray());
        var stored = new BaccaratStoreRound(
            data.GetProperty("roundId").GetString() ?? "", data.GetProperty("userId").GetString() ?? "",
            data.GetProperty("betSide").Deserialize<BaccaratBetSide>(RoundAuditCapture.Json), round,
            data.GetProperty("settlement").Deserialize<BaccaratBetSettlement>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing Baccarat settlement."),
            data.GetProperty("playerPairSettlement").Deserialize<BaccaratPairSettlement>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing player pair settlement."),
            data.GetProperty("bankerPairSettlement").Deserialize<BaccaratPairSettlement>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing banker pair settlement."),
            data.GetProperty("shoeCardsUsed").GetInt32(), data.GetProperty("shoeCardsRemaining").GetInt32());
        return stored.RoundId == summary.RoundId && stored.UserId == summary.Participants[0].ActorId &&
            EqualSaved(data, stored) && BaccaratOutcomeAudit.Verify(stored, wager, payout);
    }

    private static bool DragonTiger(RoundAuditSummary summary, JsonElement data, long wager, long payout)
    {
        var stored = data.Deserialize<DragonTigerStoredRound>(RoundAuditCapture.Json) ?? throw new JsonException("Missing Dragon Tiger round.");
        return stored.RoundId == summary.RoundId && stored.UserId == summary.Participants[0].ActorId &&
            DragonTigerOutcomeAudit.Verify(stored, wager, payout);
    }

    private static bool AndarBahar(RoundAuditSummary summary, JsonElement data, long wager, long payout)
    {
        if (!Identity(summary, data)) return false;
        var saved = data.GetProperty("round");
        var joker = saved.GetProperty("joker").Deserialize<PlayingCard>(RoundAuditCapture.Json);
        var deals = saved.GetProperty("deals").EnumerateArray()
            .Select(deal => deal.GetProperty("card").Deserialize<PlayingCard>(RoundAuditCapture.Json)).ToArray();
        var prefix = new[] { joker }.Concat(deals).ToArray();
        var standard = StandardDeck.Create();
        if (prefix.Length is < 2 or > 50 || prefix.Distinct().Count() != prefix.Length ||
            prefix.Any(card => !standard.Contains(card))) return false;
        var fullDeck = prefix.Concat(standard.Where(card => !prefix.Contains(card))).ToArray();
        var rebuilt = AndarBaharRules.Deal(fullDeck);
        var side = data.GetProperty("side").Deserialize<AndarBaharSide>(RoundAuditCapture.Json);
        var stake = data.GetProperty("stakeCents").GetInt64();
        var afterWager = data.GetProperty("balanceAfterWagerCents").GetInt64();
        var afterSettlement = data.GetProperty("balanceAfterSettlementCents").GetInt64();
        var stored = new AndarBaharStoredRound(summary.RoundId, summary.Participants[0].ActorId,
            data.GetProperty("idempotencyKey").GetString() ?? "", side, stake, rebuilt,
            afterWager, afterSettlement, data.GetProperty("createdAtUtc").GetDateTimeOffset());
        return EqualSaved(saved, rebuilt) && stake == wager &&
            AndarBaharOutcomeAudit.Verify(stored, payout);
    }

    private static bool Identity(RoundAuditSummary summary, JsonElement data) =>
        data.GetProperty("roundId").GetString() == summary.RoundId &&
        data.GetProperty("userId").GetString() == summary.Participants[0].ActorId;

    private static bool ValidCard(PlayingCard card) => Enum.IsDefined(card.Rank) && Enum.IsDefined(card.Suit);

    private static PlayingCard[] Cards(JsonElement value) =>
        value.Deserialize<PlayingCard[]>(RoundAuditCapture.Json) ?? throw new JsonException("Missing cards.");

    private static VideoPokerDeal Deal(JsonElement value) => new(Cards(value.GetProperty("cards")));

    private static VideoPokerHeldCardPositions Holds(JsonElement value) => new(
        value.GetProperty("positions").Deserialize<VideoPokerCardPosition[]>(RoundAuditCapture.Json)
            ?? throw new JsonException("Missing held positions."));

    private static VideoPokerRound ReadVideoPoker(JsonElement data)
    {
        var initial = Deal(data.GetProperty("initialDeal"));
        var remaining = data.GetProperty("remainingDecks").EnumerateArray()
            .Select(deck => Cards(deck).ToImmutableArray()).ToImmutableArray();
        var held = data.GetProperty("heldCardPositions").ValueKind == JsonValueKind.Null
            ? null : Holds(data.GetProperty("heldCardPositions"));
        var draws = data.GetProperty("draws").EnumerateArray().Select(draw => new VideoPokerDraw(
            Deal(draw.GetProperty("initialDeal")), Holds(draw.GetProperty("heldCardPositions")),
            Deal(draw.GetProperty("finalHand")))).ToImmutableArray();
        var results = data.GetProperty("results").EnumerateArray().Select(result => new VideoPokerHandResult(
            Deal(result.GetProperty("finalHand")),
            result.GetProperty("handRank").Deserialize<VideoPokerHandRank>(RoundAuditCapture.Json),
            result.GetProperty("paytableOutcome").Deserialize<VideoPokerPaytableOutcome>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing Video Poker paytable outcome."))).ToImmutableArray();
        return new(initial, remaining, data.GetProperty("coinsWagered").GetInt32(),
            data.GetProperty("handCount").GetInt32(),
            data.GetProperty("status").Deserialize<VideoPokerRoundStatus>(RoundAuditCapture.Json),
            held, draws, results);
    }

    private static bool EqualSaved<T>(JsonElement saved, T replayed) =>
        JsonElement.DeepEquals(saved, JsonSerializer.SerializeToElement(replayed, RoundAuditCapture.Json));
}
