using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Server.Bots;

internal sealed record ManagedTablePopulationObservation(
    string GameId,
    string TableId,
    int RoundNumber,
    int HumanPlayers,
    int ManagedPlayers,
    int MinimumOccupancy,
    int MaximumOccupancy,
    bool TableOpen = true);

internal sealed record ManagedTablePopulationDecision(
    bool KeepTableOpen,
    int ManagedArrivals,
    bool BotOnly);

internal interface IManagedTablePopulationDirector
{
    ManagedTablePopulationDecision Observe(ManagedTablePopulationObservation observation);
    void Close(string gameId, string tableId);
}

/// <summary>
/// Owns managed-player population decisions outside every game. A departure never
/// schedules an arrival: each table has its own independent arrival checkpoint.
/// </summary>
internal sealed class ManagedTablePopulationDirector : IManagedTablePopulationDirector
{
    public const int MaximumBotOnlyTables = 2;
    private readonly object gate = new();
    private readonly Dictionary<string, TablePopulationState> tables = new(StringComparer.Ordinal);
    private readonly HashSet<string> botOnlyTables = new(StringComparer.Ordinal);

    public ManagedTablePopulationDecision Observe(ManagedTablePopulationObservation observation)
    {
        Validate(observation);
        var key = Key(observation.GameId, observation.TableId);
        lock (gate)
        {
            if (!observation.TableOpen || observation.HumanPlayers + observation.ManagedPlayers == 0)
            {
                Remove(key);
                return new(false, 0, false);
            }

            var state = State(key, observation);
            if (observation.HumanPlayers == 0)
            {
                if (!botOnlyTables.Contains(key) && botOnlyTables.Count >= MaximumBotOnlyTables)
                {
                    Remove(key);
                    return new(false, 0, true);
                }

                botOnlyTables.Add(key);
                state.WasBotOnly = true;
                return new(true, 0, true);
            }

            botOnlyTables.Remove(key);
            if (state.WasBotOnly)
            {
                state.WasBotOnly = false;
                state.NextArrivalRound = NextArrivalRound(key, observation.RoundNumber);
            }
            var openSeats = Math.Max(0,
                observation.MaximumOccupancy - observation.HumanPlayers - observation.ManagedPlayers);
            if (openSeats == 0)
                return new(true, 0, false);

            var required = Math.Max(0,
                observation.MinimumOccupancy - observation.HumanPlayers - observation.ManagedPlayers);
            if (observation.ManagedPlayers > 0 || required == 0)
                state.InitialPopulationEstablished = true;
            if (required > 0 && !state.InitialPopulationEstablished)
            {
                var arrivals = Math.Min(openSeats, required);
                state.LastArrivalRound = observation.RoundNumber;
                state.NextArrivalRound = NextArrivalRound(key, observation.RoundNumber);
                return new(true, arrivals, false);
            }

            if (state.LastArrivalRound == observation.RoundNumber ||
                observation.RoundNumber < state.NextArrivalRound)
                return new(true, 0, false);

            state.LastArrivalRound = observation.RoundNumber;
            state.NextArrivalRound = NextArrivalRound(key, observation.RoundNumber);
            return new(true, 1, false);
        }
    }

    public void Close(string gameId, string tableId)
    {
        lock (gate) Remove(Key(gameId, tableId));
    }

    private TablePopulationState State(
        string key,
        ManagedTablePopulationObservation observation)
    {
        if (tables.TryGetValue(key, out var state)) return state;
        state = new(
            NextArrivalRound(key, observation.RoundNumber),
            int.MinValue);
        tables.Add(key, state);
        return state;
    }

    private void Remove(string key)
    {
        tables.Remove(key);
        botOnlyTables.Remove(key);
    }

    private static int NextArrivalRound(string key, int currentRound)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"managed-arrival\n{key}\n{currentRound}"));
        return checked(currentRound + 2 + BitConverter.ToUInt16(digest, 0) % 4);
    }

    private static string Key(string gameId, string tableId) => $"{gameId}:{tableId}";

    private static void Validate(ManagedTablePopulationObservation observation)
    {
        if (string.IsNullOrWhiteSpace(observation.GameId))
            throw new ArgumentException("A game id is required.", nameof(observation));
        if (string.IsNullOrWhiteSpace(observation.TableId))
            throw new ArgumentException("A table id is required.", nameof(observation));
        if (observation.RoundNumber < 0 || observation.HumanPlayers < 0 ||
            observation.ManagedPlayers < 0 || observation.MinimumOccupancy < 1 ||
            observation.MaximumOccupancy < observation.MinimumOccupancy ||
            observation.HumanPlayers + observation.ManagedPlayers > observation.MaximumOccupancy)
            throw new ArgumentOutOfRangeException(nameof(observation));
    }

    private sealed class TablePopulationState(int nextArrivalRound, int lastArrivalRound)
    {
        public int NextArrivalRound { get; set; } = nextArrivalRound;
        public int LastArrivalRound { get; set; } = lastArrivalRound;
        public bool WasBotOnly { get; set; }
        public bool InitialPopulationEstablished { get; set; }
    }
}
