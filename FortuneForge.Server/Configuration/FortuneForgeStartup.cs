using FortuneForge.Server.Payments;

namespace FortuneForge.Server.Configuration;

public static class FortuneForgeStartup
{
    public static WebApplicationBuilder AddFortuneForge(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 32 * 1024;
        });

        if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var cloudRunPort))
            builder.WebHost.UseUrls($"http://0.0.0.0:{cloudRunPort}");

        builder.AddServiceDefaults();
        builder.Services.AddControllers();
        builder.Services.AddOpenApi();
        builder.Services.AddAccountServices(builder.Configuration);
        builder.Services.AddPayments(builder.Configuration);
        builder.Services.AddSlotServices(builder.Configuration);

        return builder;
    }
}
