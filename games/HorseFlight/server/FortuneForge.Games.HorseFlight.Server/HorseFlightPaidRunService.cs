using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.HorseFlight.Server;

public sealed class HorseFlightPaidRunService(FirestoreDb database,
    ArcadeCompetitionPaidEntries<HorseFlightPaidRun> entries, HorseFlightPaidRuns runs,
    TimeProvider clock, ArcadeCompetitionRulesOptions rules)
{
    public Task<ArcadeCompetitionPaidEntry<HorseFlightPaidRun>> StartAsync(string attemptId, string userId,
        ArcadeCompetitionWindowKind kind, CancellationToken token,
        ArcadeCompetitionPaidAdmission? admission = null, bool recoverOnly = false, string? requestBodySha256 = null)
    {
        var now = clock.GetUtcNow();
        var window = ArcadeCompetitionRules.GetWindow(kind, now, rules.TimeZone);
        var competition = new ArcadeCompetitionIdentity(HorseFlightModule.GameId, kind, window.StartsAtUtc, window.EndsAtUtc);
        var allowNewEntries = (admission ?? new()).AllowsNewEntries(window, rules.TimeZone, recoverOnly);
        return entries.StartAsync(new(attemptId, competition, userId, rules.EntryFeeCents, now, requestBodySha256), allowNewEntries, token);
    }

    public async Task<HorseFlightRecordedRunCompletion> CompleteAsync(ArcadeCompetitionWindowKind kind,
        string runId, string userId, HorseFlightReplay replay, CancellationToken token)
    {
        HorseFlightReplayEvaluator.ValidateReplayInput(replay);
        var original = await OriginalAttemptAsync(kind, runId, userId, token);
        var now = clock.GetUtcNow();
        return await entries.CompleteAsync(original.Competition, original.DocumentId, userId, now,
            (attempt, snapshot) => runs.Complete(database, attempt, snapshot, replay, now), token);
    }

    public async Task<HorseFlightPaidRunRecovery> GetAsync(ArcadeCompetitionWindowKind kind,
        string runId, string userId, CancellationToken token)
    {
        var original = await OriginalAttemptAsync(kind, runId, userId, token);
        var snapshot = await runs.RunDocument(database, original).GetSnapshotAsync(token);
        var run = runs.ReadRun(snapshot, original);
        return new(run.RunId, run.Seed, run.RulesVersion, original.Competition.WindowKind.ToString().ToLowerInvariant(),
            original.Competition.StartsAtUtc, original.Competition.EndsAtUtc, original.EntryFeeCents,
            run.Status, run.Score, HorseFlightRunReplay.PhaseName(run.Phase), run.CompletedAtUtc,
            run.Status == "completed" ? new HorseFlightReplay(run.TotalTicks, [.. run.JumpTicks])
                { RightClickTicks = [.. run.RightClickTicks], RulesVersion = run.RulesVersion } : null);
    }

    private async Task<ArcadeCompetitionAttemptRecord> OriginalAttemptAsync(ArcadeCompetitionWindowKind kind,
        string runId, string userId, CancellationToken token)
    {
        var original = await entries.ReadAttemptAsync(HorseFlightPaidRuns.AttemptDocumentId(runId), userId, token);
        if (original.Competition.WindowKind != kind)
            throw new ArgumentException("The HorseFlight replay period does not match its original entry.", nameof(kind));
        return original;
    }
}

public sealed record HorseFlightPaidRunRecovery(string RunId, uint Seed, int RulesVersion, string Period,
    DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, long EntryFeeCents,
    string Status, long Score, string Phase, DateTimeOffset? CompletedAtUtc, HorseFlightReplay? Replay);
