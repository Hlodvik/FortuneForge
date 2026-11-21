namespace FortuneForge.Games.Abstractions.Wallet;

public sealed record GameWalletJournalBalance(string AccountId, long NetCents, long MinimumBalanceBeforeCents);

public static class GameWalletJournal
{
    public static IReadOnlyList<GameWalletJournalBalance> Aggregate(IEnumerable<(string AccountId, long AmountCents)> orderedEntries)
    {
        ArgumentNullException.ThrowIfNull(orderedEntries);
        var balances = new SortedDictionary<string, (long Net, long Lowest)>(StringComparer.Ordinal);
        foreach (var (account, amount) in orderedEntries)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(account);
            var previous = balances.GetValueOrDefault(account);
            var net = checked(previous.Net + amount);
            balances[account] = (net, Math.Min(previous.Lowest, net));
        }

        return balances.Select(entry => new GameWalletJournalBalance(entry.Key,
            entry.Value.Net, checked(-entry.Value.Lowest))).ToArray();
    }
}
