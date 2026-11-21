using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Server.Cards.Solitaire;

public sealed record SolitaireForfeitRequest(int ExpectedVersion);

public static class CompetitiveSolitaireEndpoints
{
    public const string GameId = SolitaireModule.GameId;
    public const string PathPrefix = SolitaireModule.ApiBasePath;

    public static IEndpointRouteBuilder MapCompetitiveSolitaire(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveAccount)
    {
        var api = endpoints.MapGroup(PathPrefix).WithMetadata(new GameWorkerPublicRoute(GameId, PathPrefix, GameId));
        api.MapGet("/session", (Func<HttpContext, Task<IResult>>)((HttpContext context) => ExecuteAsync(context, resolveAccount,
            async (service, user, token) => (await service.GetSessionAsync(user, token)).Session)));
        api.MapPost("/queue", (JoinSolitaireQueueRequest request, HttpContext context) => ExecuteAsync(context, resolveAccount,
            async (service, user, token) =>
            {
                var saved = await context.RequestServices.GetRequiredService<FirestoreCompetitiveSolitaireStore>()
                    .TryReplayJoinAsync(user, request, token);
                if (saved is not null) return Mutation(saved);
                var account = await context.RequestServices.GetRequiredService<IGameWalletAccountReader>().GetStateAsync(user, token);
                if (!GameWalletAccountState.IsValidPlayerName(account.PlayerName)) throw new GameWalletUnavailableException();
                return Mutation(await service.JoinAsync(user, account.PlayerName!, request, token));
            }));
        api.MapDelete("/queue/{ticketId}", (string ticketId, HttpContext context) => ExecuteAsync(context, resolveAccount,
            async (service, user, token) => Mutation(await service.CancelAsync(user, ticketId, Key(context), token))));
        api.MapPost("/matches/{matchId}/commands", (string matchId, SolitaireCommandRequest request, HttpContext context) =>
            ExecuteAsync(context, resolveAccount, async (service, user, token) =>
                Mutation(await service.CommandAsync(user, matchId, request, Key(context), token))));
        api.MapPost("/matches/{matchId}/forfeit", (string matchId, SolitaireForfeitRequest request, HttpContext context) =>
            ExecuteAsync(context, resolveAccount, async (service, user, token) =>
                Mutation(await service.ForfeitAsync(user, matchId, request.ExpectedVersion, Key(context), token))));
        api.MapPost("/matches/{matchId}/dismiss", (string matchId, HttpContext context) =>
            ExecuteAsync(context, resolveAccount, async (service, user, token) =>
                Mutation(await service.DismissAsync(user, matchId, Key(context), token))));
        api.MapPost("/matches/{matchId}/claim", (string matchId, HttpContext context) =>
            ExecuteAsync(context, resolveAccount, async (service, user, token) =>
                Mutation(await service.ClaimAsync(user, matchId, Key(context), token))));
        api.MapGet("/history", (int? limit, HttpContext context) => ExecuteAsync(context, resolveAccount,
            (service, user, token) => service.GetHistoryAsync(user, limit ?? 30, token)));
        api.MapGet("/history-page", (int? limit, string? cursor, HttpContext context) => ExecuteAsync(context, resolveAccount,
            (service, user, token) => service.GetHistoryPageAsync(user, limit ?? 30, cursor, token)));
        return endpoints;
    }

    public static TBuilder WithCompetitiveSolitaireWorkerRoutes<TBuilder>(this TBuilder endpoints)
        where TBuilder : IEndpointConventionBuilder
    {
        endpoints.Add(builder =>
        {
            if (builder is not RouteEndpointBuilder route || route.RoutePattern.RawText is not { } raw) return;
            var path = "/" + raw.TrimStart('/');
            if (path.StartsWith(PathPrefix + "/", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith(PathPrefix + "/free/", StringComparison.OrdinalIgnoreCase))
                builder.Metadata.Add(new GameWorkerPublicRoute(GameId, PathPrefix, GameId));
        });
        return endpoints;
    }

    internal static SolitaireMutationResponse Mutation(SolitaireStoreSession value) =>
        new(value.Session, SolitaireMoney.ToCredits(value.BalanceCents));

    private static string Key(HttpContext context)
    {
        var values = context.Request.Headers["Idempotency-Key"];
        if (values.Count != 1) throw new ArgumentException("Include one Solitaire idempotency key.");
        return values[0]!;
    }

    private static async Task<IResult> ExecuteAsync<T>(HttpContext context, Func<HttpContext, string?> resolveAccount,
        Func<CompetitiveSolitaireService, string, CancellationToken, Task<T>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (resolveAccount(context) is not { Length: > 0 } user)
            return Results.Json(new { error = "Sign in to play competitive Solitaire." }, statusCode: 401);
        try { return Results.Ok(await action(context.RequestServices.GetRequiredService<CompetitiveSolitaireService>(), user, context.RequestAborted)); }
        catch (SolitaireInsufficientCreditsException exception)
        {
            return Results.Json(new { code = "insufficient-slot-credits", error = exception.Message,
                available = exception.Available, required = exception.Required }, statusCode: 409);
        }
        catch (SolitaireNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
        catch (SolitaireConflictException exception) { return Results.Conflict(new { code = "solitaire-state-conflict", error = exception.Message }); }
        catch (SolitaireIllegalMoveException exception) { return Results.BadRequest(new { code = "illegal-solitaire-move", error = exception.Message }); }
        catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        catch (Exception exception) when (exception is GameWalletUnavailableException or GameWalletConflictException or
            ManagedPlayerRosterUnavailableException or SolitaireFinancialPendingException or SolitaireAdmissionPausedException or RpcException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(CompetitiveSolitaireEndpoints))
                .LogWarning("Competitive Solitaire request could not be confirmed ({ErrorType}).", exception.GetType().Name);
            return Results.Json(new { code = "solitaire-unavailable", error = exception.Message }, statusCode: 503);
        }
    }
}
