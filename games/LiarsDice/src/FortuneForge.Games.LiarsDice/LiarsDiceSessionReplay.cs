using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Dice;

namespace FortuneForge.Games.LiarsDice;

public static class LiarsDiceSessionReplay
{
    public const string Version = "liars-dice-durable-replay-v1";
    public const string EvidenceKind = "liars-dice-durable-private-v3";

    public static bool Supports(RoundAuditSummary summary) => summary.GameId == LiarsDiceSession.GameId &&
        summary.AuditorVersion == Version && summary.EvidenceKind == EvidenceKind;

    public static IReadOnlyList<RoundAuditFinding> Inspect(RoundAuditSummary summary, string? payloadJson)
    {
        if (!Supports(summary)) return [];
        try
        {
            var session = JsonSerializer.Deserialize<LiarsDiceSession>(payloadJson ?? "null", LiarsDiceStateJson.Options)
                ?? throw new JsonException("Missing private match.");
            var replay = Check(session);
            if (replay.Count > 0) return replay;
            if (session.Id.ToString("N") != summary.RoundId || summary.TableId != summary.RoundId ||
                session.State.RoundNumber != summary.RoundNumber || session.StartedAtUtc != summary.StartedAtUtc ||
                session.CompletedAtUtc != summary.CompletedAtUtc || summary.Participants.Count != 1 ||
                summary.Participants[0].ActorId != session.UserId || !summary.Participants[0].IsAccountBacked ||
                summary.Participants[0].WagerCents != 0 || summary.Participants[0].PayoutCents != 0 ||
                summary.Participants[0].NetCents != 0 || session.State.Winner is null)
                return Error("The completed match identity, account, times or free-play classification disagree with its private evidence.");
            return [];
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        { return Error("The private match evidence could not be read."); }
    }

    public static IReadOnlyList<RoundAuditFinding> Check(LiarsDiceSession session)
    {
        if (session.State is null || session.Actions is null || session.ManagedActors is null ||
            session.State.AllPlayerIds.IsDefaultOrEmpty || session.State.DiceCounts is null || session.State.Round is null ||
            string.IsNullOrWhiteSpace(session.UserId) || session.Id == Guid.Empty ||
            session.StartedAtUtc.Kind != DateTimeKind.Utc || session.CompletedAtUtc is { } ended &&
                (ended.Kind != DateTimeKind.Utc || ended < session.StartedAtUtc) ||
            session.ManagedActors.Count != 3 || session.ManagedActors.Values.Any(actor => actor?.Profile is null))
            return Error("The private match is missing required identity, state, roster or time fields.");
        if (session.State.AllPlayerIds.Length != 4 || session.State.AllPlayerIds[0] != LiarsDiceSession.HumanId ||
            !session.ManagedActors.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(session.State.AllPlayerIds.Skip(1)) ||
            session.ManagedActors.Any(pair => pair.Key != pair.Value.Profile.UserId ||
                string.IsNullOrWhiteSpace(pair.Value.Profile.PlayerName) || pair.Value.Profile.SkillLevel is not (2 or 3 or 4) ||
                pair.Value.DecisionVersion < 0 || pair.Value.TimingVersion < 0))
            return Error("The saved roster or managed-player cursors are inconsistent.");
        if (session.Actions.Any(action => action is null)) return Error("The accepted action trace contains a missing command.");
        if (session.Actions.Count > 10_000) return Error("The accepted action count exceeds the game limit.");
        try
        {
            var replay = LiarsDiceMatchEngine.Start(session.InitialSeed, session.State.AllPlayerIds, session.State.StartingDicePerPlayer);
            var decisions = session.ManagedActors.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
            var timings = session.ManagedActors.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
            string? scheduled = null;
            DateTime? dueAtUtc = null;
            var previousTime = session.StartedAtUtc;
            var roundSeed = session.InitialSeed;
            var history = new List<LiarsDiceResolvedRound>();
            foreach (var action in session.Actions)
            {
                if (action.AcceptedAtUtc is not { Kind: DateTimeKind.Utc } acceptedAt || acceptedAt < previousTime)
                    return Error("Accepted command times are missing or reversed.");
                previousTime = acceptedAt;
                if (action.Kind == "next-round")
                {
                    replay = LiarsDiceMatchEngine.StartNextRound(replay, action.Seed);
                    roundSeed = action.Seed;
                    Schedule(acceptedAt);
                    continue;
                }
                LiarsDiceCommand command = action.Kind switch
                {
                    "bid" => new PlaceLiarsDiceBid(action.PlayerId, new(action.Quantity, new DieValue(action.Face))),
                    "challenge" => new ChallengeLiarsDiceBid(action.PlayerId),
                    "spot-on" => new SpotOnLiarsDiceBid(action.PlayerId),
                    _ => throw new LiarsDiceRuleException("Unknown saved action."),
                };
                if (action.PlayerId != LiarsDiceSession.HumanId)
                {
                    if (scheduled != action.PlayerId || dueAtUtc is null || acceptedAt < dueAtUtc.Value)
                        return Error("A managed-player action was accepted before its saved schedule.");
                    var actor = session.ManagedActors[action.PlayerId];
                    if (command != LiarsDiceManagedActionPolicy.Choose(replay, actor.Profile.SkillLevel,
                            actor.DecisionSeed, decisions[action.PlayerId]++))
                        return Error("A managed-player action differs from its saved decision seed and cursor.");
                }
                var transition = LiarsDiceMatchEngine.Apply(replay, command);
                if (transition.Outcome is { } outcome) history.Add(new(replay.RoundNumber, outcome));
                replay = transition.State;
                Schedule(acceptedAt);
            }
            if (session.ResolvedRounds is null || !history.SequenceEqual(session.ResolvedRounds))
                return Error("The saved resolved-round history differs from replay of accepted inputs.");
            if (roundSeed != session.RoundSeed || session.EventVersion != session.Actions.Count + 1 ||
                decisions.Any(pair => session.ManagedActors[pair.Key].DecisionVersion != pair.Value) ||
                timings.Any(pair => session.ManagedActors[pair.Key].TimingVersion != pair.Value) ||
                scheduled != session.ScheduledManagedPlayerId || dueAtUtc != session.NextManagedActionAtUtc ||
                !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(replay, RoundAuditCapture.Json),
                    JsonSerializer.SerializeToElement(session.State, RoundAuditCapture.Json)))
                return Error("The saved private state differs from replay of the accepted inputs.");
            if ((session.State.Winner is null) != (session.CompletedAtUtc is null))
                return Error("The saved winner and completion time disagree.");
            if (session.CompletedAtUtc is { } completedAt && completedAt != previousTime)
                return Error("The match completion time differs from its final accepted command.");
            return [];

            void Schedule(DateTime acceptedAt)
            {
                if (replay.Winner is not null || replay.Round.Phase == LiarsDiceRoundPhase.Resolved ||
                    !session.ManagedActors.TryGetValue(replay.CurrentPlayerId, out var actor))
                { scheduled = null; dueAtUtc = null; return; }
                scheduled = actor.Profile.UserId;
                dueAtUtc = acceptedAt + (actor with { TimingVersion = timings[scheduled]++ }).ThinkDelay();
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { return Error("The private match trace could not be replayed."); }
    }

    public static RoundAuditCapture Capture(LiarsDiceSession session) => RoundAuditCapture.Create(
        LiarsDiceSession.GameId, session.Id.ToString("N"), session.Id.ToString("N"), session.State.RoundNumber,
        session.StartedAtUtc, session.CompletedAtUtc ?? throw new InvalidOperationException("The match has not completed."),
        session, EvidenceKind, Version, [new RoundAuditParticipant(session.UserId, session.UserId, true, 0, 0, 0)], Check(session));

    private static IReadOnlyList<RoundAuditFinding> Error(string message) => [new("liars-dice-session-replay-mismatch", "error", message)];
}
