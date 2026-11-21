using FortuneForge.Server.Accounts.Development;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.HiLo.Server;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.ArcaneArchives.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Mines.Server;
using FortuneForge.Games.FreeSolitaire.Server;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Risk;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Cards.Solitaire;
using FortuneForge.Server.Games.Hearts;

namespace FortuneForge.Server.Configuration;

public static class FortuneForgePipeline
{
    public static WebApplication UseFortuneForgePipeline(this WebApplication app)
    {
        app.MapDefaultEndpoints();

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapStaticAssets();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseHttpsRedirection();
        }

        app.UseRouting();
        app.UseDevelopmentDefaultAccount();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.Headers.CacheControl = "no-store, max-age=0";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers.XContentTypeOptions = "nosniff";

                // Reject legacy demo and practice requests before they can reach a game or wallet.
                if (context.Request.Path.StartsWithSegments("/api/slots/demo") ||
                    context.Request.Path.StartsWithSegments("/api/cards/blackjack/demo") ||
                    (!app.Environment.IsDevelopment() &&
                        context.Request.Headers.ContainsKey("X-FortuneForge-Practice")))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
            }

            await next();
        });
        app.UseRateLimiter();
        app.UseAuthorization();
        FortuneForge.Server.SecurityAudit.GameSecurityRequestConfiguration.UseGameSecurityRequests(app);
        app.UsePlayerRiskEnforcement();
        app.UseGameActivity();
        app.UsePackagedGameWorkerForwarding();

        app.MapControllers()
            .WithFreeSolitaireWorkerRoutes()
            .WithCompetitiveSolitaireWorkerRoutes()
            .WithHeartsWorkerRoutes();
        app.MapGameWalletSettlement();
        app.MapArcadeCompetitionWallet();
        app.MapGameWalletBatchSettlement();
        app.MapGameManagedPlayerRoster();
        var arcaneFeatures = app.MapGroup(ArcaneFeatureEndpoints.RoutePrefix)
            .WithMetadata(new GameWorkerPublicRoute(ArcaneArchivesEngine.GameId, ArcaneFeatureEndpoints.RoutePrefix,
                ArcaneFeatureEndpoints.AuditGameId))
            .RequireRateLimiting(RateLimitPolicies.SlotSpins);
        arcaneFeatures.MapPost(ArcaneFeatureEndpoints.PotionRoute, ArcaneFeatureWorkerUnavailableAsync);
        arcaneFeatures.MapPost(ArcaneFeatureEndpoints.TomeRoute, ArcaneFeatureWorkerUnavailableAsync);
        app.MapDragonTiger(async (context, cancellationToken) =>
        {
            var profile = await context.RequestServices.GetRequiredService<AccountService>()
                .GetProfileAsync(AccountSessionCookie.Read(context.Request), cancellationToken);
            return profile.Value is { } account
                ? new DragonTigerAccount(account.UserId, account.Balances.SlotsCredits)
                : null;
        });
        app.MapHiLo(async (context, cancellationToken) =>
        {
            var profile = await context.RequestServices.GetRequiredService<AccountService>()
                .GetProfileAsync(AccountSessionCookie.Read(context.Request), cancellationToken);
            return profile.Value is { } account ? new HiLoAccount(account.UserId) : null;
        });
        app.MapAndarBahar(async (context, cancellationToken) =>
        {
            var profile = await context.RequestServices.GetRequiredService<AccountService>()
                .GetProfileAsync(AccountSessionCookie.Read(context.Request), cancellationToken);
            return profile.Value is { } account
                ? new AndarBaharAccount(account.UserId, account.Balances.SlotsCredits)
                : null;
        });
        app.MapMines(async (context, cancellationToken) =>
        {
            var profile = await context.RequestServices.GetRequiredService<AccountService>()
                .GetProfileAsync(AccountSessionCookie.Read(context.Request), cancellationToken);
            return profile.Value is { } account ? new MinesAccount(account.UserId) : null;
        });
        app.MapFallbackToFile("/index.html");

        return app;
    }

    private static async Task<IResult> ArcaneFeatureWorkerUnavailableAsync(
        HttpContext context, AccountService accounts, CancellationToken cancellationToken)
    {
        var profile = await accounts.GetProfileAsync(AccountSessionCookie.Read(context.Request), cancellationToken);
        if (profile.Value is not { } account)
            return Results.Unauthorized();
        if (!GameAccountBinding.Matches(context.Request, account.UserId))
            return Results.Json(GameAccountBinding.SessionChanged, statusCode: StatusCodes.Status403Forbidden);
        return Results.Json(new { error = "Arcane Archives is temporarily unavailable. Retry your original request." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
