namespace FortuneForge.Games.HorseFlight;

public sealed record HorseFlightStatusResponse(bool Available, int TickMilliseconds, decimal Balance, string Mode);
public sealed record HorseFlightStartResponse(string RunId, uint Seed, int TickMilliseconds)
{
    public bool WasReplay { get; init; }
    public int RulesVersion { get; init; } = HorseFlightEngine.CurrentRulesVersion;
}
public sealed record CompleteHorseFlightRunRequest(int TotalTicks, IReadOnlyList<int>? JumpTicks, IReadOnlyList<int>? RightClickTicks = null)
{
    public const int MaximumBodyBytes = 192 * 1024;
}
public sealed record HorseFlightRunResponse(string RunId, decimal Balance, int Score, string Phase, int TotalTicks, IReadOnlyList<int> JumpTicks)
{
    public IReadOnlyList<int> RightClickTicks { get; init; } = [];
    public bool WasReplay { get; init; }
}
public sealed record HorseFlightErrorResponse(string Code, string Message);

public sealed class HorseFlightRunNotFoundException() : Exception("Horse Flight run not found.");
public sealed class HorseFlightRunConflictException(string message) : Exception(message);
