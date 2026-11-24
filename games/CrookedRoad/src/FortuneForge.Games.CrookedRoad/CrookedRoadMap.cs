namespace FortuneForge.Games.CrookedRoad;

public static class CrookedRoadMap
{
    public static IReadOnlyList<string> Destinations { get; } = CrookedRoadRules.Data.Road.Destinations.Select(destination => destination.Id).ToArray();
    public static IReadOnlyList<string> TileKinds { get; } = CrookedRoadRules.Data.Road.TilePorts.Keys.ToArray();
    public static int Ports(CrookedRoadTile tile)
    {
        var mask = CrookedRoadRules.Data.Road.TilePorts[tile.Kind];
        for (var turn = 0; turn < tile.Rotation; turn++) mask = ((mask << 1) & 15) | (mask >> 3);
        return mask;
    }

    public static IReadOnlyList<int> Path(IReadOnlyList<CrookedRoadTile?> board, string destination)
    {
        var map = CrookedRoadRules.Data.Road;
        var exit = map.Destinations.SingleOrDefault(value => value.Id == destination);
        if (board.Count != 9 || exit is null || board[map.EntryCell] is not { } entry ||
            (Ports(entry) & map.EntryPort) == 0) return [];
        var previous = new Dictionary<int, int> { [map.EntryCell] = -1 };
        var queue = new Queue<int>();
        queue.Enqueue(map.EntryCell);
        while (queue.TryDequeue(out var cell))
        {
            if (cell == exit.Cell && (Ports(board[cell]!) & exit.Port) != 0)
            {
                var route = new List<int>();
                for (var current = cell; current >= 0; current = previous[current]) route.Add(current);
                route.Reverse();
                return route;
            }
            var row = cell / 3;
            var column = cell % 3;
            foreach (var (delta, port, reciprocal, allowed) in new[]
            {
                (-3, 1, 4, row > 0), (1, 2, 8, column < 2), (3, 4, 1, row < 2), (-1, 8, 2, column > 0)
            })
            {
                var neighbor = cell + delta;
                if (allowed && !previous.ContainsKey(neighbor) && board[neighbor] is { } tile &&
                    (Ports(board[cell]!) & port) != 0 && (Ports(tile) & reciprocal) != 0)
                {
                    previous[neighbor] = cell;
                    queue.Enqueue(neighbor);
                }
            }
        }
        return [];
    }

    public static CrookedRoadSavedState Act(CrookedRoadSavedState before, CrookedRoadAction action,
        Func<CrookedRoadTicket, string, CrookedRoadBonus> beginBonus)
    {
        CrookedRoadState.Validate(before);
        if (action.ExpectedRevision != before.Revision) throw Conflict("road-stale-revision", "The road changed. Refresh before trying again.");
        if (before.ActiveBonus is { } active)
        {
            if (action.Action is not ("choose-spring" or "choose-door") ||
                active.Kind != (action.Action == "choose-spring" ? "well" : "hill") || active.Choice is not null ||
                action.Choice is not (>= 0 and < 3))
                throw Conflict("road-feature-active", "Finish the active destination before changing the road.");
            return before with { Revision = checked(before.Revision + 1), ActiveBonus = active with { Choice = action.Choice } };
        }
        var board = before.Board.ToArray();
        var hand = before.Hand.ToList();
        var queue = before.QueuedTiles.ToList();
        var reserves = before.ReserveTileCounts.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (action.Action == "set-off")
        {
            if (action.Destination is not { } destination || !Destinations.Contains(destination))
                throw new ArgumentException("Choose the Well, Fort or Hill.");
            var route = Path(board, destination);
            if (route.Count == 0) throw Conflict("road-route-incomplete", "Connect the entrance to that destination first.");
            if (before.Tickets.Length == 0) throw Conflict("road-ticket-required", "Earn a funded ticket before setting off.");
            // Road pieces are reusable. Prioritize the old route so a full ticket bank never requires
            // another paid spin to rebuild a route and redeem an already-funded entitlement.
            var returned = route.Select(cell => board[cell]!).Concat(hand).Concat(queue).ToArray();
            var returnedHand = returned.Take(CrookedRoadRules.HandCapacity).ToArray();
            var returnedQueue = returned.Skip(CrookedRoadRules.HandCapacity).Take(CrookedRoadRules.QueueCapacity).ToArray();
            foreach (var displaced in returned.Skip(CrookedRoadRules.HandCapacity + CrookedRoadRules.QueueCapacity))
                reserves[displaced.Kind] = checked(reserves[displaced.Kind] + 1);
            foreach (var cell in route) board[cell] = null;
            return before with
            {
                Revision = checked(before.Revision + 1), Board = board, Hand = returnedHand, QueuedTiles = returnedQueue,
                ReserveTileCounts = reserves, Tickets = before.Tickets.Skip(1).ToArray(),
                ActiveBonus = beginBonus(before.Tickets[0], destination), LastTravel = new(destination, route)
            };
        }
        if (action.CellIndex is not (>= 0 and < 9)) throw new ArgumentException("Choose a road-map cell.");
        var index = action.CellIndex.Value;
        switch (action.Action)
        {
            case "place":
                if (action.HandIndex is not { } selected || selected < 0 || selected >= hand.Count || action.Rotation is not (>= 0 and < 4))
                    throw new ArgumentException("Choose an available tile and a rotation from zero to three.");
                var placing = hand[selected] with { Rotation = action.Rotation.Value };
                hand.RemoveAt(selected);
                if (board[index] is { } replaced) hand.Add(replaced);
                board[index] = placing;
                break;
            case "rotate":
                if (board[index] is not { } rotating || action.Rotation is not (>= 0 and < 4))
                    throw new ArgumentException("Choose a placed tile and a rotation from zero to three.");
                board[index] = rotating with { Rotation = action.Rotation.Value };
                break;
            case "remove":
                if (board[index] is not { } removed) throw new ArgumentException("There is no tile in that cell.");
                board[index] = null;
                if (hand.Count < CrookedRoadRules.HandCapacity) hand.Add(removed);
                else if (queue.Count < CrookedRoadRules.QueueCapacity) queue.Add(removed);
                else reserves[removed.Kind] = checked(reserves[removed.Kind] + 1);
                break;
            default: throw new ArgumentException("That Crooked Road action is not supported.");
        }
        return Refill(before with { Board = board, Hand = hand.ToArray(), QueuedTiles = queue.ToArray(),
            ReserveTileCounts = reserves, Revision = checked(before.Revision + 1) });
    }

    public static CrookedRoadSavedState EarnTile(CrookedRoadSavedState state, string kind)
    {
        var reserves = state.ReserveTileCounts.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        reserves[kind] = checked(reserves[kind] + 1);
        return Refill(state with { ReserveTileCounts = reserves });
    }

    private static CrookedRoadSavedState Refill(CrookedRoadSavedState state)
    {
        var hand = state.Hand.ToList();
        var queue = state.QueuedTiles.ToList();
        var reserves = state.ReserveTileCounts.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var id = state.NextTileId;
        while (hand.Count < CrookedRoadRules.HandCapacity || queue.Count < CrookedRoadRules.QueueCapacity)
        {
            if (hand.Count < CrookedRoadRules.HandCapacity && queue.Count > 0)
            {
                hand.Add(queue[0]);
                queue.RemoveAt(0);
                continue;
            }
            var kind = TileKinds.FirstOrDefault(kind => reserves[kind] > 0);
            if (kind is null) break;
            reserves[kind]--;
            var tile = new CrookedRoadTile($"tile-{id++}", kind, 0);
            if (hand.Count < CrookedRoadRules.HandCapacity) hand.Add(tile); else queue.Add(tile);
        }
        return state with { Hand = hand.ToArray(), QueuedTiles = queue.ToArray(), ReserveTileCounts = reserves, NextTileId = id };
    }

    private static CrookedRoadConflictException Conflict(string code, string message) => new(code, message);
}
