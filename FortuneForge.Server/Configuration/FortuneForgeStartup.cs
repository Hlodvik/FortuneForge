using FortuneForge.Server.Payments;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Admin.Players;
using FortuneForge.Server.Admin.Management;
using FortuneForge.Server.GameReports;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using FortuneForge.Server.Cards.Blackjack.Table;
using FortuneForge.Server.Cards.ThreeCardPoker.Table;
using FortuneForge.Server.Cards.Solitaire;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.Games.Roulette;
using FortuneForge.Server.Games.Craps;
using FortuneForge.Server.Games.DropMerge;
using FortuneForge.Server.Games.LiarsDice;
using FortuneForge.Server.Games.TwentyFortyEight;
using FortuneForge.Server.Games.Snake;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.Hearts;
using FortuneForge.Server.Bots.LiarsDice;
using FortuneForge.Server.Arcade.Competition;
using FortuneForge.Server.Cards.VideoPoker;
using FortuneForge.Server.Cards.Baccarat;
using FortuneForge.Server.Cards.CasinoWar;
using FortuneForge.Server.Dice.SicBo;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.HiLo.Server;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.Mines.Server;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Matchmaking;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Catalog;

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
        builder.Services.AddManagedPlayers();
        builder.Services.AddMultiplayerMatchmaking();
        builder.Services.AddHeartsManagedPlayers();
        builder.Services.AddLiarsDiceManagedPlayers();
        builder.Services.AddCreditHoldem(builder.Configuration);
        builder.Services.AddBlackjackTables();
        builder.Services.AddThreeCardPokerTables();
        builder.Services.AddCompetitiveSolitaireQueues(builder.Configuration);
        builder.Services.AddAdminOperations(builder.Configuration);
        builder.Services.AddAdminPlayers(builder.Configuration);
        builder.Services.AddAdminManagement(builder.Configuration);
        builder.Services.AddCataloguePopularity();
        builder.Services.AddGameReports(builder.Configuration, builder.Environment.IsDevelopment());
        builder.Services.AddGameActivity();
        builder.Services.AddSingleton<FortuneForge.Server.SecurityAudit.IGameSecurityEventSink,
            FortuneForge.Server.SecurityAudit.FirestoreGameSecurityEventSink>();
        FortuneForge.Server.SecurityAudit.GameSecurityRequestConfiguration.AddGameSecurityRequests(builder.Services, builder.Configuration);
        FortuneForge.Server.SecurityAudit.SecurityAuditInvestigationConfiguration.AddSecurityAuditInvestigations(builder.Services, builder.Configuration);
        FortuneForge.Server.Risk.RiskConfiguration.AddPlayerRiskEnforcement(builder.Services);
        FortuneForge.Server.RoundAudit.GameRoundAuditConfiguration.AddGameRoundAuditing(builder.Services, builder.Configuration);
        builder.Services.AddSingleton<HeartsGameService>();
        builder.Services.AddSingleton<IHeartsOnboardingStore, FirestoreHeartsOnboardingStore>();
        builder.Services.AddPaidHearts(builder.Configuration);
        builder.Services.AddSingleton(provider => new RouletteGameService(
            provider.GetRequiredService<FortuneForge.Server.Matchmaking.IMultiplayerMatchmaker>(),
            provider.GetRequiredService<IManagedTablePopulationDirector>(),
            provider.GetRequiredService<FortuneForge.Server.Activity.IGameAnalyticsSink>(),
            provider.GetRequiredService<FortuneForge.Server.SecurityAudit.IGameSecurityEventSink>(),
            provider.GetRequiredService<GameWorkers.GameWorkerClient>()));
        builder.Services.AddHostedService<RouletteTableWorker>();
        builder.Services.AddSingleton(provider =>
        {
            var database = provider.GetRequiredService<Google.Cloud.Firestore.FirestoreDb>();
            return new FirestorePaidCrapsStore(database, writeActivity: (transaction, observation) =>
                Activity.AcceptedGameTransactionWitness.Write(database, observation,
                    (reference, fields) => transaction.Set(reference, fields, Google.Cloud.Firestore.SetOptions.MergeAll)));
        });
        builder.Services.AddDragonTigerServer(options =>
        {
            options.ReadRateLimitPolicy = RateLimitPolicies.SlotReads;
            options.WriteRateLimitPolicy = RateLimitPolicies.SlotSpins;
        });
        builder.Services.AddAndarBaharServer(options =>
        {
            options.ReadRateLimitPolicy = RateLimitPolicies.SlotReads;
            options.WriteRateLimitPolicy = RateLimitPolicies.SlotSpins;
            options.AllowPracticeRequests = true;
            // Explicit practice requests use this account-scoped QA ledger, never
            // the Firestore wallet. Like Hi-Lo, it is ephemeral across host replacement.
            options.PracticeStatePath = Path.Combine(Path.GetTempPath(), "fortuneforge-andar-bahar", "practice.json");
        });
        builder.Services.AddHiLoServer(options =>
        {
            options.ReadRateLimitPolicy = RateLimitPolicies.SlotReads;
            options.WriteRateLimitPolicy = RateLimitPolicies.SlotSpins;
            // Practice funds and recovery are account-scoped but intentionally
            // ephemeral across host replacement; this file never holds real funds.
            options.PracticeStorePath = Path.Combine(Path.GetTempPath(), "fortuneforge-hi-lo", "practice.json");
            // Firebase Hosting proxies the API to a different Cloud Run host.
            // Trust only the site's exact origins, not arbitrary forwarded headers.
            options.AllowedOrigins = builder.Environment.IsDevelopment()
                ? ["https://fortuneforgegame.web.app", "https://fortuneforgegame.firebaseapp.com",
                    "http://127.0.0.1:4178", "http://localhost:4178", "http://127.0.0.1:5173", "http://localhost:5173"]
                : ["https://fortuneforgegame.web.app", "https://fortuneforgegame.firebaseapp.com"];
        });
        builder.Services.AddSingleton<DropMergeGameService>();
        builder.Services.AddMinesServer(options =>
        {
            options.ReadRateLimitPolicy = RateLimitPolicies.SlotReads;
            options.WriteRateLimitPolicy = RateLimitPolicies.SlotSpins;
            options.AllowPracticeRequests = false;
            // Firebase proxies retain the public browser origin while Cloud Run has a different host.
            // This allowlist comes only from trusted deployment configuration, never forwarded headers.
            options.AllowedOrigins = new[] { "https://fortuneforgegame.web.app", "https://fortuneforgegame.firebaseapp.com" }
                .Concat(builder.Configuration.GetSection("Mines:AllowedOrigins").Get<string[]>() ?? [])
                .Concat(builder.Environment.IsDevelopment()
                    ? ["http://127.0.0.1:4178", "http://localhost:4178", "http://127.0.0.1:5173", "http://localhost:5173"]
                    : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        });
        builder.Services.AddSingleton<LiarsDiceGameService>();
        builder.Services.AddSingleton<TwentyFortyEightGameService>();
        builder.Services.AddSingleton<FirestoreSnakeGameService>();
        builder.Services.AddSingleton<PracticeVideoPokerStore>();
        builder.Services.AddSingleton<PracticeBaccaratStore>();
        builder.Services.AddSingleton<PracticeCasinoWarStore>();
        builder.Services.AddSingleton<PracticeSicBoStore>();
        builder.Services.AddHttpClient<GameWorkers.GameWorkerClient>(client =>
                client.MaxResponseContentBufferSize = 8 * 1024 * 1024)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        return builder;
    }
}
