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
        app.MapFallbackToFile("/index.html");

        return app;
    }
}
