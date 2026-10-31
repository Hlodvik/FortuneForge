using System.Text.Json;
using FortuneForge.Server.Activity;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Server.Accounts.Security;

namespace FortuneForge.Server.Risk;

internal sealed class RiskEnforcementMiddleware(RequestDelegate next, ILogger<RiskEnforcementMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IGameActivityAccountResolver accounts, IRiskStateReader risks)
    {
        if (!context.Request.Path.StartsWithSegments("/api") || IsExempt(context.Request)) { await next(context); return; }
        if (string.IsNullOrWhiteSpace(AccountSessionCookie.Read(context.Request))) { await next(context); return; }
        PlayerRiskState? state;
        try
        {
            var userId = await accounts.UserIdAsync(context, context.RequestAborted);
            state = userId is null ? null : await risks.GetStateAsync(userId, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogWarning(error, "Could not verify player access restrictions");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { code = "restriction_check_unavailable", error = "Account access could not be checked. Please try again shortly." }, context.RequestAborted);
            return;
        }
        if (state is null) { await next(context); return; }
        if (state.AccountRestricted)
        {
            await Deny(context, "account_restricted", "Your account is under review.", reviewPath: "/account/review");
            return;
        }
        if (state.WithdrawalsBlocked && HttpMethods.IsPost(context.Request.Method) && context.Request.Path.Value?.TrimEnd('/').Equals("/api/payments/withdrawals", StringComparison.OrdinalIgnoreCase) == true)
        {
            await Deny(context, "withdrawals_restricted", "Withdrawals are unavailable while your account is reviewed.");
            return;
        }
        if (state.BlockedGameIds.Count > 0)
        {
            var game = await GameIdAsync(context.Request, context.RequestAborted);
            if (game is not null && state.BlockedGameIds.Contains(game, StringComparer.Ordinal))
            {
                await Deny(context, "game_restricted", "This game is unavailable while your activity is reviewed.", gameId: game);
                return;
            }
        }
        await next(context);
    }

    private static bool IsExempt(HttpRequest request)
    {
        var path = request.Path.Value?.TrimEnd('/') ?? "";
        return HttpMethods.IsPost(request.Method) && (path.Equals("/api/accounts/login", StringComparison.OrdinalIgnoreCase) || path.Equals("/api/accounts/logout", StringComparison.OrdinalIgnoreCase))
            || HttpMethods.IsGet(request.Method) && path.Equals("/api/account/review", StringComparison.OrdinalIgnoreCase)
            // Provider webhooks must settle already authorized payments even if
            // a player's access was restricted after the request began.
            || path.StartsWith("/api/payments/webhooks/", StringComparison.OrdinalIgnoreCase);
    }

    internal static Task Deny(HttpContext context, string code, string error, string? reviewPath = null, string? gameId = null)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new { code, error, reviewPath, gameId }, context.RequestAborted);
    }

    internal static async Task<string?> GameIdAsync(HttpRequest request, CancellationToken ct)
    {
        var segments = (request.Path.Value ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)) return null;
        if (segments[1].Equals("solitaire", StringComparison.OrdinalIgnoreCase)) return "solitaire";
        if (segments.Length >= 3 && (segments[1].Equals("games", StringComparison.OrdinalIgnoreCase) || segments[1].Equals("cards", StringComparison.OrdinalIgnoreCase)))
        {
            var canonical = segments[2].ToLowerInvariant();
            return GameAnalyticsOutbox.GameIds.Contains(canonical) ? canonical : null;
        }
        if (!segments[1].Equals("slots", StringComparison.OrdinalIgnoreCase)) return null;
        string? id = HttpMethods.IsGet(request.Method) ? request.Query["gameId"].FirstOrDefault() : null;
        if (!HttpMethods.IsGet(request.Method) && request.HasJsonContentType())
        {
            request.EnableBuffering();
            try
            {
                using var json = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
                if (json.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var property in json.RootElement.EnumerateObject())
                        if (property.Name.Equals("gameId", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                        { id = property.Value.GetString(); }
            }
            catch (JsonException) { /* Endpoint still owns malformed JSON validation. */ }
            finally { request.Body.Position = 0; }
        }
        id = id?.Trim().ToLowerInvariant();
        return id is not null && AcceptedGameActivityReader.SlotIds.TryGetValue(id, out var game) ? game : id is not null && GameAnalyticsOutbox.GameIds.Contains(id) ? id : null;
    }
}

public static class RiskConfiguration
{
    public static IServiceCollection AddPlayerRiskEnforcement(this IServiceCollection services)
    {
        services.AddSingleton<FirestoreRiskIncidentStore>();
        services.AddSingleton<IRiskStateReader>(provider => provider.GetRequiredService<FirestoreRiskIncidentStore>());
        return services;
    }
    public static IApplicationBuilder UsePlayerRiskEnforcement(this IApplicationBuilder app) => app.UseMiddleware<RiskEnforcementMiddleware>();
}
