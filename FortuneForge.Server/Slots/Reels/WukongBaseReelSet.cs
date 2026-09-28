using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Reels;

public static class WukongBaseReelSet
{
    public const string Id = "wukong-reels-v1";

    private static readonly string[] RetiredScatterReplacements = ["5", "6", "7"];

    public static ReelSetDefinition Create(ReelSetDefinition source) => new()
    {
        Id = Id,
        SymbolSetId = source.SymbolSetId,
        Reels = source.Reels.Select(ReplaceRetiredScatters).ToList()
    };

    private static List<string> ReplaceRetiredScatters(List<string> reel)
    {
        var replacementIndex = 0;
        return reel.Select(symbol =>
        {
            if (!string.Equals(symbol, "FREE", StringComparison.Ordinal))
            {
                return symbol;
            }

            var replacement = RetiredScatterReplacements[
                replacementIndex % RetiredScatterReplacements.Length];
            replacementIndex++;
            return replacement;
        }).ToList();
    }
}
