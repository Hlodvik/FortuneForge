using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Server.SecurityAudit;

public sealed class GameSecurityRequestOptions
{
    public bool Enabled { get; set; } = true;
    public int MaximumBodyBytes { get; set; } = 32_768;
    public int StorageTimeoutSeconds { get; set; } = 5;
    // DirectConnection ignores forwarded headers. TrustedProxyChain requires explicit ingress CIDRs.
    public string IpMode { get; set; } = "DirectConnection";
    public string[] TrustedProxyNetworks { get; set; } = [];
    public int MaximumForwardedHops { get; set; } = 8;
    public bool RequireTrustedProxy { get; set; }
    // Optional operator-supplied local JSON dataset. Absent by default; no network enrichment occurs.
    public string? GeoDatasetPath { get; set; }
}

internal sealed record GameRequestResult(
    string RequestId, DateTime CompletedAtUtc, int? HttpStatusCode,
    string Outcome, long DurationMilliseconds);

internal interface IGameSecurityRequestStore
{
    Task CreateIntentAsync(GameRequestObservation observation, CancellationToken ct);
    Task CreateResultAsync(GameRequestResult result, CancellationToken ct);
}

// Optional local/cached provider. No location service is called by the default implementation.
public interface IGameRequestGeoResolver
{
    Task<GameGeoObservation?> ObserveAsync(string ipAddress, DateTime receivedAtUtc, CancellationToken ct);
}

internal sealed class NoGameRequestGeoResolver : IGameRequestGeoResolver
{
    public Task<GameGeoObservation?> ObserveAsync(string ipAddress, DateTime receivedAtUtc, CancellationToken ct) =>
        Task.FromResult<GameGeoObservation?>(null);
}
