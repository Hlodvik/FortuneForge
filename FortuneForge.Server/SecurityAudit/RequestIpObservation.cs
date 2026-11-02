using System.Net;
using System.Net.Sockets;

namespace FortuneForge.Server.SecurityAudit;

internal sealed record GameRequestIp(string? Address, string Source, bool OriginAllowed);

internal static class GameRequestIpObservation
{
    internal static bool ValidOptions(GameSecurityRequestOptions options) =>
        options.MaximumBodyBytes is >= 1_024 and <= 65_536 &&
        options.StorageTimeoutSeconds is >= 1 and <= 30 &&
        options.MaximumForwardedHops is >= 1 and <= 32 &&
        options.IpMode is "DirectConnection" or "TrustedProxyChain" &&
        (options.IpMode != "TrustedProxyChain" || options.TrustedProxyNetworks.Length > 0) &&
        (!options.RequireTrustedProxy || options.IpMode == "TrustedProxyChain") &&
        options.TrustedProxyNetworks.All(value => TryNetwork(value, out _, out var prefix) && prefix > 0);

    // Must run before ForwardedHeadersMiddleware. Only the actual connection peer establishes trust.
    internal static GameRequestIp Read(IPAddress? connectionPeer, string forwardedFor, GameSecurityRequestOptions options)
    {
        var peer = Normalize(connectionPeer);
        if (options.IpMode == "DirectConnection") return new(peer?.ToString(), "connection", true);
        if (peer is null || !Trusted(peer, options))
            return new(peer?.ToString(), "untrusted-forwarding-ignored", !options.RequireTrustedProxy);

        var parts = forwardedFor.Split(',', StringSplitOptions.TrimEntries);
        if (string.IsNullOrWhiteSpace(forwardedFor) || parts.Length > options.MaximumForwardedHops ||
            parts.Any(part => !IPAddress.TryParse(part, out _)))
            return new(peer.ToString(), "invalid-forwarding-ignored", !options.RequireTrustedProxy);

        var candidate = peer;
        for (var index = parts.Length - 1; index >= 0 && Trusted(candidate, options); index--)
            candidate = Normalize(IPAddress.Parse(parts[index]))!;
        // Stop at the first untrusted hop; client-supplied earlier entries cannot override it.
        return new(candidate.ToString(), "trusted-proxy-chain", true);
    }

    private static bool Trusted(IPAddress address, GameSecurityRequestOptions options) =>
        options.TrustedProxyNetworks.Any(value => Contains(value, address));

    internal static bool Contains(string cidr, IPAddress address)
    {
        if (!TryNetwork(cidr, out var network, out var bits)) return false;
        var actual = Normalize(address)!.GetAddressBytes();
        var expected = network!.GetAddressBytes();
        if (actual.Length != expected.Length) return false;
        for (var index = 0; bits > 0; index++, bits -= 8)
        {
            var mask = (byte)(0xff << (8 - Math.Min(bits, 8)));
            if ((actual[index] & mask) != (expected[index] & mask)) return false;
        }
        return true;
    }

    internal static bool ValidNetwork(string value) => TryNetwork(value, out _, out _);

    private static bool TryNetwork(string value, out IPAddress? network, out int prefix)
    {
        network = null;
        prefix = 0;
        var parts = value.Split('/');
        if (parts.Length is < 1 or > 2 || !IPAddress.TryParse(parts[0], out var parsed)) return false;
        network = Normalize(parsed);
        var maximum = network!.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        prefix = maximum;
        return parts.Length == 1 || int.TryParse(parts[1], out prefix) && prefix >= 0 && prefix <= maximum;
    }

    private static IPAddress? Normalize(IPAddress? address) => address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address;
}
