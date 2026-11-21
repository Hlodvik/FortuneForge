using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Firestore;
using FortuneForge.Server.Bots.Hearts;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Games.Hearts;

/// <summary>Runs the existing free policy against a transaction-local private session set.</summary>
public sealed class FirestoreHeartsFreeStore(FirestoreDb database, TimeProvider timeProvider, Func<bool>? allowNewGames = null,
    ILogger<FirestoreHeartsFreeStore>? logger = null)
{
    internal const string Sessions = "heartsFreeSessions";
    internal const string Commands = "heartsFreeCommands";
    internal const string Accounts = "heartsFreeAccounts";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<HeartsMatchResponse> StartAsync(string userId, StartHeartsMatchRequest request, string? bodySha, CancellationToken token) =>
        ExecuteAsync(userId, bodySha, (games, _) => games.StartAsync(userId, request, token), token);
    public Task<HeartsMatchResponse> TrialAsync(string userId, string? bodySha, CancellationToken token) =>
        ExecuteAsync(userId, bodySha, (games, _) => games.StartTrialAsync(userId, token), token, trialOnly: true);
    public Task<HeartsMatchResponse> GetAsync(string userId, Guid matchId, string? bodySha, CancellationToken token) =>
        ExecuteAsync(userId, bodySha, (games, _) => games.GetAsync(userId, matchId, token), token, matchId);
    public Task<HeartsMatchResponse> PassAsync(string userId, Guid matchId, IReadOnlyList<string> cards, string? bodySha, CancellationToken token) =>
        ExecuteAsync(userId, bodySha, (games, _) => games.PassAsync(userId, matchId, cards, token), token, matchId);
    public Task<HeartsMatchResponse> CardAsync(string userId, Guid matchId, string card, string? bodySha, CancellationToken token) =>
        ExecuteAsync(userId, bodySha, (games, _) => games.PlayCardAsync(userId, matchId, card, token), token, matchId);
    public Task<HeartsMatchResponse> AdvanceAsync(string userId, Guid matchId, string? bodySha, CancellationToken token) =>
        ExecuteAsync(userId, bodySha, (games, _) => games.AdvanceAsync(userId, matchId, token), token, matchId);
    public Task<HeartsMatchResponse> NextAsync(string userId, Guid matchId, uint? seed, string? bodySha, CancellationToken token) =>
        ExecuteAsync(userId, bodySha, (games, _) => games.NextRoundAsync(userId, matchId, seed, token), token, matchId);
    public Task<bool> LeaveAsync(string userId, Guid matchId, string? bodySha, CancellationToken token) =>
        ExecuteAsync(userId, bodySha, async (games, _) => { await games.LeaveAsync(userId, matchId, token); return true; }, token, matchId);
    public Task<HeartsOnboardingResponse> CancelTrialAsync(string userId, Guid matchId, string? bodySha, CancellationToken token) =>
        ExecuteAsync(userId, bodySha, async (games, onboarding) => { await games.CancelTrialAsync(userId, matchId, token); return await onboarding.GetAsync(userId, token); }, token, matchId);
    public Task<HeartsOnboardingResponse> OnboardingAsync(string userId, string? bodySha, CancellationToken token) =>
        ExecuteAsync(userId, bodySha, async (games, onboarding) => { await games.RetryTrialCompletionAsync(userId, token); return await onboarding.GetAsync(userId, token); }, token, trialOnly: true);
    public Task<HeartsOnboardingResponse> ConfirmAsync(string userId, bool hasPlayedBefore, string? bodySha, CancellationToken token)
    {
        if (!hasPlayedBefore) throw new HeartsRuleException("Confirm that you have played before, or choose Learn to play.");
        return ExecuteAsync(userId, bodySha, (_, onboarding) => onboarding.ConfirmExperienceAsync(userId, timeProvider.GetUtcNow().UtcDateTime, token), token);
    }

    internal async Task AdvanceDueAsync(CancellationToken token)
    {
        var due = await database.Collection(Sessions).WhereLessThanOrEqualTo("nextSweepAt", Timestamp.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
            .Limit(100).GetSnapshotAsync(token);
        foreach (var group in due.Documents.GroupBy(document => document.GetValue<string>("userId"), StringComparer.Ordinal))
        {
            try { await ExecuteAsync(group.Key, null, async (games, _) => { await games.AdvanceDueManagedTurnsAsync(token); return true; }, token, dueIds: group.Select(document => document.Id).ToArray()); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger?.LogWarning(exception, "A private Hearts account could not recover its due turn."); }
        }
    }

    private Task<T> ExecuteAsync<T>(string userId, string? bodySha,
        Func<HeartsGameService, IHeartsOnboardingStore, Task<T>> action, CancellationToken token,
        Guid? sessionId = null, bool trialOnly = false, IReadOnlyList<string>? dueIds = null) =>
        FirestoreGameCommands.ExecuteAsync(database, Commands, userId, bodySha, async (transaction, ct) =>
        {
            var account = database.Collection(Accounts).Document(GameSecurityEventDocuments.Hash(userId));
            var gate = await transaction.GetSnapshotAsync(account, ct);
            var documents = new List<DocumentSnapshot>();
            var activeTrialId = gate.Exists && gate.TryGetValue<string>("activeTrialId", out var activeId) ? activeId : "";
            IReadOnlyList<string> ids = sessionId is { } id ? [id.ToString("N")]
                : trialOnly && activeTrialId.Length > 0 ? new[] { activeTrialId }
                : dueIds ?? Array.Empty<string>();
            foreach (var session in ids.Distinct(StringComparer.Ordinal))
            {
                var document = await transaction.GetSnapshotAsync(database.Collection(Sessions).Document(session), ct);
                if (document.Exists) documents.Add(document);
            }
            var onboardingDocument = HeartsOnboardingDocuments.Account(database, userId);
            var onboardingBefore = await transaction.GetSnapshotAsync(onboardingDocument, ct);
            var policy = await transaction.GetSnapshotAsync(database.Document(FirestoreManagedPlayerPolicy.StatePath), ct);
            var now = timeProvider.GetUtcNow();
            var buffer = new FreeEffects(userId, HeartsOnboardingDocuments.IsComplete(onboardingBefore));
            var games = new HeartsGameService(new HeartsManagedPlayerRoster(_ => Task.FromResult(FirestoreManagedPlayerPolicy.AllowsBots(policy, HeartsBackend.GameId))),
                new CommandTime(now), buffer, analytics: buffer, securityEvents: buffer, allowNewGames: allowNewGames);
            var before = documents.Select(Read).ToArray();
            if (before.Any(snapshot => snapshot.UserId != userId)) throw new HeartsAccessException();
            games.Restore(before);
            var result = await action(games, buffer);
            var after = games.Snapshot();
            var completions = buffer.Completions.GroupBy(capture => capture.Summary.Id, StringComparer.Ordinal).Select(group => group.First()).ToArray();
            var existingCompletions = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var capture in completions)
                existingCompletions[capture.Summary.Id] = (await transaction.GetSnapshotAsync(database.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id), ct)).Exists;
            var outboxes = buffer.Analytics.Select(GameAnalyticsOutbox.Create).Where(document => document is not null).Cast<GameAnalyticsOutboxDocument>().ToArray();
            var existingOutboxes = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var outbox in outboxes)
                existingOutboxes[outbox.Id] = (await transaction.GetSnapshotAsync(database.Collection(GameAnalyticsOutbox.CollectionName).Document(outbox.Id), ct)).Exists;
            // All reads precede every write, including onboarding, evidence and receipts.
            foreach (var snapshot in after)
            {
                var json = JsonSerializer.Serialize(snapshot, Json);
                if (documents.FirstOrDefault(document => document.Id == snapshot.Id.ToString("N")) is { } unchanged &&
                    unchanged.GetValue<string>("payloadJson") == json) continue;
                var next = snapshot.NextManagedActionAtUtc ?? snapshot.LastActivityAtUtc + HeartsGameService.AbandonedSessionTimeout;
                transaction.Set(database.Collection(Sessions).Document(snapshot.Id.ToString("N")), new Dictionary<string, object>
                { ["userId"] = userId, ["payloadJson"] = json, ["sha256"] = GameSecurityEventDocuments.Hash(json),
                  ["nextSweepAt"] = Timestamp.FromDateTime(next), ["isTrial"] = snapshot.IsTrial, ["backendOwner"] = HeartsBackend.Owner, ["schemaVersion"] = 1L });
            }
            foreach (var previous in documents.Where(document => !after.Any(snapshot => snapshot.Id.ToString("N") == document.Id))) transaction.Delete(previous.Reference);
            foreach (var observation in buffer.Observations) FirestoreGameAudit.WriteObservation(transaction, database, observation);
            foreach (var capture in completions.Where(capture => !existingCompletions[capture.Summary.Id])) FirestoreGameAudit.WriteCompletion(transaction, database, capture);
            foreach (var outbox in outboxes.Where(outbox => !existingOutboxes[outbox.Id])) transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(outbox.Id), outbox.Fields);
            if (buffer.CompletionFields is { } fields && !HeartsOnboardingDocuments.IsComplete(onboardingBefore)) transaction.Set(onboardingDocument, fields);
            var accountFields = new Dictionary<string, object> { ["version"] = gate.Exists ? checked(gate.GetValue<long>("version") + 1) : 1L };
            if (trialOnly || before.Any(snapshot => snapshot.Id.ToString("N") == activeTrialId) ||
                after.Any(snapshot => snapshot.IsTrial && !before.Any(previous => previous.Id == snapshot.Id)))
                accountFields["activeTrialId"] = after.Where(snapshot => snapshot.IsTrial && !snapshot.IsCancelled && !snapshot.IsExpired &&
                    snapshot.State.Winner is null && snapshot.State.Round.Phase != HeartsPhase.Complete)
                    .OrderByDescending(snapshot => snapshot.StartedAtUtc).Select(snapshot => snapshot.Id.ToString("N")).FirstOrDefault() ?? "";
            transaction.Set(account, accountFields, SetOptions.MergeAll);
            if (after.Any(snapshot => !before.Any(previous => previous.Id == snapshot.Id)))
                FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, HeartsBackend.GameId, userId, now.UtcDateTime);
            return result;
        }, () => new HeartsCommandConflictException(), token);

    private static HeartsFreeSessionSnapshot Read(DocumentSnapshot document)
    {
        if (!document.TryGetValue<long>("schemaVersion", out var schema) || schema != 1 ||
            !document.TryGetValue<string>("backendOwner", out var owner) || owner != HeartsBackend.Owner)
            throw new InvalidOperationException("Private Hearts state has an unknown owner or schema.");
        var json = document.GetValue<string>("payloadJson");
        if (GameSecurityEventDocuments.Hash(json) != document.GetValue<string>("sha256")) throw new InvalidOperationException("Private Hearts state failed its integrity check.");
        var value = JsonSerializer.Deserialize<HeartsFreeSessionSnapshot>(json, Json) ?? throw new InvalidOperationException("Private Hearts state is missing.");
        if (value.Id.ToString("N") != document.Id || value.UserId != document.GetValue<string>("userId")) throw new InvalidOperationException("Private Hearts identity does not match.");
        return value;
    }
    private sealed class CommandTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class FreeEffects(string userId, bool isComplete) : IGameSecurityEventSink, IGameAnalyticsSink, IHeartsOnboardingStore
    {
        internal List<GameSecurityEventDocument> Observations { get; } = [];
        internal List<RoundAuditCapture> Completions { get; } = [];
        internal List<GameAnalyticsSettlement> Analytics { get; } = [];
        internal Dictionary<string, object>? CompletionFields { get; private set; }
        public Task WriteAsync(GameSecurityEventDocument observation, CancellationToken cancellationToken = default) { Observations.Add(observation); return Task.CompletedTask; }
        public Task WriteCompletedAsync(GameSecurityEventDocument observation, RoundAuditCapture capture, CancellationToken cancellationToken = default)
        { Observations.Add(observation); Completions.Add(capture); return Task.CompletedTask; }
        public Task RecordAsync(GameAnalyticsSettlement settlement, CancellationToken token) { Analytics.Add(settlement); return Task.CompletedTask; }
        public bool TryEnqueuePractice(GameAnalyticsSettlement settlement) { Analytics.Add(settlement); return true; }
        public Task<HeartsOnboardingResponse> GetAsync(string user, CancellationToken token) { Require(user); return Task.FromResult(new HeartsOnboardingResponse(!(isComplete || CompletionFields is not null))); }
        public Task<HeartsOnboardingResponse> ConfirmExperienceAsync(string user, DateTime nowUtc, CancellationToken token)
        { Require(user); CompletionFields ??= HeartsOnboardingDocuments.Completed(userId, "experienced", null, nowUtc); return Task.FromResult(new HeartsOnboardingResponse(false)); }
        public Task CompleteTrialAsync(string user, Guid trialMatchId, DateTime nowUtc, CancellationToken token)
        { Require(user); CompletionFields ??= HeartsOnboardingDocuments.Completed(userId, "trial-completed", trialMatchId, nowUtc); return Task.CompletedTask; }
        private void Require(string user) { if (user != userId) throw new HeartsAccessException(); }
    }
}

public sealed class HeartsCommandConflictException() : InvalidOperationException("This Idempotency-Key was already used for different Hearts request terms.");
