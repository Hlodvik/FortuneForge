using Shared = FortuneForge.Games.Abstractions.ManagedPlayers;

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

internal sealed class ManagedTablePopulationDirector : IManagedTablePopulationDirector
{
    public const int MaximumBotOnlyTables = Shared.ManagedTablePopulationDirector.MaximumBotOnlyTables;
    private readonly Shared.ManagedTablePopulationDirector director = new();

    public ManagedTablePopulationDecision Observe(ManagedTablePopulationObservation observation)
    {
        var decision = director.Observe(new(observation.GameId, observation.TableId, observation.RoundNumber,
            observation.HumanPlayers, observation.ManagedPlayers, observation.MinimumOccupancy,
            observation.MaximumOccupancy, observation.TableOpen));
        return new(decision.KeepTableOpen, decision.ManagedArrivals, decision.BotOnly);
    }

    public void Close(string gameId, string tableId) => director.Close(gameId, tableId);
}
