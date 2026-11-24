using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PharaohsAscent;

/// <summary>Scarab groups connect through shared edges; each group is paid exactly once.</summary>
public static class PharaohsAscentScarabs
{
    public const string SymbolId = "BANANA";
    public const int FirstPaylineId = 801;

    public static int CountFromRoll(int roll)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(roll);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(roll, 100);
        return roll switch { < 48 => 0, < 88 => 1, < 96 => 2, < 98 => 3, 98 => 4, _ => 5 };
    }

    public static ClassicFeaturePayout Calculate(IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints, decimal payoutMultiplier)
    {
        ArgumentNullException.ThrowIfNull(reels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wagerPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(payoutMultiplier);
        var visited = new HashSet<GridPosition>();
        var payouts = new List<PaylinePayout>();
        for (var reel = 0; reel < reels.Count; reel++)
        {
            for (var row = 0; row < reels[reel].Count; row++)
            {
                var start = new GridPosition(reel, row);
                if (reels[reel][row] != SymbolId || !visited.Add(start)) continue;

                var group = new List<GridPosition>();
                var pending = new Queue<GridPosition>();
                pending.Enqueue(start);
                while (pending.TryDequeue(out var position))
                {
                    group.Add(position);
                    Visit(position.Reel - 1, position.Row);
                    Visit(position.Reel + 1, position.Row);
                    Visit(position.Reel, position.Row - 1);
                    Visit(position.Reel, position.Row + 1);
                }

                if (group.Count < 3) continue;
                var multiplier = group.Count switch { 3 => 3L, 4 => 5L, _ => 8L };
                var amount = ClassicSlotEngine.Scale(wagerPoints, multiplier, payoutMultiplier);
                var id = FirstPaylineId + payouts.Count;
                var positions = group.OrderBy(value => value.Reel).ThenBy(value => value.Row).ToArray();
                payouts.Add(new(id, amount,
                    [new PaidMatch(new SymbolMatch(id, SymbolId, positions.Length, positions, []), multiplier, amount)]));

                void Visit(int nextReel, int nextRow)
                {
                    if (nextReel < 0 || nextReel >= reels.Count || nextRow < 0 ||
                        nextRow >= reels[nextReel].Count || reels[nextReel][nextRow] != SymbolId) return;
                    var neighbor = new GridPosition(nextReel, nextRow);
                    if (visited.Add(neighbor)) pending.Enqueue(neighbor);
                }
            }
        }

        return new(0, 0, payouts.Sum(value => value.AmountPoints), payouts);
    }
}
