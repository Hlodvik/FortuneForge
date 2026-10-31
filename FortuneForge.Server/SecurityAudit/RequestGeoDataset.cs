using System.Net;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.SecurityAudit;

// Explicitly configured, bounded local data avoids hidden network calls or third-party credentials.
// Updates take effect on restart, and each observation records the loaded provider and dataset version.
internal sealed class ConfiguredGameRequestGeoResolver : IGameRequestGeoResolver
{
    private readonly GameGeoDataset? dataset;
    private readonly IReadOnlyList<GameGeoNetwork> networks;
    public ConfiguredGameRequestGeoResolver(IOptions<GameSecurityRequestOptions> options)
    {
        dataset = Load(options.Value.GeoDatasetPath);
        networks = dataset?.Networks.OrderByDescending(network => Prefix(network.Cidr)).ToArray() ?? [];
    }

    public Task<GameGeoObservation?> ObserveAsync(string ipAddress, DateTime receivedAtUtc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (dataset is null || !IPAddress.TryParse(ipAddress, out var address)) return Task.FromResult<GameGeoObservation?>(null);
        var network = networks.FirstOrDefault(item => GameRequestIpObservation.Contains(item.Cidr, address));
        return Task.FromResult(network is null ? null : new GameGeoObservation(dataset.Provider, dataset.Version, receivedAtUtc,
            network.CountryCode, network.RegionCode, network.City, network.Latitude, network.Longitude,
            network.AccuracyRadiusKm, network.Confidence, network.AutonomousSystem, network.NetworkId, network.IsProxyOrVpn));
    }

    internal static bool ValidOptions(GameSecurityRequestOptions options)
    {
        try { _ = Load(options.GeoDatasetPath); return true; }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return false; }
    }

    private static GameGeoDataset? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("Use an absolute local dataset path.");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 2 or > 1_048_576) throw new ArgumentException("The local dataset must be at most 1 MiB.");
        using var stream = File.OpenRead(path);
        var value = JsonSerializer.Deserialize<GameGeoDataset>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 8 })
            ?? throw new ArgumentException("A dataset is required.");
        if (value.Networks is null || value.Networks.Count > 10_000 || value.Networks.Any(network => network is null) ||
            GameSecurityRequestMiddleware.ValidateGeo(new(value.Provider, value.Version, DateTime.UnixEpoch, null, null, null, null, null)) is null)
            throw new ArgumentException("The dataset header or network count is invalid.");
        foreach (var network in value.Networks)
        {
            if (network.Cidr is null || !GameRequestIpObservation.ValidNetwork(network.Cidr) ||
                GameSecurityRequestMiddleware.ValidateGeo(new(value.Provider, value.Version, DateTime.UnixEpoch,
                    network.CountryCode, network.RegionCode, network.City, network.Latitude, network.Longitude,
                    network.AccuracyRadiusKm, network.Confidence, network.AutonomousSystem, network.NetworkId, network.IsProxyOrVpn)) is null)
                throw new ArgumentException("A dataset network is invalid.");
        }
        return value;
    }

    private static int Prefix(string cidr) => cidr.Contains('/') ? int.Parse(cidr.Split('/')[1], System.Globalization.CultureInfo.InvariantCulture) :
        IPAddress.Parse(cidr).AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
}

internal sealed record GameGeoDataset(string Provider, string Version, IReadOnlyList<GameGeoNetwork> Networks);
internal sealed record GameGeoNetwork(string Cidr, string? CountryCode = null, string? RegionCode = null, string? City = null,
    double? Latitude = null, double? Longitude = null, double? AccuracyRadiusKm = null, double? Confidence = null,
    string? AutonomousSystem = null, string? NetworkId = null, bool? IsProxyOrVpn = null);
