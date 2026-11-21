using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Flappy.Server;

public sealed class FlappyPaidRunService(FirestoreDb database,
    ArcadeCompetitionPaidEntries<FlappyPaidRun> entries, FlappyPaidRuns runs,
    TimeProvider clock, ArcadeCompetitionRulesOptions rules)
{
    public Task<ArcadeCompetitionPaidEntry<FlappyPaidRun>> StartAsync(string attemptId, string userId,
        ArcadeCompetitionWindowKind kind, CancellationToken token,
        ArcadeCompetitionPaidAdmission? admission = null, bool recoverOnly = false, string? requestBodySha256 = null)
    {
        var now = clock.GetUtcNow();
        var window = ArcadeCompetitionRules.GetWindow(kind, now, rules.TimeZone);
        var competition = new ArcadeCompetitionIdentity(FlappyModule.GameId, kind, window.StartsAtUtc, window.EndsAtUtc);
        var allowNewEntries = (admission ?? new()).AllowsNewEntries(window, rules.TimeZone, recoverOnly);
        return entries.StartAsync(new(attemptId, competition, userId, rules.EntryFeeCents, now, requestBodySha256), allowNewEntries, token);
    }

    public async Task<FlappyRecordedRunCompletion> CompleteAsync(ArcadeCompetitionWindowKind kind,
        string runId, string userId, FlappyReplay replay, CancellationToken token)
    {
        FlappyReplayEvaluator.ValidateReplayInput(replay);
        var original = await OriginalAttemptAsync(kind, runId, userId, token);
        var now = clock.GetUtcNow();
        return await entries.CompleteAsync(original.Competition, original.DocumentId, userId, now,
            (attempt, snapshot) => runs.Complete(database, attempt, snapshot, replay, now), token);
    }

    public async Task<FlappyPaidRunRecovery> GetAsync(ArcadeCompetitionWindowKind kind,
        string runId, string userId, CancellationToken token)
    {
        var original = await OriginalAttemptAsync(kind, runId, userId, token);
        var snapshot = await runs.RunDocument(database, original).GetSnapshotAsync(token);
        var run = runs.ReadRun(snapshot, original);
        return new(run.RunId, run.Seed, original.Competition.WindowKind.ToString().ToLowerInvariant(),
            original.Competition.StartsAtUtc, original.Competition.EndsAtUtc, original.EntryFeeCents,
            run.Status, run.Score, run.Terminal, run.CompletedAtUtc,
            run.Status == "completed" ? FlappyRunReplay.ParseCanonicalReplay(run.ReplayCanonical) : null);
    }

    private async Task<ArcadeCompetitionAttemptRecord> OriginalAttemptAsync(ArcadeCompetitionWindowKind kind,
        string runId, string userId, CancellationToken token)
    {
        var original = await entries.ReadAttemptAsync(FlappyPaidRuns.AttemptDocumentId(runId), userId, token);
        if (original.Competition.WindowKind != kind)
            throw new ArgumentException("The Flappy replay period does not match its original entry.", nameof(kind));
        return original;
    }
}

public sealed record FlappyPaidRunRecovery(string RunId, uint Seed, string Period,
    DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, long EntryFeeCents,
    string Status, long Score, string Terminal, DateTimeOffset? CompletedAtUtc, FlappyReplay? Replay);
