using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Checks accepted free Craps and Roulette transitions against the complete saved instance journal.</summary>
internal static class CasinoPracticeActionReplayAudit
{
    internal static bool Supports(RoundAuditSummary summary) =>
        (summary.GameId, summary.AuditorVersion, summary.EvidenceKind) switch
        {
            ("craps", PracticeOutcomeAudit.CrapsVersion, "craps-private-dice-and-bets-v1") => true,
            ("roulette", PracticeOutcomeAudit.RouletteVersion, "roulette-private-table-v1") => true,
            _ => false
        };

    internal static IReadOnlyList<RoundAuditFinding> Inspect(
        RoundAuditSummary summary, string? payloadJson, IReadOnlyList<string> eventPayloads)
    {
        if (!Supports(summary)) return [];
        if (payloadJson is null) return Error("casino-practice-terminal-missing", "The completed free round lacks its private terminal snapshot.");
        try
        {
            using var terminal = JsonDocument.Parse(payloadJson);
            var saved = terminal.RootElement;
            var observations = new List<Observation>(eventPayloads.Count);
            foreach (var payload in eventPayloads)
            {
                using var document = JsonDocument.Parse(payload);
                var envelope = document.RootElement;
                observations.Add(new(envelope.GetProperty("version").GetInt64(),
                    envelope.GetProperty("eventKind").GetString() ?? "",
                    envelope.GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime(),
                    envelope.GetProperty("state").Clone(),
                    envelope.GetProperty("request").Clone()));
            }
            observations.Sort((a, b) => a.Version.CompareTo(b.Version));
            if (observations.Count < 2)
                return Error("casino-practice-action-history-missing", "The free round has no complete start-to-settlement engine journal.");
            for (var i = 1; i < observations.Count; i++)
            if (observations[i].Version != observations[i - 1].Version + 1 ||
                    observations[i].OccurredAtUtc < observations[i - 1].OccurredAtUtc)
                    return Error("casino-practice-journal-sequence-mismatch", "The free round journal has a missing version or reversed server time.");

            var findings = summary.GameId == "craps" ? Craps(summary, saved, observations) : Roulette(summary, saved, observations);
            if (findings.Any(finding => finding.Severity == "error")) return findings;
            var warnings = new List<RoundAuditFinding>(findings);
            if (observations.Any(HasMissingCommandArguments))
                warnings.Add(new("casino-practice-command-arguments-absent", "warning",
                    "One or more accepted observations lack complete sanitized command arguments; replay checks the recorded state delta but cannot prove the omitted user choice."));
            warnings.Add(new("casino-practice-in-memory-journal-non-atomic", "warning",
                "Gameplay state and the acknowledged Firestore journal are separate writes; an accepted local transition can be missing after an outage."));
            return warnings;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or
            FormatException or OverflowException or ArithmeticException or IndexOutOfRangeException or NotSupportedException)
        {
            return Error("casino-practice-action-evidence-invalid", "The free round terminal state or engine observations cannot be parsed for action replay.");
        }
    }

    private static IReadOnlyList<RoundAuditFinding> Craps(
        RoundAuditSummary summary, JsonElement saved, IReadOnlyList<Observation> observations)
    {
        var first = observations[0];
        var last = observations[^1];
        if (summary.RoundId != saved.GetProperty("id").GetGuid().ToString("N") ||
            summary.TableId != summary.RoundId || summary.RoundNumber != 1 ||
            summary.StartedAtUtc != saved.GetProperty("startedAtUtc").GetDateTime().ToUniversalTime() ||
            summary.CompletedAtUtc != saved.GetProperty("completedAtUtc").GetDateTime().ToUniversalTime() ||
            summary.Participants.Count != 1 || !summary.Participants[0].IsAccountBacked ||
            summary.Participants[0].WagerCents != 0 || summary.Participants[0].PayoutCents != 0 ||
            summary.Participants[0].NetCents != 0 ||
            !JsonElement.DeepEquals(last.Payload, saved) ||
            first.Version != 0 || first.Kind != "started" ||
            last.Kind != "rolled")
            return Error("craps-action-classification-mismatch", "The free Craps episode identity, participant, or terminal observation differs from its private receipt.");
        var prior = ReadCraps(first.Payload);
        if (prior.PlayerId != summary.Participants[0].ActorId ||
            prior.Stake is < 1m or > 100m || prior.Stake != decimal.Truncate(prior.Stake) ||
            prior.Phase != 0 || prior.Point is not null || prior.Rolls.Length != 0 || prior.Last is not null ||
            prior.Extras.Any(extra => !ValidInitialExtra(extra)) ||
            !MatchesActor(first.Request, prior.PlayerId) ||
            !MatchesCrapsStartRequest(first.Request, prior))
            return Error("craps-action-start-mismatch", "The opening Craps wager or side-bet state is not valid.");
        for (var i = 1; i < observations.Count; i++)
        {
            var current = ReadCraps(observations[i].Payload);
            if (observations[i].Payload.GetProperty("id").GetGuid() != first.Payload.GetProperty("id").GetGuid() ||
                observations[i].Payload.GetProperty("startedAtUtc").GetDateTime().ToUniversalTime() != summary.StartedAtUtc ||
                current.PlayerId != prior.PlayerId || current.Stake != prior.Stake || prior.Phase == 2)
                return Error("craps-action-sequence-mismatch", "A Craps action changed the accepted wager or continued after settlement.");
            if (observations[i].Kind == "odds-placed")
            {
                if (prior.Phase != 1 || prior.Point is null || current.Phase != prior.Phase ||
                    current.Point != prior.Point || !SameRolls(current.Rolls, prior.Rolls) ||
                    !SameOutcome(current.Last, prior.Last) || current.Extras.Length != prior.Extras.Length + 1 ||
                    !SameExtras(current.Extras.Take(prior.Extras.Length).ToArray(), prior.Extras) ||
                    current.Extras[^1] is not { Kind: "odds", Resolved: false, Won: false, Return: null } added ||
                    added.Stake is < 1m or > 100m || added.Stake != decimal.Truncate(added.Stake) ||
                    prior.Extras.Any(extra => extra.Kind == "odds" && !extra.Resolved) ||
                    !MatchesActor(observations[i].Request, prior.PlayerId) ||
                    !MatchesDecimalRequest(observations[i].Request, "stake", added.Stake))
                    return Error("craps-odds-action-mismatch", "A saved odds placement is not a legal addition to the open point.");
            }
            else if (observations[i].Kind == "rolled")
            {
                if (!MatchesActor(observations[i].Request, prior.PlayerId) || !ValidCrapsRoll(prior, current))
                    return Error("craps-roll-action-mismatch", "A saved die roll, pass-line transition or side-bet return differs from the preceding state.");
            }
            else return Error("craps-action-kind-invalid", "The Craps journal contains an unsupported accepted action.");
            prior = current;
        }
        return prior.Phase == 2 ? [] : Error("craps-action-terminal-mismatch", "The saved Craps action history does not end in a resolved round.");
    }

    private static bool ValidCrapsRoll(CrapsState before, CrapsState after)
    {
        if (after.Rolls.Length != before.Rolls.Length + 1 ||
            !SameRolls(after.Rolls.Take(before.Rolls.Length).ToArray(), before.Rolls) ||
            after.Extras.Length != before.Extras.Length) return false;
        var dice = after.Rolls[^1];
        if (dice.First is < 1 or > 6 || dice.Second is < 1 or > 6) return false;
        var total = dice.First + dice.Second;
        var phase = before.Phase;
        var point = before.Point;
        int result;
        decimal? returned;
        if (phase == 0)
        {
            if (total is 7 or 11) { phase = 2; result = 0; returned = before.Stake * 2; }
            else if (total is 2 or 3 or 12) { phase = 2; result = 1; returned = 0m; }
            else { phase = 1; point = total; result = 2; returned = null; }
        }
        else if (phase == 1 && point is not null)
        {
            if (total == point) { phase = 2; result = 3; returned = before.Stake * 2; }
            else if (total == 7) { phase = 2; result = 4; returned = 0m; }
            else { result = 5; returned = null; }
        }
        else return false;
        var expected = new CrapsOutcome(dice, result, phase == 2, returned);
        if (after.Phase != phase || after.Point != point || !SameOutcome(after.Last, expected)) return false;
        for (var i = 0; i < before.Extras.Length; i++)
        {
            var old = before.Extras[i];
            var actual = after.Extras[i];
            if (old.Resolved || old.Kind == "odds" && phase != 2)
            {
                if (old != actual) return false;
                continue;
            }
            var amount = old.Kind switch
            {
                "field" when total is 2 or 12 => old.Stake * 3m,
                "field" when total is 3 or 4 or 9 or 10 or 11 => old.Stake * 2m,
                "any-seven" when total == 7 => old.Stake * 5m,
                "any-craps" when total is 2 or 3 or 12 => old.Stake * 8m,
                "odds" when result == 3 && point is (4 or 10) => old.Stake * 3m,
                "odds" when result == 3 && point is (5 or 9) => old.Stake * 2.5m,
                "odds" when result == 3 && point is (6 or 8) => old.Stake * 2.2m,
                _ => 0m
            };
            if (actual != (old with { Resolved = true, Won = amount > 0m, Return = amount })) return false;
        }
        return true;
    }

    private static CrapsState ReadCraps(JsonElement payload)
    {
        var value = payload.GetProperty("state");
        var bet = value.GetProperty("bet");
        var outcome = value.GetProperty("lastOutcome");
        return new(bet.GetProperty("playerId").GetString() ?? "", bet.GetProperty("stake").GetDecimal(),
            value.GetProperty("phase").GetInt32(), NullableInt(value.GetProperty("point")),
            value.GetProperty("rolls").EnumerateArray().Select(ReadDice).ToArray(),
            outcome.ValueKind == JsonValueKind.Null ? null : new(ReadDice(outcome.GetProperty("dice")),
                outcome.GetProperty("result").GetInt32(), outcome.GetProperty("isTerminal").GetBoolean(),
                NullableDecimal(outcome.GetProperty("totalReturn"))),
            payload.GetProperty("extraBets").EnumerateArray().Select(extra => new CrapsExtra(
                extra.GetProperty("kind").GetString() ?? "", extra.GetProperty("stake").GetDecimal(),
                extra.GetProperty("resolved").GetBoolean(), extra.GetProperty("won").GetBoolean(),
                NullableDecimal(extra.GetProperty("totalReturn")))).ToArray());
    }

    private static bool ValidInitialExtra(CrapsExtra extra) =>
        extra.Kind is ("field" or "any-seven" or "any-craps") && extra.Stake is >= 1m and <= 100m &&
        extra.Stake == decimal.Truncate(extra.Stake) && !extra.Resolved && !extra.Won && extra.Return is null;

    private static bool SameRolls(IReadOnlyList<Dice> a, IReadOnlyList<Dice> b) => a.SequenceEqual(b);
    private static bool SameOutcome(CrapsOutcome? a, CrapsOutcome? b) => a == b;
    private static bool SameExtras(IReadOnlyList<CrapsExtra> a, IReadOnlyList<CrapsExtra> b) => a.SequenceEqual(b);
    private static Dice ReadDice(JsonElement value) => new(value.GetProperty("first").GetProperty("value").GetInt32(),
        value.GetProperty("second").GetProperty("value").GetInt32());

    private static IReadOnlyList<RoundAuditFinding> Roulette(
        RoundAuditSummary summary, JsonElement saved, IReadOnlyList<Observation> observations)
    {
        var table = saved.GetProperty("id").GetGuid();
        var roundNumber = saved.GetProperty("roundNumber").GetInt32();
        var humanIds = saved.GetProperty("humanParticipants").EnumerateArray()
            .Select(value => value.GetString() ?? "").Order(StringComparer.Ordinal).ToArray();
        if (summary.RoundId != $"{table:N}:{roundNumber}" || summary.TableId != table.ToString("N") ||
            summary.RoundNumber != roundNumber ||
            summary.StartedAtUtc != saved.GetProperty("roundStartedAtUtc").GetDateTime().ToUniversalTime() ||
            summary.CompletedAtUtc != saved.GetProperty("roundCompletedAtUtc").GetDateTime().ToUniversalTime() ||
            humanIds.Length == 0 || humanIds.Distinct(StringComparer.Ordinal).Count() != humanIds.Length ||
            !summary.Participants.Select(value => value.ActorId).Order(StringComparer.Ordinal).SequenceEqual(humanIds) ||
            summary.Participants.Any(value => !value.IsAccountBacked || value.WagerCents != 0 ||
                value.PayoutCents != 0 || value.NetCents != 0) ||
            !observations.Any(value => JsonElement.DeepEquals(value.Payload, saved)))
            return Error("roulette-action-classification-mismatch", "The shared Roulette table identity, human participants or terminal state differs from the private receipt.");

        foreach (var item in observations)
            if (item.Payload.GetProperty("id").GetGuid() != table ||
                item.Payload.GetProperty("roundNumber").GetInt32() != roundNumber ||
                item.Payload.GetProperty("roundStartedAtUtc").GetDateTime().ToUniversalTime() != summary.StartedAtUtc)
                return Error("roulette-action-identity-mismatch", "An accepted Roulette observation changed the table or round identity.");
        var prior = ReadRoulette(observations[0].Payload);
        if (prior.Phase != 0 || prior.Bets.Any(bet => !ValidRouletteBet(bet)) || prior.Settlements.Length != 0 ||
            prior.Pocket is not null || !ValidRouletteSnapshot(prior))
            return Error("roulette-action-start-mismatch", "The first Roulette observation is not a valid open round.");
        var findings = new List<RoundAuditFinding>();
        var terminalSeen = false;
        for (var i = 1; i < observations.Count; i++)
        {
            var current = ReadRoulette(observations[i].Payload);
            var eventKind = observations[i].Kind;
            if (current.RoundId != prior.RoundId || current.Bets.Any(bet => !ValidRouletteBet(bet)) ||
                !ValidRouletteSnapshot(current) ||
                current.HumanParticipants.Except(prior.HumanParticipants, StringComparer.Ordinal)
                    .Any(id => eventKind != "bet-placed" || !current.Bets.Any(bet => bet.PlayerId == id)))
                return Error("roulette-action-state-mismatch", "A Roulette observation changed the engine round, participant set or a saved bet illegally.");
            if (prior.Phase == 1)
            {
                if (current.Phase != 1 || !SameBets(current.Bets, prior.Bets) ||
                    !SameSettlements(current.Settlements, prior.Settlements) ||
                    !SameBalancesForKnownHumans(prior, current, null, 0m))
                    return Error("roulette-post-settlement-mismatch", "An observation changed a settled Roulette outcome or a human table balance.");
                prior = current;
                continue;
            }
            if (current.Phase == 1)
            {
                if (eventKind is not ("spun" or "background-transition") ||
                    !SameHumanBets(prior, current, null, out _) ||
                    eventKind == "spun" && RequestActor(observations[i].Request) is { } spinner &&
                        !prior.Bets.Any(bet => bet.PlayerId == spinner) ||
                    !ValidRouletteTerminal(summary, observations[i].Payload, prior, current))
                    return Error("roulette-spin-action-mismatch", "A Roulette spin changed accepted human bets, settlement or virtual balances unexpectedly.");
                terminalSeen = true;
            }
            else if (current.Phase != 0)
                return Error("roulette-action-phase-mismatch", "A Roulette observation has an unsupported phase transition.");
            else if (eventKind == "bet-placed")
            {
                if (!OneHumanBetAdded(prior, current, observations[i].Request))
                    return Error("roulette-bet-action-mismatch", "A saved bet placement does not match one human wager and its virtual balance debit.");
            }
            else if (eventKind == "bet-removed")
            {
                if (!OneHumanBetRemoved(prior, current, observations[i].Request))
                    return Error("roulette-remove-action-mismatch", "A saved bet removal does not restore the matching human virtual balance.");
            }
            else if (eventKind == "bets-cleared")
            {
                if (!HumanBetsCleared(prior, current, observations[i].Request))
                    return Error("roulette-clear-action-mismatch", "A saved bet clear does not restore the matching human virtual balance.");
            }
            else if (eventKind is "joined" or "observed" or "background-transition")
            {
                if (!SameHumanBets(prior, current, null, out _) ||
                    !SameBalancesForKnownHumans(prior, current, null, 0m))
                    return Error("roulette-managed-transition-mismatch", "A table observation changed a human wager or balance without a saved human action.");
            }
            else return Error("roulette-action-kind-invalid", "The Roulette journal contains an unsupported accepted action.");
            prior = current;
        }
        if (!terminalSeen || prior.Phase != 1)
            return Error("roulette-action-terminal-missing", "The saved Roulette observation stream never reached a valid settled round.");
        findings.Add(new("roulette-managed-actions-not-fully-replayed", "warning",
            "Managed-seat joins and automatic wagers are validated as states and settlements, but their private choice process is not independently reconstructed."));
        return findings;
    }

    private static bool ValidRouletteTerminal(RoundAuditSummary summary, JsonElement payload,
        RouletteState before, RouletteState after)
    {
        if (after.Pocket is not (>= 0 and <= 36) || after.Bets.Length != after.Settlements.Length ||
            after.Settlements.Length == 0) return false;
        if (PracticeRoundReplayAudit.Inspect(summary, payload.GetRawText()).Any(value => value.Severity == "error"))
            return false;
        foreach (var (id, oldBalance) in before.Balances)
        {
            if (!before.Humans.Contains(id) && !before.HumanParticipants.Contains(id)) continue;
            if (!after.Balances.TryGetValue(id, out var actual)) return false;
            var returned = after.Settlements.Where(value => value.PlayerId == id).Sum(value => value.Return);
            if (actual != oldBalance + returned) return false;
        }
        return true;
    }

    private static bool OneHumanBetAdded(RouletteState before, RouletteState after, JsonElement request)
    {
        var prior = HumanBets(before);
        var current = HumanBets(after);
        var actors = current.Keys.Union(prior.Keys, StringComparer.Ordinal).Where(id =>
            current.GetValueOrDefault(id, []).Length == prior.GetValueOrDefault(id, []).Length + 1).ToArray();
        if (actors.Length != 1) return false;
        var actor = actors[0];
        var earlier = prior.GetValueOrDefault(actor, []);
        var later = current[actor];
        if (!SameBets(later.Take(earlier.Length).ToArray(), earlier)) return false;
        var added = later[^1];
        if (!MatchesActor(request, actor) || !MatchesRouletteRequest(request, added) ||
            !after.HumanParticipants.Contains(actor) ||
            !SameHumanBets(before, after, actor, out _) ||
            !SameBalancesForKnownHumans(before, after, actor, -added.Stake)) return false;
        return true;
    }

    private static bool OneHumanBetRemoved(RouletteState before, RouletteState after, JsonElement request)
    {
        var prior = HumanBets(before); var current = HumanBets(after);
        var actors = prior.Keys.Where(id => prior[id].Length == current.GetValueOrDefault(id, []).Length + 1).ToArray();
        if (actors.Length != 1) return false;
        var actor = actors[0];
        var earlier = prior[actor]; var later = current.GetValueOrDefault(actor, []);
        var removed = Enumerable.Range(0, earlier.Length)
            .Where(index => SameBets(earlier.Where((_, at) => at != index).ToArray(), later)).ToArray();
        var requestedIndex = RequestBetIndex(request);
        if (requestedIndex is { } exact && !removed.Contains(exact)) return false;
        var removedIndex = requestedIndex ?? removed.FirstOrDefault(-1);
        if (removed.Length == 0 || !MatchesActor(request, actor) ||
            !SameHumanBets(before, after, actor, out _) ||
            !SameBalancesForKnownHumans(before, after, actor, earlier[removedIndex].Stake)) return false;
        return true;
    }

    private static bool HumanBetsCleared(RouletteState before, RouletteState after, JsonElement request)
    {
        var prior = HumanBets(before); var current = HumanBets(after);
        var requestActor = RequestActor(request);
        string[] actors = requestActor is not null ? [requestActor] : prior.Keys
            .Where(id => prior[id].Length > 0 && current.GetValueOrDefault(id, []).Length == 0).ToArray();
        if (actors.Length != 1) return false;
        var actor = actors[0];
        if (current.GetValueOrDefault(actor, []).Length != 0 ||
            !SameHumanBets(before, after, actor, out _) ||
            !SameBalancesForKnownHumans(before, after, actor, prior.GetValueOrDefault(actor, []).Sum(value => value.Stake)))
            return false;
        return true;
    }

    private static bool SameHumanBets(RouletteState before, RouletteState after,
        string? changingActor, out string[] humanIds)
    {
        var prior = HumanBets(before); var current = HumanBets(after);
        humanIds = prior.Keys.Union(current.Keys, StringComparer.Ordinal).ToArray();
        return humanIds.Where(id => id != changingActor)
            .All(id => SameBets(prior.GetValueOrDefault(id, []), current.GetValueOrDefault(id, [])));
    }

    private static bool SameBalancesForKnownHumans(RouletteState before, RouletteState after,
        string? changingActor, decimal delta)
    {
        foreach (var id in before.Humans.Union(before.HumanParticipants, StringComparer.Ordinal))
        {
            if (!before.Balances.TryGetValue(id, out var prior) ||
                !after.Balances.TryGetValue(id, out var current) ||
                current != prior + (id == changingActor ? delta : 0m)) return false;
        }
        return true;
    }

    private static Dictionary<string, RouletteBet[]> HumanBets(RouletteState value)
    {
        var humans = value.Humans.Union(value.HumanParticipants, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        return value.Bets.Where(bet => humans.Contains(bet.PlayerId)).GroupBy(bet => bet.PlayerId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
    }

    private static bool ValidRouletteSnapshot(RouletteState value) =>
        value.Balances.All(pair => pair.Value >= 0m) &&
        value.Bets.All(bet => value.Balances.ContainsKey(bet.PlayerId)) &&
        value.HumanParticipants.All(id => !string.IsNullOrWhiteSpace(id) && value.Balances.ContainsKey(id));

    private static RouletteState ReadRoulette(JsonElement payload)
    {
        var state = payload.GetProperty("state");
        var players = payload.GetProperty("players").EnumerateArray().ToArray();
        var balances = payload.GetProperty("balances").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetDecimal(), StringComparer.Ordinal);
        var bets = state.GetProperty("bets").EnumerateArray().Select(item => new RouletteBet(
            item.GetProperty("playerId").GetString() ?? "", item.GetProperty("kind").GetInt32(),
            item.GetProperty("stake").GetDecimal(), NullableInt(item.GetProperty("number")),
            item.GetProperty("numbers").EnumerateArray().Select(number => number.GetInt32()).ToArray())).ToArray();
        var settlements = state.GetProperty("settlements").EnumerateArray().Select(item => new RouletteSettlement(
            item.GetProperty("playerId").GetString() ?? "", item.GetProperty("kind").GetInt32(),
            item.GetProperty("stake").GetDecimal(), item.GetProperty("won").GetBoolean(),
            item.GetProperty("totalReturn").GetDecimal())).ToArray();
        return new(state.GetProperty("roundId").GetString() ?? "", state.GetProperty("phase").GetInt32(), bets,
            state.GetProperty("winningPocket").ValueKind == JsonValueKind.Null ? null :
                state.GetProperty("winningPocket").GetProperty("number").GetInt32(), settlements,
            balances, players.Where(player => !player.GetProperty("isManaged").GetBoolean())
                .Select(player => player.GetProperty("userId").GetString() ?? "").ToHashSet(StringComparer.Ordinal),
            payload.GetProperty("humanParticipants").EnumerateArray()
                .Select(value => value.GetString() ?? "").ToHashSet(StringComparer.Ordinal));
    }

    private static bool ValidRouletteBet(RouletteBet bet)
    {
        if (string.IsNullOrWhiteSpace(bet.PlayerId) || bet.Stake is < 1m or > 100m ||
            bet.Stake != decimal.Truncate(bet.Stake) || bet.Kind is < 0 or > 12 ||
            bet.Numbers.Any(value => value is < 0 or > 36) ||
            bet.Numbers.Distinct().Count() != bet.Numbers.Length) return false;
        if (bet.Kind == 0) return bet.Number is >= 0 and <= 36 && bet.Numbers.Length == 0;
        if (bet.Kind is 5 or 6) return bet.Number is >= 1 and <= 3 && bet.Numbers.Length == 0;
        if (bet.Kind >= 7) return bet.Number is null && bet.Numbers.Length == 0;
        if (bet.Number is not null) return false;
        var sorted = bet.Numbers.Order().ToArray();
        var rows = sorted.Where(value => value > 0).Select(value => (value - 1) / 3).Distinct().ToArray();
        var columns = sorted.Where(value => value > 0).Select(value => (value - 1) % 3).Distinct().ToArray();
        return bet.Kind switch
        {
            1 => sorted.Length == 2 && (sorted[0] == 0 && sorted[1] is >= 1 and <= 3 ||
                sorted[0] > 0 && (rows.Length == 1 && sorted[1] - sorted[0] == 1 ||
                    columns.Length == 1 && sorted[1] - sorted[0] == 3)),
            2 => sorted.Length == 3 && (sorted.SequenceEqual([0, 1, 2]) || sorted.SequenceEqual([0, 2, 3]) ||
                sorted[0] > 0 && rows.Length == 1 && columns.Length == 3),
            3 => sorted.Length == 4 && sorted[0] > 0 && rows.Length == 2 && columns.Length == 2 &&
                sorted[1] - sorted[0] == 1 && sorted[2] - sorted[0] == 3 && sorted[3] - sorted[2] == 1,
            4 => sorted.Length == 6 && sorted[0] > 0 && rows.Length == 2 && columns.Length == 3 &&
                sorted[3] - sorted[0] == 3 && sorted[5] - sorted[2] == 3,
            _ => false
        };
    }

    private static bool SameBets(IReadOnlyList<RouletteBet> a, IReadOnlyList<RouletteBet> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => pair.First.PlayerId == pair.Second.PlayerId &&
            pair.First.Kind == pair.Second.Kind && pair.First.Stake == pair.Second.Stake &&
            pair.First.Number == pair.Second.Number && pair.First.Numbers.SequenceEqual(pair.Second.Numbers));

    private static bool SameSettlements(IReadOnlyList<RouletteSettlement> a, IReadOnlyList<RouletteSettlement> b) =>
        a.SequenceEqual(b);

    private static bool MatchesCrapsStartRequest(JsonElement request, CrapsState state)
    {
        var json = RequestJson(request);
        if (json is null) return true;
        using var document = JsonDocument.Parse(json);
        var body = document.RootElement;
        if (body.ValueKind != JsonValueKind.Object) return false;
        if (TryProperty(body, "stake", out var stake) &&
            (stake.ValueKind != JsonValueKind.Number || !stake.TryGetDecimal(out var amount) || amount != state.Stake)) return false;
        if (!TryProperty(body, "extraBets", out var extras)) return true;
        if (extras.ValueKind is JsonValueKind.Null && state.Extras.Length == 0) return true;
        if (extras.ValueKind != JsonValueKind.Array || extras.GetArrayLength() != state.Extras.Length) return false;
        var index = 0;
        foreach (var extra in extras.EnumerateArray())
        {
            if (extra.ValueKind != JsonValueKind.Object) return false;
            var observed = state.Extras[index++];
            if (TryProperty(extra, "kind", out var kind) &&
                (kind.ValueKind != JsonValueKind.String ||
                 !string.Equals(kind.GetString()?.Trim(), observed.Kind, StringComparison.OrdinalIgnoreCase))) return false;
            if (TryProperty(extra, "stake", out var extraStake) &&
                (extraStake.ValueKind != JsonValueKind.Number || !extraStake.TryGetDecimal(out var value) ||
                 value != observed.Stake)) return false;
        }
        return true;
    }

    private static bool MatchesRouletteRequest(JsonElement request, RouletteBet bet)
    {
        var json = RequestJson(request);
        if (json is null) return true;
        using var document = JsonDocument.Parse(json);
        var body = document.RootElement;
        if (body.ValueKind != JsonValueKind.Object) return false;
        if (!MatchesDecimalRequestField(body, "stake", bet.Stake)) return false;
        if (TryProperty(body, "kind", out var kind) &&
            (kind.ValueKind != JsonValueKind.String || kind.GetString() != RouletteKindName(bet.Kind))) return false;
        if (TryProperty(body, "number", out var number) &&
            (number.ValueKind == JsonValueKind.Null ? bet.Number is not null :
                number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out var value) || value != bet.Number)) return false;
        if (TryProperty(body, "numbers", out var numbers))
        {
            if (numbers.ValueKind == JsonValueKind.Null) return bet.Numbers.Length == 0;
            if (numbers.ValueKind != JsonValueKind.Array ||
                !numbers.EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(bet.Numbers)) return false;
        }
        return true;
    }

    private static string RouletteKindName(int kind) => kind switch
    {
        0 => "straight", 1 => "split", 2 => "street", 3 => "corner", 4 => "six-line",
        5 => "column", 6 => "dozen", 7 => "red", 8 => "black", 9 => "even",
        10 => "odd", 11 => "low", 12 => "high", _ => ""
    };

    private static bool MatchesDecimalRequest(JsonElement request, string field, decimal value)
    {
        var json = RequestJson(request);
        if (json is null) return true;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object &&
            MatchesDecimalRequestField(document.RootElement, field, value);
    }

    private static bool MatchesDecimalRequestField(JsonElement body, string field, decimal value) =>
        !TryProperty(body, field, out var received) ||
        received.ValueKind == JsonValueKind.Number && received.TryGetDecimal(out var actual) && actual == value;

    private static bool MatchesActor(JsonElement request, string actor) =>
        RequestActor(request) is not { } received || string.Equals(received, actor, StringComparison.Ordinal);

    private static string? RequestActor(JsonElement request) =>
        request.ValueKind == JsonValueKind.Object && TryProperty(request, "userId", out var user) &&
        user.ValueKind == JsonValueKind.String ? user.GetString() : null;

    private static string? RequestJson(JsonElement request) =>
        request.ValueKind == JsonValueKind.Object && TryProperty(request, "requestJson", out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? RequestBetIndex(JsonElement request)
    {
        int? bodyIndex = null;
        if (RequestJson(request) is { } json)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                TryProperty(document.RootElement, "index", out var index))
                bodyIndex = index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out var number)
                    ? number : -1;
        }
        if (request.ValueKind != JsonValueKind.Object || !TryProperty(request, "path", out var path) ||
            path.ValueKind != JsonValueKind.String) return bodyIndex;
        var parts = (path.GetString() ?? "").TrimEnd('/').Split('/');
        if (parts.Length < 2 || parts[^2] != "bets") return bodyIndex;
        var pathIndex = int.TryParse(parts[^1], out var parsed) ? parsed : -1;
        return bodyIndex is { } fromBody && fromBody != pathIndex ? -1 : pathIndex;
    }

    private static bool TryProperty(JsonElement value, string name, out JsonElement result)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    result = property.Value;
                    return true;
                }
        result = default;
        return false;
    }

    private static bool HasMissingCommandArguments(Observation observation)
    {
        string[] fields = observation.Kind switch
        {
            "started" when observation.Payload.TryGetProperty("extraBets", out _) => ["stake"],
            "odds-placed" => ["stake"],
            "bet-placed" => ["kind", "stake"],
            "bet-removed" => [],
            _ => []
        };
        if (observation.Kind == "bet-removed") return RequestBetIndex(observation.Request) is null;
        if (fields.Length == 0) return false;
        var json = RequestJson(observation.Request);
        if (json is null) return true;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind != JsonValueKind.Object ||
            fields.Any(field => !TryProperty(document.RootElement, field, out _));
    }

    private sealed record RouletteBet(string PlayerId, int Kind, decimal Stake, int? Number, int[] Numbers);
    private sealed record RouletteSettlement(string PlayerId, int Kind, decimal Stake, bool Won, decimal Return);
    private sealed record RouletteState(string RoundId, int Phase, RouletteBet[] Bets, int? Pocket,
        RouletteSettlement[] Settlements, Dictionary<string, decimal> Balances,
        HashSet<string> Humans, HashSet<string> HumanParticipants);

    private sealed record Observation(long Version, string Kind, DateTime OccurredAtUtc, JsonElement Payload, JsonElement Request);
    private sealed record Dice(int First, int Second);
    private sealed record CrapsOutcome(Dice Dice, int Result, bool Terminal, decimal? Return);
    private sealed record CrapsExtra(string Kind, decimal Stake, bool Resolved, bool Won, decimal? Return);
    private sealed record CrapsState(string PlayerId, decimal Stake, int Phase, int? Point,
        Dice[] Rolls, CrapsOutcome? Last, CrapsExtra[] Extras);

    private static int? NullableInt(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
    private static decimal? NullableDecimal(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();
    private static IReadOnlyList<RoundAuditFinding> Error(string code, string message) => [new(code, "error", message)];
}
