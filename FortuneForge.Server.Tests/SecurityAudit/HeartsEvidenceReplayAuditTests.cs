using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class HeartsEvidenceReplayAuditTests
{
    [Fact]
    public void Free_match_replays_deal_passes_all_tricks_and_detects_altered_winner()
    {
        var states = PlayOneRound(1842);
        var id = Guid.NewGuid();
        var roundId = id.ToString("N");
        var start = Utc(0);
        var end = Utc(states.Count);
        var final = states[^1];
        var capture = FreeCapture(id, start, end, final);
        var events = states.Select((state, index) => Event(roundId, index + 1, Utc(index + 1), new { state })).ToArray();

        var replay = HeartsEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson, events);
        Assert.DoesNotContain(replay, finding => finding.Severity == "error");
        Assert.Contains(replay, finding => finding.Code == "hearts-free-journal-non-atomic");

        var first = final.Round.CompletedTricks[0];
        var changedTricks = final.Round.CompletedTricks.ToArray();
        changedTricks[0] = first with { Winner = (PlayerSeat)(((int)first.Winner + 1) % 4) };
        var changed = final with { Round = final.Round with { CompletedTricks = changedTricks } };
        var tamperedEvents = events.ToArray();
        tamperedEvents[^1] = Event(roundId, states.Count, end, new { state = changed });
        var tamperedCapture = FreeCapture(id, start, end, changed);
        Assert.Contains(HeartsEvidenceReplayAudit.Inspect(tamperedCapture.Summary,
            tamperedCapture.PayloadJson, tamperedEvents), finding => finding.Code == "hearts-intermediate-state-mismatch");

        var intermediate = states[10];
        var changedScores = intermediate.Round.Scores.Select(score =>
            score.Seat == PlayerSeat.North ? score with { Points = score.Points + 1 } : score).ToArray();
        var changedIntermediate = intermediate with { Round = intermediate.Round with { Scores = changedScores } };
        var corruptedEvents = events.ToArray();
        corruptedEvents[10] = Event(roundId, 11, Utc(11), new { state = changedIntermediate });
        Assert.Contains(HeartsEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson, corruptedEvents),
            finding => finding.Code == "hearts-intermediate-state-mismatch");

        var skippedActionEvents = events.ToArray();
        skippedActionEvents[10] = Event(roundId, 11, Utc(11), new { state = states[11] });
        Assert.Contains(HeartsEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson, skippedActionEvents),
            finding => finding.Code == "hearts-intermediate-transition-mismatch");
    }

    [Fact]
    public void Paid_match_replays_cards_and_settlement_links()
    {
        var states = PlayOneRound(497);
        var final = states[^1];
        var match = new PaidHeartsMatch
        {
            Id = new string('a', 64), Status = "result", Version = states.Count + 1,
            EntryCents = 100, CreatedAtUtc = Utc(0), UpdatedAtUtc = Utc(60), CompletedAtUtc = Utc(60),
            State = final,
            Players = Enum.GetValues<PlayerSeat>().Select((seat, index) => new PaidHeartsParticipant
            {
                UserId = $"player-{index}", EntryId = $"entry-{index}", DisplayName = $"Player {index}",
                Seat = seat, IsAccountBacked = true
            }).ToList()
        };
        var awards = PaidHeartsSettlement.Split(match.Id, match.PotCents, match.Players, final.Score);
        foreach (var player in match.Players) player.PayoutCents = awards.GetValueOrDefault(player.UserId);
        match.Winners = match.Players.Where(player => awards.ContainsKey(player.UserId)).Select(player => player.Seat).ToList();
        var capture = PaidHeartsRoundAudit.Capture(match, false, Utc(60));
        var events = states.Select((state, index) => Event(match.Id, index + 2, Utc(index + 1),
            new PaidHeartsMatch { State = state })).ToArray();

        Assert.Empty(HeartsEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson, events));

        var changed = JsonNode.Parse(capture.PayloadJson)!;
        changed["payoutLedgerIds"] = new JsonArray();
        Assert.Contains(HeartsEvidenceReplayAudit.Inspect(capture.Summary, changed.ToJsonString(), events),
            finding => finding.Code == "hearts-paid-ledger-link-mismatch");
    }

    [Fact]
    public void Paid_match_reconstructs_prior_hands_before_accepting_cumulative_score()
    {
        var states = PlayFullMatch();
        var completed = states.Where(state => state.Round.Phase == HeartsPhase.Complete).ToArray();
        Assert.True(completed.Length > 1);
        var final = states[^1];
        var completedAt = Utc(states.Count + 1);
        var match = new PaidHeartsMatch
        {
            Id = new string('b', 64), Status = "result", Version = states.Count + 1,
            EntryCents = 100, CreatedAtUtc = Utc(0), UpdatedAtUtc = completedAt, CompletedAtUtc = completedAt,
            State = final,
            Players = Enum.GetValues<PlayerSeat>().Select((seat, index) => new PaidHeartsParticipant
            {
                UserId = $"account-{index}", EntryId = $"entry-{index}", DisplayName = $"Account {index}",
                Seat = seat, IsAccountBacked = true
            }).ToList()
        };
        var awards = PaidHeartsSettlement.Split(match.Id, match.PotCents, match.Players, final.Score);
        foreach (var player in match.Players) player.PayoutCents = awards.GetValueOrDefault(player.UserId);
        match.Winners = match.Players.Where(player => awards.ContainsKey(player.UserId)).Select(player => player.Seat).ToList();
        var capture = PaidHeartsRoundAudit.Capture(match, false, completedAt);
        var events = states.Select((state, index) => Event(match.Id, index + 2, Utc(index + 1),
            new PaidHeartsMatch { State = state })).ToArray();

        Assert.Empty(HeartsEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson, events));

        var first = completed[0];
        var altered = first with { Score = first.Score with { North = first.Score.North + 1 } };
        var firstCompletedIndex = Array.FindIndex(states.ToArray(), state => state.Round.Phase == HeartsPhase.Complete);
        events[firstCompletedIndex] = Event(match.Id, firstCompletedIndex + 2, Utc(firstCompletedIndex + 1),
            new PaidHeartsMatch { State = altered });
        Assert.Contains(HeartsEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson, events),
            finding => finding.Code == "hearts-intermediate-transition-mismatch");
    }

    private static IReadOnlyList<HeartsMatchState> PlayOneRound(uint seed)
    {
        var states = new List<HeartsMatchState>();
        var state = HeartsMatchEngine.Start(seed, 1);
        states.Add(state);
        foreach (var seat in Enum.GetValues<PlayerSeat>())
        {
            state = HeartsMatchEngine.Apply(state, new PassHeartsCards(seat,
                state.Round.HandFor(seat).Take(3).ToArray())).State;
            states.Add(state);
        }
        while (state.Round.Phase != HeartsPhase.Complete)
        {
            var seat = state.Round.Turn;
            var card = HeartsEngine.LegalCards(state.Round, seat)[0];
            state = HeartsMatchEngine.Apply(state, new PlayHeartsCard(seat, card)).State;
            states.Add(state);
        }
        return states;
    }

    private static IReadOnlyList<HeartsMatchState> PlayFullMatch()
    {
        var states = new List<HeartsMatchState>();
        var state = HeartsMatchEngine.Start(1842, 100);
        states.Add(state);
        for (uint round = 1; round <= 40 && state.Winner is null; round++)
        {
            if (round > 1)
            {
                state = HeartsMatchEngine.StartNextRound(state, 1842 + round);
                states.Add(state);
            }
            if (state.Round.Phase == HeartsPhase.Passing)
                foreach (var seat in Enum.GetValues<PlayerSeat>())
                {
                    state = HeartsMatchEngine.Apply(state, new PassHeartsCards(seat,
                        state.Round.HandFor(seat).Take(3).ToArray())).State;
                    states.Add(state);
                }
            while (state.Round.Phase != HeartsPhase.Complete)
            {
                var seat = state.Round.Turn;
                state = HeartsMatchEngine.Apply(state, new PlayHeartsCard(seat,
                    HeartsEngine.LegalCards(state.Round, seat)[0])).State;
                states.Add(state);
            }
        }
        Assert.NotNull(state.Winner);
        return states;
    }

    private static RoundAuditCapture FreeCapture(Guid id, DateTime start, DateTime end, HeartsMatchState state) =>
        RoundAuditCapture.Create("hearts", id.ToString("N"), id.ToString("N"), state.RoundNumber,
            start, end, new { id, startedAtUtc = start, completedAtUtc = end, state,
                isTrial = false, isCancelled = false, isExpired = false },
            "hearts-private-hands-and-tricks-v1", "free-engine-receipt-v1",
            [new RoundAuditParticipant("player-0", "player-0", true, 0, 0, 0)],
            [new RoundAuditFinding("independent-replay-pending", "warning", "Waiting for database replay.")]);

    private static string Event(string roundId, long version, DateTime occurredAtUtc, object state) =>
        (string)GameSecurityEventDocuments.Capture("hearts", roundId, version, occurredAtUtc,
            state, ["player-0"]).Fields["payloadJson"];

    private static DateTime Utc(int seconds) =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 26), DateTimeKind.Utc).AddSeconds(seconds);
}
