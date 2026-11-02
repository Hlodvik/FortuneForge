using System.Collections.Immutable;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Craps;
using FortuneForge.Games.Dice;
using FortuneForge.Server.Games.Craps;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Replays paid Craps from its accepted state journal and reconciles its wallet ledgers.</summary>
internal static class PaidCrapsReplayAudit
{
    private const string EvidenceKind = "craps-private-dice-and-bets-v1";
    private const string ReceiptVersion = "server-settlement-receipt-v1";

    internal static bool Supports(RoundAuditSummary summary) =>
        summary.GameId == "craps" && summary.EvidenceKind == EvidenceKind &&
        summary.AuditorVersion == ReceiptVersion;

    internal static IReadOnlyList<RoundAuditFinding> Inspect(
        RoundAuditSummary summary, string? payloadJson, IReadOnlyList<string> eventPayloads)
    {
        if (!Supports(summary)) return [];
        if (payloadJson is null) return Error("craps-paid-evidence-missing", "The paid Craps terminal evidence is missing.");
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var round = ReadRound(root.GetProperty("data"));
            var participant = summary.Participants.Count == 1 ? summary.Participants[0] : null;
            if (participant is null || !participant.IsAccountBacked ||
                root.GetProperty("sourceCollection").GetString() != "crapsPaidRounds" ||
                root.GetProperty("sourceDocumentId").GetString() != summary.RoundId ||
                round.Id.ToString("N") != summary.RoundId || round.UserId != participant.ActorId ||
                round.State.Bet.PlayerId != round.UserId || round.State.Phase != CrapsPassLinePhase.Resolved ||
                round.CompletedAtUtc is null || !SameTime(round.StartedAtUtc, summary.StartedAtUtc) ||
                !SameTime(round.CompletedAtUtc.Value, summary.CompletedAtUtc) ||
                round.TotalWagerCents != root.GetProperty("wagerCents").GetInt64() ||
                round.TotalReturnCents != root.GetProperty("payoutCents").GetInt64() ||
                root.GetProperty("netCents").GetInt64() != round.TotalReturnCents - round.TotalWagerCents ||
                participant.WagerCents != round.TotalWagerCents || participant.PayoutCents != round.TotalReturnCents ||
                participant.NetCents != round.TotalReturnCents - round.TotalWagerCents)
                return Error("craps-paid-terminal-mismatch", "The paid Craps round and its private settlement receipt disagree.");

            var events = eventPayloads.Select(ReadEvent).OrderBy(item => item.Version).ToArray();
            if (events.Any(item => item.GameId != "craps" || item.InstanceId != summary.RoundId ||
                item.Participants.Length != 1 || item.Participants[0] != round.UserId))
                return Error("craps-paid-action-identity-mismatch", "A paid Craps observation belongs to another game, round, or account.");
            if (events.Length < 2 || events[0].Version != 0 || events[0].Kind != "started" ||
                events[^1].Version != round.Revision || events[^1].Kind != "rolled" ||
                !SameTime(events[0].When, round.StartedAtUtc) ||
                !SameTime(events[^1].When, round.CompletedAtUtc.Value))
                return Error("craps-paid-action-history-missing", "The paid Craps state journal lacks its opening or final accepted action.");

            var first = events[0].State;
            if (first.Pass.Phase != CrapsPassLinePhase.ComeOut || first.Pass.Point is not null ||
                first.Pass.Rolls.Length != 0 || first.Pass.LastOutcome is not null ||
                first.Pass.Bet != round.State.Bet || !ValidStake(first.Pass.Bet.Stake) ||
                first.Extras.Length > 3 || first.Extras.Any(extra => !ValidInitialExtra(extra)) ||
                first.Extras.Select(extra => extra.Kind).Distinct(StringComparer.Ordinal).Count() != first.Extras.Length ||
                first.WagerCents != Cents(first.Pass.Bet.Stake + first.Extras.Sum(extra => extra.Stake)) ||
                first.ReturnCents != 0 || !ValidJournal(null, events[0]))
                return Error("craps-paid-opening-mismatch", "The paid Craps opening wager or side bets are invalid.");

            for (var index = 1; index < events.Length; index++)
            {
                var previous = events[index - 1];
                var current = events[index];
                if (current.Version != index || current.When < previous.When ||
                    current.State.Pass.Bet != first.Pass.Bet ||
                    previous.State.Pass.Phase == CrapsPassLinePhase.Resolved || !ValidJournal(previous, current))
                    return Error("craps-paid-action-sequence-mismatch", "The paid Craps journal changes the player or wager, skips an action, or continues after settlement.");
                if (current.Kind == "odds-placed")
                {
                    if (!ValidOddsPlacement(previous.State, current.State))
                        return Error("craps-paid-odds-mismatch", "An odds placement is inconsistent with the open point or wallet stake.");
                }
                else if (current.Kind == "wager-adjusted")
                {
                    if (!ValidWagerAdjustment(previous.State, current.State))
                        return Error("craps-paid-wager-mismatch", "A wager adjustment changes a locked bet, settled history, target roll, or wallet stake.");
                }
                else if (current.Kind == "rolled")
                {
                    if (!ValidRoll(previous.State, current.State))
                        return Error("craps-paid-roll-mismatch", "A die roll, side bet, odds return, or pass-line transition differs from the independent calculation.");
                }
                else return Error("craps-paid-action-kind-invalid", "The paid Craps journal contains an unsupported accepted action.");
            }

            var last = events[^1].State;
            if (!SameState(last.Pass, round.State) || !SameExtras(last.Extras, round.Extras) ||
                last.WagerCents != round.TotalWagerCents || last.ReturnCents != round.TotalReturnCents ||
                last.Pass.Phase != CrapsPassLinePhase.Resolved || last.SchemaVersion != round.SchemaVersion ||
                !SameActions(last.Actions, round.Actions))
                return Error("craps-paid-action-terminal-mismatch", "The paid Craps final observation differs from the settled private round.");
            return [];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or
            FormatException or ArithmeticException or OverflowException or IndexOutOfRangeException or NotSupportedException)
        {
            return Error("craps-paid-evidence-invalid", "The paid Craps round or action journal cannot be parsed for replay.");
        }
    }

    internal static async Task<IReadOnlyList<RoundAuditFinding>> InspectFinancialAsync(
        FirestoreDb database, RoundAuditSummary summary, string? payloadJson, CancellationToken ct)
    {
        if (!Supports(summary)) return [];
        if (payloadJson is null) return Error("craps-paid-financial-evidence-missing", "The paid Craps financial evidence is missing.");
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var round = ReadRound(document.RootElement.GetProperty("data"));
            var sourceTask = database.Collection("crapsPaidRounds").Document(summary.RoundId).GetSnapshotAsync(ct);
            var prefix = $"craps-{summary.RoundId}-";
            var ledgerTask = database.Collection("balanceTransactions").OrderBy(FieldPath.DocumentId)
                .StartAt(prefix).EndAt(prefix + "\uf8ff").GetSnapshotAsync(ct);
            await Task.WhenAll(sourceTask, ledgerTask);
            var source = await sourceTask;
            if (!source.Exists || Text(source, "userId") != round.UserId ||
                Text(source, "roundId") != summary.RoundId ||
                Text(source, "phase") != CrapsPassLinePhase.Resolved.ToString() ||
                !source.TryGetValue<long>("revision", out var revision) || revision != round.Revision ||
                !source.TryGetValue<string>("roundJson", out var sourceJson) || string.IsNullOrWhiteSpace(sourceJson))
                return Error("craps-paid-source-mismatch", "The stored paid Craps round differs from its private settlement evidence.");
            using var sourceDocument = JsonDocument.Parse(sourceJson);
            if (!SameRound(round, ReadRound(sourceDocument.RootElement)))
                return Error("craps-paid-source-mismatch", "The stored paid Craps round differs from its private settlement evidence.");

            var ledgers = (await ledgerTask).Documents;
            if (round.SchemaVersion >= 2)
                return await InspectContinuationFinancialAsync(database, summary, payloadJson, round, ledgers, ct);
            var odds = round.Extras.SingleOrDefault(extra => extra.Kind == "odds");
            var sideReturnCents = Cents(round.Extras.Where(extra => extra.Kind != "odds").Sum(extra => extra.TotalReturn ?? 0m));
            var terminalReturnCents = round.TotalReturnCents - sideReturnCents;
            var payoutAmounts = round.State.Rolls.Length == 1
                ? (round.TotalReturnCents > 0 ? new[] { round.TotalReturnCents } : [])
                : new[] { sideReturnCents, terminalReturnCents }.Where(value => value > 0).ToArray();
            var expectedCount = 1 + (odds is null ? 0 : 1) + payoutAmounts.Length;
            if (ledgers.Count != expectedCount)
                return Error("craps-paid-ledger-set-mismatch", "The paid Craps round has a missing or extra wallet ledger.");
            var initialCents = Cents(round.State.Bet.Stake + round.Extras.Where(extra => extra.Kind != "odds").Sum(extra => extra.Stake));
            var entry = ledgers.SingleOrDefault(item => item.Id == $"craps-{summary.RoundId}-entry");
            if (entry is null || !ValidLedger(entry, round.UserId, "craps-wager", -initialCents))
                return Error("craps-paid-ledger-mismatch", "The paid Craps opening debit differs from the accepted wager.");
            if (odds is not null)
            {
                var oddsLedgers = ledgers.Where(item => item.Id.EndsWith("-odds", StringComparison.Ordinal)).ToArray();
                if (oddsLedgers.Length != 1 || !ValidLedger(oddsLedgers[0], round.UserId, "craps-odds", -Cents(odds.Stake)) ||
                    !oddsLedgers[0].Id.StartsWith(prefix, StringComparison.Ordinal) ||
                    oddsLedgers[0].Id != $"{prefix}{Text(oddsLedgers[0], "idempotencyKey")}-odds")
                    return Error("craps-paid-ledger-mismatch", "The paid Craps odds debit differs from the accepted wager.");
            }
            var payouts = ledgers.Where(item => Text(item, "type") == "craps-payout").ToArray();
            var sortedPayoutAmounts = payoutAmounts.Order().ToArray();
            if (payouts.Length != payoutAmounts.Length || terminalReturnCents < 0 ||
                !payouts.Select(item => (Item: item, Cents: LedgerCents(item)))
                    .OrderBy(item => item.Cents).Select((item, index) =>
                        item.Item.Id == $"{prefix}{Text(item.Item, "idempotencyKey")}-payout" &&
                        ValidLedger(item.Item, round.UserId, "craps-payout", sortedPayoutAmounts[index])).All(valid => valid))
                return Error("craps-paid-ledger-mismatch", "The paid Craps payout credit differs from the independent return.");
            if (round.TotalWagerCents != initialCents + (odds is null ? 0 : Cents(odds.Stake)) ||
                summary.Participants.Count != 1 || summary.Participants[0].WagerCents != round.TotalWagerCents ||
                summary.Participants[0].PayoutCents != round.TotalReturnCents)
                return Error("craps-paid-financial-receipt-mismatch", "The paid Craps wager and payout totals disagree with the wallet debits and credit.");
            return [];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or
            FormatException or ArithmeticException or OverflowException or IndexOutOfRangeException or NotSupportedException)
        {
            return Error("craps-paid-financial-evidence-invalid", "The paid Craps source round or wallet records cannot be reconciled.");
        }
    }

    private static PaidCrapsRound ReadRound(JsonElement element) => new(
        Get(element, "id").GetGuid(), Get(element, "userId").GetString() ?? "",
        ReadPass(Get(element, "state")), ReadExtras(Get(element, "extras")),
        Get(element, "totalWagerCents").GetInt64(), Get(element, "totalReturnCents").GetInt64(),
        Get(element, "startedAtUtc").GetDateTime().ToUniversalTime(),
        Get(element, "completedAtUtc").ValueKind == JsonValueKind.Null ? null :
            Get(element, "completedAtUtc").GetDateTime().ToUniversalTime(),
        Get(element, "revision").GetInt64(), ReadActions(element), SchemaVersion(element));

    private static CrapsPassLineState ReadPass(JsonElement element)
    {
        var bet = Get(element, "bet");
        var last = Get(element, "lastOutcome");
        return new(new CrapsPassLineBet(Get(bet, "playerId").GetString() ?? "", Get(bet, "stake").GetDecimal()),
            (CrapsPassLinePhase)Get(element, "phase").GetInt32(),
            Get(element, "point").ValueKind == JsonValueKind.Null ? null : Get(element, "point").GetInt32(),
            Get(element, "rolls").EnumerateArray().Select(ReadDice).ToImmutableArray(),
            last.ValueKind == JsonValueKind.Null ? null : new CrapsRollOutcome(
                ReadDice(Get(last, "dice")), (CrapsRollResult)Get(last, "result").GetInt32(),
                Get(last, "isTerminal").GetBoolean(), Get(last, "totalReturn").ValueKind == JsonValueKind.Null
                    ? null : Get(last, "totalReturn").GetDecimal()));
    }

    private static DicePair ReadDice(JsonElement element) => new(
        new DieValue(Get(Get(element, "first"), "value").GetInt32()),
        new DieValue(Get(Get(element, "second"), "value").GetInt32()));

    private static PaidCrapsExtra[] ReadExtras(JsonElement element) =>
        JsonSerializer.Deserialize<PaidCrapsExtra[]>(element.GetRawText(), RoundAuditCapture.Json) ??
        throw new JsonException("Missing paid Craps bets.");

    private static JsonElement Get(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) ? property :
        element.GetProperty(char.ToUpperInvariant(name[0]) + name[1..]);

    private static ObservedEvent ReadEvent(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var state = root.GetProperty("state");
        var pass = ReadPass(state.GetProperty("state"));
        var extras = ReadExtras(state.GetProperty("extras"));
        return new(root.GetProperty("version").GetInt64(), root.GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime(),
            root.GetProperty("eventKind").GetString() ?? "", new(pass, extras,
                state.GetProperty("totalWagerCents").GetInt64(), state.GetProperty("totalReturnCents").GetInt64(),
                ReadActions(state), SchemaVersion(state)), root.GetProperty("gameId").GetString() ?? "",
            root.GetProperty("instanceId").GetString() ?? "",
            root.GetProperty("participants").EnumerateArray().Select(item => item.GetString() ?? "").ToArray());
    }

    private static PaidCrapsAction[]? ReadActions(JsonElement element) =>
        TryGet(element, "actions", out var actions) && actions.ValueKind != JsonValueKind.Null
            ? JsonSerializer.Deserialize<PaidCrapsAction[]>(actions.GetRawText(), RoundAuditCapture.Json)
            : null;

    private static int SchemaVersion(JsonElement element) =>
        TryGet(element, "schemaVersion", out var version) ? version.GetInt32() : 1;

    private static bool TryGet(JsonElement element, string name, out JsonElement property) =>
        element.TryGetProperty(name, out property) ||
        element.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out property);

    private static bool SameActions(PaidCrapsAction[]? first, PaidCrapsAction[]? second) =>
        first is null ? second is null : second is not null && first.SequenceEqual(second);

    // The journal may begin midway through a historical round. Earlier accepted observations
    // remain schema 1; only newly accepted actions receive identities and revision preconditions.
    private static bool ValidJournal(ObservedEvent? previous, ObservedEvent current)
    {
        var state = current.State;
        if (state.SchemaVersion == 1)
            return state.Actions is null && previous?.State.SchemaVersion is not 2 &&
                state.Extras.All(extra => extra.WagerId is null && extra.PlacedForRoll is null &&
                    extra.ResolvedOnRoll is null && !extra.Cancelled);
        if (state.SchemaVersion != 2 || state.Actions is not { Length: > 0 } actions ||
            state.WagerCents < Cents(state.Pass.Bet.Stake) || state.ReturnCents < 0) return false;
        var oldActions = previous?.State.Actions ?? [];
        if (actions.Length != oldActions.Length + 1 ||
            !oldActions.SequenceEqual(actions.Take(oldActions.Length)) ||
            actions.Select(action => action.OperationId).Distinct(StringComparer.Ordinal).Count() != actions.Length)
            return false;
        var action = actions[^1];
        if (!Guid.TryParseExact(action.OperationId, "N", out var operation) || operation == Guid.Empty ||
            action.Revision != current.Version || action.WalletBeforeCents < 0 || action.WalletAfterCents < 0)
            return false;
        long expectedDelta;
        if (previous is null)
        {
            if (action.Kind != "start" || current.Version != 0 || action.WagerKind is not null ||
                action.Stake != state.Pass.Bet.Stake || action.ExpectedRevision is not null ||
                action.ExpectedRollNumber is not null || state.Extras.Any(extra =>
                    extra.WagerId != action.OperationId + ":" + extra.Kind || extra.PlacedForRoll != 1))
                return false;
            expectedDelta = -state.WagerCents;
        }
        else
        {
            if (action.ExpectedRevision != previous.Version ||
                action.ExpectedRollNumber != previous.State.Pass.Rolls.Length + 1) return false;
            if (current.Kind == "rolled")
            {
                if (action.Kind != "roll" || action.WagerKind is not null || action.Stake is not null) return false;
                expectedDelta = checked(state.ReturnCents - previous.State.ReturnCents);
                if (expectedDelta < 0) return false;
            }
            else if (current.Kind == "wager-adjusted")
            {
                if (action.Kind is not ("wager" or "odds") || action.Stake is not { } stake ||
                    stake < 0 || stake > 100 || stake % 1m != 0 ||
                    action.WagerKind is not ("odds" or "field" or "any-seven" or "any-craps") ||
                    action.Kind == "odds" && action.WagerKind != "odds") return false;
                expectedDelta = checked(previous.State.WagerCents - state.WagerCents);
                if (expectedDelta == 0 || state.ReturnCents != previous.State.ReturnCents) return false;
            }
            else return false;
        }
        return checked(action.WalletAfterCents - action.WalletBeforeCents) == expectedDelta;
    }

    private static bool ValidWagerAdjustment(Observed before, Observed after)
    {
        if (before.Pass.Phase != CrapsPassLinePhase.Point || before.Pass.Point is null ||
            !SameState(before.Pass, after.Pass) || after.ReturnCents != before.ReturnCents ||
            after.Actions is not { Length: > 0 }) return false;
        var action = after.Actions[^1];
        var kind = action.WagerKind;
        var target = action.Stake!.Value;
        var active = before.Extras.Select((extra, index) => (Extra: extra, Index: index))
            .Where(item => item.Extra.Kind == kind && !item.Extra.Resolved).ToArray();
        if (active.Length > 1) return false;
        var previous = active.SingleOrDefault();
        var oldStake = active.Length == 0 ? 0m : previous.Extra.Stake;
        if (target == oldStake || after.WagerCents != checked(before.WagerCents + Cents(target - oldStake)))
            return false;
        var expected = before.Extras.ToList();
        if (active.Length == 1)
            expected[previous.Index] = previous.Extra with { Resolved = true, Won = false,
                TotalReturn = 0m, Cancelled = true, ResolvedOnRoll = null };
        if (target > 0m)
            expected.Add(new(kind!, target, false, false, null, action.OperationId + ":" + kind,
                before.Pass.Rolls.Length + 1));
        return SameExtras(expected, after.Extras) && after.Extras.Where(extra => extra.WagerId is not null)
            .Select(extra => extra.WagerId).Distinct(StringComparer.Ordinal).Count() ==
                after.Extras.Count(extra => extra.WagerId is not null);
    }

    // Recompute the pass-line rules here instead of invoking the production settlement engine.
    private static CrapsPassLineState IndependentRoll(CrapsPassLineState before, DicePair dice)
    {
        if (dice.First.Value is < 1 or > 6 || dice.Second.Value is < 1 or > 6)
            throw new InvalidOperationException("Invalid Craps dice.");
        CrapsRollResult result;
        var terminal = false;
        var phase = before.Phase;
        var point = before.Point;
        decimal? totalReturn = null;
        if (phase == CrapsPassLinePhase.ComeOut)
        {
            result = dice.Total switch
            {
                7 or 11 => CrapsRollResult.NaturalWin,
                2 or 3 or 12 => CrapsRollResult.CrapsLoss,
                _ => CrapsRollResult.PointEstablished,
            };
            terminal = result != CrapsRollResult.PointEstablished;
            phase = terminal ? CrapsPassLinePhase.Resolved : CrapsPassLinePhase.Point;
            if (!terminal) point = dice.Total;
        }
        else if (phase == CrapsPassLinePhase.Point && point is 4 or 5 or 6 or 8 or 9 or 10)
        {
            result = dice.Total == point ? CrapsRollResult.PointHit :
                dice.Total == 7 ? CrapsRollResult.SevenOut : CrapsRollResult.NoDecision;
            terminal = result != CrapsRollResult.NoDecision;
            if (terminal) phase = CrapsPassLinePhase.Resolved;
        }
        else throw new InvalidOperationException("Invalid Craps phase or point.");
        if (terminal) totalReturn = result is CrapsRollResult.NaturalWin or CrapsRollResult.PointHit
            ? before.Bet.Stake * 2m : 0m;
        return before with { Phase = phase, Point = point, Rolls = before.Rolls.Add(dice),
            LastOutcome = new(dice, result, terminal, totalReturn) };
    }

    private static bool ValidOddsPlacement(Observed before, Observed after)
    {
        if (before.Pass.Phase != CrapsPassLinePhase.Point || before.Pass.Point is null ||
            before.Extras.Any(extra => extra.Kind == "odds") || after.Extras.Length != before.Extras.Length + 1 ||
            !SameState(before.Pass, after.Pass) || !before.Extras.SequenceEqual(after.Extras.Take(before.Extras.Length)) ||
            after.ReturnCents != before.ReturnCents) return false;
        var odds = after.Extras[^1];
        return odds.Kind == "odds" && ValidStake(odds.Stake) && !odds.Resolved && !odds.Won &&
            odds.TotalReturn is null && after.WagerCents == before.WagerCents + Cents(odds.Stake);
    }

    private static async Task<IReadOnlyList<RoundAuditFinding>> InspectContinuationFinancialAsync(
        FirestoreDb database, RoundAuditSummary summary, string payloadJson, PaidCrapsRound round,
        IReadOnlyList<DocumentSnapshot> ledgers, CancellationToken ct)
    {
        // Read the complete committed stream. Financial effects are derived from independently
        // replayed transitions, never from the claimed wallet delta in the action itself.
        var snapshots = await database.Collection(GameSecurityEventDocuments.CollectionName)
            .WhereEqualTo("gameId", "craps").WhereEqualTo("instanceId", summary.RoundId).GetSnapshotAsync(ct);
        var payloads = new List<string>(snapshots.Count);
        foreach (var item in snapshots.Documents)
        {
            var payload = Text(item, "payloadJson");
            if (string.IsNullOrEmpty(payload) || GameSecurityEventDocuments.Hash(payload) != item.Id ||
                Text(item, "sha256") != item.Id)
                return Error("craps-paid-financial-journal-mismatch", "A paid Craps financial observation has an invalid fingerprint.");
            payloads.Add(payload);
        }
        var replayFindings = Inspect(summary, payloadJson, payloads);
        if (replayFindings.Count != 0) return replayFindings;
        var events = payloads.Select(ReadEvent).OrderBy(item => item.Version).ToArray();
        var expected = new List<ExpectedLedger>();
        var prefix = $"craps-{summary.RoundId}-";
        var opening = events[0];
        var openingAction = opening.State.Actions?.LastOrDefault();
        expected.Add(new(prefix + "entry", "craps-wager", -opening.State.WagerCents,
            openingAction?.OperationId, openingAction?.WalletAfterCents));
        for (var index = 1; index < events.Length; index++)
        {
            var before = events[index - 1];
            var current = events[index];
            var action = current.State.Actions?.LastOrDefault();
            if (current.State.SchemaVersion >= 2)
            {
                if (action is null)
                    return Error("craps-paid-financial-journal-mismatch", "A paid Craps action lacks a financial identity.");
                var amount = current.Kind == "rolled"
                    ? checked(current.State.ReturnCents - before.State.ReturnCents)
                    : checked(before.State.WagerCents - current.State.WagerCents);
                if (amount == 0) continue;
                var suffix = action.Kind == "roll" ? "payout" : action.Kind;
                var type = action.Kind switch
                {
                    "roll" => "craps-payout", "odds" => "craps-odds", _ => "craps-wager-adjustment",
                };
                expected.Add(new(prefix + action.OperationId + "-" + suffix, type, amount,
                    action.OperationId, action.WalletAfterCents));
            }
            else if (current.Kind == "odds-placed")
                expected.Add(new(null, "craps-odds", checked(before.State.WagerCents - current.State.WagerCents), null, null));
            else if (current.Kind == "rolled" && current.State.ReturnCents > before.State.ReturnCents)
                expected.Add(new(null, "craps-payout", checked(current.State.ReturnCents - before.State.ReturnCents), null, null));
        }
        if (ledgers.Count != expected.Count)
            return Error("craps-paid-ledger-set-mismatch", "The paid Craps round has a missing or extra wallet ledger.");
        if (ledgers.Select(item => Text(item, "idempotencyKey")).Distinct(StringComparer.Ordinal).Count() != ledgers.Count)
            return Error("craps-paid-ledger-mismatch", "A paid Craps wallet operation has more than one financial effect.");
        var remaining = ledgers.ToDictionary(item => item.Id, StringComparer.Ordinal);
        // Reserve exact new identities before matching the unknown IDs of historical actions.
        foreach (var expectation in expected.OrderBy(item => item.Id is null ? 1 : 0))
        {
            DocumentSnapshot? ledger;
            if (expectation.Id is { } id)
                remaining.TryGetValue(id, out ledger);
            else
                ledger = remaining.Values.FirstOrDefault(item => ValidExpectedLedger(item, expectation, round.UserId, prefix));
            if (ledger is null || !ValidExpectedLedger(ledger, expectation, round.UserId, prefix))
                return Error("craps-paid-ledger-mismatch", "A paid Craps wallet amount, operation identity, or resulting balance differs from its replayed action.");
            remaining.Remove(ledger.Id);
        }
        var netWagerCents = checked(-expected.Where(item => item.Type != "craps-payout").Sum(item => item.Cents));
        var payoutCents = checked(expected.Where(item => item.Type == "craps-payout").Sum(item => item.Cents));
        if (remaining.Count != 0 || round.TotalWagerCents != netWagerCents || round.TotalReturnCents != payoutCents ||
            summary.Participants.Count != 1 || summary.Participants[0].WagerCents != netWagerCents ||
            summary.Participants[0].PayoutCents != payoutCents ||
            summary.Participants[0].NetCents != checked(payoutCents - netWagerCents))
            return Error("craps-paid-financial-receipt-mismatch", "Paid Craps refunds, debits, payouts, and settlement totals disagree.");
        return [];
    }

    private static bool ValidExpectedLedger(DocumentSnapshot item, ExpectedLedger expected, string userId, string prefix)
    {
        if (!ValidLedger(item, userId, expected.Type, expected.Cents) ||
            expected.OperationId is { } operationId && Text(item, "idempotencyKey") != operationId ||
            expected.WalletAfterCents is { } after &&
                (!item.TryGetValue<double>("balanceAfter", out var balance) || !SameCents(balance, after))) return false;
        if (expected.Id is { } id) return item.Id == id;
        var suffix = expected.Type == "craps-odds" ? "odds" : "payout";
        return item.Id == prefix + Text(item, "idempotencyKey") + "-" + suffix;
    }

    private sealed record ExpectedLedger(string? Id, string Type, long Cents, string? OperationId, long? WalletAfterCents);

    private static bool ValidRoll(Observed before, Observed after)
    {
        if (after.Pass.Rolls.Length != before.Pass.Rolls.Length + 1 ||
            !after.Pass.Rolls.Take(before.Pass.Rolls.Length).SequenceEqual(before.Pass.Rolls) ||
            after.WagerCents != before.WagerCents || after.Extras.Length != before.Extras.Length)
            return false;
        var dice = after.Pass.Rolls[^1];
        var expected = IndependentRoll(before.Pass, dice);
        if (!SameState(expected, after.Pass)) return false;
        var rollNumber = expected.Rolls.Length;
        if (before.Extras.Any(extra => !extra.Resolved && extra.Kind != "odds" &&
            extra.PlacedForRoll is { } target && target != rollNumber)) return false;
        var expectedExtras = before.Extras.Select(extra =>
            extra.Resolved || extra.Kind == "odds" && expected.Phase != CrapsPassLinePhase.Resolved
                ? extra : Settle(extra, expected, dice.Total)).ToArray();
        var newExtraReturn = expectedExtras.Sum(extra => extra.TotalReturn ?? 0m) -
            before.Extras.Sum(extra => extra.TotalReturn ?? 0m);
        var expectedReturn = checked(before.ReturnCents + Cents(newExtraReturn +
            (expected.Phase == CrapsPassLinePhase.Resolved ? expected.LastOutcome?.TotalReturn ?? 0m : 0m)));
        return SameExtras(expectedExtras, after.Extras) && after.ReturnCents == expectedReturn;
    }

    private static PaidCrapsExtra Settle(PaidCrapsExtra extra, CrapsPassLineState state, int total)
    {
        var payout = extra.Kind switch
        {
            "field" when total is 2 or 12 => extra.Stake * 3m,
            "field" when total is 3 or 4 or 9 or 10 or 11 => extra.Stake * 2m,
            "any-seven" when total == 7 => extra.Stake * 5m,
            "any-craps" when total is 2 or 3 or 12 => extra.Stake * 8m,
            "odds" when state.LastOutcome?.Result == CrapsRollResult.PointHit => state.Point switch
            {
                4 or 10 => extra.Stake * 3m,
                5 or 9 => extra.Stake * 2.5m,
                6 or 8 => extra.Stake * 2.2m,
                _ => throw new InvalidOperationException("Invalid Craps point for odds."),
            },
            _ => 0m,
        };
        return extra with { Resolved = true, Won = payout > 0, TotalReturn = payout,
            ResolvedOnRoll = extra.WagerId is null ? null : state.Rolls.Length };
    }

    private static bool ValidInitialExtra(PaidCrapsExtra extra) =>
        extra.Kind is "field" or "any-seven" or "any-craps" && ValidStake(extra.Stake) &&
        !extra.Resolved && !extra.Won && extra.TotalReturn is null && !extra.Cancelled && extra.ResolvedOnRoll is null;

    private static bool ValidStake(decimal stake) => stake is >= 1m and <= 100m && stake % 1m == 0m;
    private static long Cents(decimal value) => checked((long)(value * 100m));
    private static bool SameTime(DateTime first, DateTime second) =>
        first.Kind == DateTimeKind.Utc && second.Kind == DateTimeKind.Utc && Math.Abs((first - second).Ticks) < 10;
    private static bool SameState(CrapsPassLineState first, CrapsPassLineState second) =>
        first.Bet == second.Bet && first.Phase == second.Phase && first.Point == second.Point &&
        first.Rolls.SequenceEqual(second.Rolls) && first.LastOutcome == second.LastOutcome;
    private static bool SameExtras(IReadOnlyList<PaidCrapsExtra> first, IReadOnlyList<PaidCrapsExtra> second) =>
        first.SequenceEqual(second);
    private static bool SameRound(PaidCrapsRound first, PaidCrapsRound? second) =>
        second is not null && first.Id == second.Id && first.UserId == second.UserId &&
        SameState(first.State, second.State) && SameExtras(first.Extras, second.Extras) &&
        first.TotalWagerCents == second.TotalWagerCents && first.TotalReturnCents == second.TotalReturnCents &&
        SameTime(first.StartedAtUtc, second.StartedAtUtc) &&
        first.CompletedAtUtc is { } completed && second.CompletedAtUtc is { } other && SameTime(completed, other) &&
        first.Revision == second.Revision && first.SchemaVersion == second.SchemaVersion &&
        SameActions(first.Actions, second.Actions);

    private static bool ValidLedger(DocumentSnapshot item, string userId, string type, long cents) =>
        Text(item, "transactionId") == item.Id && Text(item, "userId") == userId &&
        Text(item, "currencyId") == "slotsCredits" && Text(item, "type") == type &&
        Guid.TryParse(Text(item, "idempotencyKey"), out _) &&
        item.TryGetValue<double>("amount", out var amount) && SameCents(amount, cents) &&
        item.TryGetValue<double>("balanceAfter", out var after) && double.IsFinite(after) && after >= 0;
    private static long LedgerCents(DocumentSnapshot item) =>
        item.TryGetValue<double>("amount", out var value) && double.IsFinite(value)
            ? checked((long)Math.Round(value * 100d)) : long.MinValue;
    private static bool SameCents(double value, long cents) =>
        double.IsFinite(value) && Math.Abs(value * 100d - cents) < 0.000001d;
    private static string Text(DocumentSnapshot item, string field) =>
        item.TryGetValue<string>(field, out var value) ? value : "";
    private static IReadOnlyList<RoundAuditFinding> Error(string code, string message) =>
        [new RoundAuditFinding(code, "error", message)];

    private sealed record Observed(CrapsPassLineState Pass, PaidCrapsExtra[] Extras, long WagerCents, long ReturnCents,
        PaidCrapsAction[]? Actions, int SchemaVersion);
    private sealed record ObservedEvent(long Version, DateTime When, string Kind, Observed State,
        string GameId, string InstanceId, string[] Participants);
}
