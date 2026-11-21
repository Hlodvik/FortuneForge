namespace FortuneForge.Server.Games.Hearts;

internal static class HeartsWorker
{
    internal static Task<HeartsMatchState> StartAsync(uint seed, int targetScore, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(HeartsMatchEngine.Start(seed, targetScore));
    }
    internal static Task<HeartsMatchState> NextRoundAsync(HeartsMatchState state, uint seed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(HeartsMatchEngine.StartNextRound(state, seed));
    }
    internal static Task<HeartsMatchTransition> ApplyAsync(HeartsMatchState state, HeartsCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(HeartsMatchEngine.Apply(state, command));
    }
}
