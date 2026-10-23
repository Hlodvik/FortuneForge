namespace FortuneForge.Server.Admin.Management;

internal static class AdminManagementRequestSecurity
{
    public const string Header = "X-FortuneForge-Admin";

    public static bool IsOrigin(string value) => TryOrigin(value, out _);

    public static bool AllowsWrite(HttpRequest request, AdminManagementOptions options)
    {
        if (request.Headers[Header].Count != 1 || request.Headers[Header] != "1") return false;
        if (!request.HasJsonContentType()) return false;
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (string.Equals(fetchSite, "cross-site", StringComparison.OrdinalIgnoreCase)) return false;
        var supplied = request.Headers.Origin;
        if (supplied.Count > 1) return false;
        if (supplied.Count == 1)
        {
            if (!TryOrigin(supplied.ToString(), out var origin)) return false;
            var sameOrigin = $"{request.Scheme}://{request.Host}";
            return string.Equals(origin, sameOrigin, StringComparison.OrdinalIgnoreCase) || options.AllowedOrigins.Any(value => string.Equals(origin, value.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        }
        // Browser cookie writes must carry an origin. Non-browser authenticated
        // API clients may use an explicit bearer token plus the custom header.
        var authorization = request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(authorization[7..]);
    }

    private static bool TryOrigin(string? value, out string origin)
    {
        origin = string.Empty;
        if (value is null || value.Length > 256 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not "https" and not "http" || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
