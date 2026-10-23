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

namespace FortuneForge.Games.DragonTiger.Server;

public sealed record DragonTigerAccount(string UserId, decimal WalletBalance);
public delegate Task<DragonTigerAccount?> DragonTigerAccountResolver(HttpContext context, CancellationToken cancellationToken);
public sealed class DragonTigerServerOptions
{
    public bool ForcePractice { get; set; }
    public string? ReadRateLimitPolicy { get; set; }
    public string? WriteRateLimitPolicy { get; set; }
}

public static class DragonTigerEndpoints
{
    public static IServiceCollection AddDragonTigerServer(this IServiceCollection services, Action<DragonTigerServerOptions>? configure = null)
    {
        services.AddSingleton<PracticeDragonTigerStore>();
        services.AddOptions<DragonTigerServerOptions>();
        if (configure is not null) services.Configure(configure);
        return services;
    }

    /// <summary>The host resolves its own account cookie; this package never owns account authentication.</summary>
    public static IEndpointRouteBuilder MapDragonTiger(this IEndpointRouteBuilder endpoints, DragonTigerAccountResolver resolveAccount)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<DragonTigerServerOptions>>().Value;
        var api = endpoints.MapGroup("/api/games/dragon-tiger");
        var status = api.MapGet("/status", (Func<HttpContext, Task<IResult>>)((HttpContext context) => Handle(context, resolveAccount, async (account, service, practice) =>
        {
            await Task.CompletedTask;
            return Results.Ok(new DragonTigerStatusResponse(true, 0.50m, 100m, 0.50m, 100m,
                practice ? DragonTigerMoney.ToRand(context.RequestServices.GetRequiredService<PracticeDragonTigerStore>().GetBalanceCents(account.UserId)) : account.WalletBalance,
                practice ? "practice-eight-deck-dragon-tiger" : "eight-deck-dragon-tiger"));
        })));
        var start = api.MapPost("/rounds", (Func<HttpContext, Task<IResult>>)((HttpContext context) => Handle(context, resolveAccount, async (account, service, practice) =>
        {
            var request = await context.Request.ReadFromJsonAsync<CreateDragonTigerRoundRequest>(context.RequestAborted)
                ?? throw new ArgumentException("The Dragon Tiger request body is required.");
            var result = await service.StartAsync(account.UserId, request, context.Request.Headers["Idempotency-Key"].ToString(), context.RequestAborted);
            return Results.Created($"/api/games/dragon-tiger/rounds/{result.RoundId}", result);
        })));
        var latest = api.MapGet("/rounds/latest", (Func<HttpContext, Task<IResult>>)((HttpContext context) => Handle(context, resolveAccount, async (account, service, practice) =>
        {
            var result = await service.LatestAsync(account.UserId, context.RequestAborted);
            return result is null ? Results.NoContent() : Results.Ok(result);
        })));
        var get = api.MapGet("/rounds/{roundId}", (HttpContext context, string roundId) => Handle(context, resolveAccount,
            async (account, service, practice) => Results.Ok(await service.GetAsync(account.UserId, roundId, context.RequestAborted))));
        if (options.ReadRateLimitPolicy is { } readPolicy)
        {
            status.RequireRateLimiting(readPolicy);
            latest.RequireRateLimiting(readPolicy);
            get.RequireRateLimiting(readPolicy);
        }
        if (options.WriteRateLimitPolicy is { } writePolicy) start.RequireRateLimiting(writePolicy);
        return endpoints;
    }

    private static async Task<IResult> Handle(HttpContext context, DragonTigerAccountResolver resolveAccount,
        Func<DragonTigerAccount, DragonTigerService, bool, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (!context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Features:DragonTigerEnabled", false))
            return Error(503, "dragon-tiger-disabled", "Dragon Tiger is still being verified and cannot accept a wager yet.");
        try
        {
            var account = await resolveAccount(context, context.RequestAborted);
            if (account is null) return Error(401, "dragon-tiger-authentication-required", "Sign in to play Dragon Tiger.");
            var options = context.RequestServices.GetRequiredService<IOptions<DragonTigerServerOptions>>().Value;
            var practice = options.ForcePractice || string.Equals(context.Request.Headers["X-FortuneForge-Practice"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
            IDragonTigerStore store = practice ? context.RequestServices.GetRequiredService<PracticeDragonTigerStore>()
                : new DragonTigerFirestoreStore(context.RequestServices.GetRequiredService<FirestoreDb>());
            return await action(account, new DragonTigerService(store, TimeProvider.System), practice);
        }
        catch (DragonTigerRoundNotFoundException exception) { return Error(404, "dragon-tiger-round-not-found", exception.Message); }
        catch (DragonTigerRoundConflictException exception) { return Error(409, "dragon-tiger-round-conflict", exception.Message); }
        catch (DragonTigerInsufficientCreditsException exception) { return Error(409, "insufficient-slot-credits", exception.Message); }
        catch (Exception exception) when (exception is ArgumentException or JsonException or BadHttpRequestException)
        { return Error(400, "dragon-tiger-invalid-request", exception.Message); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.Aborted or StatusCode.DeadlineExceeded or StatusCode.Unavailable)
        {
            context.Response.Headers.RetryAfter = "1";
            return Error(503, "dragon-tiger-retry-required", "The wallet is busy. Retry the same wager with its original Idempotency-Key to recover its result.");
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("DragonTiger").LogError(exception,
                "Dragon Tiger request failed; trace {TraceIdentifier}.", context.TraceIdentifier);
            return Error(500, "dragon-tiger-unavailable", "The Dragon Tiger service could not complete the request. Retry the same request to recover its result.");
        }
    }
    private static IResult Error(int status, string code, string message) => Results.Json(new DragonTigerErrorResponse(code, message), statusCode: status);
}
