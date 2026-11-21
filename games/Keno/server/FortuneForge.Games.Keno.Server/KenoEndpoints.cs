using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Games.Keno.Server;

public static class KenoEndpoints
{
    public static void AddKenoWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(provider => new KenoWalletStore(provider.GetRequiredService<FirestoreDb>(),
            provider.GetRequiredService<IGameWalletClient>(), provider.GetRequiredService<IGameWalletAccountReader>(),
            () => configuration.GetValue("Features:KenoEnabled", false)));
    }

    public static void MapKenoWorker(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> accountResolver)
    {
        var api = endpoints.MapGroup("/api/games/keno");
        api.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            if (accountResolver(context.HttpContext) is null)
                return Error(401, "keno-authentication-required", "Sign in to play Keno.");
            if (string.Equals(context.HttpContext.Request.Headers["X-FortuneForge-Practice"], "true", StringComparison.OrdinalIgnoreCase))
                return Error(403, "keno-practice-disabled", "Practice requests cannot use account credits.");
            try { return await next(context); }
            catch (KenoRoundNotFoundException error) { return Error(404, "keno-round-not-found", error.Message); }
            catch (KenoRoundConflictException error) { return Error(409, "keno-round-conflict", error.Message); }
            catch (KenoInsufficientCreditsException error) { return Error(409, "insufficient-slot-credits", error.Message); }
            catch (KenoAdmissionClosedException error) { return Error(503, "keno-disabled", error.Message); }
            catch (GameWalletUnavailableException error) { return Error(503, "keno-unavailable", error.Message); }
            catch (GameWalletConflictException error) { return Error(409, "keno-round-conflict", error.Message); }
            catch (ArgumentException error) { return Error(400, "keno-invalid-request", error.Message); }
        });
        api.MapGet("/status", async (HttpContext context, IGameWalletAccountReader wallet, IConfiguration configuration, CancellationToken token) =>
        {
            var state = await wallet.GetStateAsync(accountResolver(context)!, token);
            return Results.Ok(new KenoStatusResponse(configuration.GetValue("Features:KenoEnabled", false),
                KenoMoney.ToRand(KenoMoney.MinimumWagerCents), KenoMoney.ToRand(KenoMoney.MaximumWagerCents),
                KenoMoney.ToRand(KenoMoney.WagerIncrementCents),
                KenoMoney.ToRand(state.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException()), "credit-keno", KenoService.Paytable()));
        });
        api.MapPost("/rounds", async (HttpContext context, CreateKenoRoundRequest request, KenoWalletStore store, CancellationToken token) =>
        {
            var result = await new KenoService(store, TimeProvider.System).StartAsync(accountResolver(context)!, request,
                context.Request.Headers["Idempotency-Key"].ToString(), token);
            return Results.Created($"/api/games/keno/rounds/{result.RoundId}", result);
        });
        api.MapGet("/rounds/{roundId}", async (HttpContext context, string roundId, KenoWalletStore store, CancellationToken token) =>
            Results.Ok(await new KenoService(store, TimeProvider.System).GetAsync(accountResolver(context)!, roundId, token)));
    }

    private static IResult Error(int status, string code, string message) => Results.Json(new KenoErrorResponse(code, message), statusCode: status);
}
