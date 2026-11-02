using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Independent terminal deal and return checks; command legality still needs a full trace.</summary>
internal static class TableOutcomeReplayAudit
{
    internal const string Version = "table-outcome-replay-v1";

    internal static IReadOnlyList<RoundAuditFinding> Inspect(RoundAuditSummary summary, string payloadJson)
    {
        var threeCard = summary.GameId == "three-card-poker" && summary.EvidenceKind == "three-card-poker-final-cards-v1";
        var blackjackTable = summary.GameId == "blackjack" && summary.EvidenceKind == "blackjack-final-cards-v1";
        var blackjackLegacy = summary.GameId == "blackjack" && summary.EvidenceKind == "legacy-blackjack-cards-and-final-action-v1";
        if (!threeCard && !blackjackTable && !blackjackLegacy) return [];
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return threeCard ? CheckThreeCardPoker(summary, document.RootElement) :
                blackjackTable ? CheckBlackjackTable(summary, document.RootElement) :
                CheckLegacyBlackjack(summary, document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or OverflowException or FormatException or IndexOutOfRangeException)
        {
            return [new(threeCard ? "three-card-poker-evidence-invalid" : "blackjack-evidence-invalid", "error",
                "The private card snapshot cannot be parsed for independent outcome review.")];
        }
    }

    private static IReadOnlyList<RoundAuditFinding> CheckThreeCardPoker(RoundAuditSummary summary, JsonElement root)
    {
        var findings = new List<RoundAuditFinding>();
        void Flag(string code, string message) => findings.Add(new(code, "error", message));
        if (root.GetProperty("tableId").GetString() != summary.TableId ||
            root.GetProperty("roundNumber").GetInt32() != summary.RoundNumber)
            Flag("three-card-poker-instance-mismatch", "The saved table and round identity differ from the audit summary.");
        if (!root.GetProperty("roundAccountingSettled").GetBoolean())
            Flag("three-card-poker-unsettled", "The saved table was not financially settled.");
        var deck = root.GetProperty("deck").EnumerateArray().Select(card => card.GetString() ?? "").ToArray();
        if (deck.Length != 52 || deck.Distinct(StringComparer.Ordinal).Count() != 52 || deck.Any(card => !ValidCard(card)))
        {
            Flag("three-card-poker-deck-invalid", "The stored table deck is not one complete standard deck.");
            return findings;
        }
        var players = root.GetProperty("players").EnumerateArray().Where(player => Money(player, "wagerCents") > 0)
            .OrderBy(player => player.GetProperty("seat").GetInt32()).ToArray();
        if (players.Length is < 1 or > 5 || players.Select(player => player.GetProperty("seat").GetInt32()).Distinct().Count() != players.Length ||
            players.Select(player => player.GetProperty("actorId").GetString()).Distinct(StringComparer.Ordinal).Count() != players.Length)
        {
            Flag("three-card-poker-seats-invalid", "The saved deal has an invalid set of participating seats.");
            return findings;
        }
        var expectedDraws = 3 * (players.Length + 1);
        if (root.GetProperty("nextCardIndex").GetInt32() != expectedDraws)
            Flag("three-card-poker-draw-count", "The saved deck position does not match the complete three-pass deal.");
        var dealerCards = Cards(root.GetProperty("dealerCards"));
        var expectedDealer = Enumerable.Range(0, 3).Select(pass => deck[pass * (players.Length + 1) + players.Length]).ToArray();
        if (!dealerCards.SequenceEqual(expectedDealer))
            Flag("three-card-poker-dealer-deal", "The dealer cards do not match the recorded deck order.");
        var dealer = Score(dealerCards);
        var qualifies = dealer.Category > 0 || dealer.Kickers[0] >= 12;
        var participants = summary.Participants.ToDictionary(player => player.ActorId, StringComparer.Ordinal);
        if (participants.Count != players.Length)
            Flag("three-card-poker-participants", "The audit summary omits or adds a participating seat.");
        for (var seatIndex = 0; seatIndex < players.Length; seatIndex++)
        {
            var player = players[seatIndex];
            var actor = player.GetProperty("actorId").GetString() ?? "";
            var cards = Cards(player.GetProperty("cards"));
            var expected = Enumerable.Range(0, 3).Select(pass => deck[pass * (players.Length + 1) + seatIndex]).ToArray();
            if (!cards.SequenceEqual(expected))
                Flag("three-card-poker-player-deal", "A player's cards do not match the recorded deck order.");
            var hand = Score(cards);
            var ante = Money(player, "wagerCents");
            var pairPlus = Money(player, "pairPlusCents");
            var play = Money(player, "playWagerCents");
            var committed = Money(player, "totalWagerCents");
            if (ante <= 0 || pairPlus < 0 || play != 0 && play != ante || committed != checked(ante + pairPlus + play))
                Flag("three-card-poker-wager-mismatch", "A player's ante, Pair Plus and Play wagers do not reconcile.");
            var pairOdds = hand.Category switch { 5 => 40, 4 => 30, 3 => 6, 2 => 3, 1 => 1, _ => 0 };
            var anteOdds = hand.Category switch { 5 => 5, 4 => 4, 3 => 1, _ => 0 };
            var pairReturn = pairOdds > 0 ? checked(pairPlus * (pairOdds + 1)) : 0;
            var anteBonus = play > 0 ? checked(ante * anteOdds) : 0;
            var comparison = Compare(hand, dealer);
            var outcome = play == 0 ? "fold" : !qualifies ? "dealer-not-qualified" :
                comparison > 0 ? "player-win" : comparison == 0 ? "push" : "dealer-win";
            var mainReturn = play == 0 ? 0 : !qualifies ? checked(ante * 2 + play) :
                comparison > 0 ? checked((ante + play) * 2) : comparison == 0 ? checked(ante + play) : 0;
            var totalReturn = checked(mainReturn + anteBonus + pairReturn);
            if (Money(player, "pairPlusPayoutCents") != pairReturn ||
                Money(player, "anteBonusPayoutCents") != anteBonus ||
                Money(player, "payoutCents") != totalReturn ||
                player.GetProperty("outcome").GetString() != outcome)
                Flag("three-card-poker-return-mismatch", "A player's outcome or gross return does not replay from the saved cards and wagers.");
            if (!participants.TryGetValue(actor, out var listed) || listed.WagerCents != committed || listed.PayoutCents != totalReturn)
                Flag("three-card-poker-summary-mismatch", "A player's private outcome differs from the audit's settlement summary.");
        }
        return findings;
    }

    private static IReadOnlyList<RoundAuditFinding> CheckBlackjackTable(RoundAuditSummary summary, JsonElement root)
    {
        var findings = new List<RoundAuditFinding>();
        void Flag(string code, string message) => findings.Add(new(code, "error", message));
        if (root.GetProperty("tableId").GetString() != summary.TableId ||
            root.GetProperty("roundNumber").GetInt32() != summary.RoundNumber ||
            !root.GetProperty("roundAccountingSettled").GetBoolean())
            Flag("blackjack-instance-mismatch", "The saved table identity or settlement state differs from the audit summary.");
        var deck = Cards(root.GetProperty("deck"));
        if (!CompleteDeck(deck))
        {
            Flag("blackjack-deck-invalid", "The stored Blackjack table deck is not one complete standard deck.");
            return findings;
        }
        var players = root.GetProperty("players").EnumerateArray().Where(player => Money(player, "wagerCents") > 0)
            .OrderBy(player => player.GetProperty("seat").GetInt32()).ToArray();
        if (players.Length is < 1 or > 5 ||
            players.Select(player => player.GetProperty("seat").GetInt32()).Distinct().Count() != players.Length ||
            players.Select(player => player.GetProperty("actorId").GetString()).Distinct(StringComparer.Ordinal).Count() != players.Length)
        {
            Flag("blackjack-seats-invalid", "The saved table has an invalid set of participating seats.");
            return findings;
        }
        var dealerCards = Cards(root.GetProperty("dealerCards"));
        if (dealerCards.Length < 2)
        {
            Flag("blackjack-hand-invalid", "The saved dealer has fewer than two cards.");
            return findings;
        }
        var drawSequence = new List<string>();
        var hands = new List<(JsonElement Player, string[] Primary, string[]? Secondary)>();
        foreach (var player in players)
        {
            var primary = Cards(player.GetProperty("cards"));
            var secondary = player.GetProperty("secondaryHand").ValueKind == JsonValueKind.Null
                ? null : Cards(player.GetProperty("secondaryHand").GetProperty("cards"));
            if (primary.Length < 2 || secondary is { Length: < 2 })
            {
                Flag("blackjack-hand-invalid", "A participating seat has too few terminal cards.");
                return findings;
            }
            hands.Add((player, primary, secondary));
        }
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var (_, primary, secondary) in hands)
                drawSequence.Add(pass == 0 ? primary[0] : secondary is null ? primary[1] : secondary[0]);
            drawSequence.Add(dealerCards[pass]);
        }
        foreach (var (_, primary, secondary) in hands)
        {
            if (secondary is null) drawSequence.AddRange(primary.Skip(2));
            else
            {
                drawSequence.Add(primary[1]);
                drawSequence.Add(secondary[1]);
                drawSequence.AddRange(primary.Skip(2));
                drawSequence.AddRange(secondary.Skip(2));
            }
        }
        drawSequence.AddRange(dealerCards.Skip(2));
        if (drawSequence.Count > deck.Length ||
            !drawSequence.SequenceEqual(deck.Take(drawSequence.Count)) ||
            root.GetProperty("nextCardIndex").GetInt32() != drawSequence.Count)
            Flag("blackjack-deal-mismatch", "The terminal cards do not replay in deck draw order across seats, splits and dealer draws.");
        var dealer = BlackjackValue(dealerCards);
        var listedParticipants = summary.Participants.ToDictionary(participant => participant.ActorId, StringComparer.Ordinal);
        if (listedParticipants.Count != players.Length)
            Flag("blackjack-participants-mismatch", "The audit summary omits or adds a participating seat.");
        var anyLiveHand = false;
        foreach (var (player, primary, secondary) in hands)
        {
            var actor = player.GetProperty("actorId").GetString() ?? "";
            var baseWager = Money(player, "wagerCents");
            var primaryCommitted = Money(player, "totalWagerCents");
            var insurance = Money(player, "insuranceWagerCents");
            var insuranceReturn = Money(player, "insurancePayoutCents");
            var insuranceAccepted = player.GetProperty("insuranceAccepted");
            var expectedInsurance = insuranceAccepted.ValueKind == JsonValueKind.True ? baseWager / 2 : 0;
            if (baseWager <= 0 || primaryCommitted is not > 0 ||
                primaryCommitted != baseWager && primaryCommitted != checked(baseWager * 2) ||
                primaryCommitted == checked(baseWager * 2) && primary.Length != 3 ||
                insurance != expectedInsurance ||
                dealerCards[0].Split('|')[0] == "A" && insuranceAccepted.ValueKind == JsonValueKind.Null ||
                insuranceAccepted.ValueKind != JsonValueKind.Null && dealerCards[0].Split('|')[0] != "A" ||
                insuranceReturn != (dealer.Natural ? checked(insurance * 3) : 0))
                Flag("blackjack-wager-mismatch", "A table seat's primary or insurance wager does not reconcile.");
            var secondaryHand = secondary is null ? (JsonElement?)null : player.GetProperty("secondaryHand");
            var secondaryCommitted = secondaryHand is { } hand ? Money(hand, "totalWagerCents") : 0;
            if (secondaryHand is { } splitHand &&
                (Money(splitHand, "wagerCents") != baseWager ||
                 secondaryCommitted != baseWager && secondaryCommitted != checked(baseWager * 2) ||
                 secondaryCommitted == checked(baseWager * 2) && secondary!.Length != 3 ||
                 BlackjackSplitValue(primary[0]) != BlackjackSplitValue(secondary![0])))
                Flag("blackjack-split-invalid", "A split hand's original pair or wager is inconsistent.");
            var primaryOutcome = player.GetProperty("outcome").GetString();
            var secondaryOutcome = secondaryHand?.GetProperty("outcome").GetString();
            var primaryExpected = BlackjackTableOutcome(primary, primaryOutcome, dealer, secondary is null);
            var secondaryExpected = secondary is null ? null : BlackjackTableOutcome(secondary, secondaryOutcome, dealer, false);
            if (primaryOutcome != primaryExpected || secondaryOutcome != secondaryExpected)
                Flag("blackjack-outcome-mismatch", "A table hand's outcome does not match its saved cards and dealer hand.");
            if (primaryOutcome is "player-blackjack" or "player-surrender" && primaryCommitted != baseWager)
                Flag("blackjack-wager-mismatch", "A natural or surrendered hand has an impossible doubled wager.");
            anyLiveHand |= primaryOutcome is not ("player-blackjack" or "player-bust" or "player-surrender" or "dealer-blackjack") &&
                !dealer.Natural || secondaryOutcome is not null and not ("player-bust" or "player-surrender" or "dealer-blackjack") && !dealer.Natural;
            var committed = checked(primaryCommitted + secondaryCommitted + insurance);
            var expectedReturn = checked(BlackjackTableReturn(primaryOutcome, primaryCommitted, secondary is null) +
                BlackjackTableReturn(secondaryOutcome, secondaryCommitted, false) + insuranceReturn);
            if (Money(player, "payoutCents") != expectedReturn)
                Flag("blackjack-return-mismatch", "A table seat's gross return does not replay from its outcomes and wagers.");
            if (!listedParticipants.TryGetValue(actor, out var listed) ||
                listed.WagerCents != committed || listed.PayoutCents != expectedReturn)
                Flag("blackjack-summary-mismatch", "A private table settlement differs from its audit summary.");
        }
        if (dealerCards.Length > 2 &&
            (!anyLiveHand || dealer.Score < 17 || BlackjackValue(dealerCards.Take(dealerCards.Length - 1).ToArray()).Score >= 17) ||
            dealerCards.Length == 2 && dealer.Score < 17 && anyLiveHand)
            Flag("blackjack-dealer-draw-invalid", "The saved dealer cards do not follow the dealer draw threshold.");
        return findings;
    }

    private static string BlackjackTableOutcome(string[] cards, string? recorded, (int Score, bool Natural, bool Bust) dealer, bool naturalAllowed)
    {
        var value = BlackjackValue(cards);
        if (recorded == "player-surrender") return naturalAllowed && cards.Length == 2 && !value.Natural && !dealer.Natural
            ? recorded : "invalid-surrender";
        if (dealer.Natural) return value.Natural && naturalAllowed ? "push" : "dealer-blackjack";
        if (value.Natural && naturalAllowed) return "player-blackjack";
        if (value.Bust) return "player-bust";
        return dealer.Bust || value.Score > dealer.Score ? "player-win" :
            value.Score == dealer.Score ? "push" : "dealer-win";
    }

    private static long BlackjackTableReturn(string? outcome, long wager, bool naturalAllowed) => outcome switch
    {
        "player-blackjack" when naturalAllowed => checked(wager * 5 / 2),
        "player-blackjack" or "player-win" => checked(wager * 2),
        "push" => wager,
        "player-surrender" => wager / 2,
        _ => 0
    };

    private static int BlackjackSplitValue(string card) => card.Split('|')[0] switch
    {
        "A" => 11,
        "J" or "Q" or "K" => 10,
        var rank => int.Parse(rank, System.Globalization.CultureInfo.InvariantCulture)
    };

    private static IReadOnlyList<RoundAuditFinding> CheckLegacyBlackjack(RoundAuditSummary summary, JsonElement root)
    {
        var findings = new List<RoundAuditFinding>();
        void Flag(string code, string message) => findings.Add(new(code, "error", message));
        var game = root.GetProperty("data").GetProperty("game");
        var action = root.GetProperty("data").GetProperty("terminalAction").GetString();
        var deck = Cards(game.GetProperty("deck"));
        if (!CompleteDeck(deck))
        {
            Flag("blackjack-deck-invalid", "The stored legacy Blackjack deck is not one complete standard deck.");
            return findings;
        }
        var player = Cards(game.GetProperty("playerCards"));
        var dealer = Cards(game.GetProperty("dealerCards"));
        if (player.Length < 2 || dealer.Length < 2 || player.Length + dealer.Length > deck.Length)
        {
            Flag("blackjack-hand-invalid", "The saved terminal Blackjack hand has an impossible card count.");
            return findings;
        }
        var expectedPlayer = new[] { deck[0], deck[2] }.Concat(deck.Skip(4).Take(player.Length - 2));
        var expectedDealer = new[] { deck[1], deck[3] }.Concat(deck.Skip(4 + player.Length - 2).Take(dealer.Length - 2));
        if (!player.SequenceEqual(expectedPlayer) || !dealer.SequenceEqual(expectedDealer) ||
            game.GetProperty("nextCardIndex").GetInt32() != player.Length + dealer.Length)
            Flag("blackjack-deal-mismatch", "The saved cards or draw position differ from the recorded deck order.");
        var initialPlayer = BlackjackValue(player.Take(2).ToArray());
        var initialDealer = BlackjackValue(dealer.Take(2).ToArray());
        var playerValue = BlackjackValue(player);
        var dealerValue = BlackjackValue(dealer);
        var baseWager = Money(game, "wagerCents");
        var committed = Money(game, "totalWagerCents");
        var payout = Money(game, "payoutCents");
        if (baseWager <= 0 || committed != (action == "double" ? checked(baseWager * 2) : baseWager))
            Flag("blackjack-wager-mismatch", "The terminal wager does not match the recorded base wager and action.");
        if (game.GetProperty("gameId").GetString() != summary.RoundId ||
            game.GetProperty("status").GetString() != "completed" ||
            root.GetProperty("sourceDocumentId").GetString() != summary.RoundId ||
            summary.Participants.Count != 1 || summary.Participants[0].ActorId != game.GetProperty("userId").GetString())
            Flag("blackjack-instance-mismatch", "The private hand identity differs from its terminal audit summary.");
        if (action is not ("deal" or "hit" or "stand" or "double") ||
            action == "deal" && (player.Length != 2 || dealer.Length != 2 || !initialPlayer.Natural && !initialDealer.Natural) ||
            action == "double" && (player.Length != 3 || initialPlayer.Natural || initialDealer.Natural) ||
            action == "hit" && (player.Length < 3 || playerValue.Score < 21 || initialPlayer.Natural || initialDealer.Natural) ||
            action == "stand" && (playerValue.Score >= 21 || initialPlayer.Natural || initialDealer.Natural))
            Flag("blackjack-terminal-action-invalid", "The final action is inconsistent with the saved terminal cards.");
        if (action != "deal" && !playerValue.Bust && dealerValue.Score < 17 ||
            (action == "deal" || playerValue.Bust) && dealer.Length != 2 ||
            dealer.Length > 2 && BlackjackValue(dealer.Take(dealer.Length - 1).ToArray()).Score >= 17)
            Flag("blackjack-dealer-draw-invalid", "The dealer draw sequence does not follow the stored hand state.");
        var outcome = initialPlayer.Natural || initialDealer.Natural
            ? initialPlayer.Natural && initialDealer.Natural ? "push" : initialPlayer.Natural ? "player-blackjack" : "dealer-blackjack"
            : playerValue.Bust ? "player-bust"
            : dealerValue.Bust || playerValue.Score > dealerValue.Score ? "player-win"
            : playerValue.Score == dealerValue.Score ? "push" : "dealer-win";
        var expectedPayout = outcome switch
        {
            "player-blackjack" => checked(committed * 5 / 2),
            "player-win" => checked(committed * 2),
            "push" => committed,
            _ => 0
        };
        if (game.GetProperty("outcome").GetString() != outcome || payout != expectedPayout)
            Flag("blackjack-return-mismatch", "The terminal outcome or gross return does not replay from the saved cards and wager.");
        if (Money(root, "wagerCents") != committed || Money(root, "payoutCents") != payout ||
            Money(root, "netCents") != payout - committed ||
            summary.Participants.Count != 1 || summary.Participants[0].WagerCents != committed ||
            summary.Participants[0].PayoutCents != payout)
            Flag("blackjack-summary-mismatch", "The private terminal hand differs from its settlement summary.");
        return findings;
    }

    private static long Money(JsonElement value, string field) => value.GetProperty(field).GetInt64();
    private static bool CompleteDeck(string[] deck) => deck.Length == 52 &&
        deck.Distinct(StringComparer.Ordinal).Count() == 52 && deck.All(ValidCard);
    private static (int Score, bool Natural, bool Bust) BlackjackValue(IReadOnlyList<string> cards)
    {
        var score = 0;
        var aces = 0;
        foreach (var card in cards)
        {
            if (!ValidCard(card)) throw new JsonException("A Blackjack hand includes an invalid card.");
            var rank = card.Split('|')[0];
            if (rank == "A") { score += 11; aces++; }
            else if (rank is "J" or "Q" or "K") score += 10;
            else score += int.Parse(rank, System.Globalization.CultureInfo.InvariantCulture);
        }
        while (score > 21 && aces > 0) { score -= 10; aces--; }
        return (score, cards.Count == 2 && score == 21, score > 21);
    }
    private static string[] Cards(JsonElement value) => value.EnumerateArray().Select(card => card.GetString() ?? "").ToArray();
    private static bool ValidCard(string card)
    {
        var parts = card.Split('|');
        return parts.Length == 2 && (parts[0] is "A" or "J" or "Q" or "K" ||
            int.TryParse(parts[0], out var rank) && rank is >= 2 and <= 10) &&
            parts[1] is "clubs" or "diamonds" or "hearts" or "spades";
    }
    private static Hand Score(IReadOnlyList<string> cards)
    {
        if (cards.Count != 3 || cards.Distinct(StringComparer.Ordinal).Count() != 3 || cards.Any(card => !ValidCard(card)))
            throw new JsonException("A hand is not three distinct standard cards.");
        var parsed = cards.Select(card => card.Split('|')).ToArray();
        var values = parsed.Select(card => card[0] switch { "A" => 14, "K" => 13, "Q" => 12, "J" => 11,
            var rank => int.Parse(rank, System.Globalization.CultureInfo.InvariantCulture) }).OrderDescending().ToArray();
        var sameSuit = parsed.All(card => card[1] == parsed[0][1]);
        var straightHigh = values.SequenceEqual([14, 3, 2]) ? 3 :
            values[0] == values[1] + 1 && values[1] == values[2] + 1 ? values[0] : 0;
        var groups = values.GroupBy(value => value).OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key).ToArray();
        if (sameSuit && straightHigh > 0) return new(5, [straightHigh]);
        if (groups[0].Count() == 3) return new(4, [groups[0].Key]);
        if (straightHigh > 0) return new(3, [straightHigh]);
        if (sameSuit) return new(2, values);
        if (groups[0].Count() == 2) return new(1, groups.Select(group => group.Key).ToArray());
        return new(0, values);
    }
    private static int Compare(Hand first, Hand second)
    {
        var category = first.Category.CompareTo(second.Category);
        if (category != 0) return category;
        for (var index = 0; index < first.Kickers.Length; index++)
        {
            var result = first.Kickers[index].CompareTo(second.Kickers[index]);
            if (result != 0) return result;
        }
        return 0;
    }
    private sealed record Hand(int Category, int[] Kickers);
}
