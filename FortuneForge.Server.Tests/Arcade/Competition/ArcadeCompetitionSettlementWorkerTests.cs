using FortuneForge.Server.Arcade.Competition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

public sealed class ArcadeCompetitionSettlementWorkerTests
{
    [Fact]
    public void ArcadeCompetitionRegistrationIncludesSettlementGraphWorkerAndConfiguredInterval()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ArcadeCompetitionSettlementWorkerOptions.SectionName}:PollingIntervalMilliseconds"] = "250"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddArcadeCompetitions(configuration);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ArcadeCompetitionSettlementPlanningService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IArcadeCompetitionPayoutCreditor));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ArcadeCompetitionSettlementExecutor));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IArcadeCompetitionSettlementExecutor));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(ArcadeCompetitionSettlementWorker));
        using var provider = services.BuildServiceProvider();
        Assert.Equal(
            250,
            provider.GetRequiredService<IOptions<ArcadeCompetitionSettlementWorkerOptions>>()
                .Value.PollingIntervalMilliseconds);
    }

    [Fact]
    public async Task StartupAndTimerTickBothProcessDueCompetitions()
    {
        var identity = Identity("asteroids", 0);
        var secondQuery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeStore([identity])
        {
            OnQuery = count =>
            {
                if (count >= 2) secondQuery.TrySetResult();
            }
        };
        var executor = new FakeExecutor((_, _) => Task.CompletedTask);
        using var worker = Worker(store, executor, new RecordingLogger(), pollingMilliseconds: 15);

        await worker.StartAsync(default);
        await secondQuery.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await worker.StopAsync(default);

        Assert.True(store.QueryCount >= 2);
        Assert.True(executor.ExecuteCount >= 2);
        Assert.All(store.Cutoffs, cutoff => Assert.Equal(Now, cutoff));
    }

    [Fact]
    public async Task EmptyDueQueryPerformsNoSettlementWork()
    {
        var secondQuery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeStore([])
        {
            OnQuery = count =>
            {
                if (count >= 2) secondQuery.TrySetResult();
            }
        };
        var executor = new FakeExecutor((_, _) => Task.CompletedTask);
        using var worker = Worker(store, executor, new RecordingLogger(), pollingMilliseconds: 15);

        await worker.StartAsync(default);
        await secondQuery.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await worker.StopAsync(default);

        Assert.True(store.QueryCount >= 2);
        Assert.Equal(0, executor.ExecuteCount);
    }

    [Fact]
    public async Task OneCompetitionFailureIsLoggedAndDoesNotBlockTheNext()
    {
        var failing = Identity("failing", 0);
        var succeeding = Identity("succeeding", 1);
        var succeeded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new RecordingLogger();
        var store = new FakeStore([failing, succeeding]);
        var executor = new FakeExecutor((competition, _) =>
        {
            if (competition == failing) throw new InvalidOperationException("Injected settlement failure.");
            succeeded.TrySetResult();
            return Task.CompletedTask;
        });
        using var worker = Worker(store, executor, logger, pollingMilliseconds: 60_000);

        await worker.StartAsync(default);
        await succeeded.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await worker.StopAsync(default);

        Assert.Equal(new[] { failing, succeeding }, executor.ExecutedCompetitions);
        Assert.Single(logger.Errors);
        Assert.IsType<InvalidOperationException>(logger.Errors[0]);
    }

    [Fact]
    public async Task CancellationStopsInFlightSettlementPromptly()
    {
        var identity = Identity("asteroids", 0);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = false;
        var store = new FakeStore([identity]);
        var executor = new FakeExecutor(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationObserved = true;
                throw;
            }
        });
        using var worker = Worker(store, executor, new RecordingLogger(), pollingMilliseconds: 60_000);

        await worker.StartAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(cancellationObserved);
        Assert.Equal(1, executor.ExecuteCount);
    }

    private static ArcadeCompetitionSettlementWorker Worker(
        FakeStore store,
        FakeExecutor executor,
        RecordingLogger logger,
        int pollingMilliseconds) => new(
            store,
            executor,
            new FixedTimeProvider(Now),
            Options.Create(new ArcadeCompetitionSettlementWorkerOptions
            {
                PollingIntervalMilliseconds = pollingMilliseconds
            }),
            logger);

    private static ArcadeCompetitionIdentity Identity(string gameId, int dayOffset) => new(
        gameId,
        ArcadeCompetitionWindowKind.Daily,
        Now.AddDays(dayOffset - 1),
        Now.AddDays(dayOffset));

    private static readonly DateTimeOffset Now = new(2026, 9, 5, 22, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class FakeExecutor(
        Func<ArcadeCompetitionIdentity, CancellationToken, Task> execute) : IArcadeCompetitionSettlementExecutor
    {
        public List<ArcadeCompetitionIdentity> ExecutedCompetitions { get; } = [];
        public int ExecuteCount => ExecutedCompetitions.Count;

        public async Task<ArcadeCompetitionSettlementExecutionResult> ExecuteAsync(
            ArcadeCompetitionIdentity competition,
            CancellationToken cancellationToken)
        {
            ExecutedCompetitions.Add(competition);
            await execute(competition, cancellationToken);
            return null!;
        }
    }

    private sealed class FakeStore(IReadOnlyList<ArcadeCompetitionIdentity> due) : IArcadeCompetitionStore
    {
        public Action<int>? OnQuery { get; init; }
        public List<DateTimeOffset> Cutoffs { get; } = [];
        public int QueryCount => Cutoffs.Count;

        public Task<IReadOnlyList<ArcadeCompetitionIdentity>> LoadDueSettlementCompetitionsAsync(
            DateTimeOffset cutoffUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Cutoffs.Add(cutoffUtc);
            OnQuery?.Invoke(QueryCount);
            return Task.FromResult(due);
        }

        public Task<ArcadeCompetitionStartAttemptResult> StartAttemptAsync(
            ArcadeCompetitionAttemptStart attempt,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ArcadeCompetitionCompleteAttemptResult> CompleteAttemptAsync(
            ArcadeCompetitionAttemptCompletion completion,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ArcadeCompetitionAttemptRecord>> LoadAttemptsAsync(
            ArcadeCompetitionIdentity competition,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ArcadeCompetitionAttemptRecord>> LoadAttemptsForGameAsync(
            string gameId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ArcadeCompetitionSettlementState> ReadSettlementStateAsync(
            ArcadeCompetitionIdentity competition,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ArcadeCompetitionSettlementState> StoreSettlementPlanAsync(
            ArcadeCompetitionPayoutPlan plan,
            DateTimeOffset createdAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ArcadeCompetitionSettlementState> MarkSettlementCompletedAsync(
            ArcadeCompetitionIdentity competition,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingLogger : ILogger<ArcadeCompetitionSettlementWorker>
    {
        public List<Exception> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error && exception is not null) Errors.Add(exception);
        }
    }
}
