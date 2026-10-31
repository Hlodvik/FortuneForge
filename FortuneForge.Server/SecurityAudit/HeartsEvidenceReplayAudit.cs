using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Games.Hearts;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Independently deals and scores persisted Hearts hands, including prior hands from the engine journal.</summary>
internal static class HeartsEvidenceReplayAudit
{
    internal static bool Supports(RoundAuditSummary summary) => summary.GameId == "hearts" &&
        (summary.AuditorVersion == PaidHeartsRoundAudit.Version && summary.EvidenceKind == "paid-hearts-match-v1" ||
         summary.AuditorVersion == "free-engine-receipt-v1" && summary.EvidenceKind == "hearts-private-hands-and-tricks-v1");

    internal static IReadOnlyList<RoundAuditFinding> Inspect(
        RoundAuditSummary summary, string payloadJson, IReadOnlyList<string> eventPayloads)
    {
        if (!Supports(summary)) return [];
        try
        {
            using var terminal = JsonDocument.Parse(payloadJson);
            var paid = summary.AuditorVersion == PaidHeartsRoundAudit.Version;
            var match = paid ? ReadPaidMatch(terminal.RootElement) : null;
            var terminalState = paid ? match?.State : ReadState(terminal.RootElement.GetProperty("state"));
            if (terminalState is null)
                return Error("hearts-final-state-missing", "The terminal Hearts evidence has no match state.");
            var classification = paid ? CheckPaidClassification(summary, terminal.RootElement, match!) :
                CheckFreeClassification(summary, terminal.RootElement, terminalState);
            if (classification.Count > 0) return classification;

            var observations = new List<Observation>();
            foreach (var payload in eventPayloads)
            {
                using var eventDocument = JsonDocument.Parse(payload);
                var envelope = eventDocument.RootElement;
                var capturedState = envelope.GetProperty("state").GetProperty("state");
                observations.Add(new(envelope.GetProperty("version").GetInt64(),
                    envelope.GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime(),
                    capturedState.ValueKind == JsonValueKind.Null ? null : ReadState(capturedState),
                    capturedState.ValueKind == JsonValueKind.Null ? "" : capturedState.GetRawText()));
            }
            observations.Sort((a, b) => a.Version != b.Version ? a.Version.CompareTo(b.Version) :
                a.OccurredAtUtc.CompareTo(b.OccurredAtUtc));
            if (observations.Count == 0 || observations[0].Version != (paid ? 2 : 1) ||
                observations.Zip(observations.Skip(1)).Any(pair =>
                    pair.Second.Version < pair.First.Version || pair.Second.Version > pair.First.Version + 1 ||
                    pair.Second.OccurredAtUtc < pair.First.OccurredAtUtc))
                return Error("hearts-journal-sequence-incomplete", "Hearts engine observations have missing versions or reversed server times.");

            HeartsMatchState? previousState = null;
            var sawState = false;
            foreach (var observation in observations)
            {
                var state = observation.State;
                if (state is null)
                {
                    if (sawState)
                        return Error("hearts-intermediate-state-mismatch", "A Hearts engine observation lost its active match state.");
                    continue;
                }
                if (state.RoundNumber is < 1 || state.RoundNumber > terminalState.RoundNumber ||
                    state.TargetScore != terminalState.TargetScore ||
                    state.PassDirection != Direction(state.RoundNumber) ||
                    state.Round.PassDirection != state.PassDirection || ReplayRound(state.Round) is null)
                    return Error("hearts-intermediate-state-mismatch", "An intermediate Hearts board, pass, trick or score does not independently replay.");
                if (!sawState && (state.RoundNumber != 1 || Progress(state.Round) != 0 ||
                                  !SameScore(state.Score, [0, 0, 0, 0]) || state.Winner is not null))
                    return Error("hearts-journal-sequence-incomplete", "The first recorded Hearts state is not the initial deal.");
                if (previousState is not null && !Adjacent(previousState, state))
                    return Error("hearts-intermediate-transition-mismatch", "Adjacent Hearts observations do not represent one accepted pass, play or next-round transition.");
                sawState = true;
                previousState = state;
            }

            var completed = new Dictionary<int, (HeartsMatchState State, string RoundJson)>();
            foreach (var observation in observations.Where(item => item.State is not null))
            {
                var state = observation.State!;
                if (state.Round.Phase != HeartsPhase.Complete) continue;
                var roundJson = JsonSerializer.Serialize(state.Round, RoundAuditCapture.Json);
                if (completed.TryGetValue(state.RoundNumber, out var prior))
                {
                    if (prior.RoundJson != roundJson)
                        return Error("hearts-completed-hand-changed", "A completed Hearts hand changed in a later engine observation.");
                }
                else completed[state.RoundNumber] = (state, roundJson);
            }
            var finalRound = terminalState.RoundNumber;
            var expectedCompleted = finalRound - (terminalState.Round.Phase == HeartsPhase.Complete ? 0 : 1);
            if (expectedCompleted < 0 || completed.Count != expectedCompleted ||
                Enumerable.Range(1, expectedCompleted).Any(number => !completed.ContainsKey(number)))
                return Error("hearts-round-history-incomplete", "The engine journal lacks a completed hand needed to verify cumulative Hearts scoring.");
            if (observations[^1].State is null ||
                JsonSerializer.Serialize(observations[^1].State, RoundAuditCapture.Json) !=
                JsonSerializer.Serialize(terminalState, RoundAuditCapture.Json))
                return Error("hearts-terminal-journal-mismatch", "The final Hearts evidence differs from the last engine observation.");

            var cumulative = new int[4];
            var scoreBeforeRound = new Dictionary<int, int[]>();
            var scoreAfterRound = new Dictionary<int, int[]>();
            for (var number = 1; number <= expectedCompleted; number++)
            {
                scoreBeforeRound[number] = cumulative.ToArray();
                var state = completed[number].State;
                if (state.RoundNumber != number || state.TargetScore != terminalState.TargetScore ||
                    state.PassDirection != Direction(number) || state.Round.PassDirection != Direction(number))
                    return Error("hearts-round-sequence-mismatch", "Hearts round numbering, target score or pass direction changed.");
                var replay = ReplayRound(state.Round);
                if (replay is null)
                    return Error("hearts-card-replay-mismatch", $"The cards, passes, trick sequence or score in Hearts round {number} are invalid.");
                for (var seat = 0; seat < 4; seat++) cumulative[seat] = checked(cumulative[seat] + replay[seat]);
                scoreAfterRound[number] = cumulative.ToArray();
                if (!SameScore(state.Score, cumulative) || state.Winner != ExpectedWinner(cumulative, state.TargetScore))
                    return Error("hearts-match-score-mismatch", $"The cumulative Hearts score or winner after round {number} is inconsistent.");
            }
            if (terminalState.Round.Phase != HeartsPhase.Complete)
                scoreBeforeRound[finalRound] = cumulative.ToArray();
            foreach (var observation in observations.Where(item => item.State is not null))
            {
                var state = observation.State!;
                var scores = state.Round.Phase == HeartsPhase.Complete ?
                    scoreAfterRound[state.RoundNumber] : scoreBeforeRound[state.RoundNumber];
                if (!SameScore(state.Score, scores) ||
                    state.Winner != (state.Round.Phase == HeartsPhase.Complete ?
                        ExpectedWinner(scores, state.TargetScore) : null))
                    return Error("hearts-intermediate-match-score-mismatch", "An intermediate Hearts match score or winner differs from completed hands.");
            }
            if (terminalState.Round.Phase != HeartsPhase.Complete)
            {
                if (terminalState.PassDirection != Direction(finalRound) ||
                    terminalState.Round.PassDirection != Direction(finalRound) ||
                    ReplayRound(terminalState.Round) is null || !SameScore(terminalState.Score, cumulative) ||
                    terminalState.Winner is not null)
                    return Error("hearts-incomplete-hand-mismatch", "The unfinished terminal Hearts hand or prior cumulative score is inconsistent.");
            }
            else if (!SameScore(terminalState.Score, cumulative) ||
                     terminalState.Winner != ExpectedWinner(cumulative, terminalState.TargetScore))
                return Error("hearts-match-score-mismatch", "The terminal Hearts score or winner is inconsistent.");
            return paid ? [] : [new RoundAuditFinding("hearts-free-journal-non-atomic", "warning",
                "The saved cards and scores replay, but free Hearts keeps its live state in memory and cannot atomically commit every accepted action with Firestore.")];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or ArithmeticException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            return Error("hearts-replay-evidence-invalid", "The saved Hearts cards or journal cannot be parsed for independent replay.");
        }
    }

    internal static async Task<IReadOnlyList<RoundAuditFinding>> InspectFinancialAsync(
        FirestoreDb database, RoundAuditSummary summary, string payloadJson, CancellationToken ct)
    {
        if (summary.GameId != "hearts" || summary.AuditorVersion != PaidHeartsRoundAudit.Version) return [];
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var match = ReadPaidMatch(document.RootElement);
            if (match.Id != summary.RoundId || match.Players.Count != 4 || match.EntryCents <= 0)
                return Error("hearts-paid-financial-source-mismatch", "The paid Hearts match identity or entry amount is invalid for ledger review.");
            var sourceRef = database.Collection("heartsPaidMatches").Document(match.Id);
            var fundingRef = database.Collection("heartsPaidFunding").Document(match.Id);
            var revenueRef = database.Collection("heartsPaidRevenue").Document(match.Id);
            var sourceReads = await Task.WhenAll(sourceRef.GetSnapshotAsync(ct), fundingRef.GetSnapshotAsync(ct),
                revenueRef.GetSnapshotAsync(ct));
            var source = sourceReads[0]; var funding = sourceReads[1]; var revenue = sourceReads[2];
            if (!source.Exists || !funding.Exists || !revenue.Exists ||
                Text(source, "status") != "result" || Long(source, "entryCents") != match.EntryCents ||
                Text(funding, "matchId") != match.Id || Text(revenue, "matchId") != match.Id)
                return Error("hearts-paid-financial-source-missing", "The settled Hearts source, funding or revenue record is absent or has a different identity.");
            var currentSource = JsonSerializer.Deserialize<PaidHeartsMatch>(Text(source, "matchJson"), RoundAuditCapture.Json);
            if (currentSource is null || currentSource.Id != match.Id || currentSource.Status != "result" ||
                currentSource.EntryCents != match.EntryCents ||
                currentSource.Players.Count != match.Players.Count ||
                currentSource.Players.Any(player => match.Players.All(saved =>
                    saved.UserId != player.UserId || saved.Seat != player.Seat ||
                    saved.EntryId != player.EntryId || saved.HasLeft != player.HasLeft ||
                    saved.IsAccountBacked != player.IsAccountBacked || saved.PayoutCents != player.PayoutCents)))
                return Error("hearts-paid-financial-source-mismatch", "The current settled Hearts source differs from the immutable terminal participant and award evidence.");
            var humanEntry = checked(match.EntryCents * match.Players.Count(player => player.IsAccountBacked));
            var initialHuman = match.InitialHumanEntryCents > 0 ? match.InitialHumanEntryCents : humanEntry;
            var humanPayout = match.Players.Where(player => player.IsAccountBacked).Sum(player => player.PayoutCents);
            var botPrize = match.Players.Where(player => !player.IsAccountBacked).Sum(player => player.PayoutCents);
            var forfeited = document.RootElement.GetProperty("abandoned").GetBoolean() ? match.PotCents : 0L;
            if (Long(funding, "entryCents") != match.EntryCents ||
                Long(funding, "humanEntryCents") != humanEntry ||
                Long(funding, "houseFundedEntryCents") != match.PotCents - humanEntry ||
                Long(funding, "initialHumanEntryCents") != initialHuman ||
                Long(funding, "initialHouseFundedEntryCents") != match.PotCents - initialHuman ||
                Long(funding, "takeoverCount") != match.HouseTakeoverCount ||
                Long(funding, "grossPoolCents") != match.PotCents ||
                Long(revenue, "humanEntryCents") != humanEntry ||
                Long(revenue, "houseFundedEntryCents") != match.PotCents - humanEntry ||
                Long(revenue, "initialHumanEntryCents") != initialHuman ||
                Long(revenue, "houseStakeReclassifiedCents") != checked(match.HouseTakeoverCount * match.EntryCents) ||
                Long(revenue, "grossPoolCents") != match.PotCents ||
                Long(revenue, "humanPayoutCents") != humanPayout ||
                Long(revenue, "botPrizeCents") != botPrize ||
                Long(revenue, "forfeitedPoolCents") != forfeited ||
                Long(revenue, "houseNetCents") != humanEntry - humanPayout ||
                Text(revenue, "financialClassification") != "house-funded-hearts-pool-v1")
                return Error("hearts-paid-pool-ledger-mismatch", "The funded pool or revenue ledger differs from the independently reconstructed Hearts entry and awards.");
            var ledgers = match.Players.Where(player => player.IsAccountBacked)
                .Select(player => (player, Entry: true,
                    Id: $"{match.Id}-entry-{player.EntryId}", Amount: -match.EntryCents, Type: "hearts-entry"))
                .Concat(match.Players.Where(player => player.IsAccountBacked)
                    .Select(player => (player, Entry: false,
                        Id: $"{match.Id}-payout-{Hash(player.UserId)}", Amount: player.PayoutCents,
                        Type: "hearts-match-payout"))).ToArray();
            var reads = await Task.WhenAll(ledgers.Select(value => database.Collection("balanceTransactions")
                .Document(value.Id).GetSnapshotAsync(ct)));
            for (var index = 0; index < ledgers.Length; index++)
            {
                var expected = ledgers[index]; var ledger = reads[index];
                if (!ledger.Exists && !expected.Entry && expected.Amount == 0) continue;
                if (!ledger.Exists || Text(ledger, "transactionId") != expected.Id ||
                    Text(ledger, "userId") != expected.player.UserId ||
                    Text(ledger, "currencyId") != "slotsCredits" ||
                    Text(ledger, "type") != expected.Type || !LedgerMoney(ledger, expected.Amount))
                    return Error("hearts-paid-wallet-ledger-mismatch", "An account-backed Hearts entry or prize ledger is absent or differs from its saved settlement.");
                if (!expected.Entry && expected.Amount == 0)
                    return Error("hearts-paid-wallet-ledger-mismatch", "A zero-award Hearts player has an unexpected payout ledger.");
            }
            var ledgerPage = await database.Collection("balanceTransactions").OrderBy(FieldPath.DocumentId)
                .StartAt(match.Id + "-").EndAt(match.Id + "-\uf8ff").Limit(101).GetSnapshotAsync(ct);
            if (ledgerPage.Count > 100)
                return Error("hearts-paid-wallet-ledger-scan-limit", "More than 100 Hearts wallet entries require manual review for this match.");
            var expectedIds = ledgers.Where(value => value.Entry || value.Amount > 0)
                .Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
            var byId = ledgerPage.Documents.ToDictionary(value => value.Id, StringComparer.Ordinal);
            foreach (var ledger in ledgerPage.Documents)
            {
                if (expectedIds.Contains(ledger.Id)) continue;
                var refundPrefix = match.Id + "-queue-refund-";
                var entryPrefix = match.Id + "-entry-";
                if (ledger.Id.StartsWith(entryPrefix, StringComparison.Ordinal) &&
                    byId.ContainsKey(refundPrefix + ledger.Id[entryPrefix.Length..])) continue;
                if (!ledger.Id.StartsWith(refundPrefix, StringComparison.Ordinal))
                    return Error("hearts-paid-wallet-ledger-extra", "An unexpected Hearts wallet entry is linked to this match.");
                var entryId = ledger.Id[refundPrefix.Length..];
                var priorId = $"{match.Id}-entry-{entryId}";
                if (entryId.Length == 0 || !byId.TryGetValue(priorId, out var prior) ||
                    match.Players.Any(player => player.EntryId == entryId) ||
                    Text(ledger, "type") != "hearts-queue-refund" ||
                    Text(prior, "type") != "hearts-entry" ||
                    Text(ledger, "userId") != Text(prior, "userId") ||
                    Text(ledger, "currencyId") != "slotsCredits" ||
                    Text(prior, "currencyId") != "slotsCredits" ||
                    Long(ledger, "amountCents") != match.EntryCents ||
                    !LedgerMoney(ledger, match.EntryCents) || !LedgerMoney(prior, -match.EntryCents))
                    return Error("hearts-paid-wallet-ledger-extra", "A queue refund is missing its matching entry or has inconsistent wallet values.");
            }
            return [];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or ArithmeticException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            return Error("hearts-paid-financial-evidence-invalid", "The paid Hearts financial source or ledger cannot be parsed for independent review.");
        }
    }

    private static string Text(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) ? value : "";

    private static long Long(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : long.MinValue;

    private static bool LedgerMoney(DocumentSnapshot snapshot, long amountCents) =>
        Long(snapshot, "amountCents") == amountCents &&
        snapshot.TryGetValue<double>("amount", out var amount) && SameCents(amount, amountCents) &&
        snapshot.TryGetValue<long>("balanceAfterCents", out var balanceAfterCents) &&
        snapshot.TryGetValue<double>("balanceAfter", out var balanceAfter) && SameCents(balanceAfter, balanceAfterCents);

    private static bool SameCents(double amount, long cents) =>
        double.IsFinite(amount) && Math.Abs(amount * 100d - cents) < 0.000001d;

    private static PaidHeartsMatch ReadPaidMatch(JsonElement root) =>
        JsonSerializer.Deserialize<PaidHeartsMatch>(root.GetProperty("match").GetRawText(), RoundAuditCapture.Json) ??
        throw new JsonException("Missing paid Hearts match.");

    private static HeartsMatchState ReadState(JsonElement json) =>
        JsonSerializer.Deserialize<HeartsMatchState>(json.GetRawText(), RoundAuditCapture.Json) ??
        throw new JsonException("Missing Hearts state.");

    private static IReadOnlyList<RoundAuditFinding> CheckPaidClassification(
        RoundAuditSummary summary, JsonElement root, PaidHeartsMatch match)
    {
        var abandoned = root.GetProperty("abandoned").GetBoolean();
        if (match.Id != summary.RoundId || match.Status != "result" || match.CompletedAtUtc != summary.CompletedAtUtc ||
            match.CreatedAtUtc != summary.StartedAtUtc || match.State is null ||
            summary.RoundNumber != match.State.RoundNumber || match.EntryCents <= 0 ||
            match.Players.Count != 4 || match.Players.Select(player => player.Seat).Distinct().Count() != 4 ||
            match.Players.Select(player => player.UserId).Distinct(StringComparer.Ordinal).Count() != 4 ||
            root.GetProperty("sourceCollection").GetString() != "heartsPaidMatches" ||
            root.GetProperty("sourceDocumentId").GetString() != match.Id ||
            root.GetProperty("revenueDocumentId").GetString() != match.Id ||
            root.GetProperty("financialClassification").GetString() != "house-funded-hearts-pool-v1" ||
            summary.Participants.Count != 4)
            return Error("hearts-paid-classification-mismatch", "The paid Hearts match identity, seats, source or settlement classification differs from its audit.");
        var humanEntry = checked(match.EntryCents * match.Players.Count(player => player.IsAccountBacked));
        if (root.GetProperty("humanEntryCents").GetInt64() != humanEntry ||
            root.GetProperty("houseFundedEntryCents").GetInt64() != match.PotCents - humanEntry ||
            root.GetProperty("forfeitedPoolCents").GetInt64() != (abandoned ? match.PotCents : 0) ||
            !match.Players.Select((player, index) => (player, summary.Participants[index])).All(pair =>
                pair.player.UserId == pair.Item2.ActorId && pair.player.DisplayName == pair.Item2.DisplayName &&
                pair.player.IsAccountBacked == pair.Item2.IsAccountBacked &&
                pair.Item2.WagerCents == (pair.player.IsAccountBacked ? match.EntryCents : 0) &&
                pair.Item2.PayoutCents == pair.player.PayoutCents &&
                pair.Item2.NetCents == pair.player.PayoutCents - pair.Item2.WagerCents))
            return Error("hearts-paid-money-mismatch", "Paid Hearts participants or the house-funded pool do not reconcile.");
        var eligible = match.Players.Where(player => !player.HasLeft || !player.IsAccountBacked).ToArray();
        if (!abandoned && (eligible.Length == 0 || match.State.Winner is null))
            return Error("hearts-paid-outcome-mismatch", "A settled paid Hearts match lacks an eligible winner or a completed match.");
        var expected = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!abandoned)
        {
            var lowest = eligible.Min(player => match.State.Score.For(player.Seat));
            var winners = eligible.Where(player => match.State.Score.For(player.Seat) == lowest)
                .OrderBy(player => Hash($"tie\n{match.Id}\n{player.UserId}"), StringComparer.Ordinal).ToArray();
            for (var index = 0; index < winners.Length; index++)
                expected[winners[index].UserId] = checked(match.PotCents / winners.Length +
                    (index < match.PotCents % winners.Length ? 1 : 0));
        }
        if (match.Players.Any(player => player.PayoutCents != expected.GetValueOrDefault(player.UserId)) ||
            match.Players.Sum(player => player.PayoutCents) + (abandoned ? match.PotCents : 0) != match.PotCents ||
            !match.Winners.SequenceEqual(match.Players.Where(player => expected.ContainsKey(player.UserId)).Select(player => player.Seat)))
            return Error("hearts-paid-award-mismatch", "Paid Hearts winners, awards or prize pool differ from the independently scored match.");
        var expectedLedgers = match.Players.Where(player => player.IsAccountBacked && player.PayoutCents > 0)
            .Select(player => $"{match.Id}-payout-{Hash(player.UserId)}").ToArray();
        var recordedLedgers = root.GetProperty("payoutLedgerIds").EnumerateArray().Select(value => value.GetString()).ToArray();
        return expectedLedgers.SequenceEqual(recordedLedgers) ? [] :
            Error("hearts-paid-ledger-link-mismatch", "Paid Hearts payout ledger references do not match the account-backed awards.");
    }

    private static IReadOnlyList<RoundAuditFinding> CheckFreeClassification(
        RoundAuditSummary summary, JsonElement root, HeartsMatchState state)
    {
        var id = root.GetProperty("id").GetGuid().ToString("N");
        if (id != summary.RoundId || summary.RoundNumber != state.RoundNumber ||
            root.GetProperty("startedAtUtc").GetDateTime().ToUniversalTime() != summary.StartedAtUtc ||
            root.GetProperty("completedAtUtc").GetDateTime().ToUniversalTime() != summary.CompletedAtUtc ||
            root.GetProperty("isCancelled").GetBoolean() || root.GetProperty("isExpired").GetBoolean() ||
            state.Round.Phase != HeartsPhase.Complete || summary.Participants.Count != 1 ||
            !summary.Participants[0].IsAccountBacked ||
            summary.Participants[0].WagerCents != 0 || summary.Participants[0].PayoutCents != 0 ||
            summary.Participants[0].NetCents != 0)
            return Error("hearts-free-classification-mismatch", "The free Hearts completion identity, player or terminal phase differs from its audit.");
        return [];
    }

    // Returns independently accumulated points for a complete hand, or current points for an unfinished hand.
    private static int[]? ReplayRound(HeartsState state)
    {
        var deck = Deal(state.Seed);
        var hands = Enumerable.Range(0, 4).Select(_ => new List<PlayingCard>()).ToArray();
        for (var index = 0; index < deck.Length; index++) hands[index % 4].Add(deck[index]);
        var opening = new PlayingCard(CardRank.Two, CardSuit.Clubs);
        var initialLeader = (PlayerSeat)Array.FindIndex(hands, hand => hand.Contains(opening));
        var passes = state.SubmittedPasses;
        if (state.PassDirection == HeartsPassDirection.Hold)
        {
            if (passes.Count != 0 || state.Phase == HeartsPhase.Passing) return null;
        }
        else
        {
            if (passes.Count > 4 || passes.Select(pass => pass.Seat).Distinct().Count() != passes.Count) return null;
            foreach (var pass in passes)
            {
                if (!SeatValid(pass.Seat) || pass.Cards.Count != 3 || pass.Cards.Distinct().Count() != 3 ||
                    pass.Cards.Any(card => !hands[(int)pass.Seat].Contains(card))) return null;
            }
            if (passes.Count < 4)
            {
                if (state.Phase != HeartsPhase.Passing || state.CompletedTricks.Count != 0 ||
                    state.CurrentTrick.Plays.Count != 0 || state.HeartsBroken || state.Turn != initialLeader ||
                    state.CurrentTrick.Leader != initialLeader || !SameHands(state.Hands, hands) ||
                    !SameRoundScore(state.Scores, [0, 0, 0, 0])) return null;
                return [0, 0, 0, 0];
            }
            foreach (var pass in passes)
                foreach (var card in pass.Cards) hands[(int)pass.Seat].Remove(card);
            foreach (var pass in passes)
                foreach (var card in pass.Cards) hands[(int)PassTarget(pass.Seat, state.PassDirection)].Add(card);
        }
        if (state.Phase == HeartsPhase.Passing || state.CompletedTricks.Count > 13) return null;
        var leader = (PlayerSeat)Array.FindIndex(hands, hand => hand.Contains(opening));
        var scores = new int[4];
        var heartsBroken = false;
        for (var trickIndex = 0; trickIndex < state.CompletedTricks.Count; trickIndex++)
        {
            var trick = state.CompletedTricks[trickIndex];
            if (trick.Number != trickIndex + 1 || trick.Leader != leader || trick.Plays.Count != 4 ||
                !PlayTrick(trick.Plays, leader, hands, trickIndex == 0,
                    ref heartsBroken, out var winner, out var points) || winner != trick.Winner) return null;
            scores[(int)winner] = checked(scores[(int)winner] + points);
            leader = winner;
        }
        var current = state.CurrentTrick;
        if (current.Leader != leader || current.Plays.Count > 3 ||
            !PlayTrick(current.Plays, leader, hands, state.CompletedTricks.Count == 0,
                ref heartsBroken, out _, out _)) return null;
        if (state.CompletedTricks.Count == 13)
        {
            if (current.Plays.Count != 0 || state.Phase != HeartsPhase.Complete || hands.Any(hand => hand.Count != 0)) return null;
            var shooter = Array.FindIndex(scores, value => value == 26);
            if (shooter >= 0)
                for (var seat = 0; seat < 4; seat++) scores[seat] = seat == shooter ? 0 : 26;
        }
        else if (state.Phase != HeartsPhase.Playing) return null;
        if (!SameHands(state.Hands, hands) || !SameRoundScore(state.Scores, scores) ||
            state.HeartsBroken != heartsBroken ||
            state.Turn != (PlayerSeat)(((int)leader + current.Plays.Count) % 4)) return null;
        return scores;
    }

    private static bool PlayTrick(IReadOnlyList<TrickPlay> plays, PlayerSeat leader,
        IReadOnlyList<List<PlayingCard>> hands, bool firstTrick, ref bool heartsBroken,
        out PlayerSeat winner, out int points)
    {
        winner = leader; points = 0;
        CardSuit? leadSuit = null;
        var bestRank = 0;
        for (var index = 0; index < plays.Count; index++)
        {
            var play = plays[index];
            var seat = (PlayerSeat)(((int)leader + index) % 4);
            if (play.Seat != seat || !CardValid(play.Card) || !hands[(int)seat].Contains(play.Card)) return false;
            var card = play.Card;
            if (firstTrick && index == 0 && card != new PlayingCard(CardRank.Two, CardSuit.Clubs)) return false;
            if (leadSuit is { } suit && card.Suit != suit && hands[(int)seat].Any(value => value.Suit == suit)) return false;
            if (index == 0 && !firstTrick && card.Suit == CardSuit.Hearts && !heartsBroken &&
                hands[(int)seat].Any(value => value.Suit != CardSuit.Hearts)) return false;
            if (firstTrick && PointValue(card) > 0)
            {
                var legal = leadSuit is { } follow && hands[(int)seat].Any(value => value.Suit == follow)
                    ? hands[(int)seat].Where(value => value.Suit == follow) : hands[(int)seat];
                if (legal.Any(value => PointValue(value) == 0)) return false;
            }
            hands[(int)seat].Remove(card);
            if (index == 0) leadSuit = card.Suit;
            if (card.Suit == CardSuit.Hearts) heartsBroken = true;
            points += PointValue(card);
            if (card.Suit == leadSuit && RankValue(card.Rank) > bestRank)
            {
                winner = seat; bestRank = RankValue(card.Rank);
            }
        }
        return true;
    }

    private static PlayingCard[] Deal(uint seed)
    {
        var deck = Enum.GetValues<CardSuit>()
            .SelectMany(suit => Enum.GetValues<CardRank>().Select(rank => new PlayingCard(rank, suit))).ToArray();
        var value = seed;
        for (var index = deck.Length - 1; index > 0; index--)
        {
            unchecked
            {
                value += 0x6d2b79f5u;
                var random = value;
                random = (random ^ (random >> 15)) * (random | 1u);
                random ^= random + (random ^ (random >> 7)) * (random | 61u);
                random ^= random >> 14;
                var swap = (int)(((ulong)random * (uint)(index + 1)) >> 32);
                (deck[index], deck[swap]) = (deck[swap], deck[index]);
            }
        }
        return deck;
    }

    private static int Progress(HeartsState round) =>
        checked(round.SubmittedPasses.Count + round.CompletedTricks.Count * 4 + round.CurrentTrick.Plays.Count);

    private static bool Adjacent(HeartsMatchState before, HeartsMatchState after)
    {
        if (before.TargetScore != after.TargetScore) return false;
        if (after.RoundNumber == before.RoundNumber + 1)
            return before.Round.Phase == HeartsPhase.Complete && before.Winner is null &&
                Progress(after.Round) == 0 &&
                JsonSerializer.Serialize(before.Score, RoundAuditCapture.Json) ==
                JsonSerializer.Serialize(after.Score, RoundAuditCapture.Json);
        if (after.RoundNumber != before.RoundNumber || before.Round.Seed != after.Round.Seed ||
            before.PassDirection != after.PassDirection || before.Round.PassDirection != after.Round.PassDirection)
            return false;
        var oldRound = before.Round; var newRound = after.Round;
        var oldPasses = oldRound.SubmittedPasses; var newPasses = newRound.SubmittedPasses;
        var oldPlays = oldRound.CompletedTricks.Count * 4 + oldRound.CurrentTrick.Plays.Count;
        var newPlays = newRound.CompletedTricks.Count * 4 + newRound.CurrentTrick.Plays.Count;
        var passDelta = newPasses.Count - oldPasses.Count;
        var playDelta = newPlays - oldPlays;
        if (passDelta < 0 || playDelta < 0 || passDelta + playDelta > 1 ||
            !Prefix(oldPasses, newPasses) || !Prefix(oldRound.CompletedTricks, newRound.CompletedTricks))
            return false;
        if (passDelta == 0 && playDelta == 0)
            return JsonSerializer.Serialize(before, RoundAuditCapture.Json) ==
                JsonSerializer.Serialize(after, RoundAuditCapture.Json);
        if (passDelta == 1)
            return before.Round.Phase == HeartsPhase.Passing && oldPlays == 0 && newPlays == 0;
        if (before.Round.Phase != HeartsPhase.Playing || passDelta != 0) return false;
        if (newRound.CompletedTricks.Count == oldRound.CompletedTricks.Count)
            return newRound.CurrentTrick.Leader == oldRound.CurrentTrick.Leader &&
                newRound.CurrentTrick.Plays.Count == oldRound.CurrentTrick.Plays.Count + 1 &&
                Prefix(oldRound.CurrentTrick.Plays, newRound.CurrentTrick.Plays);
        if (newRound.CompletedTricks.Count != oldRound.CompletedTricks.Count + 1 ||
            oldRound.CurrentTrick.Plays.Count != 3 || newRound.CurrentTrick.Plays.Count != 0)
            return false;
        var finished = newRound.CompletedTricks[^1];
        return finished.Leader == oldRound.CurrentTrick.Leader &&
            Prefix(oldRound.CurrentTrick.Plays, finished.Plays);
    }

    private static bool Prefix<T>(IReadOnlyList<T> earlier, IReadOnlyList<T> later) =>
        earlier.Count <= later.Count && Enumerable.Range(0, earlier.Count).All(index =>
            JsonSerializer.Serialize(earlier[index], RoundAuditCapture.Json) ==
            JsonSerializer.Serialize(later[index], RoundAuditCapture.Json));

    private static bool SameHands(IReadOnlyList<PlayerHand> recorded, IReadOnlyList<List<PlayingCard>> expected) =>
        recorded.Count == 4 && recorded.Select(hand => hand.Seat).Distinct().Count() == 4 &&
        recorded.All(hand => SeatValid(hand.Seat) && hand.Cards.Count == expected[(int)hand.Seat].Count &&
            hand.Cards.OrderBy(card => (int)card.Suit).ThenBy(card => (int)card.Rank).SequenceEqual(
                expected[(int)hand.Seat].OrderBy(card => (int)card.Suit).ThenBy(card => (int)card.Rank)));

    private static bool SameRoundScore(IReadOnlyList<HeartsScore> recorded, int[] expected) =>
        recorded.Count == 4 && recorded.Select(score => score.Seat).Distinct().Count() == 4 &&
        recorded.All(score => SeatValid(score.Seat) && score.Points == expected[(int)score.Seat]);

    private static bool SameScore(HeartsMatchScore score, int[] expected) =>
        score.North == expected[0] && score.East == expected[1] &&
        score.South == expected[2] && score.West == expected[3];

    private static PlayerSeat? ExpectedWinner(int[] scores, int target) =>
        target > 0 && scores.Any(value => value >= target) ?
            (PlayerSeat)Enumerable.Range(0, 4).OrderBy(seat => scores[seat]).ThenBy(seat => seat).First() : null;

    private static HeartsPassDirection Direction(int roundNumber) => (HeartsPassDirection)((roundNumber - 1) % 4);
    private static PlayerSeat PassTarget(PlayerSeat seat, HeartsPassDirection direction) =>
        (PlayerSeat)(((int)seat + (direction switch
        { HeartsPassDirection.Left => 1, HeartsPassDirection.Right => 3, HeartsPassDirection.Across => 2, _ => 0 })) % 4);
    private static bool SeatValid(PlayerSeat seat) => seat is >= PlayerSeat.North and <= PlayerSeat.West;
    private static bool CardValid(PlayingCard card) => card.Suit is >= CardSuit.Clubs and <= CardSuit.Spades &&
        card.Rank is >= CardRank.Ace and <= CardRank.King;
    private static int RankValue(CardRank rank) => rank == CardRank.Ace ? 14 : (int)rank;
    private static int PointValue(PlayingCard card) => card.Suit == CardSuit.Hearts ? 1 :
        card.Suit == CardSuit.Spades && card.Rank == CardRank.Queen ? 13 : 0;
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static IReadOnlyList<RoundAuditFinding> Error(string code, string message) => [new(code, "error", message)];
    private sealed record Observation(long Version, DateTime OccurredAtUtc, HeartsMatchState? State, string RawState);
}
