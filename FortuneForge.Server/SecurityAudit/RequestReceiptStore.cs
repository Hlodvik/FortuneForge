using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Server.SecurityAudit;

internal sealed class FirestoreGameSecurityRequestStore(FirestoreDb database) : IGameSecurityRequestStore
{
    internal const string Intents = "gameSecurityRequestIntents";
    internal const string Results = "gameSecurityRequestResults";
    internal const int MinimumOrphanRetentionDays = 7;

    public Task CreateIntentAsync(GameRequestObservation observation, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(observation, RoundAuditCapture.Json);
        return CreateAsync(Intents, observation.RequestId, new Dictionary<string, object>
        {
            ["requestId"] = observation.RequestId, ["userId"] = observation.UserId,
            ["gameId"] = observation.GameId, ["instanceId"] = observation.InstanceId!,
            ["receivedAtUtc"] = Timestamp.FromDateTime(observation.ReceivedAtUtc),
            ["sessionId"] = observation.SessionId!, ["ipAddress"] = observation.IpAddress!,
            ["ipSource"] = observation.IpSource, ["commandId"] = observation.CommandId!,
            ["stateVersion"] = observation.StateVersion is { } version ? version : null!,
            ["requestBodyState"] = observation.RequestBodyState,
            ["observationJson"] = json, ["sha256"] = GameSecurityEventDocuments.Hash(json), ["schemaVersion"] = 1L,
            // A command can be rejected before an instance or engine event exists. Keep its
            // private receipt long enough for reconciliation, then review it for deletion.
            ["orphanRetentionPending"] = true,
            ["orphanRetentionNextAttemptAtUtc"] = Timestamp.FromDateTime(
                observation.ReceivedAtUtc.AddMinutes(2))
        }, json, ct);
    }

    public Task CreateResultAsync(GameRequestResult result, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(result, RoundAuditCapture.Json);
        return CreateAsync(Results, result.RequestId, new Dictionary<string, object>
        {
            ["requestId"] = result.RequestId, ["completedAtUtc"] = Timestamp.FromDateTime(result.CompletedAtUtc),
            ["httpStatusCode"] = result.HttpStatusCode is { } code ? code : null!,
            ["outcome"] = result.Outcome, ["durationMilliseconds"] = result.DurationMilliseconds,
            ["observationJson"] = json, ["sha256"] = GameSecurityEventDocuments.Hash(json), ["schemaVersion"] = 1L
        }, json, ct);
    }

    private async Task CreateAsync(string collection, string id, Dictionary<string, object> data, string canonical, CancellationToken ct)
    {
        var reference = database.Collection(collection).Document(id);
        try { await reference.CreateAsync(data, cancellationToken: ct); }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.AlreadyExists)
        {
            // A retried delivery can only acknowledge exactly the original receipt, never overwrite it.
            var existing = await reference.GetSnapshotAsync(ct);
            if (!existing.TryGetValue<string>("observationJson", out var previous) || previous != canonical)
                throw new InvalidOperationException("An immutable game request receipt conflicts with its original content.");
        }
    }
}
