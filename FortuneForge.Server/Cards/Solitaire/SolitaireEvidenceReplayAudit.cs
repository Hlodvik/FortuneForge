using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Solitaire;

namespace FortuneForge.Server.Cards.Solitaire;

/// <summary>Rechecks a paid Solitaire match from private final boards and the committed state timeline.</summary>
internal static class SolitaireEvidenceReplayAudit
{
    private const string HouseFunded = "house-funded-bot-pool-v1";
    private static readonly JsonSerializerOptions Json = RoundAuditCapture.Json;

    internal static IReadOnlyList<RoundAuditFinding> Inspect(
        RoundAuditSummary summary, string? payloadJson, IReadOnlyList<string> eventPayloads)
    {
        if (summary.GameId != "solitaire" || summary.AuditorVersion != CompetitiveSolitaireRoundAudit.Version) return [];
        if (payloadJson is null) return Error("solitaire-final-evidence-missing", "The private settled match is missing.");
        try
        {
            using var terminal = JsonDocument.Parse(payloadJson);
            var root = terminal.RootElement;
            var match = Read<SolitaireMatch>(root.GetProperty("match"));
            var players = Read<SolitairePlayerState[]>(root.GetProperty("players"));
            var findings = new List<RoundAuditFinding>();
            if (summary.EvidenceKind != "paid-solitaire-match-v1" ||
                root.GetProperty("sourceCollection").GetString() != "solitaireMatches" ||
                root.GetProperty("sourceDocumentId").GetString() != summary.RoundId ||
                root.GetProperty("financialClassification").GetString() != match.FinancialModel ||
                match.MatchId != summary.RoundId || match.Status != "settled" ||
                match.CompletedAtUtc != summary.CompletedAtUtc || match.StartedAtUtc != summary.StartedAtUtc ||
                match.PlayerIds.Count != players.Length || players.Length is < 1 or > 8 ||
                players.Select(player => player.UserId).Distinct(StringComparer.Ordinal).Count() != players.Length ||
                players.Any(player => player.MatchId != match.MatchId || player.Seat is < 1 or > 8 ||
                    player.Game.Seed != match.DealSeed || player.Game.DrawCount != match.DrawCount) ||
                !players.Select(player => player.UserId).SequenceEqual(match.PlayerIds))
                return Error("solitaire-final-classification-mismatch", "The match identity, seats, final status or private board terms differ from the audit summary.");
            if (players.Any(player => !ValidBoard(player.Game)))
                findings.Add(new("solitaire-board-invalid", "error", "A final private Solitaire board does not contain one legal deck and pile arrangement."));
            CheckSettlement(summary, root, match, players, findings);

            var observations = new List<Observation>();
            foreach (var payload in eventPayloads)
            {
                using var document = JsonDocument.Parse(payload);
                var envelope = document.RootElement;
                var state = envelope.GetProperty("state");
                observations.Add(new(envelope.GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime(),
                    envelope.GetProperty("eventKind").GetString() ?? "",
                    Read<SolitaireMatch>(state.GetProperty("match")),
                    Read<SolitairePlayerState[]>(state.GetProperty("players")),
                    ReadRequest(envelope)));
            }
            if (observations.Count == 0)
                return findings.Concat(Error("solitaire-journal-missing", "No private match state observations were saved.")).ToArray();
            observations.Sort((a, b) =>
            {
                var time = a.At.CompareTo(b.At);
                return time != 0 ? time : EventOrder(a.Kind).CompareTo(EventOrder(b.Kind));
            });
            var latest = new Dictionary<string, SolitairePlayerState>(StringComparer.Ordinal);
            var firstSeen = new HashSet<string>(StringComparer.Ordinal);
            var requestCoverageMissing = false;
            foreach (var observed in observations)
            {
                if (observed.Match.MatchId != match.MatchId || observed.Players.Length > players.Length ||
                    observed.Players.Select(player => player.UserId).Distinct(StringComparer.Ordinal).Count() != observed.Players.Length)
                {
                    findings.Add(new("solitaire-journal-identity-mismatch", "error", "An intermediate match observation has different identity or duplicate seats."));
                    continue;
                }
                foreach (var player in observed.Players)
                {
                    if (player.MatchId != match.MatchId || player.Game.Seed != match.DealSeed ||
                        player.Game.DrawCount != match.DrawCount || !ValidBoard(player.Game))
                    {
                        findings.Add(new("solitaire-journal-board-invalid", "error", "An intermediate Solitaire board or deal term is invalid."));
                        continue;
                    }
                    if (firstSeen.Add(player.UserId))
                    {
                        var dealt = SolitaireEngine.CreateGame(player.Game.Seed, player.Game.DrawCount);
                        if (player.Version != 1 || !SameBoardScore(dealt, player.Game))
                            findings.Add(new("solitaire-initial-deal-mismatch", "error", "A seat's first saved board does not match its frozen deal seed."));
                    }
                    else if (latest.TryGetValue(player.UserId, out var previous))
                    {
                        if (player.Version < previous.Version ||
                            player.Version == previous.Version && !SameBoardScore(previous.Game, player.Game))
                            findings.Add(new("solitaire-version-state-mismatch", "error", "A Solitaire seat regressed or changed its board without a version advance."));
                        else if (player.Version > previous.Version && player.IsAccountBacked)
                        {
                            if (player.Version != previous.Version + 1)
                                findings.Add(new("solitaire-human-version-gap", "error", "A human Solitaire seat skipped an accepted state version."));
                            else if (observed.Kind == "human-command")
                            {
                                if (observed.Request is { } request && request.UserId != player.UserId)
                                    findings.Add(new("solitaire-command-actor-mismatch", "error",
                                        "A human command advanced a different player's Solitaire seat."));
                                else if (observed.Request is { Command: { } command })
                                {
                                    if (!ValidCommandTransition(previous, player, command))
                                        findings.Add(new("solitaire-command-replay-mismatch", "error", "An accepted human Solitaire command does not legally produce the saved board."));
                                }
                                else if (!SameBoardScore(previous.Game, player.Game))
                                    requestCoverageMissing = true;
                            }
                            else if (observed.Kind != "human-command" && !SameCardLayout(previous.Game, player.Game))
                                findings.Add(new("solitaire-unattributed-board-change", "error", "A human board changed outside an accepted command observation."));
                        }
                    }
                    latest[player.UserId] = player;
                }
            }
            var settlement = observations.LastOrDefault(value => value.Kind == "settlement");
            if (settlement is null || settlement.Match.Status != "settled" ||
                settlement.Players.Length != players.Length ||
                players.Any(player => !settlement.Players.Any(saved => saved.UserId == player.UserId &&
                    saved.Version == player.Version && saved.Status == player.Status &&
                    saved.PayoutCents == player.PayoutCents && SameBoardScore(saved.Game, player.Game))))
                findings.Add(new("solitaire-settlement-observation-mismatch", "error", "The committed settlement observation differs from the private final match."));
            if (requestCoverageMissing)
                findings.Add(new("solitaire-command-request-unavailable", "warning", "Some accepted board changes lack a retained sanitized command, so their exact input cannot be replayed."));
            return findings;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or ArithmeticException or FormatException or OverflowException or IndexOutOfRangeException or NotSupportedException)
        {
            return Error("solitaire-replay-evidence-invalid", "The private Solitaire final state or journal cannot be parsed for database replay.");
        }
    }

    private static void CheckSettlement(RoundAuditSummary summary, JsonElement root, SolitaireMatch match,
        SolitairePlayerState[] players, List<RoundAuditFinding> findings)
    {
        var ranking = players.OrderByDescending(player => player.Game.Score)
            .ThenBy(player => player.ElapsedMilliseconds ?? long.MaxValue)
            .ThenBy(player => player.Game.Moves)
            .ThenBy(player => player.CompletedAtUtc ?? DateTime.MaxValue)
            .ThenBy(player => player.UserId, StringComparer.Ordinal).Select(player => player.UserId).ToArray();
        var savedRanking = root.GetProperty("ranking").EnumerateArray().Select(value => value.GetString()).ToArray();
        var winner = match.FinancialModel == HouseFunded ? ranking.FirstOrDefault() :
            ranking.FirstOrDefault(id => players.First(player => player.UserId == id).IsAccountBacked);
        if (!ranking.SequenceEqual(savedRanking) || match.WinnerUserId != winner ||
            players.Any(player => player.PayoutCents != (player.UserId == winner ? match.WinnerPayoutCents : 0)) ||
            match.PrizePoolCents != checked(match.WinnerPayoutCents + match.PlatformFeeCents) ||
            match.FinancialModel == HouseFunded &&
            (match.HumanEntryCents != checked(players.Count(player => player.IsAccountBacked) * match.BuyInCents) ||
             match.HouseFundedEntryCents != checked(players.Count(player => !player.IsAccountBacked) * match.BuyInCents)))
            findings.Add(new("solitaire-settlement-replay-mismatch", "error", "Rank, winner, participant entitlements or the prize pool do not reconcile."));
        if (summary.Participants.Count != players.Length || players.Any(player =>
            !summary.Participants.Any(participant => participant.ActorId == player.UserId &&
                participant.IsAccountBacked == player.IsAccountBacked &&
                participant.WagerCents == (player.IsAccountBacked ? match.BuyInCents : 0) &&
                participant.PayoutCents == player.PayoutCents &&
                participant.NetCents == player.PayoutCents - (player.IsAccountBacked ? match.BuyInCents : 0))))
            findings.Add(new("solitaire-participant-money-mismatch", "error", "The private seats do not reconcile to every audit participant's entry and entitlement."));
    }

    private static bool ValidBoard(SolitaireGameState game)
    {
        if (game.DrawCount is not (1 or 3) || game.Score < 0 || game.Moves < 0 ||
            game.Foundations.Count != 4 || game.Tableau.Count != 7) return false;
        var cards = game.Stock.Concat(game.Waste).Concat(game.Foundations.SelectMany(pile => pile))
            .Concat(game.Tableau.SelectMany(pile => pile)).ToArray();
        if (cards.Length != 52 || cards.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count() != 52 ||
            cards.Any(card => card.Suit is not ("clubs" or "diamonds" or "hearts" or "spades") ||
                card.Rank is < 1 or > 13 || card.Id != $"{card.Suit}-{card.Rank}")) return false;
        if (game.Stock.Any(card => card.FaceUp) || game.Waste.Any(card => !card.FaceUp)) return false;
        foreach (var foundation in game.Foundations)
            for (var index = 0; index < foundation.Count; index++)
                if (!foundation[index].FaceUp || foundation[index].Rank != index + 1 ||
                    foundation[index].Suit != foundation[0].Suit) return false;
        foreach (var tableau in game.Tableau)
        {
            var faceUp = -1;
            for (var index = 0; index < tableau.Count; index++)
                if (tableau[index].FaceUp) { faceUp = index; break; }
            if (faceUp < 0) continue;
            if (tableau.Skip(faceUp).Any(card => !card.FaceUp)) return false;
            for (var index = faceUp; index < tableau.Count - 1; index++)
                if (tableau[index].Rank != tableau[index + 1].Rank + 1 ||
                    IsRed(tableau[index].Suit) == IsRed(tableau[index + 1].Suit)) return false;
        }
        return true;
    }

    private static bool ValidCommandTransition(SolitairePlayerState before, SolitairePlayerState after,
        SolitaireCommandRequest command)
    {
        var type = command.Type?.Trim().ToLowerInvariant();
        if (command.ExpectedVersion != before.Version)
            return SameBoardScore(before.Game, after.Game) && after.IntegrityWarnings.Count > before.IntegrityWarnings.Count;
        if (type is SolitaireCommandTypes.Draw or SolitaireCommandTypes.Flip or SolitaireCommandTypes.Move)
        {
            try
            {
                var calculated = SolitaireEngine.Apply(before.Game, command);
                if (SolitaireEngine.IsWon(calculated) && after.ElapsedMilliseconds is { } elapsed)
                    calculated = calculated with { Score = SolitaireScoreRules.FinalScore(calculated, elapsed) };
                return SameBoardScore(calculated, after.Game) &&
                    after.UndoHistory.Count == Math.Min(10, before.UndoHistory.Count + 1) &&
                    SameBoardScore(after.UndoHistory[^1], before.Game);
            }
            catch (SolitaireIllegalMoveException)
            {
                return SameBoardScore(before.Game, after.Game) && after.IntegrityWarnings.Count > before.IntegrityWarnings.Count;
            }
        }
        if (type == SolitaireCommandTypes.Undo)
            return before.UndoHistory.Count > 0 && SameBoardScore(before.UndoHistory[^1], after.Game) &&
                after.UndoHistory.Count == before.UndoHistory.Count - 1;
        if (type == SolitaireCommandTypes.IntegrityFailure)
            return SameCardLayout(before.Game, after.Game) && after.Game.Score == 0 &&
                after.Status == SolitairePlayerStatuses.IntegrityFailed;
        if (type == SolitaireCommandTypes.Submit)
            return SameBoardScore(before.Game, after.Game) && after.Status == SolitairePlayerStatuses.Finished;
        if (type is SolitaireCommandTypes.Pause or SolitaireCommandTypes.Resume or SolitaireCommandTypes.AcknowledgeWarning)
            return SameBoardScore(before.Game, after.Game);
        return false;
    }

    private static bool SameBoardScore(SolitaireGameState a, SolitaireGameState b) =>
        a.Score == b.Score && a.Moves == b.Moves && SameCardLayout(a, b);

    private static bool SameCardLayout(SolitaireGameState a, SolitaireGameState b) =>
        a.Seed == b.Seed && a.DrawCount == b.DrawCount &&
        Cards(a.Stock, b.Stock) && Cards(a.Waste, b.Waste) &&
        a.Foundations.Count == b.Foundations.Count &&
        a.Foundations.Zip(b.Foundations).All(pair => Cards(pair.First, pair.Second)) &&
        a.Tableau.Count == b.Tableau.Count &&
        a.Tableau.Zip(b.Tableau).All(pair => Cards(pair.First, pair.Second));

    private static bool Cards(IReadOnlyList<SolitaireCard> a, IReadOnlyList<SolitaireCard> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => pair.First == pair.Second);

    private static bool IsRed(string suit) => suit is "diamonds" or "hearts";

    private static (string UserId, SolitaireCommandRequest? Command)? ReadRequest(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("request", out var request) || request.ValueKind != JsonValueKind.Object) return null;
        var user = request.GetProperty("userId").GetString() ?? "";
        if (!request.TryGetProperty("requestJson", out var json) || json.ValueKind != JsonValueKind.String) return (user, null);
        var body = json.GetString();
        if (body is null) return (user, null);
        try
        {
            var command = JsonSerializer.Deserialize<SolitaireCommandRequest>(body, Json);
            return (user, string.IsNullOrWhiteSpace(command?.Type) ? null : command);
        }
        catch (JsonException) { return (user, null); }
    }

    private static int EventOrder(string kind) => kind switch
    {
        "human-entry" or "managed-seat-admitted" or "managed-match-start" => 0,
        "settlement" => 9,
        "payout-claimed" => 10,
        _ => 5
    };

    private static T Read<T>(JsonElement json) => JsonSerializer.Deserialize<T>(json.GetRawText(), Json) ??
        throw new JsonException("The Solitaire private state is missing.");

    private static IReadOnlyList<RoundAuditFinding> Error(string code, string message) => [new(code, "error", message)];

    private sealed record Observation(DateTime At, string Kind, SolitaireMatch Match,
        SolitairePlayerState[] Players, (string UserId, SolitaireCommandRequest? Command)? Request);
}
