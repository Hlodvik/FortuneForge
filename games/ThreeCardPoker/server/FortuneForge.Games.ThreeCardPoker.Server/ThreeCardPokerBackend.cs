using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Cards.ThreeCardPoker.Table;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Games.ThreeCardPoker.Server;

public static class ThreeCardPokerBackend
{
    public static string GameId => ThreeCardPokerModule.Descriptor.Id;
    public const string Owner = "three-card-poker-worker-v1";
    public const string FinancialEvidenceContract = "three-card-poker-protected-wallet-evidence-v1";
    public const string TableOperations = "threeCardPokerTableOperations";
    public const string TableLedgerEvidence = "threeCardPokerTableLedger";
    public const string TableState = "threeCardPokerTableState";
    public const string PublicPath = "/api/cards/three-card-poker/table";

    public static IServiceCollection AddThreeCardPokerBackend(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddGameWalletClient(GameId);
        services.AddManagedPlayerRosterClient(GameId);
        services.AddSingleton<IManagedTablePopulationDirector, ManagedTablePopulationDirector>();
        services.AddSingleton<FirestoreThreeCardPokerTableStore>();
        services.AddSingleton<IThreeCardPokerTableStore>(provider => provider.GetRequiredService<FirestoreThreeCardPokerTableStore>());
        services.AddSingleton<ThreeCardPokerTableService>();
        services.AddHostedService<ThreeCardPokerDeadlineWorker>();
        return services;
    }

    public static ThreeCardPokerTableStatusResponse StatusContract(bool available = true) => new(
        available, ThreeCardPokerMoney.ToRand(ThreeCardPokerMoney.MinimumWagerCents),
        ThreeCardPokerMoney.ToRand(ThreeCardPokerMoney.MaximumWagerCents), ThreeCardPokerMoney.ToRand(ThreeCardPokerMoney.WagerIncrementCents),
        1, 1, 0, 0, "Dealer qualifies with queen high or better; Play equals Ante", true,
        "Straight 1:1; Three of a kind 4:1; Straight flush 5:1",
        "Pair 1:1; Flush 3:1; Straight 6:1; Three of a kind 30:1; Straight flush 40:1", true);
}

internal sealed class ThreeCardPokerDeadlineWorker(IThreeCardPokerTableStore store, TimeProvider clock,
    ILogger<ThreeCardPokerDeadlineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await store.SweepAsync(clock.GetUtcNow().UtcDateTime, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Three Card Poker deadline recovery sweep failed."); }
        }
    }
}
