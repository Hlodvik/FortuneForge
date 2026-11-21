using FortuneForge.Games.Hosting;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

public static class CreditHoldemEndpoints
{
    public static IEndpointRouteBuilder MapCreditHoldem(this IEndpointRouteBuilder routes, Func<HttpContext, string?> resolveAccount)
    {
        var group = routes.MapGroup(CreditHoldemBackend.ApiPath).WithMetadata(new GameWorkerPublicRoute(
            CreditHoldemBackend.GameId, CreditHoldemBackend.ApiPath, CreditHoldemBackend.GameId));
        group.MapGet("/status", (HttpContext context) => resolveAccount(context) is null ? Results.Unauthorized() :
            Results.Ok(CreditHoldemServerContract.Status(context.RequestServices.GetRequiredService<IConfiguration>()) with
            { Available = context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>(CreditHoldemBackend.EnabledSetting) }));
        group.MapGet("/session", (HttpContext context, CancellationToken token) => Handle(context, resolveAccount,
            async (account, service) => Results.Ok((await service.GetSessionAsync(account, token)).Session)));
        group.MapPost("/queue", (JoinCreditHoldemQueueRequest request, HttpContext context, CancellationToken token) => Handle(context, resolveAccount,
            async (account, service) => Results.Ok(Mutation(await service.JoinAsync(account, "", request, Key(context), token)))));
        group.MapDelete("/queue/{ticketId}", (string ticketId, [FromBody] CreditHoldemVersionRequest request, HttpContext context, CancellationToken token) =>
            Handle(context, resolveAccount, async (account, service) => Results.Ok(Mutation(
                await service.CancelAsync(account, ticketId, request, Key(context), token)))));
        group.MapPost("/matches/{matchId}/actions", (string matchId, CreditHoldemActionRequest request, HttpContext context, CancellationToken token) =>
            Handle(context, resolveAccount, async (account, service) => Results.Ok(Mutation(
                await service.ActionAsync(account, matchId, request, Key(context), token)))));
        group.MapPost("/matches/{matchId}/next-hand", (string matchId, CreditHoldemVersionRequest request, HttpContext context, CancellationToken token) =>
            Handle(context, resolveAccount, async (account, service) => Results.Ok(Mutation(
                await service.NextHandAsync(account, matchId, request, Key(context), token)))));
        group.MapPost("/matches/{matchId}/leave", (string matchId, CreditHoldemVersionRequest request, HttpContext context, CancellationToken token) =>
            Handle(context, resolveAccount, async (account, service) => Results.Ok(Mutation(
                await service.LeaveAsync(account, matchId, request, Key(context), token)))));
        group.MapGet("/history", (int? limit, HttpContext context, CancellationToken token) => Handle(context, resolveAccount,
            async (account, service) => Results.Ok(await service.HistoryAsync(account, limit ?? 30, token))));
        group.MapPost("/history/{eventId}/seen", (string eventId, HttpContext context, CancellationToken token) => Handle(context, resolveAccount,
            async (account, service) => Results.Ok(await service.MarkHistorySeenAsync(account, eventId, token))));
        return routes;
    }

    internal static FirestoreCreditHoldemStore Store(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        return new(services.GetRequiredService<FirestoreDb>(), configuration.GetValue<bool>($"{CreditHoldemOptions.SectionName}:AllowSingleHumanBotFill"),
            services.GetRequiredService<IGameWalletBatchClient>(), services.GetRequiredService<IGameWalletAccountReader>(),
            services.GetRequiredService<IManagedPlayerRosterClient>(), services.GetRequiredService<IManagedTablePopulationDirector>(),
            configuration.GetValue<bool>(CreditHoldemBackend.EnabledSetting));
    }

    private static string Key(HttpContext context) => context.Request.Headers["Idempotency-Key"] is { Count: 1 } keys
        ? keys[0] ?? string.Empty : throw new ArgumentException("Include exactly one Idempotency-Key header.");
    private static CreditHoldemMutationResponse Mutation(CreditHoldemStoreResult value) => new(value.Session, CreditHoldemMoney.ToCredits(value.BalanceCents));

    private static async Task<IResult> Handle(HttpContext context, Func<HttpContext, string?> resolveAccount,
        Func<string, CreditHoldemService, Task<IResult>> action)
    {
        if (resolveAccount(context) is not { Length: > 0 } account) return Results.Unauthorized();
        try { return await action(account, new CreditHoldemService(Store(context.RequestServices))); }
        catch (CreditHoldemNotFoundException error) { return Error(404, "credit-holdem-not-found", error.Message); }
        catch (CreditHoldemInsufficientCreditsException error)
        {
            return Results.Json(new { code = "insufficient-slot-credits", error = error.Message, available = error.Available, required = error.Required }, statusCode: 409);
        }
        catch (CreditHoldemConflictException error) { return Error(409, "credit-holdem-state-conflict", error.Message); }
        catch (CreditHoldemIllegalActionException error) { return Error(400, "illegal-holdem-action", error.Message); }
        catch (CreditHoldemPausedException error) { return Error(503, "credit-texas-holdem-disabled", error.Message); }
        catch (ManagedPlayerRosterUnavailableException error) { return Error(503, "managed-roster-unavailable", error.Message); }
        catch (GameWalletUnavailableException error) { return Error(503, "retry-required", error.Message); }
        catch (GameWalletConflictException error) { return Error(503, "retry-required", error.Message); }
        catch (ArgumentException error) { return Error(400, "invalid-holdem-request", error.Message); }
    }

    private static IResult Error(int status, string code, string error) => Results.Json(new { code, error }, statusCode: status);
}

public sealed class CreditHoldemDeadlineWorker(IServiceProvider services, ILogger<CreditHoldemDeadlineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(350));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await CreditHoldemEndpoints.Store(services).SweepAsync(DateTime.UtcNow, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Texas Hold'em pending/deadline recovery failed."); }
        }
    }
}
