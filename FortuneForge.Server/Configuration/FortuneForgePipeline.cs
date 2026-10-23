using FortuneForge.Server.Accounts.Development;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Games.DragonTiger.Server;

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

        app.MapControllers();
        app.MapDragonTiger(async (context, cancellationToken) =>
        {
            var profile = await context.RequestServices.GetRequiredService<AccountService>()
                .GetProfileAsync(AccountSessionCookie.Read(context.Request), cancellationToken);
            return profile.Value is { } account
                ? new DragonTigerAccount(account.UserId, account.Balances.SlotsCredits)
                : null;
        });
        app.MapFallbackToFile("/index.html");

        return app;
    }
}
