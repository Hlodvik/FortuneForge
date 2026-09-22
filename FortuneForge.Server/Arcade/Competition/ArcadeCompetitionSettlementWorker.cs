using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Arcade.Competition;

internal sealed record ArcadeCompetitionSettlementWorkerOptions
{
    public const string SectionName = "ArcadeCompetition:SettlementWorker";
    public int PollingIntervalMilliseconds { get; init; } = 10_000;
}

internal sealed class ArcadeCompetitionSettlementWorker : BackgroundService
{
    private readonly IArcadeCompetitionStore store;
    private readonly IArcadeCompetitionSettlementExecutor executor;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan pollingInterval;
    private readonly ILogger<ArcadeCompetitionSettlementWorker> logger;

    public ArcadeCompetitionSettlementWorker(
        IArcadeCompetitionStore store,
        IArcadeCompetitionSettlementExecutor executor,
        TimeProvider timeProvider,
        IOptions<ArcadeCompetitionSettlementWorkerOptions> options,
        ILogger<ArcadeCompetitionSettlementWorker> logger)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);
        if (options.Value.PollingIntervalMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The settlement polling interval must be positive.");
        pollingInterval = TimeSpan.FromMilliseconds(options.Value.PollingIntervalMilliseconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ProcessDueCompetitionsAsync(stoppingToken);
            using var timer = new PeriodicTimer(pollingInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ProcessDueCompetitionsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private async Task ProcessDueCompetitionsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ArcadeCompetitionIdentity> competitions;
        try
        {
            competitions = await store.LoadDueSettlementCompetitionsAsync(
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Arcade competition settlement discovery failed; the next pass will retry.");
            return;
        }

        foreach (var competition in competitions)
        {
            try
            {
                _ = await executor.ExecuteAsync(competition, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Arcade competition settlement failed for {CompetitionId}; the next pass will retry.",
                    competition.DocumentId);
            }
        }
    }
}
