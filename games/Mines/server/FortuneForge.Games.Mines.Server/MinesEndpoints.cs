using System.Text.Json;
using Google.Cloud.Firestore;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FortuneForge.Games.Mines.Server;

public sealed record MinesAccount(string UserId);
public delegate Task<MinesAccount?> MinesAccountResolver(HttpContext context, CancellationToken cancellationToken);
public sealed class MinesServerOptions
{
    public bool ForcePractice { get; set; }
    public string? PracticeLedgerPath { get; set; }
    public string? ReadRateLimitPolicy { get; set; }
    public string? WriteRateLimitPolicy { get; set; }
    /// <summary>Local sample origins only. Paid writes always require the request's own origin.</summary>
    public string[] PracticeOrigins { get; set; } = [];
}

public static class MinesEndpoints
{
    public static IServiceCollection AddMinesServer(this IServiceCollection services, Action<MinesServerOptions>? configure = null)
    {
        services.AddOptions<MinesServerOptions>();
        if (configure is not null) services.Configure(configure);
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MinesServerOptions>>().Value;
            var path = options.PracticeLedgerPath ?? Path.Combine(provider.GetRequiredService<IHostEnvironment>().ContentRootPath, "App_Data", "mines-practice.json");
            return new FileMinesPracticeStore(path);
        });
        return services;
    }

    /// <summary>The host owns authentication and supplies its existing authenticated account resolver.</summary>
    public static IEndpointRouteBuilder MapMines(this IEndpointRouteBuilder endpoints, MinesAccountResolver resolver)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<MinesServerOptions>>().Value;
        var api = endpoints.MapGroup("/api/games/mines");
        var session = api.MapGet("/session", (Func<HttpContext, Task<IResult>>)((HttpContext context) => Handle(context, resolver, false,
            (service, user) => service.SessionAsync(user, context.RequestAborted))));
        var start = api.MapPost("/rounds", (Func<HttpContext, Task<IResult>>)((HttpContext context) => Handle(context, resolver, true, async (service, user) =>
        {
            var request = await context.Request.ReadFromJsonAsync<StartMinesRequest>(context.RequestAborted) ?? throw new ArgumentException("A Mines request body is required.");
            return await service.StartAsync(user, request, context.RequestAborted);
        }, created: true)));
        var reveal = api.MapPost("/rounds/{roundId}/reveal", (HttpContext context, string roundId) => Handle(context, resolver, true, async (service, user) =>
        {
            var request = await context.Request.ReadFromJsonAsync<RevealMinesRequest>(context.RequestAborted) ?? throw new ArgumentException("A Mines request body is required.");
            return await service.RevealAsync(user, roundId, request, context.RequestAborted);
        }));
        var cashout = api.MapPost("/rounds/{roundId}/cashout", (HttpContext context, string roundId) => Handle(context, resolver, true, async (service, user) =>
        {
            var request = await context.Request.ReadFromJsonAsync<CashOutMinesRequest>(context.RequestAborted) ?? throw new ArgumentException("A Mines request body is required.");
            return await service.CashOutAsync(user, roundId, request, context.RequestAborted);
        }));
        if (options.ReadRateLimitPolicy is { } read) session.RequireRateLimiting(read);
        if (options.WriteRateLimitPolicy is { } write)
        {
            start.RequireRateLimiting(write); reveal.RequireRateLimiting(write); cashout.RequireRateLimiting(write);
        }
        return endpoints;
    }

    private static async Task<IResult> Handle(HttpContext context, MinesAccountResolver resolver, bool write,
        Func<MinesService, string, Task<MinesSessionResponse>> action, bool created = false)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue("Features:MinesEnabled", false))
            return Error(503, "mines-disabled", "Mines is not available yet.");
        try
        {
            var account = await resolver(context, context.RequestAborted);
            if (account is null) return Error(401, "mines-authentication-required", "Sign in to play Mines.");
            var options = context.RequestServices.GetRequiredService<IOptions<MinesServerOptions>>().Value;
            var practice = options.ForcePractice || string.Equals(context.Request.Headers["X-FortuneForge-Practice"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
            if (!practice && !configuration.GetValue("Features:MinesPaidEnabled", false))
                return Error(503, "mines-paid-disabled", "Paid Mines play is not enabled. Choose practice mode.");
            if (write && !TrustedWrite(context, practice ? options.PracticeOrigins : []))
                return Error(403, "mines-origin-rejected", "This request must originate from the game page and use JSON.");
            IMinesStore store = practice ? context.RequestServices.GetRequiredService<FileMinesPracticeStore>()
                : new FirestoreMinesStore(context.RequestServices.GetRequiredService<FirestoreDb>());
            var result = await action(new MinesService(store), account.UserId);
            return created ? Results.Json(result, statusCode: 201) : Results.Ok(result);
        }
        catch (MinesNotFoundException exception) { return Error(404, "mines-round-not-found", exception.Message); }
        catch (MinesConflictException exception) { return Error(409, "mines-conflict", exception.Message); }
        catch (MinesInsufficientFundsException exception) { return Error(409, "mines-insufficient-funds", exception.Message); }
        catch (Exception exception) when (exception is ArgumentException or JsonException or BadHttpRequestException)
        { return Error(400, "mines-invalid-request", exception.Message); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.Aborted or StatusCode.DeadlineExceeded or StatusCode.Unavailable)
        {
            context.Response.Headers.RetryAfter = "1";
            return Error(503, "mines-retry-required", "The wallet is busy. Retry the same action with its original requestId to recover its result.");
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Mines").LogError(exception,
                "Mines request failed; trace {TraceIdentifier}.", context.TraceIdentifier);
            return Error(500, "mines-unavailable", "The action could not be confirmed. Retry with its original requestId, then refresh the session.");
        }
    }

    private static bool TrustedWrite(HttpContext context, string[] additionalOrigins)
    {
        if (!context.Request.HasJsonContentType()) return false;
        var origin = context.Request.Headers.Origin.ToString();
        var sameOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
        if (origin.Length > 0)
            return string.Equals(origin, sameOrigin, StringComparison.OrdinalIgnoreCase) || additionalOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
        return !string.Equals(context.Request.Headers["Sec-Fetch-Site"].ToString(), "cross-site", StringComparison.OrdinalIgnoreCase);
    }
    private static IResult Error(int status, string code, string message) => Results.Json(new MinesErrorResponse(code, message), statusCode: status);
}
