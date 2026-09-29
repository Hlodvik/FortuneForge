using FortuneForge.Games.Blackjack;
using FortuneForge.Games.Abstractions;
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

    [Fact]
    public void PackageDefinesNoAutomatedPlayerConcepts()
    {
        var packageTypes = typeof(BlackjackModule).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(BlackjackModule).Namespace)
            .ToArray();
        var domainProperties = packageTypes.SelectMany(type => type.GetProperties()).ToArray();

        Assert.DoesNotContain(packageTypes, type =>
            type.Name.Contains("Bot", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Managed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(domainProperties, property =>
            property.Name.Contains("Bot", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Managed", StringComparison.OrdinalIgnoreCase));
        Assert.False(BlackjackModule.Descriptor.Capabilities.HasFlag(GameCapability.Bots));
    }

    [Fact]
    public void BeginDealRevealsExactlyOneOpeningCardPerTransition()
    {
        var table = Table();
        BlackjackTableEngine.BeginDeal(table, Deck(), 42, Start);

        Assert.Equal(BlackjackTablePhases.Dealing, table.Phase);
        Assert.Empty(table.Players[0].Cards);
        Assert.Empty(table.DealerCards);

        var now = Start;
        var priorCount = 0;
        for (var step = 0; step < 4; step++)
        {
            now = now.Add(BlackjackTableEngine.InitialCardDuration);
            BlackjackTableEngine.AdvanceAutomatedTurns(table, now);
            var currentCount = table.Players.Sum(player => player.Cards.Count) + table.DealerCards.Count;
            Assert.Equal(priorCount + 1, currentCount);
            priorCount = currentCount;
        }

        Assert.Equal(2, table.Players[0].Cards.Count);
        Assert.Equal(2, table.DealerCards.Count);
        Assert.NotEqual(BlackjackTablePhases.Dealing, table.Phase);
    }

    [Fact]
    public void PrepareForBettingClearsEveryPreviousRoundArtifactButKeepsNextWagers()
    {
        var table = Table();
        BlackjackTableEngine.Deal(table, Deck(), 42, Start);
        var player = table.Players[0];
        player.NextWagerCents = 750;
        player.PayoutCents = 1_250;
        player.Outcome = BlackjackOutcomes.PlayerWin;
        player.LastAction = BlackjackActions.Stand;
        player.InsuranceWagerCents = 100;
        player.InsurancePayoutCents = 300;
        player.InsuranceAccepted = true;
        player.SecondaryHand = new BlackjackTableSecondaryHand
        {
            Cards = ["A|spades"],
            WagerCents = 500,
            TotalWagerCents = 500,
            Status = "completed",
            Outcome = BlackjackOutcomes.PlayerWin,
            LastAction = BlackjackActions.Stand
        };

        BlackjackTableEngine.PrepareForBetting(table);

        Assert.Empty(table.Deck);
        Assert.Empty(table.DealerCards);
        Assert.Equal(0, table.DealerVisibleCardCount);
        Assert.All(table.Players, seated =>
        {
            Assert.Empty(seated.Cards);
            Assert.Equal(0, seated.WagerCents);
            Assert.Equal(0, seated.TotalWagerCents);
            Assert.Equal(0, seated.PayoutCents);
            Assert.Null(seated.Outcome);
            Assert.Null(seated.LastAction);
            Assert.Null(seated.SecondaryHand);
            Assert.Equal(0, seated.InsuranceWagerCents);
            Assert.Equal(0, seated.InsurancePayoutCents);
            Assert.Null(seated.InsuranceAccepted);
        });
        Assert.Equal(750, player.NextWagerCents);
    }

    private static BlackjackTableState Table() => new()
    {
        TableId = "table-1",
        CreatedAtUtc = Start,
        UpdatedAtUtc = Start,
        Players =
        [
            Player("human", 0, 500),
            Player("managed-1", 1, 0),
            Player("managed-2", 2, 0),
        ],
    };

    private static BlackjackTablePlayer Player(string actor, int seat, long wager) => new()
    {
        ActorId = actor,
        PublicSeatId = $"seat-{seat}",
        DisplayName = actor,
        Seat = seat,
        SessionId = $"session-{seat}",
        SessionStartedAtUtc = Start,
        NextWagerCents = wager,
    };

    private static IReadOnlyList<string> Deck() => StandardDeck.Create().Select(CardCode.Format).ToArray();
}
