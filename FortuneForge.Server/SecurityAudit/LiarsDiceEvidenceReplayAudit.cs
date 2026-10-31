using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Dice;
using FortuneForge.Games.LiarsDice;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Checks each private Liar's Dice observation against an independent bid, challenge and dice-roll replay.</summary>
internal static class LiarsDiceEvidenceReplayAudit
{
    private static readonly JsonSerializerOptions ReplayJson = CreateReplayJson();

    internal static bool Supports(RoundAuditSummary summary) =>
        summary.GameId == "liars-dice" && summary.AuditorVersion == "free-engine-receipt-v1" &&
        summary.EvidenceKind == "liars-dice-private-hands-and-actions-v2";

    internal static IReadOnlyList<RoundAuditFinding> Inspect(
        RoundAuditSummary summary, string? payloadJson, IReadOnlyList<string> eventPayloads)
    {
        if (!Supports(summary)) return [];
        if (payloadJson is null)
            return Error("liars-dice-terminal-evidence-missing", "The completed match has no private terminal evidence.");
        try
        {
            using var terminal = JsonDocument.Parse(payloadJson);
            var saved = terminal.RootElement;
            var final = ReadMatch(saved.GetProperty("state"));
            if (!Classification(summary, saved, final))
                return Error("liars-dice-classification-mismatch", "The match identity, participant, completion time or winner differs from its private evidence.");
            var observations = new List<Observation>(eventPayloads.Count);
            foreach (var payload in eventPayloads)
            {
                using var eventDocument = JsonDocument.Parse(payload);
                var envelope = eventDocument.RootElement;
                var detail = envelope.GetProperty("state");
                observations.Add(new(envelope.GetProperty("version").GetInt64(),
                    envelope.GetProperty("eventKind").GetString() ?? "",
                    envelope.GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime(),
                    detail.GetProperty("roundSeed").GetUInt32(), ReadMatch(detail.GetProperty("state")),
                    detail.Clone(), envelope.GetProperty("participants").EnumerateArray()
                        .Select(value => value.GetString() ?? "").ToArray()));
            }
            observations.Sort((a, b) => a.Version.CompareTo(b.Version));
            if (observations.Count == 0 || !JsonElement.DeepEquals(observations[^1].Payload, saved))
                return Error("liars-dice-terminal-journal-mismatch", "The completed match differs from the last committed engine observation.");
            if (observations[0].Version != 1 || observations[0].Kind != "started" ||
                !InitialMatch(observations[0].Match, observations[0].RoundSeed))
                return Error("liars-dice-initial-deal-mismatch", "The first private dice hands do not reproduce from the saved round seed and participant counts.");
            for (var index = 0; index < observations.Count; index++)
            {
                var current = observations[index];
                if (current.Version != index + 1 ||
                    (index > 0 && current.OccurredAtUtc < observations[index - 1].OccurredAtUtc) ||
                    current.Participants.Length != 1 || current.Participants[0] != summary.Participants[0].ActorId ||
                    current.Payload.GetProperty("id").GetGuid().ToString("N") != summary.RoundId ||
                    !RosterConsistent(current.Payload, current.Match))
                    return Error("liars-dice-journal-sequence-mismatch", "The engine journal has missing versions, reversed times or inconsistent participants.");
                if (index == 0) continue;
                var prior = observations[index - 1];
                if (!Transition(prior.Match, current.Match, prior.RoundSeed, current.RoundSeed, current.Kind))
                    return Error("liars-dice-action-replay-mismatch", "A saved bid, challenge or new round is not a legal transition from the prior private state.");
            }
            var active = final.AllPlayerIds.Where(id => final.DiceCounts[id] > 0).ToArray();
            if (final.Round.Phase != LiarsDiceRoundPhase.Resolved || final.LastOutcome is null ||
                final.Winner is null || active.Length != 1 || active[0] != final.Winner)
                return Error("liars-dice-final-winner-mismatch", "The recorded winner does not follow from the remaining private dice counts.");
            return [new("liars-dice-in-memory-state-journal-non-atomic", "warning",
                "The in-memory match and acknowledged Firestore journal cannot commit atomically; a failed write may leave an accepted action unrecorded."),
                new("liars-dice-bot-decision-policy-not-replayed", "warning",
                    "Recorded managed-player actions are checked for legal transitions, but the bot's internal decision sequence is not independently reproduced."),
                new("liars-dice-seed-provenance-limited", "info",
                "Saved seeds reproduce dice hands, but free-play clients may supply a seed and the journal alone cannot prove unbiased or secret generation.")];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or ArithmeticException or FormatException or OverflowException or IndexOutOfRangeException or NotSupportedException)
        {
            return Error("liars-dice-replay-evidence-invalid", "The private match or journal cannot be parsed for independent action replay.");
        }
    }

    private static LiarsDiceMatchState ReadMatch(JsonElement json) =>
        JsonSerializer.Deserialize<LiarsDiceMatchState>(json, ReplayJson) ??
        throw new JsonException("Missing Liar's Dice state.");

    private static JsonSerializerOptions CreateReplayJson()
    {
        var options = new JsonSerializerOptions(RoundAuditCapture.Json);
        options.Converters.Add(new DieConverter());
        return options;
    }

    private sealed class DieConverter : JsonConverter<DieValue>
    {
        public override DieValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var json = JsonDocument.ParseValue(ref reader);
            var root = json.RootElement;
            var value = root.ValueKind == JsonValueKind.Number ? root.GetInt32() : root.GetProperty("value").GetInt32();
            return new DieValue(value);
        }

        public override void Write(Utf8JsonWriter writer, DieValue value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("value", value.Value);
            writer.WriteEndObject();
        }
    }

    private static bool Classification(RoundAuditSummary summary, JsonElement saved, LiarsDiceMatchState final) =>
        saved.GetProperty("id").GetGuid().ToString("N") == summary.RoundId &&
        summary.TableId == summary.RoundId && summary.RoundNumber == final.RoundNumber &&
        summary.StartedAtUtc == saved.GetProperty("startedAtUtc").GetDateTime().ToUniversalTime() &&
        summary.CompletedAtUtc == saved.GetProperty("completedAtUtc").GetDateTime().ToUniversalTime() &&
        summary.Participants.Count == 1 && summary.Participants[0].IsAccountBacked &&
        summary.Participants[0].WagerCents == 0 && summary.Participants[0].PayoutCents == 0 &&
        summary.Participants[0].NetCents == 0 && final.Winner is not null;

    private static bool RosterConsistent(JsonElement payload, LiarsDiceMatchState state)
    {
        var actors = payload.GetProperty("managedActors").EnumerateArray()
            .Select(value => value.GetProperty("userId").GetString() ?? "").ToArray();
        return state.AllPlayerIds.Length == 4 && state.AllPlayerIds[0] == "you" &&
            actors.Length == 3 && actors.Distinct(StringComparer.Ordinal).Count() == 3 &&
            actors.ToHashSet(StringComparer.Ordinal).SetEquals(state.AllPlayerIds.Skip(1));
    }

    private static bool InitialMatch(LiarsDiceMatchState match, uint seed)
    {
        if (match.StartingDicePerPlayer <= 0 || match.RoundNumber != 1 || match.Winner is not null ||
            match.LastOutcome is not null || match.AllPlayerIds.Length != 4 ||
            match.DiceCounts.Count != 4 || match.AllPlayerIds.Any(id => match.DiceCounts.GetValueOrDefault(id) != match.StartingDicePerPlayer))
            return false;
        return NewRoundMatches(match.Round, match.AllPlayerIds, match.DiceCounts, seed, match.AllPlayerIds[0]);
    }

    private static bool NewRoundMatches(LiarsDiceRoundState round, IReadOnlyList<string> order,
        IReadOnlyDictionary<string, int> counts, uint seed, string starter)
    {
        if (round.Variant != LiarsDiceVariant.ExactFace || !round.TurnOrder.SequenceEqual(order) ||
            round.Hands.Count != order.Count || round.Phase != LiarsDiceRoundPhase.Bidding ||
            round.CurrentBid is not null || round.CurrentBidderId is not null || round.Outcome is not null ||
            round.CurrentPlayerIndex != Array.IndexOf(order.ToArray(), starter)) return false;
        var random = seed;
        foreach (var id in order)
        {
            if (!counts.TryGetValue(id, out var count) || count <= 0 ||
                !round.Hands.TryGetValue(id, out var hand) || hand.Length != count) return false;
            for (var index = 0; index < count; index++)
                if (hand[index].Value != NextFace(ref random)) return false;
        }
        return true;
    }

    private static int NextFace(ref uint value)
    {
        unchecked
        {
            value += 0x6d2b79f5u;
            var mixed = value;
            mixed = (mixed ^ (mixed >> 15)) * (mixed | 1u);
            mixed ^= mixed + (mixed ^ (mixed >> 7)) * (mixed | 61u);
            return (int)((mixed ^ (mixed >> 14)) % 6) + 1;
        }
    }

    private static bool Transition(LiarsDiceMatchState prior, LiarsDiceMatchState next,
        uint priorSeed, uint nextSeed, string kind)
    {
        if (!prior.AllPlayerIds.SequenceEqual(next.AllPlayerIds) ||
            prior.StartingDicePerPlayer != next.StartingDicePerPlayer) return false;
        if (kind == "next-round") return NextRound(prior, next, nextSeed);
        if (priorSeed != nextSeed || prior.RoundNumber != next.RoundNumber || prior.Winner is not null) return false;
        if (kind == "managed-advance-request" && SameMatch(prior, next)) return true;
        if (prior.Round.Phase != LiarsDiceRoundPhase.Bidding ||
            prior.Round.CurrentPlayerIndex < 0 || prior.Round.CurrentPlayerIndex >= prior.Round.TurnOrder.Length ||
            !SameHands(prior.Round, next.Round) || !prior.Round.TurnOrder.SequenceEqual(next.Round.TurnOrder) ||
            prior.Round.Variant != next.Round.Variant) return false;
        var actor = prior.Round.CurrentPlayerId;
        if (kind.StartsWith("human-", StringComparison.Ordinal) && actor != "you" ||
            kind.StartsWith("managed-", StringComparison.Ordinal) && actor == "you") return false;
        return next.Round.Phase switch
        {
            LiarsDiceRoundPhase.Bidding => Bid(prior, next, actor, kind),
            LiarsDiceRoundPhase.Resolved => Resolve(prior, next, actor, kind),
            _ => false
        };
    }

    private static bool Bid(LiarsDiceMatchState prior, LiarsDiceMatchState next, string actor, string kind)
    {
        if (kind is not ("human-bid" or "managed-advance-request" or "managed-background-turn") ||
            next.Round.CurrentBid is not { } bid || bid.Quantity <= 0 || bid.Face.Value is < 1 or > 6 ||
            bid.Quantity > prior.Round.Hands.Values.Sum(hand => hand.Length) ||
            prior.Round.CurrentBid is { } previous && !bid.IsHigherThan(previous) ||
            next.Round.CurrentBidderId != actor ||
            next.Round.CurrentPlayerIndex != (prior.Round.CurrentPlayerIndex + 1) % prior.Round.TurnOrder.Length ||
            next.Round.Outcome is not null || next.LastOutcome != prior.LastOutcome ||
            next.Winner != prior.Winner || !SameCounts(prior, next)) return false;
        return true;
    }

    private static bool Resolve(LiarsDiceMatchState prior, LiarsDiceMatchState next, string actor, string kind)
    {
        if (kind is not ("human-challenge" or "human-spot-on" or "managed-advance-request" or "managed-background-turn") ||
            prior.Round.CurrentBid is not { } bid || prior.Round.CurrentBidderId is not { } bidder ||
            next.Round.CurrentBid != bid || next.Round.CurrentBidderId != bidder ||
            next.Round.CurrentPlayerIndex != prior.Round.CurrentPlayerIndex ||
            next.Round.Outcome is not { } outcome || next.LastOutcome != outcome ||
            outcome.ChallengerId != actor || outcome.BidderId != bidder || outcome.Bid != bid ||
            (kind == "human-spot-on") != outcome.IsSpotOn && kind.StartsWith("human-", StringComparison.Ordinal)) return false;
        var matches = prior.Round.Hands.Values.SelectMany(hand => hand).Count(die => die == bid.Face);
        var loser = outcome.IsSpotOn ? matches == bid.Quantity ? bidder : actor :
            matches >= bid.Quantity ? actor : bidder;
        if (outcome.MatchingDice != matches || outcome.LoserId != loser ||
            !prior.DiceCounts.TryGetValue(loser, out var before) || before <= 0 ||
            next.DiceCounts.GetValueOrDefault(loser) != before - 1 ||
            prior.AllPlayerIds.Any(id => id != loser && prior.DiceCounts[id] != next.DiceCounts.GetValueOrDefault(id))) return false;
        var remaining = prior.AllPlayerIds.Where(id => next.DiceCounts[id] > 0).ToArray();
        return next.Winner == (remaining.Length == 1 ? remaining[0] : null);
    }

    private static bool NextRound(LiarsDiceMatchState prior, LiarsDiceMatchState next, uint seed)
    {
        if (prior.Winner is not null || prior.Round.Phase != LiarsDiceRoundPhase.Resolved ||
            prior.LastOutcome is not { } outcome || next.RoundNumber != prior.RoundNumber + 1 ||
            next.LastOutcome is not null || next.Winner is not null || !SameCounts(prior, next)) return false;
        var active = prior.AllPlayerIds.Where(id => prior.DiceCounts[id] > 0).ToArray();
        if (active.Length < 2) return false;
        var loserIndex = prior.Round.TurnOrder.IndexOf(outcome.LoserId);
        if (loserIndex < 0) return false;
        string? starter = null;
        for (var offset = 1; offset <= prior.Round.TurnOrder.Length; offset++)
        {
            var candidate = prior.Round.TurnOrder[(loserIndex + offset) % prior.Round.TurnOrder.Length];
            if (active.Contains(candidate, StringComparer.Ordinal)) { starter = candidate; break; }
        }
        return starter is not null && NewRoundMatches(next.Round, active, next.DiceCounts, seed, starter);
    }

    private static bool SameMatch(LiarsDiceMatchState a, LiarsDiceMatchState b) =>
        a.StartingDicePerPlayer == b.StartingDicePerPlayer && a.RoundNumber == b.RoundNumber &&
        a.AllPlayerIds.SequenceEqual(b.AllPlayerIds) && SameCounts(a, b) &&
        a.Winner == b.Winner && a.LastOutcome == b.LastOutcome &&
        a.Round.Variant == b.Round.Variant &&
        a.Round.CurrentPlayerIndex == b.Round.CurrentPlayerIndex &&
        a.Round.CurrentBid == b.Round.CurrentBid && a.Round.CurrentBidderId == b.Round.CurrentBidderId &&
        a.Round.Phase == b.Round.Phase && a.Round.Outcome == b.Round.Outcome &&
        a.Round.TurnOrder.SequenceEqual(b.Round.TurnOrder) && SameHands(a.Round, b.Round);

    private static bool SameCounts(LiarsDiceMatchState a, LiarsDiceMatchState b) =>
        a.DiceCounts.Count == b.DiceCounts.Count && a.AllPlayerIds.All(id =>
            a.DiceCounts.TryGetValue(id, out var value) && b.DiceCounts.TryGetValue(id, out var other) && value == other);

    private static bool SameHands(LiarsDiceRoundState a, LiarsDiceRoundState b) =>
        a.Hands.Count == b.Hands.Count && a.TurnOrder.All(id =>
            a.Hands.TryGetValue(id, out var first) && b.Hands.TryGetValue(id, out var second) &&
            first.SequenceEqual(second));

    private static IReadOnlyList<RoundAuditFinding> Error(string code, string message) => [new(code, "error", message)];

    private sealed record Observation(long Version, string Kind, DateTime OccurredAtUtc, uint RoundSeed,
        LiarsDiceMatchState Match, JsonElement Payload, string[] Participants);
}
