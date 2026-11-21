using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Firestore;
using FortuneForge.Games.Hosting;
using Google.Cloud.Firestore;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FortuneForge.Server.Games.Hearts;

public static class HeartsFreeEndpoints
{
    internal static string Prefix => HeartsBackend.ApiPath;
    public static IServiceCollection AddHeartsFreeBackend(this IServiceCollection services)
    {
        services.AddSingleton(provider => new FirestoreHeartsFreeStore(provider.GetRequiredService<FirestoreDb>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            () => provider.GetRequiredService<IConfiguration>().GetValue(HeartsBackend.FreeAdmissionSetting, false),
            provider.GetRequiredService<ILogger<FirestoreHeartsFreeStore>>()));
        if (services.Any(service => service.ServiceType == typeof(FirestoreDb)))
            services.AddHostedService<FortuneForge.Server.Bots.Hearts.HeartsManagedTurnWorker>();
        return services;
    }

    public static IEndpointRouteBuilder MapHeartsFree(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> account)
    {
        var api = endpoints.MapGroup(Prefix).WithMetadata(new GameWorkerPublicRoute(HeartsBackend.GameId, Prefix, HeartsBackend.GameId));
        api.MapGet("/status", IResult (HttpContext context, IConfiguration configuration) => account(context) is null
            ? Error(401, "hearts-authentication-required", "Sign in to play Hearts.")
            : Results.Ok(new HeartsStatusResponse(configuration.GetValue(HeartsBackend.FreeAdmissionSetting, false), HeartsMatchEngine.DefaultTargetScore, "free-play")));
        api.MapPost("/matches", (StartHeartsMatchRequest? request, HttpContext context) => Execute(context, account,
            async (store, user, sha, token) => Results.Ok(await store.StartAsync(user, request ?? new(null, null), sha, token))));
        api.MapGet("/matches/{matchId:guid}", (Guid matchId, HttpContext context) => Execute(context, account,
            async (store, user, sha, token) => Results.Ok(await store.GetAsync(user, matchId, sha, token))));
        api.MapPost("/matches/{matchId:guid}/pass", (Guid matchId, PassHeartsRequest request, HttpContext context) => Execute(context, account,
            async (store, user, sha, token) => Results.Ok(await store.PassAsync(user, matchId, request.Cards ?? [], sha, token))));
        api.MapPost("/matches/{matchId:guid}/card", (Guid matchId, PlayHeartsCardRequest request, HttpContext context) => Execute(context, account,
            async (store, user, sha, token) => Results.Ok(await store.CardAsync(user, matchId, request.Card ?? "", sha, token))));
        api.MapPost("/matches/{matchId:guid}/advance", (Guid matchId, HttpContext context) => Execute(context, account,
            async (store, user, sha, token) => Results.Ok(await store.AdvanceAsync(user, matchId, sha, token))));
        api.MapPost("/matches/{matchId:guid}/next-round", (Guid matchId, NextHeartsRoundRequest? request, HttpContext context) => Execute(context, account,
            async (store, user, sha, token) => Results.Ok(await store.NextAsync(user, matchId, request?.Seed, sha, token))));
        api.MapPost("/matches/{matchId:guid}/leave", (Guid matchId, HttpContext context) => Execute(context, account,
            async (store, user, sha, token) => { await store.LeaveAsync(user, matchId, sha, token); return Results.NoContent(); }));
        api.MapGet("/onboarding", new Func<HttpContext, Task<IResult>>(context => Execute(context, account,
            async (store, user, sha, token) => Results.Ok(await store.OnboardingAsync(user, sha, token)))));
        api.MapPost("/onboarding", (ConfirmHeartsExperienceRequest request, HttpContext context) => Execute(context, account,
            async (store, user, sha, token) => Results.Ok(await store.ConfirmAsync(user, request.HasPlayedBefore, sha, token))));
        api.MapPost("/trial", new Func<HttpContext, Task<IResult>>(context => Execute(context, account,
            async (store, user, sha, token) => Results.Ok(await store.TrialAsync(user, sha, token)))));
        api.MapPost("/trial/{matchId:guid}/cancel", (Guid matchId, HttpContext context) => Execute(context, account,
            async (store, user, sha, token) => Results.Ok(await store.CancelTrialAsync(user, matchId, sha, token))));
        return endpoints;
    }

    public static TBuilder WithHeartsWorkerRoutes<TBuilder>(this TBuilder endpoints) where TBuilder : IEndpointConventionBuilder
    {
        endpoints.Add(builder =>
        {
            if (builder is not RouteEndpointBuilder route || route.RoutePattern.RawText is not { } raw) return;
            var path = "/" + raw.TrimStart('/');
            if (path.StartsWith(Prefix + "/", StringComparison.OrdinalIgnoreCase))
                builder.Metadata.Add(new GameWorkerPublicRoute(HeartsBackend.GameId, Prefix, HeartsBackend.GameId));
        });
        return endpoints;
    }

    private static async Task<IResult> Execute(HttpContext context, Func<HttpContext, string?> account,
        Func<FirestoreHeartsFreeStore, string, string, CancellationToken, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (account(context) is not { Length: > 0 } user) return Error(401, "hearts-authentication-required", "Sign in to play Hearts.");
        try
        {
            var sha = GameWorkerAuthentication.GetCommandBodySha256(context) ?? GameSecurityEventDocuments.Hash("");
            var keys = context.Request.Headers["Idempotency-Key"];
            if (context.Request.Method != "GET" && (keys.Count != 1 || string.IsNullOrWhiteSpace(keys[0]) || keys[0]!.Length > 200))
                throw new ArgumentException("Include one valid Idempotency-Key header.");
            using var local = GameSecurityContext.Current is null ? GameSecurityContext.Push(new(context.TraceIdentifier, user,
                null, null, DateTime.UtcNow, context.Request.Method, context.Request.Path.Value ?? Prefix,
                HeartsBackend.GameId, null, keys.Count == 1 ? keys[0] : null, null, null, "observed", "unknown")) : null;
            return await action(context.RequestServices.GetRequiredService<FirestoreHeartsFreeStore>(), user, sha, context.RequestAborted);
        }
        catch (HeartsAccessException error) { return Error(404, "hearts-match-not-found", error.Message); }
        catch (HeartsCommandConflictException error) { return Error(409, "hearts-command-conflict", error.Message); }
        catch (HeartsAdmissionPausedException error) { return Error(503, "hearts-disabled", error.Message); }
        catch (HeartsRuleException error) { return Error(400, "hearts-invalid-action", error.Message); }
        catch (ArgumentException error) { return Error(400, "hearts-invalid-action", error.Message); }
        catch (Exception error) when (error is ManagedPlayerRosterUnavailableException or FirestorePuzzleStorageException or RpcException or GameSecurityRecordingException)
        { return Error(503, "hearts-unavailable", "Retry the original Hearts request."); }
    }
    private static IResult Error(int status, string code, string message) => Results.Json(new HeartsErrorResponse(code, message), statusCode: status);
}
