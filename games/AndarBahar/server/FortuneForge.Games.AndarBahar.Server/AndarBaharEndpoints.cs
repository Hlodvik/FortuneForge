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

namespace FortuneForge.Games.AndarBahar.Server;

public sealed record AndarBaharAccount(string UserId, decimal WalletBalance);
public delegate Task<AndarBaharAccount?> AndarBaharAccountResolver(HttpContext context, CancellationToken cancellationToken);
public sealed class AndarBaharServerOptions
{
    public bool ForcePractice { get; set; }
    public bool AllowPracticeRequests { get; set; }
    public string PracticeStatePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "practice", "andar-bahar.json");
    public long PracticeStartingBalanceCents { get; set; } = 1_000_000;
    public string? ReadRateLimitPolicy { get; set; }
    public string? WriteRateLimitPolicy { get; set; }
}

public static class AndarBaharEndpoints
{
    public static IServiceCollection AddAndarBaharServer(this IServiceCollection services, Action<AndarBaharServerOptions>? configure = null)
    {
        services.AddOptions<AndarBaharServerOptions>();
        if (configure is not null) services.Configure(configure);
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AndarBaharServerOptions>>().Value;
            return new PracticeAndarBaharStore(options.PracticeStatePath, options.PracticeStartingBalanceCents);
        });
        return services;
    }
    /// <summary>The host resolves its authenticated account cookie and attaches its rate limit policy names.</summary>
    public static IEndpointRouteBuilder MapAndarBahar(this IEndpointRouteBuilder endpoints, AndarBaharAccountResolver resolveAccount)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<AndarBaharServerOptions>>().Value;
        var api = endpoints.MapGroup("/api/games/andar-bahar");
        var status = api.MapGet("/status", (Func<HttpContext, Task<IResult>>)((HttpContext context) => Handle(context, resolveAccount, async (account, store, service, practice) =>
            Results.Ok(new AndarBaharStatusResponse(Enabled(context), .50m, 100m, .50m,
                AndarBaharMoney.ToRand(await store.BalanceAsync(account.UserId, context.RequestAborted)),
                practice ? "development-practice-andar-first" : "andar-first")))));
        var create = api.MapPost("/rounds", (Func<HttpContext, Task<IResult>>)((HttpContext context) => Handle(context, resolveAccount, async (account, store, service, practice) =>
        {
            var request = await context.Request.ReadFromJsonAsync<CreateAndarBaharRoundRequest>(context.RequestAborted)
                ?? throw new ArgumentException("The Andar Bahar request body is required.");
            var result = await service.StartAsync(account.UserId, request, context.Request.Headers["Idempotency-Key"].ToString(), context.RequestAborted);
            return Results.Created($"/api/games/andar-bahar/rounds/{result.RoundId}", result);
        })));
        var latest = api.MapGet("/rounds/latest", (Func<HttpContext, Task<IResult>>)((HttpContext context) => Handle(context, resolveAccount, async (account, store, service, practice) =>
        {
            var round = await service.LatestAsync(account.UserId, context.RequestAborted);
            return round is null ? Results.NoContent() : Results.Ok(round);
        })));
        var get = api.MapGet("/rounds/{roundId}", (HttpContext context, string roundId) => Handle(context, resolveAccount,
            async (account, store, service, practice) => Results.Ok(await service.GetAsync(account.UserId, roundId, context.RequestAborted))));
        if (options.ReadRateLimitPolicy is { } read) { status.RequireRateLimiting(read); latest.RequireRateLimiting(read); get.RequireRateLimiting(read); }
        if (options.WriteRateLimitPolicy is { } write) create.RequireRateLimiting(write);
        return endpoints;
    }
    private static async Task<IResult> Handle(HttpContext context, AndarBaharAccountResolver resolveAccount,
        Func<AndarBaharAccount, IAndarBaharStore, AndarBaharService, bool, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        context.Response.Headers.Vary = "Cookie, X-FortuneForge-Practice";
        try
        {
            var account = await resolveAccount(context, context.RequestAborted);
            if (account is null) return Error(401, "andar-bahar-authentication-required", "Sign in to play Andar Bahar.");
            AndarBaharService.ValidateUser(account.UserId);
            var options = context.RequestServices.GetRequiredService<IOptions<AndarBaharServerOptions>>().Value;
            var requestedPractice = string.Equals(context.Request.Headers["X-FortuneForge-Practice"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
            if (requestedPractice && !options.ForcePractice && !options.AllowPracticeRequests)
                return Error(403, "andar-bahar-practice-disabled", "Practice play is unavailable on this host.");
            var practice = options.ForcePractice || requestedPractice;
            IAndarBaharStore store = practice ? context.RequestServices.GetRequiredService<PracticeAndarBaharStore>()
                : new AndarBaharFirestoreStore(context.RequestServices.GetRequiredService<FirestoreDb>());
            // Gate creation inside the store's atomic operation, after an exact-key replay
            // has been checked. A kill switch must never hide an already settled wager.
            return await action(account, store, new AndarBaharService(store, TimeProvider.System, allowNewRounds: Enabled(context)), practice);
        }
        catch (AndarBaharDisabledException exception) { return Error(503, "andar-bahar-disabled", exception.Message); }
        catch (AndarBaharRoundNotFoundException exception) { return Error(404, "andar-bahar-round-not-found", exception.Message); }
        catch (AndarBaharRoundConflictException exception) { return Error(409, "andar-bahar-round-conflict", exception.Message); }
        catch (AndarBaharInsufficientCreditsException exception) { return Error(409, "insufficient-slot-credits", exception.Message); }
        catch (Exception exception) when (exception is ArgumentException or JsonException or BadHttpRequestException)
        { return Error(400, "andar-bahar-invalid-request", exception.Message); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.Aborted or StatusCode.DeadlineExceeded or StatusCode.Unavailable)
        {
            context.Response.Headers.RetryAfter = "1";
            return Error(503, "andar-bahar-retry-required", "The wallet is busy. Retry this same wager with its original Idempotency-Key to recover its result.");
        }
        catch (IOException exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AndarBahar").LogError(exception, "Andar Bahar persistence failed; trace {Trace}.", context.TraceIdentifier);
            context.Response.Headers.RetryAfter = "1";
            return Error(503, "andar-bahar-retry-required", "The wallet is temporarily unavailable. Retry the same wager with its original Idempotency-Key.");
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AndarBahar").LogError(exception, "Andar Bahar failed; trace {Trace}.", context.TraceIdentifier);
            return Error(500, "andar-bahar-unavailable", "The service could not complete the request. Retry the same wager to recover its result.");
        }
    }
    private static bool Enabled(HttpContext context) => context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Features:AndarBaharEnabled", false);
    private static IResult Error(int status, string code, string message) => Results.Json(new AndarBaharErrorResponse(code, message), statusCode: status);
}
