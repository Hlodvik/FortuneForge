using FortuneForge.Games.Blackjack;
using FortuneForge.Games.Cards;
using Xunit;

namespace FortuneForge.Games.Tests;

public sealed class BlackjackTableEngineTests
{
    private static readonly DateTime Start = DateTime.UnixEpoch;

    [Fact]
    public void DealAndPlayerActionAdvanceWithoutHostServices()
    {
        var table = Table();
        var deck = Deck();

        BlackjackTableEngine.Deal(table, deck, 42, Start);

        Assert.Equal(BlackjackTablePhases.Active, table.Phase);
        Assert.Equal(0, table.ActiveSeat);
        Assert.Contains(BlackjackActions.Hit, BlackjackTableEngine.LegalActions(table, table.Players[0]));
        Assert.Contains(BlackjackActions.Stand, BlackjackTableEngine.LegalActions(table, table.Players[0]));

        BlackjackTableEngine.ApplyAction(table, "human", BlackjackActions.Stand, Start.AddSeconds(1));

        Assert.Equal("stood", table.Players[0].Status);
        Assert.Equal("action-settle", table.Transition);
    }

    [Fact]
    public void PackageEngineNeverTouchesAccountBalances()
    {
        var playerProperties = typeof(BlackjackTablePlayer).GetProperties().Select(value => value.Name).ToArray();
        var tableProperties = typeof(BlackjackTableState).GetProperties().Select(value => value.Name).ToArray();

        Assert.DoesNotContain(playerProperties, value => value.Contains("Balance", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tableProperties, value => value.Contains("Ledger", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tableProperties, value => value.Contains("Revenue", StringComparison.OrdinalIgnoreCase));
    }

    private static BlackjackTableState Table() => new()
    {
        TableId = "table-1",
        CreatedAtUtc = Start,
        UpdatedAtUtc = Start,
        Players =
        [
            Player("human", 0, false, null, 500),
            Player("bot-1", 1, true, 2, 0),
            Player("bot-2", 2, true, 3, 0),
        ],
    };

    private static BlackjackTablePlayer Player(string actor, int seat, bool bot, int? skill, long wager) => new()
    {
        ActorId = actor,
        PublicSeatId = $"seat-{seat}",
        DisplayName = actor,
        IsBot = bot,
        BotSkillLevel = skill,
        Seat = seat,
        SessionId = $"session-{seat}",
        SessionStartedAtUtc = Start,
        NextWagerCents = wager,
    };

    private static IReadOnlyList<string> Deck() => StandardDeck.Create().Select(CardCode.Format).ToArray();
}
