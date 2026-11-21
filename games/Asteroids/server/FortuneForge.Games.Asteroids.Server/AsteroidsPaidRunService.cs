using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Asteroids.Server;

/// <summary>Owns paid Asteroids admission and replay orchestration over generic wallet/competition operations.</summary>
public sealed class AsteroidsPaidRunService(FirestoreDb database,
    ArcadeCompetitionPaidEntries<AsteroidsPaidRun> entries, AsteroidsPaidRuns runs,
    TimeProvider clock, ArcadeCompetitionRulesOptions rules)
{
    public Task<ArcadeCompetitionPaidEntry<AsteroidsPaidRun>> StartAsync(string attemptId, string userId,
        ArcadeCompetitionWindowKind kind, CancellationToken token, ArcadeCompetitionPaidAdmission? admission = null,
        bool recoverOnly = false, string? requestBodySha256 = null)
    {
        var now = clock.GetUtcNow();
        var window = ArcadeCompetitionRules.GetWindow(kind, now, rules.TimeZone);
        var competition = new ArcadeCompetitionIdentity(AsteroidsModule.GameId, kind, window.StartsAtUtc, window.EndsAtUtc);
        var allowNewEntries = (admission ?? new()).AllowsNewEntries(window, rules.TimeZone, recoverOnly);
        return entries.StartAsync(new(attemptId, competition, userId, rules.EntryFeeCents, now, requestBodySha256), allowNewEntries, token);
    }

    public Task<AsteroidsPaidReplayCompletion> CompleteAsync(ArcadeCompetitionIdentity competition,
        string runId, string userId, AsteroidsReplay replay, CancellationToken token)
    {
        AsteroidsReplayEvaluator.ValidateReplayInput(replay);
        var completedAt = clock.GetUtcNow();
        return entries.CompleteAsync(competition, AsteroidsPaidRuns.AttemptDocumentId(runId), userId, completedAt,
            (attempt, snapshot) => runs.Complete(database, attempt, snapshot, replay, completedAt), token);
    }

    public Task<int> RecoverPendingAsync(ArcadeCompetitionIdentity competition, CancellationToken token) =>
        entries.RecoverPendingAsync(competition, token);

    public async Task<AsteroidsPaidReplayCompletion> CompleteAsync(ArcadeCompetitionWindowKind kind,
        string runId, string userId, AsteroidsReplay replay, CancellationToken token)
    {
        var original = await entries.ReadAttemptAsync(AsteroidsPaidRuns.AttemptDocumentId(runId), userId, token);
        if (original.Competition.WindowKind != kind)
            throw new ArgumentException("The Asteroids replay period does not match its original entry.", nameof(kind));
        return await CompleteAsync(original.Competition, runId, userId, replay, token);
    }

    public async Task<AsteroidsPaidRunRecovery> GetAsync(ArcadeCompetitionWindowKind kind,
        string runId, string userId, CancellationToken token)
    {
        var original = await entries.ReadAttemptAsync(AsteroidsPaidRuns.AttemptDocumentId(runId), userId, token);
        if (original.Competition.WindowKind != kind)
            throw new ArgumentException("The Asteroids period does not match its original entry.", nameof(kind));
        var snapshot = await runs.RunDocument(database, original).GetSnapshotAsync(token);
        var run = runs.ReadRun(snapshot, original);
        return new(run.Run.RunId, run.Run.Seed.ToString("x16"), AsteroidsReplayEvaluator.RulesVersion,
            original.Competition.WindowKind.ToString().ToLowerInvariant(), original.Competition.StartsAtUtc,
            original.Competition.EndsAtUtc, original.EntryFeeCents, run.Status, run.Score,
            run.Terminal.ToString().ToLowerInvariant(), run.CompletedAtUtc,
            run.Status == "completed" ? AsteroidsReplayEvaluator.ParseCanonicalReplay(run.ReplayCanonical) : null);
    }
}

public sealed record AsteroidsPaidRunRecovery(string RunId, string SeedHex, int RulesVersion,
    string Period, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, long EntryFeeCents,
    string Status, long Score, string Terminal, DateTimeOffset? CompletedAtUtc, AsteroidsReplay? Replay);
