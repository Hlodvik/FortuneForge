using FortuneForge.Games.Hearts;
using FortuneForge.Server.GameWorkers;

namespace FortuneForge.Server.Games.Hearts;

internal static class HeartsWorker
{
    private static readonly string GameId = HeartsModule.Descriptor.Id;

    internal static Task<HeartsMatchState> StartAsync(
        GameWorkerClient? workers, string userId, uint seed, int targetScore, CancellationToken cancellationToken) =>
        workers?.IsConfigured(GameId) == true
            ? workers.InvokeAsync<HeartsComputeStartRequest, HeartsMatchState>(
                GameId, userId, HeartsCompute.StartRoute, new(seed, targetScore), cancellationToken)
            : Task.FromResult(HeartsMatchEngine.Start(seed, targetScore));

    internal static Task<HeartsMatchState> NextRoundAsync(
        GameWorkerClient? workers, string userId, HeartsMatchState state, uint seed, CancellationToken cancellationToken) =>
        workers?.IsConfigured(GameId) == true
            ? workers.InvokeAsync<HeartsComputeNextRoundRequest, HeartsMatchState>(
                GameId, userId, HeartsCompute.NextRoundRoute, new(state, seed), cancellationToken)
            : Task.FromResult(HeartsMatchEngine.StartNextRound(state, seed));

    internal static Task<HeartsMatchTransition> ApplyAsync(
        GameWorkerClient? workers, string userId, HeartsMatchState state, HeartsCommand command,
        CancellationToken cancellationToken)
    {
        if (workers?.IsConfigured(GameId) != true)
            return Task.FromResult(HeartsMatchEngine.Apply(state, command));

        return command switch
        {
            PassHeartsCards pass => workers.InvokeAsync<HeartsComputePassRequest, HeartsMatchTransition>(
                GameId, userId, HeartsCompute.PassRoute, new(state, pass.Seat, pass.Cards), cancellationToken),
            PlayHeartsCard play => workers.InvokeAsync<HeartsComputePlayRequest, HeartsMatchTransition>(
                GameId, userId, HeartsCompute.PlayRoute, new(state, play.Seat, play.Card), cancellationToken),
            _ => throw new ArgumentException("This Hearts action is not supported.", nameof(command)),
        };
    }
}
