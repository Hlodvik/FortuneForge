using FortuneForge.Server.Payments;
using FortuneForge.Server.Cards.Bots;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using FortuneForge.Server.Cards.Blackjack.Table;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.Games.Roulette;
using FortuneForge.Server.Games.Craps;
using FortuneForge.Server.Games.DropMerge;
using FortuneForge.Server.Games.LiarsDice;
using FortuneForge.Server.Games.TwentyFortyEight;
using FortuneForge.Server.Matchmaking;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Arcade.Competition;

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
        builder.Services.AddArcadeCompetitions(builder.Configuration);
        builder.Services.AddPayments(builder.Configuration);
        builder.Services.AddSlotServices(builder.Configuration);
        builder.Services.AddBotDirectory(builder.Configuration);
        builder.Services.AddMatchmaking();
        builder.Services.AddCardBotServices(builder.Configuration);
        builder.Services.AddCreditHoldem(builder.Configuration);
        builder.Services.AddBlackjackTables();
        builder.Services.AddAdminOperations(builder.Configuration);
        builder.Services.AddSingleton<HeartsGameService>();
        builder.Services.AddSingleton<RouletteGameService>();
        builder.Services.AddSingleton<CrapsGameService>();
        builder.Services.AddSingleton<DropMergeGameService>();
        builder.Services.AddSingleton<LiarsDiceGameService>();
        builder.Services.AddSingleton<TwentyFortyEightGameService>();

        return builder;
    }
}
