namespace FortuneForge.Games.Abstractions.Auditing;

// Observed request metadata is evidence of a request, never proof of an accepted game action.
public sealed record GameRequestObservation(
    string RequestId, string UserId, string? SessionId, string? IpAddress,
    DateTime ReceivedAtUtc, string Method, string Path, string GameId,
    string? InstanceId, string? CommandId, long? StateVersion,
    string? RequestJson, string RequestBodyState, string IpSource,
    GameGeoObservation? Geo = null);

// Provider estimates are observations, not authoritative player location or identity.
public sealed record GameGeoObservation(
    string Provider, string Version, DateTime ObservedAtUtc,
    string? CountryCode, string? RegionCode, string? City,
    double? Latitude, double? Longitude, double? AccuracyRadiusKm = null,
    double? Confidence = null, string? AutonomousSystem = null,
    string? NetworkId = null, bool? IsProxyOrVpn = null);

public static class GameSecurityContext
{
    private static readonly AsyncLocal<GameRequestObservation?> Observation = new();
    public static GameRequestObservation? Current => Observation.Value;

    public static IDisposable Push(GameRequestObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var previous = Observation.Value;
        Observation.Value = observation;
        return new Scope(previous);
    }

    private sealed class Scope(GameRequestObservation? previous) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            Observation.Value = previous;
            disposed = true;
        }
    }
}
