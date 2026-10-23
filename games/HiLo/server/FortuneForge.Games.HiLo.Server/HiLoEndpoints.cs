using System.Text.Json;
using Google.Cloud.Firestore;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FortuneForge.Games.HiLo.Server;

/// <summary>The host authenticates its account cookie and returns its canonical account ID. No client-supplied player identifier is read.</summary>
public sealed record HiLoAccount(string UserId);
public delegate Task<HiLoAccount?> HiLoAccountResolver(HttpContext context, CancellationToken cancellationToken);
public sealed class HiLoServerOptions
{
    public bool ForcePractice { get; set; }
    public string PracticeStorePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "hi-lo-practice.json");
    public string[] AllowedOrigins { get; set; } = [];
    public string? ReadRateLimitPolicy { get; set; }
    public string? WriteRateLimitPolicy { get; set; }
}

public static class HiLoEndpoints
{
    public static IServiceCollection AddHiLoServer(this IServiceCollection services, Action<HiLoServerOptions>? configure = null)
    {
        services.AddOptions<HiLoServerOptions>();
        if (configure is not null) services.Configure(configure);
        services.AddSingleton(provider => new DurablePracticeHiLoStore(provider.GetRequiredService<IOptions<HiLoServerOptions>>().Value.PracticeStorePath));
        return services;
    }
    public static IEndpointRouteBuilder MapHiLo(this IEndpointRouteBuilder endpoints, HiLoAccountResolver resolveAccount)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<HiLoServerOptions>>().Value;
        var api = endpoints.MapGroup("/api/games/hi-lo");
        var session = api.MapGet("/session", (Func<HttpContext, Task<IResult>>)((HttpContext context) => Handle(context, resolveAccount, false,
            (service, user) => service.GetSessionAsync(user, context.RequestAborted))));
        var start = api.MapPost("/rounds", (Func<HttpContext, Task<IResult>>)((HttpContext context) => Handle(context, resolveAccount, true, async (service, user) =>
        {
            var request = await context.Request.ReadFromJsonAsync<HiLoStartRequest>(context.RequestAborted)
                ?? throw new HiLoException("invalid_request", "A JSON wager body is required.", 400);
            return await service.StartAsync(user, request, context.RequestAborted);
        })));
        var action = api.MapPost("/rounds/{roundId}/actions", (HttpContext context, string roundId) => Handle(context, resolveAccount, true, async (service, user) =>
        {
            var request = await context.Request.ReadFromJsonAsync<HiLoActionRequest>(context.RequestAborted)
                ?? throw new HiLoException("invalid_request", "A JSON action body is required.", 400);
            return await service.ActAsync(user, roundId, request, context.RequestAborted);
        }));
        if (options.ReadRateLimitPolicy is { } read) session.RequireRateLimiting(read);
        if (options.WriteRateLimitPolicy is { } write) { start.RequireRateLimiting(write); action.RequireRateLimiting(write); }
        return endpoints;
    }
    private static async Task<IResult> Handle(HttpContext context, HiLoAccountResolver resolveAccount, bool mutating,
        Func<HiLoService, string, Task<HiLoSessionResponse>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        context.Response.Headers["Pragma"] = "no-cache";
        var enabled = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Features:HiLoEnabled", false);
        try
        {
            var options = context.RequestServices.GetRequiredService<IOptions<HiLoServerOptions>>().Value;
            if (mutating && !SafeMutation(context, options)) return Error(403, "invalid_origin", "This action must originate from the Hi-Lo game.");
            var account = await resolveAccount(context, context.RequestAborted);
            if (account is null || string.IsNullOrWhiteSpace(account.UserId)) return Error(401, "authentication_required", "Sign in to play Hi-Lo.");
            var practice = options.ForcePractice || string.Equals(context.Request.Headers["X-FortuneForge-Practice"], "true", StringComparison.OrdinalIgnoreCase);
            IHiLoStore store = practice ? context.RequestServices.GetRequiredService<DurablePracticeHiLoStore>()
                : new HiLoFirestoreStore(context.RequestServices.GetRequiredService<FirestoreDb>());
            var response = await action(new(store, allowNewMutations: enabled), account.UserId);
            if (!enabled && !mutating && response.Round is null)
                return Error(503, "hi_lo_disabled", "Hi-Lo is still being verified and cannot accept wagers.");
            return Results.Ok(mutating ? response : response with { Enabled = enabled });
        }
        catch (HiLoException exception) { return Error(exception.StatusCode, exception.Code, exception.Message); }
        catch (Exception exception) when (exception is JsonException or BadHttpRequestException or ArgumentException)
        { return Error(400, "invalid_request", "The Hi-Lo request is invalid. Check the wager and action fields."); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.Aborted or StatusCode.DeadlineExceeded or StatusCode.Unavailable)
        {
            context.Response.Headers.RetryAfter = "1";
            return Error(503, "retry_required", "The wallet is busy. Retry the exact request with its original operationId.");
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("HiLo").LogError(exception,
                "Hi-Lo request failed; trace {TraceIdentifier}.", context.TraceIdentifier);
            return Error(503, "retry_required", "Hi-Lo could not confirm this request. Retry the exact request with its original operationId.");
        }
    }
    private static bool SafeMutation(HttpContext context, HiLoServerOptions options)
    {
        if (context.Request.Headers["X-FortuneForge-Game"] != "hi-lo") return false;
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) return context.Request.Headers["Sec-Fetch-Site"] != "cross-site";
        var sameOrigin = context.Request.Scheme + "://" + context.Request.Host;
        return string.Equals(origin, sameOrigin, StringComparison.OrdinalIgnoreCase) || options.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }
    private static IResult Error(int status, string code, string message) => Results.Json(new HiLoErrorResponse(code, message), statusCode: status);
}
