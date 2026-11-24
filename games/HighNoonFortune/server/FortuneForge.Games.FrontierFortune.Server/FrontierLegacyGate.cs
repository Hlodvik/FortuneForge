namespace FortuneForge.Games.FrontierFortune.Server;

/// <summary>Leaves historical records untouched and refuses to consume unknown obligations under new rules.</summary>
public static class FrontierLegacyGate
{
    public static void Validate(IReadOnlyDictionary<string, object> fields, long legacyFreeGames, bool ownsPending)
    {
        if (legacyFreeGames != 0) Block("Saved High Noon free games require reconciliation before Frontier play.");
        if (!ownsPending && (HasText(fields, "activeAdmissionId") || HasText(fields, "preparedAdmissionId")))
            Block("An earlier slot admission is still open. Retry or reconcile that original round first.");
        if (fields.TryGetValue("highNoonFortuneRoundState", out var raw))
        {
            if (raw is not IReadOnlyDictionary<string, object> state || Counter(state, "version") != 1 ||
                Counter(state, "nativeRemaining") != 0 || Counter(state, "legacyRemaining") != 0 ||
                state.TryGetValue("nativeStateJson", out var native) && native is not null and not "")
                Block("The saved High Noon feature cannot be interpreted by Frontier rules. Its obligation has been preserved.");
        }
        if (fields.TryGetValue("freeSpinsRemaining", out var count) && (count is not long value || value != 0))
            Block("The historical bonus state requires reconciliation before Frontier play.");
    }
    private static long Counter(IReadOnlyDictionary<string, object> data, string key) =>
        data.GetValueOrDefault(key) is long value && value >= 0 ? value :
        data.GetValueOrDefault(key) is int number && number >= 0 ? number : -1;
    private static bool HasText(IReadOnlyDictionary<string, object> data, string key) =>
        data.TryGetValue(key, out var value) && value is not null and not "";
    private static void Block(string message) => throw new FrontierConflictException(message, "frontier-legacy-obligation");
}
