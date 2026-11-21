using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Games.SicBo.Server;

public static class SicBoEndpoints
{
    public static void AddSicBoWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(provider => new SicBoWalletStore(provider.GetRequiredService<FirestoreDb>(),
            provider.GetRequiredService<IGameWalletClient>(), provider.GetRequiredService<IGameWalletAccountReader>(),
            () => configuration.GetValue("Features:SicBoEnabled", false)));
    }

    public static void MapSicBoWorker(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> accountResolver)
    {
        var api = endpoints.MapGroup("/api/games/sic-bo");
        api.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            if (accountResolver(context.HttpContext) is null)
                return Error(401, "sic-bo-authentication-required", "Sign in to play Sic Bo.");
            if (string.Equals(context.HttpContext.Request.Headers["X-FortuneForge-Practice"], "true", StringComparison.OrdinalIgnoreCase))
                return Error(403, "sic-bo-practice-disabled", "Practice requests cannot use account credits.");
            try { return await next(context); }
            catch (SicBoRoundNotFoundException error) { return Error(404, "sic-bo-round-not-found", error.Message); }
            catch (SicBoRoundConflictException error) { return Error(409, "sic-bo-round-conflict", error.Message); }
            catch (SicBoInsufficientCreditsException error) { return Error(409, "insufficient-slot-credits", error.Message); }
            catch (SicBoAdmissionClosedException error) { return Error(503, "sic-bo-disabled", error.Message); }
            catch (GameWalletUnavailableException error) { return Error(503, "sic-bo-unavailable", error.Message); }
            catch (GameWalletConflictException error) { return Error(409, "sic-bo-round-conflict", error.Message); }
            catch (ArgumentException error) { return Error(400, "sic-bo-invalid-request", error.Message); }
        });
        api.MapGet("/status", async (HttpContext context, IGameWalletAccountReader wallet, IConfiguration configuration, CancellationToken token) =>
        {
            var state = await wallet.GetStateAsync(accountResolver(context)!, token);
            return Results.Ok(new SicBoStatusResponse(configuration.GetValue("Features:SicBoEnabled", false),
                SicBoMoney.ToRand(SicBoMoney.MinimumStakeCents), SicBoMoney.ToRand(SicBoMoney.MaximumStakeCents),
                SicBoMoney.ToRand(SicBoMoney.StakeIncrementCents), SicBoMoney.MaximumBetsPerRound,
                SicBoMoney.ToRand(state.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException()), "three-dice-sic-bo"));
        });
        api.MapPost("/rounds", async (HttpContext context, CreateSicBoRoundRequest request, SicBoWalletStore store, CancellationToken token) =>
        {
            var result = await new SicBoService(store, TimeProvider.System).StartAsync(accountResolver(context)!, request,
                context.Request.Headers["Idempotency-Key"].ToString(), token);
            return Results.Created($"/api/games/sic-bo/rounds/{result.RoundId}", result);
        });
        api.MapGet("/rounds/{roundId}", async (HttpContext context, string roundId, SicBoWalletStore store, CancellationToken token) =>
            Results.Ok(await new SicBoService(store, TimeProvider.System).GetAsync(accountResolver(context)!, roundId, token)));
    }

    private static IResult Error(int status, string code, string message) => Results.Json(new SicBoErrorResponse(code, message), statusCode: status);
}
