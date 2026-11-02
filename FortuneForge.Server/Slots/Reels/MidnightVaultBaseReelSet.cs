using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Reels;

public static class MidnightVaultBaseReelSet
{
    public const string Id = "midnight-vault-reels-v1";

    // The source's retired energy stops become the seventh ordinary paying symbol.
    // Copy every strip so this game cannot change another game's configured weights.
    public static ReelSetDefinition Create(ReelSetDefinition source) => new()
    {
        Id = Id,
        SymbolSetId = MidnightVaultSlotDefinitions.SymbolSetId,
        Reels = source.Reels.Select(CreateStrip).ToList()
    };

    private static List<string> CreateStrip(List<string> source)
    {
        var powerOccurrence = 0;
        return source.Select(symbol =>
        {
            if (string.Equals(symbol, "BOLT", StringComparison.Ordinal))
                return MidnightVaultSlotDefinitions.AdditionalOrdinarySymbolId;

            // Spread three A stops around each circular strip. These source stops
            // do not border an existing A, so the extra weight does not form A stacks.
            // The ordinary letter weights become J15/Q11/K10/A9 on every strip.
            if (string.Equals(symbol, "POWER", StringComparison.Ordinal) &&
                powerOccurrence++ is 0 or 3 or 6)
                return "5";

            return symbol;
        }).ToList();
    }
}
