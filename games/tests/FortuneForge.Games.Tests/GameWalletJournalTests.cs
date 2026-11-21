using FortuneForge.Games.Abstractions.Wallet;
using Xunit;

namespace FortuneForge.Games.Tests;

public sealed class GameWalletJournalTests
{
    [Fact]
    public void RetainsTheLowestBalanceForEachAccountInStableOrder()
    {
        var balances = GameWalletJournal.Aggregate([
            ("beta", -200L), ("alpha", 400L), ("alpha", -700L), ("beta", 200L), ("alpha", 500L), ("gamma", 0L)
        ]);

        GameWalletJournalBalance[] expected = [new("alpha", 200, 300), new("beta", 0, 200), new("gamma", 0, 0)];
        Assert.Equal(expected, balances);
    }

    [Fact]
    public void FailsWhenTheNetOrTheRequiredBalanceCannotBeRepresented()
    {
        Assert.Throws<OverflowException>(() => GameWalletJournal.Aggregate([("account", long.MaxValue), ("account", 1)]));
        Assert.Throws<OverflowException>(() => GameWalletJournal.Aggregate([("account", long.MinValue)]));
    }
}
