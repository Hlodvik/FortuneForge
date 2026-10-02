using System.Globalization;
using FortuneForge.Games.Craps;
using FortuneForge.Games.Dice;

return CrapsConsoleApp.Run(args);

internal static class CrapsConsoleApp
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var (playerId, stake, rolls) = ParseArguments(args);
            var state = CrapsEngine.StartPassLine(new CrapsPassLineBet(playerId, stake));

            Console.WriteLine($"Pass-line bet started for {playerId} at {stake.ToString(CultureInfo.InvariantCulture)}.");
            foreach (var dice in rolls)
            {
                var transition = CrapsEngine.Roll(state, dice);
                state = transition.State;
                var outcome = transition.Outcome;
                Console.WriteLine(
                    $"Roll {outcome.Dice.First.Value},{outcome.Dice.Second.Value}: " +
                    $"{outcome.Result}; phase={state.Phase}; point={state.Point?.ToString() ?? "-"}; " +
                    $"return={outcome.TotalReturn?.ToString(CultureInfo.InvariantCulture) ?? "-"}.");

                if (outcome.IsTerminal)
                    break;
            }

            if (state.Phase != CrapsPassLinePhase.Resolved)
            {
                Console.Error.WriteLine("The supplied rolls ended before the pass-line bet resolved.");
                return 2;
            }

            Console.WriteLine($"Pass-line round resolved: {state.LastOutcome!.Result}.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or CrapsRuleException)
        {
            Console.Error.WriteLine($"Unable to play Craps: {exception.Message}");
            return 1;
        }
    }

    private static (string PlayerId, decimal Stake, IReadOnlyList<DicePair> Rolls) ParseArguments(string[] args)
    {
        string? playerId = null;
        decimal? stake = null;
        var rolls = new List<DicePair>();

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--player":
                    playerId = NextValue(args, ref index, "--player");
                    break;
                case "--stake":
                    var stakeText = NextValue(args, ref index, "--stake");
                    if (!decimal.TryParse(stakeText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedStake))
                        throw new FormatException($"Stake '{stakeText}' must be a valid decimal amount.");
                    stake = parsedStake;
                    break;
                case "--roll":
                    rolls.Add(ParseRoll(NextValue(args, ref index, "--roll")));
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(playerId))
            throw new ArgumentException("--player is required.");
        if (stake is null)
            throw new ArgumentException("--stake is required.");
        if (rolls.Count == 0)
            throw new ArgumentException("At least one --roll is required.");

        return (playerId, stake.Value, rolls);
    }

    private static string NextValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length)
            throw new ArgumentException($"A value is required after {option}.");
        return args[index];
    }

    private static DicePair ParseRoll(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var second))
            throw new FormatException($"Roll '{value}' must use the format FIRST,SECOND.");

        return new DicePair(new DieValue(first), new DieValue(second));
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run --project games/Craps/samples/FortuneForge.Games.Craps.Console -- --player PLAYER --stake AMOUNT --roll FIRST,SECOND [--roll FIRST,SECOND ...]");
        Console.WriteLine("Example: dotnet run --project games/Craps/samples/FortuneForge.Games.Craps.Console -- --player player --stake 10 --roll 2,2 --roll 2,3 --roll 1,3");
    }
}
