namespace FortuneForge.Games.Solitaire;

/// <summary>The durable identities shared by legacy free play and standalone Solitaire.</summary>
public static class SolitaireFreeRunIdentity
{
    public const string CollectionName = "solitaireFreeRuns";
    public const string RunIdPrefix = "solitaire_free_";
    public const string LegacyGameId = SolitaireModule.GameId;
    public const string StandaloneGameId = SolitaireBoardCompute.FreeGameId;
}

public sealed record SolitaireFreeReplayCommand(
    string Type,
    SolitairePileReference? From,
    int? StartIndex,
    SolitairePileReference? To,
    int? Column);

public sealed record SolitaireFreeRunStartRequest(int DrawCount, string? ReplayRunId = null, string? GameId = null);
public sealed record SolitaireFreeRunReplayRequest(IReadOnlyList<SolitaireFreeReplayCommand>? Commands);

public sealed record SolitaireFreeRunStartResult(
    string RunId,
    uint Seed,
    int DrawCount,
    DateTimeOffset StartedAtUtc,
    bool WasAlreadyStarted,
    string GameId = SolitaireFreeRunIdentity.LegacyGameId);

public sealed record SolitaireFreeRunCompletionResult(
    string RunId,
    uint Seed,
    int DrawCount,
    int Score,
    int Moves,
    long ElapsedMilliseconds,
    string Terminal,
    DateTimeOffset CompletedAtUtc,
    bool WasAlreadyCompleted,
    string GameId = SolitaireFreeRunIdentity.LegacyGameId);

public sealed record SolitaireFreeRunPauseResult(string RunId, bool IsPaused, long ElapsedMilliseconds);

/// <summary>Owner-only recovery data; saved replay commands reconstruct a completed board.</summary>
public sealed record SolitaireFreeRunStateResult(
    string RunId,
    uint Seed,
    int DrawCount,
    string GameId,
    string Status,
    DateTimeOffset StartedAtUtc,
    bool IsPaused,
    long ElapsedMilliseconds,
    SolitaireFreeRunCompletionResult? Completion,
    IReadOnlyList<SolitaireFreeReplayCommand>? Commands);
