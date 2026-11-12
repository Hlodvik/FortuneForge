namespace FortuneForge.Games.Solitaire;

public static class SolitaireBoardCompute
{
    public const string FreeGameId = "free-solitaire";
    public const string StartRoute = "/internal/v1/boards/start";
    public const string ApplyRoute = "/internal/v1/boards/apply";
    public const string ApplyBatchesRoute = "/internal/v1/boards/apply-batches";
    public const string ReplayRoute = "/internal/v1/boards/replay";

    // A fully populated move command is roughly 120 bytes in the public replay request.
    public const int MaximumReplayCommands = 256;

    public static SolitaireBoardSnapshot Snapshot(SolitaireGameState state) =>
        new(state, SolitaireEngine.ToResponse(state), SolitaireEngine.IsWon(state));

    public static SolitaireBoardReplayResult Replay(SolitaireBoardReplayRequest request)
    {
        var game = ApplyCommands(SolitaireEngine.CreateGame(request.Seed, request.DrawCount), request.Commands);
        return new SolitaireBoardReplayResult(game, SolitaireEngine.IsWon(game),
            SolitaireScoreRules.FinalScore(game, request.ElapsedMilliseconds));
    }

    public static SolitaireBoardBatchResult ApplyBatches(SolitaireBoardBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Batches);
        if (request.Batches.Count == 0 || request.Batches.Count > SolitaireMoney.PlayerCounts.Max())
            throw new ArgumentException("The board batch count must fit a Solitaire match.");
        return new SolitaireBoardBatchResult(request.Batches.Select(batch =>
        {
            ArgumentNullException.ThrowIfNull(batch);
            return ApplyCommands(batch.State, batch.Commands);
        }).ToArray());
    }

    private static SolitaireGameState ApplyCommands(SolitaireGameState game, IReadOnlyList<SolitaireCommandRequest> commands)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count > MaximumReplayCommands)
            throw new ArgumentException($"A Solitaire replay may contain at most {MaximumReplayCommands} commands.", "commands");

        for (var index = 0; index < commands.Count; index++)
        {
            try
            {
                game = SolitaireEngine.Apply(game, commands[index]);
            }
            catch (SolitaireIllegalMoveException exception)
            {
                throw new ArgumentException($"Replay command {index + 1} is illegal: {exception.Message}", "commands", exception);
            }
        }

        return game;
    }
}

public sealed record SolitaireBoardStartRequest(int DrawCount = 3, uint? Seed = null);
public sealed record SolitaireBoardApplyRequest(SolitaireGameState State, SolitaireCommandRequest Command);
public sealed record SolitaireBoardCommandBatch(SolitaireGameState State, IReadOnlyList<SolitaireCommandRequest> Commands);
public sealed record SolitaireBoardBatchRequest(IReadOnlyList<SolitaireBoardCommandBatch> Batches);
public sealed record SolitaireBoardBatchResult(IReadOnlyList<SolitaireGameState> Boards);
public sealed record SolitaireBoardSnapshot(SolitaireGameState PrivateState, SolitaireGameResponse PlayerView, bool Won);
public sealed record SolitaireBoardReplayRequest(
    uint Seed, int DrawCount, IReadOnlyList<SolitaireCommandRequest> Commands, long ElapsedMilliseconds);
public sealed record SolitaireBoardReplayResult(SolitaireGameState PrivateState, bool Won, int Score);
