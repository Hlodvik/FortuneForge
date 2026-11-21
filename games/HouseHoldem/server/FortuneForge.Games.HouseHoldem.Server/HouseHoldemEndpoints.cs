using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.HouseHoldem;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.HouseHoldem;

public static class HouseHoldemEndpoints
{
    public static IEndpointRouteBuilder MapHouseHoldem(this IEndpointRouteBuilder routes, Func<HttpContext, string?> resolveAccount)
    {
        const string prefix = HouseHoldemBackend.ApiPath;
        var group = routes.MapGroup(prefix).WithMetadata(new GameWorkerPublicRoute(HouseHoldemBackend.GameId, prefix, HouseHoldemBackend.GameId));
        group.MapGet("/status", async (HttpContext context, IConfiguration configuration, CancellationToken token) =>
            await Handle(context, resolveAccount, async (account, store, _) =>
            {
                var active = await store.GetActiveRoundIdAsync(account, token);
                var state = await context.RequestServices.GetRequiredService<IGameWalletAccountReader>().GetStateAsync(account, token);
                return Results.Ok(new HouseHoldemStatusResponse(configuration.GetValue<bool>(HouseHoldemBackend.EnabledSetting),
                    (state.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException()) / 100m,
                    HouseHoldemRules.MinimumBetCents / 100m, HouseHoldemRules.MaximumAnteCents / 100m,
                    HouseHoldemRules.BetIncrementCents / 100m, HouseHoldemRules.MaximumTripsCents / 100m, active));
            }));
        group.MapPost("/rounds", async (CreateHouseHoldemRoundRequest request, HttpContext context, CancellationToken token) =>
            await Handle(context, resolveAccount, async (account, _, service) =>
            {
                var result = await service.StartAsync(account, request, Key(context), token);
                return Results.Created($"{prefix}/rounds/{result.RoundId}", result);
            }));
        group.MapGet("/rounds/{roundId}", async (string roundId, HttpContext context, CancellationToken token) =>
            await Handle(context, resolveAccount, async (account, _, service) =>
                Results.Ok(await service.GetAsync(account, roundId, token))));
        group.MapPost("/rounds/{roundId}/actions", async (string roundId, HouseHoldemActionRequest request,
            HttpContext context, CancellationToken token) =>
            await Handle(context, resolveAccount, async (account, _, service) =>
                Results.Ok(await service.ActionAsync(account, roundId, request, Key(context), token))));
        group.MapPost("/rounds/{roundId}/advance", async (string roundId, HttpContext context, CancellationToken token) =>
            await Handle(context, resolveAccount, async (account, _, service) =>
                Results.Ok(await service.AdvanceAsync(account, roundId, Key(context), token))));
        return routes;
    }

    private static string Key(HttpContext context)
    {
        var keys = context.Request.Headers["Idempotency-Key"];
        if (keys.Count != 1) throw new ArgumentException("Include exactly one Idempotency-Key header.");
        return keys[0] ?? string.Empty;
    }

    private static async Task<IResult> Handle(HttpContext context, Func<HttpContext, string?> resolveAccount,
        Func<string, HouseHoldemFirestoreStore, HouseHoldemService, Task<IResult>> action)
    {
        if (resolveAccount(context) is not { Length: > 0 } account)
            return Error(401, "house-holdem-authentication-required", "Sign in to play.");
        try
        {
            var services = context.RequestServices;
            var store = new HouseHoldemFirestoreStore(services.GetRequiredService<FirestoreDb>(),
                services.GetRequiredService<IGameWalletClient>(), services.GetRequiredService<IGameWalletAccountReader>(),
                services.GetRequiredService<IConfiguration>().GetValue<bool>(HouseHoldemBackend.EnabledSetting));
            return await action(account, store, new(store, services.GetService<TimeProvider>() ?? TimeProvider.System));
        }
        catch (HouseHoldemRoundNotFoundException error) { return Error(404, "house-holdem-round-not-found", error.Message); }
        catch (HouseHoldemInsufficientCreditsException error) { return Error(409, "insufficient-slot-credits", error.Message); }
        catch (HouseHoldemRoundConflictException error) { return Error(409, "house-holdem-round-conflict", error.Message); }
        catch (HouseHoldemRoundPausedException error) { return Error(503, "house-holdem-disabled", error.Message); }
        catch (GameWalletUnavailableException error) { return Error(503, "retry-required", error.Message); }
        catch (GameWalletConflictException error) { return Error(503, "retry-required", error.Message); }
        catch (ArgumentException error) { return Error(400, "house-holdem-invalid-request", error.Message); }
    }

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new HouseHoldemErrorResponse(code, message), statusCode: status);
}
