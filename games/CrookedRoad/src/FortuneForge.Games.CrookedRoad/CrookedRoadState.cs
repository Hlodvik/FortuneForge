using System.Text.Json;

namespace FortuneForge.Games.CrookedRoad;

public static class CrookedRoadState
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public static CrookedRoadSavedState Empty => new(1, 0, 2, new CrookedRoadTile?[9],
        [new("tile-0", "junction", 0), new("tile-1", "corner", 0)], [],
        CrookedRoadMap.TileKinds.ToDictionary(kind => kind, _ => 0L), new(0, CrookedRoadRules.Data.FundingSpins, 0), [], null, null, 0);
    public static string Serialize(CrookedRoadSavedState state) { Validate(state); return JsonSerializer.Serialize(state, Json); }
    public static CrookedRoadSavedState Parse(string? json)
    {
        var state = json is null ? Empty : JsonSerializer.Deserialize<CrookedRoadSavedState>(json, Json)
            ?? throw new InvalidOperationException("The saved Crooked Road state is missing.");
        Validate(state);
        return state;
    }

    public static void Validate(CrookedRoadSavedState state)
    {
        if (state is null || state.ContractVersion != 1 || state.Revision < 0 || state.NextTileId < 2 ||
            state.CompletedJourneys < 0 || state.Board is not { Length: 9 } || state.Hand is null || state.Hand.Length > 2 ||
            state.QueuedTiles is null || state.QueuedTiles.Length > 12 || state.ReserveTileCounts is null ||
            state.ReserveTileCounts.Count != 3 || CrookedRoadMap.TileKinds.Any(kind => !state.ReserveTileCounts.TryGetValue(kind, out var count) || count < 0) ||
            state.Funding is null || state.Funding.RequiredSpins != CrookedRoadRules.Data.FundingSpins ||
            state.Funding.Spins < 0 || state.Funding.Spins >= CrookedRoadRules.Data.FundingSpins || state.Funding.WagerPointsTotal < 0 ||
            state.Funding.WagerPointsTotal < state.Funding.Spins * CrookedRoadRules.Data.MinimumWagerPoints ||
            state.Funding.WagerPointsTotal > state.Funding.Spins * CrookedRoadRules.Data.MaximumWagerPoints ||
            state.Tickets is null || state.Tickets.Length > CrookedRoadRules.MaximumTickets)
            throw Invalid();
        var tiles = state.Board.Where(tile => tile is not null).Select(tile => tile!).Concat(state.Hand).Concat(state.QueuedTiles).ToArray();
        if (tiles.Any(tile => tile is null || string.IsNullOrWhiteSpace(tile.Id) || tile.Id.Length > 64 ||
                !CrookedRoadMap.TileKinds.Contains(tile.Kind) || tile.Rotation is < 0 or > 3) ||
            tiles.Select(tile => tile.Id).Distinct(StringComparer.Ordinal).Count() != tiles.Length) throw Invalid();
        if (state.Tickets.Any(ticket => ticket is null || string.IsNullOrWhiteSpace(ticket.Id) || ticket.Id.Length > 128) ||
            state.Tickets.Select(ticket => ticket.Id).Distinct(StringComparer.Ordinal).Count() != state.Tickets.Length) throw Invalid();
        foreach (var ticket in state.Tickets) CrookedRoadRules.ValidateWager(ticket.FundedWagerPoints, true);
        if (state.ActiveBonus is { } bonus)
        {
            CrookedRoadRules.ValidateWager(bonus.FundedWagerPoints, true);
            if (string.IsNullOrWhiteSpace(bonus.Id) || bonus.Id.Length > 128 || state.Tickets.Any(ticket => ticket.Id == bonus.Id) ||
                !CrookedRoadMap.Destinations.Contains(bonus.Kind) || bonus.Step < 0 ||
                bonus.AwardPoints < 0 || bonus.Choice is < 0 or > 2 || bonus.LastDoors is null ||
                bonus.Kind is "well" or "hill" && bonus.Step >= 3 || bonus.Kind == "fort" && (bonus.Step >= 27 ||
                    bonus.Held is not { Length: 9 } || bonus.Held.Any(value => value != 0 && !CrookedRoadRules.Data.FortCoinValues.Any(coin => coin == value)) ||
                    bonus.RespinsRemaining is < 1 or > 3 || bonus.Held.All(value => value != 0))) throw Invalid();
            if (bonus.Kind != "fort" && (bonus.Held is not null || bonus.RespinsRemaining != 0) ||
                bonus.Kind == "fort" && (bonus.Choice is not null || bonus.AwardPoints != 0) ||
                bonus.Kind == "well" && bonus.Step > 0 && (bonus.Choice is null || bonus.Board is null) ||
                bonus.Kind == "hill" && bonus.Board is not null || bonus.LastDoors.Length > 3 ||
                bonus.LastDoors.Any(door => door.Index is < 0 or > 2 || door.PrizePoints < 0 ||
                    door.PrizePoints > CrookedRoadRules.Data.MaximumWagerPoints * CrookedRoadRules.Data.HillPrizes.Max(stage => stage.Max()))) throw Invalid();
            if (bonus.Kind == "hill" && (bonus.HiddenDoors is not { Length: 3 } ||
                bonus.HiddenDoors.Where((doors, index) => doors is not { Length: 3 } ||
                    !doors.Order().SequenceEqual(CrookedRoadRules.Data.HillPrizes[index].Order())).Any())) throw Invalid();
            if (bonus.Kind != "hill" && bonus.HiddenDoors is not null) throw Invalid();
            if (bonus.Board is { } reels && (reels.Length != 5 || reels.Any(reel => reel.Length != 3 ||
                reel.Any(symbol => !CrookedRoadRules.Data.Symbols.Any(rule => rule.Id == symbol))))) throw Invalid();
        }
        if (state.LastTravel is { } travel && (!CrookedRoadMap.Destinations.Contains(travel.Destination) ||
            travel.Path is null || travel.Path.Any(cell => cell is < 0 or > 8) || travel.Path.Distinct().Count() != travel.Path.Count)) throw Invalid();
    }
    private static InvalidOperationException Invalid() => new("The saved Crooked Road state is invalid.");
}
