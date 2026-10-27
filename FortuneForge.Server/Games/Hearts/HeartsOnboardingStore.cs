using System.Security.Cryptography;
using System.Text;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Server.Games.Hearts;

public sealed record HeartsOnboardingResponse(bool ShouldAskPlayedBefore);
public sealed record ConfirmHeartsExperienceRequest(bool HasPlayedBefore);

public interface IHeartsOnboardingStore
{
    Task<HeartsOnboardingResponse> GetAsync(string userId, CancellationToken cancellationToken);
    Task<HeartsOnboardingResponse> ConfirmExperienceAsync(string userId, DateTime nowUtc, CancellationToken cancellationToken);
    Task CompleteTrialAsync(string userId, Guid trialMatchId, DateTime nowUtc, CancellationToken cancellationToken);
}

internal sealed class FirestoreHeartsOnboardingStore(FirestoreDb database) : IHeartsOnboardingStore
{
    public async Task<HeartsOnboardingResponse> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var snapshot = await Account(userId).GetSnapshotAsync(cancellationToken);
        return new(!IsComplete(snapshot));
    }

    public async Task<HeartsOnboardingResponse> ConfirmExperienceAsync(string userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await CompleteAsync(userId, "experienced", null, nowUtc, cancellationToken);
        return new(false);
    }

    public Task CompleteTrialAsync(string userId, Guid trialMatchId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (trialMatchId == Guid.Empty) throw new ArgumentException("An authoritative Hearts trial is required.", nameof(trialMatchId));
        return CompleteAsync(userId, "trial-completed", trialMatchId, nowUtc, cancellationToken);
    }

    private async Task CompleteAsync(string userId, string reason, Guid? trialMatchId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // Concurrent confirmations can exhaust Firestore's internal transaction retries
        // while waiting for the same onboarding document. Re-read it on a fresh attempt.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await CompleteOnceAsync(userId, reason, trialMatchId, nowUtc, cancellationToken);
                return;
            }
            catch (RpcException exception) when (
                exception.StatusCode == StatusCode.Aborted &&
                exception.Status.Detail.Contains("Transaction lock timeout", StringComparison.OrdinalIgnoreCase) &&
                attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(30, 120) * (attempt + 1)), cancellationToken);
            }
        }
    }

    private Task CompleteOnceAsync(string userId, string reason, Guid? trialMatchId, DateTime nowUtc, CancellationToken cancellationToken) =>
        database.RunTransactionAsync(async transaction =>
        {
            var reference = Account(userId);
            if (IsComplete(await transaction.GetSnapshotAsync(reference, cancellationToken))) return false;
            var fields = new Dictionary<string, object>
            {
                ["userId"] = userId,
                ["completed"] = true,
                ["completionReason"] = reason,
                ["completedAt"] = Timestamp.FromDateTime(nowUtc),
                ["schemaVersion"] = 1L
            };
            if (trialMatchId is { } matchId) fields["trialMatchId"] = matchId.ToString("N");
            transaction.Set(reference, fields);
            return true;
        }, cancellationToken: cancellationToken);

    private DocumentReference Account(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)));
        return database.Collection("heartsAccountOnboarding").Document(key);
    }

    private static bool IsComplete(DocumentSnapshot snapshot) =>
        snapshot.Exists && snapshot.TryGetValue<bool>("completed", out var value) && value;
}
