using System.Text.RegularExpressions;
using System.Globalization;
using System.Security.Cryptography;
using GameControl = FortuneForge.Games.Asteroids.AsteroidsControl;
using GameEngine = FortuneForge.Games.Asteroids.AsteroidsEngine;
using GamePhase = FortuneForge.Games.Asteroids.AsteroidsPhase;

namespace FortuneForge.Games.Asteroids;

/// <summary>
/// Authoritatively evaluates canonical, persistent input-state transitions with the vendored full
/// Asteroids engine. The persisted 64-bit seed folds to the engine's 32-bit seed by XORing its low
/// and high words: <c>(uint)seed ^ (uint)(seed >> 32)</c>. A zero fold is intentionally passed to
/// <see cref="GameEngine.Start(uint, int, int, int)"/>, whose documented seed normalization owns it.
/// </summary>
public static class AsteroidsReplayEvaluator
{
    public const int RulesVersion = 2;
    public const int MaximumReplaySteps = 3_600;
    public const int MaximumCommands = 512;

    public static AsteroidsGameState Evaluate(AsteroidsRunIdentity run, AsteroidsReplay replay)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(replay);
        ValidateReplay(replay);

        var game = GameEngine.Start(FoldSeed(run.Seed));
        var commandIndex = 0;
        var activeInput = AsteroidsInput.None;
        for (var step = 0; step < replay.TotalSteps; step++)
        {
            if (game.Phase == GamePhase.GameOver)
            {
                if (commandIndex < replay.Commands.Count)
                    throw new ArgumentException("Replay commands cannot follow a terminal state.", nameof(replay));
                return Result(run, step, game.Score, AsteroidsTerminalState.GameOver);
            }

            if (commandIndex < replay.Commands.Count && replay.Commands[commandIndex].Step == step)
            {
                activeInput = replay.Commands[commandIndex].Input;
                commandIndex++;
            }

            game = GameEngine.AdvanceFrame(game, ToGameControl(activeInput)).State;
        }

        return game.Phase == GamePhase.GameOver
            ? Result(run, replay.TotalSteps, game.Score, AsteroidsTerminalState.GameOver)
            : Result(run, replay.TotalSteps, game.Score,
                replay.TotalSteps == MaximumReplaySteps ? AsteroidsTerminalState.Completed : AsteroidsTerminalState.Active);
    }

    public static void ValidateReplayInput(AsteroidsReplay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ValidateReplay(replay);
    }

    /// <summary>Produces the only persisted replay form: ordered input-state transitions, not per-frame impulses.</summary>
    public static string CanonicalizeReplay(AsteroidsReplay replay)
    {
        ValidateReplayInput(replay);
        return $"v{RulesVersion}|{replay.TotalSteps}|{string.Join(',', replay.Commands.Select(command => $"{command.Step}:{(int)command.Input}"))}";
    }

    /// <summary>Reopens persisted replay evidence without accepting alternate encodings.</summary>
    public static AsteroidsReplay ParseCanonicalReplay(string canonical)
    {
        if (string.IsNullOrEmpty(canonical) || canonical.Length > 20_000)
            throw new ArgumentException("Asteroids replay evidence is invalid.", nameof(canonical));
        var parts = canonical.Split('|');
        if (parts.Length != 3 || parts[0] != $"v{RulesVersion}" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var steps))
            throw new ArgumentException("Asteroids replay evidence is invalid.", nameof(canonical));
        var commands = new List<AsteroidsInputCommand>();
        foreach (var encoded in parts[2].Length == 0 ? [] : parts[2].Split(','))
        {
            var fields = encoded.Split(':');
            if (fields.Length != 2 ||
                !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var step) ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var input))
                throw new ArgumentException("Asteroids replay evidence is invalid.", nameof(canonical));
            commands.Add(new(step, (AsteroidsInput)input));
        }
        var replay = new AsteroidsReplay(steps, commands);
        if (CanonicalizeReplay(replay) != canonical)
            throw new ArgumentException("Asteroids replay evidence is not canonical.", nameof(canonical));
        return replay;
    }

    public static uint FoldSeed(ulong seed) => (uint)seed ^ (uint)(seed >> 32);

    private static AsteroidsGameState Result(AsteroidsRunIdentity run, int stepsElapsed, int score, AsteroidsTerminalState terminal) =>
        new(run, stepsElapsed, score, terminal);

    private static GameControl ToGameControl(AsteroidsInput input) => (GameControl)(int)input;

    private static void ValidateReplay(AsteroidsReplay replay)
    {
        if (replay.TotalSteps is < 1 or > MaximumReplaySteps)
            throw new ArgumentOutOfRangeException(nameof(replay), "Replay duration is outside the supported fixed-step limit.");
        if (replay.Commands is null || replay.Commands.Count > MaximumCommands)
            throw new ArgumentOutOfRangeException(nameof(replay), "Replay command count is outside the supported limit.");

        var priorStep = -1;
        var priorInput = AsteroidsInput.None;
        foreach (var command in replay.Commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (command.Step < 0 || command.Step >= replay.TotalSteps || command.Step <= priorStep)
                throw new ArgumentException("Replay commands must have strictly increasing in-range steps.", nameof(replay));
            ValidateInput(command.Input);
            if (command.Input == priorInput)
                throw new ArgumentException("Replay commands must change the active input state.", nameof(replay));
            priorStep = command.Step;
            priorInput = command.Input;
        }
    }

    private static void ValidateInput(AsteroidsInput input)
    {
        const AsteroidsInput known = AsteroidsInput.Thrust | AsteroidsInput.TurnLeft | AsteroidsInput.TurnRight | AsteroidsInput.Fire;
        if ((input & ~known) != 0 || (input & AsteroidsInput.TurnLeft) != 0 && (input & AsteroidsInput.TurnRight) != 0)
            throw new ArgumentException("Asteroids input contains unsupported or contradictory commands.", nameof(input));
    }
}

public sealed record AsteroidsRunIdentity
{
    private static readonly Regex RunIdPattern = new("^[A-Za-z0-9_-]{16,128}$", RegexOptions.CultureInvariant);

    public AsteroidsRunIdentity(string runId, ulong seed)
    {
        if (string.IsNullOrWhiteSpace(runId) || !RunIdPattern.IsMatch(runId))
            throw new ArgumentException("Asteroids run ids must be 16 to 128 URL-safe characters.", nameof(runId));
        if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed), "Asteroids seeds must be non-zero.");
        RunId = runId;
        Seed = seed;
    }

    public string RunId { get; }
    public ulong Seed { get; }

    public static ulong CreateSeed()
    {
        ulong seed;
        do { seed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))); } while (seed == 0);
        return seed;
    }
}

[Flags]
public enum AsteroidsInput
{
    None = 0,
    Thrust = 1,
    TurnLeft = 2,
    TurnRight = 4,
    Fire = 8,
}

/// <summary>At Step, replaces the active input bitmask; that state remains active until the next command.</summary>
public sealed record AsteroidsInputCommand(int Step, AsteroidsInput Input);

public sealed record AsteroidsReplay(int TotalSteps, IReadOnlyList<AsteroidsInputCommand> Commands);

public enum AsteroidsTerminalState { Active, Completed, GameOver }

public sealed record AsteroidsGameState(
    AsteroidsRunIdentity Run,
    int StepsElapsed,
    long Score,
    AsteroidsTerminalState Terminal)
{
    public bool IsTerminal => Terminal != AsteroidsTerminalState.Active;
}
