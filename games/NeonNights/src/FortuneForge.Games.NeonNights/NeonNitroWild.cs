using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

internal static class NeonNitroWild
{
    internal const string SymbolId = "NITRO";

    internal static bool IsCarPlay(bool usesAwardedPlay, string? featureMode) =>
        usesAwardedPlay && featureMode == NeonArcadeBonus.PaidCarFeatureMode;

    internal static (IReadOnlyList<IReadOnlyList<string>> Reels, NeonNitroWildOutcome? Evidence) Resolve(
        IReadOnlyList<IReadOnlyList<string>> initialReels, string wildSymbolId, string engineVersion,
        int? selectedReel = null)
    {
        for (var reel = 0; reel < initialReels.Count - 1; reel++)
        {
            for (var row = 0; row < initialReels[reel].Count; row++)
            {
                if (initialReels[reel][row] != SymbolId) continue;

                var eligibleReel = reel + 1;
                var trigger = new GridPosition(reel, row);
                var secondRow = FindNitroRow(initialReels[eligibleReel]);
                if (secondRow < 0)
                    return (initialReels, new(trigger, eligibleReel, null, initialReels));

                if (!NeonArcadeSlotDefinitions.SupportsNitroChoice(engineVersion))
                    return (ExpandReel(initialReels, eligibleReel, wildSymbolId),
                        new(trigger, eligibleReel, eligibleReel, initialReels));

                var secondNitro = new GridPosition(eligibleReel, secondRow);
                var choosesReel = eligibleReel == initialReels.Count - 1;
                var target = choosesReel ? selectedReel : eligibleReel + 1;
                return (target is { } index ? ExpandReel(initialReels, index, wildSymbolId) : initialReels,
                    new(trigger, eligibleReel, target, initialReels)
                    {
                        SecondNitro = secondNitro,
                        RequiresChoice = choosesReel && target is null
                    });
            }
        }
        return (initialReels, null);
    }

    private static int FindNitroRow(IReadOnlyList<string> reel)
    {
        for (var row = 0; row < reel.Count; row++)
            if (reel[row] == SymbolId) return row;
        return -1;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExpandReel(
        IReadOnlyList<IReadOnlyList<string>> reels, int index, string wildSymbolId)
    {
        var expanded = reels.ToArray();
        expanded[index] = Enumerable.Repeat(wildSymbolId, reels[index].Count).ToArray();
        return expanded;
    }
}
