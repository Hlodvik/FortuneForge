namespace FortuneForge.Games.Slots;

/// <summary>Store-neutral values in the established slot result document format.</summary>
public static class SlotSpinResultFields
{
    public static Dictionary<string, object> VisibleSymbolWindow(IReadOnlyList<IReadOnlyList<string>> reels) => new()
    {
        ["reels"] = reels
            .Select((symbols, reel) => (object)new Dictionary<string, object>
            {
                ["reel"] = (long)reel,
                ["symbols"] = symbols
                    .Select((symbolId, row) => (object)new Dictionary<string, object>
                    {
                        ["row"] = (long)row,
                        ["symbolId"] = symbolId
                    })
                    .ToArray()
            })
            .ToArray()
    };

    public static Dictionary<string, object> PayoutBreakdown(SpinPayout payout) => new()
    {
        ["totalPoints"] = payout.TotalPoints,
        ["paylines"] = payout.Paylines
            .Select(static payline => (object)new Dictionary<string, object>
            {
                ["paylineId"] = (long)payline.PaylineId,
                ["amountPoints"] = payline.AmountPoints,
                ["matches"] = payline.Matches
                    .Select(static match => (object)new Dictionary<string, object>
                    {
                        ["paylineId"] = (long)match.Match.PaylineId,
                        ["symbolId"] = match.Match.SymbolId,
                        ["length"] = (long)match.Match.MatchLength,
                        ["multiplier"] = match.Multiplier,
                        ["amountPoints"] = match.AmountPoints,
                        ["positions"] = GridPositions(match.Match.Positions),
                        ["wildPositions"] = GridPositions(match.Match.WildPositions)
                    })
                    .ToArray()
            })
            .ToArray()
    };

    private static object[] GridPositions(IReadOnlyList<GridPosition> positions) =>
        positions.Select(static position => (object)new Dictionary<string, object>
        {
            ["reel"] = (long)position.Reel,
            ["row"] = (long)position.Row
        }).ToArray();
}
