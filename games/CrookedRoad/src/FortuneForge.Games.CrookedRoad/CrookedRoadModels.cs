namespace FortuneForge.Games.CrookedRoad;

public sealed record CrookedRoadTile(string Id, string Kind, int Rotation);
public sealed record CrookedRoadFunding(int Spins, int RequiredSpins, long WagerPointsTotal);
public sealed record CrookedRoadTicket(string Id, long FundedWagerPoints);
public sealed record CrookedRoadTravel(string Destination, IReadOnlyList<int> Path);
public sealed record CrookedRoadDoor(int Index, bool Selected, bool Revealed, long? PrizePoints);
public sealed record CrookedRoadBonus(string Id, string Kind, long FundedWagerPoints, int Step, long AwardPoints,
    int? Choice, string[][]? Board, long[]? Held, int RespinsRemaining, int[][]? HiddenDoors,
    CrookedRoadDoor[] LastDoors);
public sealed record CrookedRoadBonusView(string Id, string Kind, int Step, int TotalSteps, long AwardPoints,
    long FundedWagerPoints, int? Choice, string[][]? Board, long[]? Held, int RespinsRemaining, CrookedRoadDoor[] Doors);
public sealed record CrookedRoadSavedState(int ContractVersion, long Revision, long NextTileId,
    CrookedRoadTile?[] Board, CrookedRoadTile[] Hand, CrookedRoadTile[] QueuedTiles,
    IReadOnlyDictionary<string, long> ReserveTileCounts, CrookedRoadFunding Funding,
    CrookedRoadTicket[] Tickets, CrookedRoadBonus? ActiveBonus, CrookedRoadTravel? LastTravel, long CompletedJourneys);
public sealed record CrookedRoadView(int ContractVersion, long Revision, CrookedRoadTile?[] Board,
    CrookedRoadTile[] Hand, CrookedRoadTile[] QueuedTiles, IReadOnlyDictionary<string, long> ReserveTileCounts,
    CrookedRoadFunding Funding, CrookedRoadTicket[] Tickets, CrookedRoadBonusView? ActiveBonus,
    IReadOnlyList<string> ReachableDestinations, CrookedRoadTravel? LastTravel, long CompletedJourneys);
public sealed record CrookedRoadAction(long ExpectedRevision, string Action, int? HandIndex = null,
    int? CellIndex = null, int? Rotation = null, string? Destination = null, int? Choice = null);
public sealed class CrookedRoadConflictException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public static class CrookedRoadViews
{
    public static CrookedRoadView Public(CrookedRoadSavedState state) => new(1, state.Revision, state.Board,
        state.Hand, state.QueuedTiles, state.ReserveTileCounts, state.Funding, state.Tickets,
        state.ActiveBonus is { } bonus ? Public(bonus) : null,
        CrookedRoadMap.Destinations.Where(destination => CrookedRoadMap.Path(state.Board, destination).Count > 0).ToArray(),
        state.LastTravel, state.CompletedJourneys);

    public static CrookedRoadBonusView Public(CrookedRoadBonus bonus) => new(bonus.Id, bonus.Kind, bonus.Step,
        bonus.Kind == "fort" ? 27 : 3, bonus.AwardPoints, bonus.FundedWagerPoints, bonus.Choice,
        bonus.Board, bonus.Held, bonus.RespinsRemaining,
        bonus.Kind == "hill" ? Enumerable.Range(0, 3).Select(index => new CrookedRoadDoor(index,
            bonus.Choice == index, false, null)).ToArray() : bonus.LastDoors);

    public static bool ActionRequired(CrookedRoadSavedState state) =>
        state.ActiveBonus is { Kind: "well" or "hill", Choice: null };

    public static int FreeSpins(CrookedRoadSavedState state) => state.ActiveBonus is not { } bonus ? 0 :
        bonus.Kind == "fort" ? bonus.RespinsRemaining : 3 - bonus.Step;
}
