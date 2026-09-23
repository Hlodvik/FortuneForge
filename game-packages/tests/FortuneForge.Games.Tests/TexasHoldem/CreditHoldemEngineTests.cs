using System.Text.Json;
using FortuneForge.Games.TexasHoldem;
using Xunit;

namespace FortuneForge.Games.Tests;

public sealed class CreditHoldemEngineTests
{
    private static readonly DateTime Start = new(2026, 8, 15, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EvaluatorRanksCanonicalHandsServerSide()
    {
        var straightFlush = TexasHoldemRules.Evaluate(
            ["A|spades", "K|spades", "Q|spades", "J|spades", "10|spades", "2|clubs", "3|diamonds"]);
        var quads = TexasHoldemRules.Evaluate(
            ["A|spades", "A|hearts", "A|diamonds", "A|clubs", "K|spades", "2|clubs", "3|diamonds"]);
        var fullHouse = TexasHoldemRules.Evaluate(
            ["K|spades", "K|hearts", "K|diamonds", "Q|clubs", "Q|spades", "2|clubs", "3|diamonds"]);

        Assert.Equal((8, "straight-flush"), (straightFlush.Category, straightFlush.Name));
        Assert.Equal((7, "four-of-a-kind"), (quads.Category, quads.Name));
        Assert.Equal((6, "full-house"), (fullHouse.Category, fullHouse.Name));
        Assert.True(straightFlush.Score > quads.Score && quads.Score > fullHouse.Score);
    }

    [Fact]
    public void ActionLegalityRejectsCheckFacingBetAndOutOfRangeRaiseWithoutMutation()
    {
        var match = Deal(2);
        var actor = match.Players[match.ActiveSeat];
        Assert.Contains(CreditHoldemActions.Call, CreditHoldemEngine.LegalActions(match, actor));
        Assert.DoesNotContain(CreditHoldemActions.Check, CreditHoldemEngine.LegalActions(match, actor));
        var version = match.Version;

        Assert.Throws<CreditHoldemIllegalActionException>(() => CreditHoldemEngine.ApplyAction(
            match, actor.ActorId, CreditHoldemActions.Check, null, Start.AddSeconds(1)));
        Assert.Throws<CreditHoldemIllegalActionException>(() => CreditHoldemEngine.ApplyAction(
            match, actor.ActorId, CreditHoldemActions.Raise, match.CurrentBet, Start.AddSeconds(1)));
        Assert.Equal(version, match.Version);
    }

    [Fact]
    public void DealOrderStartsLeftOfButtonAtTheSupportedThreeSeatMinimum()
    {
        var deck = TexasHoldemRules.CreateDeck(12345);
        var multiway = Deal(3);
        Assert.Equal([deck[2], deck[5]], multiway.Players[0].HoleCards);
        Assert.Equal([deck[0], deck[3]], multiway.Players[1].HoleCards);
        Assert.Equal([deck[1], deck[4]], multiway.Players[2].HoleCards);
        Assert.Equal(0, multiway.DealerSeat);
        Assert.Equal(0, multiway.ActiveSeat); // left of the big blind in a three-handed deal
    }

    [Fact]
    public void ShortAllInRaiseIsLegalButDoesNotReopenPriorAction()
    {
        var match = Deal(3);
        foreach (var player in match.Players)
        {
            player.Stack = 100;
            player.CommittedRound = 0;
            player.CommittedHand = 0;
            player.Status = "active";
            player.HasActed = false;
            player.CanRaise = true;
        }
        match.ActiveSeat = 0;
        match.CurrentBet = 0;
        match.MinimumRaise = 10;
        match.Players[1].Stack = 5;

        CreditHoldemEngine.ApplyAction(
            match, match.Players[0].ActorId, CreditHoldemActions.Check, null, Start.AddSeconds(1));
        Assert.Contains(CreditHoldemActions.Raise, CreditHoldemEngine.LegalActions(match, match.Players[1]));
        CreditHoldemEngine.ApplyAction(
            match, match.Players[1].ActorId, CreditHoldemActions.Raise, 5, Start.AddSeconds(2));
        Assert.Equal("all-in", match.Players[1].Status);
        Assert.Equal(10, match.MinimumRaise); // a short raise does not lower the full-raise increment
        CreditHoldemEngine.ApplyAction(
            match, match.Players[2].ActorId, CreditHoldemActions.Call, null, Start.AddSeconds(3));

        var priorActorActions = CreditHoldemEngine.LegalActions(match, match.Players[0]);
        Assert.Contains(CreditHoldemActions.Call, priorActorActions);
        Assert.DoesNotContain(CreditHoldemActions.Raise, priorActorActions);
    }

    [Fact]
    public void CumulativeShortAllInsReopenAfterAFullRaiseIncrement()
    {
        var match = Deal(3);
        foreach (var player in match.Players)
        {
            player.Stack = 500;
            player.CommittedRound = 100;
            player.CommittedHand = 100;
            player.Status = "active";
            player.HasActed = false;
            player.CanRaise = true;
            player.BetWhenLastActed = 0;
            player.ReopenRaiseIncrement = 0;
        }
        match.ActiveSeat = 0;
        match.CurrentBet = 100;
        match.MinimumRaise = 100;
        match.Players[1].Stack = 40;
        match.Players[2].Stack = 100;

        CreditHoldemEngine.ApplyAction(
            match, match.Players[0].ActorId, CreditHoldemActions.Check, null, Start.AddSeconds(1));
        CreditHoldemEngine.ApplyAction(
            match, match.Players[1].ActorId, CreditHoldemActions.Raise, 140, Start.AddSeconds(2));
        Assert.Contains(CreditHoldemActions.Raise, CreditHoldemEngine.LegalActions(match, match.Players[2]));
        CreditHoldemEngine.ApplyAction(
            match, match.Players[2].ActorId, CreditHoldemActions.Raise, 200, Start.AddSeconds(3));

        var reopened = CreditHoldemEngine.LegalActions(match, match.Players[0]);
        Assert.Contains(CreditHoldemActions.Call, reopened);
        Assert.Contains(CreditHoldemActions.Raise, reopened);
        Assert.Equal(100, match.CurrentBet - match.Players[0].BetWhenLastActed);
    }

    [Fact]
    public void HiddenServerSkillAssignmentSupportsExactlyLevelsTwoThreeAndFour()
    {
        var ticket = new CreditHoldemTicket(
            "ticket-human",
            "user-human",
            $"seat_{Guid.NewGuid():N}",
            "Alice",
            "test",
            "queued",
            1,
            Start,
            Start,
            null);

        var observed = Enumerable.Range(1, 24)
            .SelectMany(seed => CreditHoldemEngine.Deal(
                "match", [ticket], 3, "test", (ulong)seed,
                new Dictionary<string, long> { [ticket.UserId] = 5_000 }, Start).Players)
            .Where(player => player.IsBot)
            .Select(player => player.BotSkillLevel!.Value)
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal([2, 3, 4], observed);
    }

    [Fact]
    public void StandardTableCapsHumanAndSyntheticStacksAtOneHundredCredits()
    {
        var ticket = new CreditHoldemTicket(
            "ticket-cap", "user-cap", $"seat_{Guid.NewGuid():N}", "Alice", "test",
            "queued", 1, Start, Start, null, CreditHoldemTableRules.StandardId);
        var match = CreditHoldemEngine.Deal(
            "match-cap", [ticket], 3, "test", 88,
            new Dictionary<string, long> { [ticket.UserId] = 30_340_125 }, Start,
            CreditHoldemTableRules.StandardId);

        Assert.All(match.Players, player => Assert.InRange(player.StartingStack, 1, 10_000));
        Assert.Equal(10_000, match.Players.Single(player => !player.IsBot).StartingStack);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void LegacyStoredMatchDefaultsCollectionsAddedAfterInitialRelease(
        bool includeLeavingActorIds,
        bool includeHumanPayoutsCents)
    {
        var current = Deal(3);
        var json = JsonSerializer.Serialize(current, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var legacyFields = document.RootElement.EnumerateObject()
            .Where(property => includeLeavingActorIds || property.Name is not "leavingActorIds")
            .Where(property => includeHumanPayoutsCents || property.Name is not "humanPayoutsCents")
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        var legacyJson = JsonSerializer.Serialize(legacyFields, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var restored = JsonSerializer.Deserialize<CreditHoldemMatch>(
            legacyJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(restored);
        Assert.Empty(restored.LeavingActorIds);
        Assert.Empty(restored.HumanPayoutsCents);
        Assert.Equal(current.MatchId, restored.MatchId);
        Assert.Equal(current.Players.Count, restored.Players.Count);
    }

    [Fact]
    public void SidePotsConserveEveryChipAndCapShortStackEligibility()
    {
        var match = Deal(3);
        var starting = new[] { 100, 200, 300 };
        for (var index = 0; index < match.Players.Count; index++)
        {
            var player = match.Players[index];
            player.Stack = starting[index];
            player.CommittedRound = 0;
            player.CommittedHand = 0;
            player.Status = "active";
            player.HasActed = false;
        }
        match.ActiveSeat = 0;
        match.CurrentBet = 0;
        match.MinimumRaise = 10;

        CreditHoldemEngine.ApplyAction(match, match.Players[0].ActorId, CreditHoldemActions.Raise, 100, Start.AddSeconds(1));
        CreditHoldemEngine.ApplyAction(match, match.Players[1].ActorId, CreditHoldemActions.Raise, 200, Start.AddSeconds(2));
        CreditHoldemEngine.ApplyAction(match, match.Players[2].ActorId, CreditHoldemActions.Call, null, Start.AddSeconds(3));

        Assert.Equal("completed", match.Status);
        Assert.Equal(600, match.Players.Sum(player => player.Stack));
        Assert.InRange(match.Players[0].Stack, 0, 300); // the 100-chip stack is ineligible for the 200-chip side pot
        Assert.InRange(match.Players[1].Stack, 0, 500);
        Assert.InRange(match.Players[2].Stack, 100, 600);
    }

    [Fact]
    public void TiedPotOddChipStartsLeftOfDealer()
    {
        var match = Deal(3);
        match.Community.Clear();
        match.Community.AddRange(["A|clubs", "K|diamonds", "Q|hearts", "2|spades", "3|clubs"]);
        match.Players[0].HoleCards.Clear();
        match.Players[0].HoleCards.AddRange(["J|spades", "10|spades"]);
        match.Players[1].HoleCards.Clear();
        match.Players[1].HoleCards.AddRange(["9|diamonds", "8|diamonds"]);
        match.Players[2].HoleCards.Clear();
        match.Players[2].HoleCards.AddRange(["J|hearts", "10|hearts"]);
        foreach (var player in match.Players)
        {
            player.Stack = 95;
            player.CommittedRound = 5;
            player.CommittedHand = 5;
            player.Status = "active";
        }
        match.Players[1].Status = "folded";

        CreditHoldemEngine.ForceComplete(match, Start.AddSeconds(1));

        Assert.Equal(300, match.Players.Sum(player => player.Stack));
        Assert.Equal(102, match.Players[0].Stack);
        Assert.Equal(95, match.Players[1].Stack);
        Assert.Equal(103, match.Players[2].Stack); // seat 2 is encountered before button seat 0
    }

    private static CreditHoldemMatch Deal(int players)
    {
        var tickets = Enumerable.Range(0, players).Select(index => new CreditHoldemTicket(
            $"ticket-{index}",
            $"user-{index}",
            $"seat_{Guid.NewGuid():N}",
            $"Player{index + 1}",
            "test",
            "queued",
            1,
            Start,
            Start,
            null)).ToArray();
        var balances = tickets.ToDictionary(ticket => ticket.UserId, _ => 5_000L, StringComparer.Ordinal);
        return CreditHoldemEngine.Deal(
            "match", tickets, Math.Max(players, CreditHoldemMoney.MinimumStartPlayers),
            "test", 12345, balances, Start);
    }
}
