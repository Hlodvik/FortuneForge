using FortuneForge.Server.Accounts.Development;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.HiLo.Server;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Risk;

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
            }

            await next();
        });
        app.UseRateLimiter();
        app.UseAuthorization();
        FortuneForge.Server.SecurityAudit.GameSecurityRequestConfiguration.UseGameSecurityRequests(app);
        app.UsePlayerRiskEnforcement();

        app.MapControllers();
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
        app.MapFallbackToFile("/index.html");

        return app;
    }
}
