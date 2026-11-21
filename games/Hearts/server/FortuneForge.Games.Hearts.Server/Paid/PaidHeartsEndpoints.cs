using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Google.Cloud.Firestore;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FortuneForge.Server.Games.Hearts;

public static class PaidHeartsEndpoints
{
    internal static string Prefix => HeartsBackend.ApiPath + "/paid";
    public static IServiceCollection AddHeartsPaidBackend(this IServiceCollection services)
    {
        services.AddGameWalletClient(HeartsBackend.GameId);
        services.AddGameWalletBatchClient(HeartsBackend.GameId);
        services.AddManagedPlayerRosterClient(HeartsBackend.GameId);
        services.AddSingleton(provider => new FirestorePaidHeartsStore(provider.GetRequiredService<FirestoreDb>(),
            provider.GetRequiredService<IGameWalletBatchClient>(), provider.GetRequiredService<IGameWalletAccountReader>(),
            provider.GetRequiredService<IManagedPlayerRosterClient>(),
            provider.GetRequiredService<IConfiguration>().GetSection("Cards:PaidHearts").Get<PaidHeartsOptions>() ?? new(),
            provider.GetRequiredService<ILogger<FirestorePaidHeartsStore>>(),
            () => provider.GetRequiredService<IConfiguration>().GetValue(HeartsBackend.FreeAdmissionSetting, false)));
        if (services.Any(service => service.ServiceType == typeof(FirestoreDb))) services.AddHostedService<PaidHeartsSweepWorker>();
        return services;
    }

    public static IEndpointRouteBuilder MapHeartsPaid(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> account)
    {
        var api = endpoints.MapGroup(Prefix).WithMetadata(new GameWorkerPublicRoute(HeartsBackend.GameId, HeartsBackend.ApiPath, HeartsBackend.GameId));
        api.MapGet("/session", new Func<HttpContext, Task<IResult>>(context => Execute(context, account, (store, user, token) => store.SessionAsync(user, DateTime.UtcNow, token))));
        api.MapGet("/matches/{matchId}", (string matchId, HttpContext context) => Execute(context, account, (store, user, token) => store.GetAsync(user, matchId, DateTime.UtcNow, token)));
        api.MapPost("/join", (JoinPaidHeartsRequest request, HttpContext context) => Execute(context, account,
            (store, user, token) => store.JoinAsync(user, "", request.EntryCredits, Key(context), DateTime.UtcNow, token)));
        api.MapPost("/matches/{matchId}/play-house", (string matchId, PaidHeartsVersionRequest request, HttpContext context) =>
            Execute(context, account, (store, user, token) => store.PlayHouseAsync(user, matchId, request.ExpectedVersion, Key(context), DateTime.UtcNow, token)));
        api.MapPost("/matches/{matchId}/pass", (string matchId, PaidHeartsPassRequest request, HttpContext context) =>
            Execute(context, account, (store, user, token) => store.CommandAsync(user, matchId, "pass", request.ExpectedVersion, request.Cards, Key(context), DateTime.UtcNow, token)));
        api.MapPost("/matches/{matchId}/card", (string matchId, PaidHeartsCardRequest request, HttpContext context) =>
            Execute(context, account, (store, user, token) => store.CommandAsync(user, matchId, "card", request.ExpectedVersion, [request.Card], Key(context), DateTime.UtcNow, token)));
        api.MapPost("/matches/{matchId}/advance", (string matchId, PaidHeartsVersionRequest? request, HttpContext context) =>
            Execute(context, account, (store, user, token) => store.AdvanceCommandAsync(user, matchId, request?.ExpectedVersion, Key(context), DateTime.UtcNow, token)));
        api.MapPost("/matches/{matchId}/next-round", (string matchId, PaidHeartsVersionRequest request, HttpContext context) =>
            Execute(context, account, (store, user, token) => store.CommandAsync(user, matchId, "next-round", request.ExpectedVersion, null, Key(context), DateTime.UtcNow, token)));
        api.MapPost("/matches/{matchId}/leave", (string matchId, PaidHeartsVersionRequest request, HttpContext context) =>
            Execute(context, account, (store, user, token) => store.CommandAsync(user, matchId, "leave", request.ExpectedVersion, null, Key(context), DateTime.UtcNow, token)));
        api.MapGet("/history", new Func<HttpContext, Task<IResult>>(context => Execute(context, account, (store, user, token) => store.HistoryAsync(user, token))));
        return endpoints;
    }
    private static string Key(HttpContext context)
    {
        var keys = context.Request.Headers["Idempotency-Key"];
        if (keys.Count != 1 || string.IsNullOrWhiteSpace(keys[0])) throw new ArgumentException("Include one valid Idempotency-Key header.");
        return keys[0]!;
    }
    private static async Task<IResult> Execute<T>(HttpContext context, Func<HttpContext, string?> account,
        Func<FirestorePaidHeartsStore, string, CancellationToken, Task<T>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (account(context) is not { Length: > 0 } user) return Error(401, "hearts-authentication-required", "Sign in to play Hearts.");
        try
        {
            var key = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
            using var scope = GameSecurityContext.Current is null ? GameSecurityContext.Push(new(context.TraceIdentifier, user,
                null, null, DateTime.UtcNow, context.Request.Method, context.Request.Path.Value ?? Prefix,
                HeartsBackend.GameId, null, key, null, null, "observed", "unknown")) : null;
            return Results.Ok(await action(context.RequestServices.GetRequiredService<FirestorePaidHeartsStore>(), user, context.RequestAborted));
        }
        catch (HeartsAccessException error) { return Error(404, "hearts-match-not-found", error.Message); }
        catch (PaidHeartsInsufficientFundsException error) { return Error(402, "hearts-insufficient-funds", error.Message); }
        catch (PaidHeartsConflictException error) { return Error(409, "hearts-conflict", error.Message); }
        catch (GameWalletConflictException error) { return Error(409, "hearts-conflict", error.Message); }
        catch (HeartsAdmissionPausedException error) { return Error(503, "hearts-disabled", error.Message); }
        catch (HeartsRuleException error) { return Error(400, "hearts-invalid-action", error.Message); }
        catch (ArgumentException error) { return Error(400, "hearts-invalid-action", error.Message); }
        catch (Exception error) when (error is GameWalletUnavailableException or ManagedPlayerRosterUnavailableException or RpcException or GameSecurityRecordingException)
        { return Error(503, "hearts-unavailable", "Retry the original Hearts request."); }
    }
    private static IResult Error(int status, string code, string message) => Results.Json(new HeartsErrorResponse(code, message), statusCode: status);
    private sealed class PaidHeartsSweepWorker(FirestorePaidHeartsStore store, ILogger<PaidHeartsSweepWorker> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(350));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await store.SweepAsync(DateTime.UtcNow, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception error) { logger.LogError(error, "Hearts paid progression will retry its frozen mutation."); }
            }
        }
    }
}
