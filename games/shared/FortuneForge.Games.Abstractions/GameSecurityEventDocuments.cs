using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FortuneForge.Games.Abstractions.Auditing;

public sealed record GameSecurityEventDocument(string Id, Dictionary<string, object> Fields);

/// <summary>Private, content-addressed engine observations. Write in the state/financial transaction.</summary>
public static class GameSecurityEventDocuments
{
    public const string CollectionName = "gameSecurityEvents";
    public const string ManifestCollectionName = "gameSecurityEventManifests";
    public const string ContractVersion = "engine-observation-v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static GameSecurityEventDocument Capture(string gameId, string instanceId, long version,
        DateTime occurredAtUtc, object state, IEnumerable<string> participantIds,
        string eventKind = "engine-state", string? tableId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKind);
        ArgumentNullException.ThrowIfNull(state);
        if (occurredAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("An engine observation requires a server UTC time.", nameof(occurredAtUtc));
        if (version < 0) throw new ArgumentOutOfRangeException(nameof(version));
        var participants = participantIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var request = GameSecurityContext.Current;
        var json = JsonSerializer.Serialize(new
        {
            contractVersion = ContractVersion, gameId, instanceId, tableId, version, eventKind,
            occurredAtUtc, engineAssembly = state.GetType().Assembly.GetName().Name,
            engineAssemblyVersion = state.GetType().Assembly.GetName().Version?.ToString(),
            participants, request, state
        }, Json);
        // Reject oversized state before committing money. Silent truncation would destroy auditability.
        if (Encoding.UTF8.GetByteCount(json) > 800_000)
            throw new InvalidOperationException("Private engine evidence exceeds the document budget; split the instance before accepting more actions.");
        var digest = Hash(json);
        return new(digest, new()
        {
            ["gameId"] = gameId, ["instanceId"] = instanceId, ["tableId"] = tableId ?? instanceId,
            ["version"] = version, ["eventKind"] = eventKind, ["occurredAtUtc"] = occurredAtUtc,
            ["participantIds"] = participants, ["requestId"] = request?.RequestId ?? "",
            ["userId"] = request?.UserId ?? "", ["payloadJson"] = json, ["sha256"] = digest,
            ["contractVersion"] = ContractVersion, ["schemaVersion"] = 1L
        });
    }

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string ManifestId(string gameId, string instanceId) => Hash($"{gameId}\n{instanceId}");
}
