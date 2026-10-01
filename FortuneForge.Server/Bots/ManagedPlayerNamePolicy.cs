namespace FortuneForge.Server.Bots;

internal static class ManagedPlayerNamePolicy
{
    public const double MaximumAllowedSimilarity = 0.90;

    public static bool IsDistinct(string candidate, IEnumerable<string> existingNames) =>
        existingNames.All(existing => Similarity(candidate, existing) < MaximumAllowedSimilarity);

    internal static double Similarity(string left, string right)
    {
        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        if (normalizedLeft.Length == 0 || normalizedRight.Length == 0)
            return normalizedLeft == normalizedRight ? 1 : 0;
        var distance = EditDistance(normalizedLeft, normalizedRight);
        return 1 - distance / (double)Math.Max(normalizedLeft.Length, normalizedRight.Length);
    }

    internal static string Normalize(string value) => new(
        value.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitution = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitution);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }
}
