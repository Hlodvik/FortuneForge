using System.Threading.RateLimiting;
using FortuneForge.Server.Accounts.Security;

namespace FortuneForge.Server.GameReports;

public static class GameReportsConfiguration
{
    public const string WritePolicy = "game-report-writes";
    public static IServiceCollection AddGameReports(this IServiceCollection services, IConfiguration configuration, bool development = false)
    {
        services.AddOptions<GameReportsOptions>().Bind(configuration.GetSection("GameReports")).PostConfigure(options =>
        {
            if (development) options.AllowedOrigins = [.. options.AllowedOrigins, "http://127.0.0.1:4177", "http://localhost:4177", "http://127.0.0.1:4178", "http://localhost:4178", "http://127.0.0.1:5173", "http://localhost:5173"];
        }).Validate(options => options.AllowedOrigins.Length <= 24 && options.AllowedOrigins.All(GameReportRequestSecurity.IsOrigin), "Report origins must be explicit HTTP(S) origins.").ValidateOnStart();
        services.AddRateLimiter(options => options.AddPolicy(WritePolicy, context => RateLimitPartition.GetFixedWindowLimiter(
            ClientRequestIdentity.GetSessionPartitionKey(context), _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 5, QueueLimit = 0, Window = TimeSpan.FromMinutes(10), AutoReplenishment = true })));
        services.AddSingleton<IGameReportAccountResolver, GameReportAccountResolver>();
        services.AddSingleton<IGameReportsStore, FirestoreGameReportsStore>();
        services.AddSingleton<GameReportsService>();
        return services;
    }
}
public sealed class GameReportsOptions
{
    public string[] AllowedOrigins { get; set; } = ["https://fortuneforgegame.web.app", "https://fortuneforgegame.firebaseapp.com"];
}
internal static class GameReportRequestSecurity
{
    public static bool IsOrigin(string value) => TryOrigin(value, out _);
    public static bool AllowsWrite(HttpRequest request, GameReportsOptions options, string header)
    {
        if (request.Headers[header].Count != 1 || request.Headers[header] != "1" || !request.HasJsonContentType() ||
            string.Equals(request.Headers["Sec-Fetch-Site"], "cross-site", StringComparison.OrdinalIgnoreCase)) return false;
        var supplied = request.Headers.Origin;
        if (supplied.Count > 1) return false;
        if (supplied.Count == 1)
        {
            if (!TryOrigin(supplied.ToString(), out var origin)) return false;
            return string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase) ||
                options.AllowedOrigins.Any(value => string.Equals(origin, value.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        }
        var authorization = request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(authorization[7..]);
    }
    private static bool TryOrigin(string value, out string origin)
    {
        origin = "";
        if (value.Length > 256 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not "http" and not "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        origin = uri.GetLeftPart(UriPartial.Authority); return true;
    }
}
