using System.Text.Json;
using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Cards.Blackjack.Bots;
using FortuneForge.Server.Cards.Bots;
using FortuneForge.Server.Cards.Solitaire;
using FortuneForge.Server.Cards.Solitaire.Bots;
using FortuneForge.Server.Cards.TexasHoldem.Bots;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FortuneForge.Server.Tests.Cards.Bots;

public sealed class CardBotSafetyTests
{
    [Fact]
    public void IdentityFactory_KeepsSkillAndAutomationMetadataInternal()
    {
        var factory = new BotIdentityFactory();
        var first = factory.Create(42, 8, CardBotSkillLevels.Strong);
        var second = factory.Create(42, 8, CardBotSkillLevels.Strong);

        Assert.Equal(first, second);
        Assert.Equal(8, first.Select(bot => bot.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(first, bot =>
        {
            Assert.Matches("^[A-Za-z]+[0-9]*$", bot.DisplayName);
            Assert.StartsWith("bot-", bot.SeatId);
            Assert.Equal(4, bot.SkillLevel);
        });
        Assert.DoesNotContain(typeof(CardBotSeatDto).GetProperties(), property =>
            property.Name.Contains("Bot", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Skill", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("ActorKind", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void SkillLevels_RejectAnythingExceptTwoThreeAndFour(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CardBotSkillLevels.Validate(value));

    [Fact]
    public void Queue_WaitsForGraceAndPendingBotsYieldToHumansUntilAtomicStart()
    {
        var now = DateTime.UnixEpoch;
        var queue = new HumanFirstBotQueue(
            "queue", CardBotGames.Blackjack, 3, now, TimeSpan.FromSeconds(5), 2, 3, 10);
        var identities = new BotIdentityFactory();
        queue.AddHuman("human-a", "HumanA", now);

        Assert.Null(queue.TryStart(now.AddSeconds(4), identities));
        var reserved = queue.ReserveBots(now.AddSeconds(5), identities);
        Assert.Equal(2, reserved.Count(seat => seat.IsBot));
        AssertPublicJsonIsAutomationNeutral(queue.ToDto());

        queue.AddHuman("human-b", "HumanB", now.AddSeconds(6));
        reserved = queue.ReserveBots(now.AddSeconds(6), identities);
        Assert.Single(reserved, seat => seat.IsBot);
        queue.AddHuman("human-c", "HumanC", now.AddSeconds(6));
        var started = queue.TryStart(now.AddSeconds(6), identities);

        Assert.NotNull(started);
        Assert.All(started!, seat => Assert.False(seat.IsBot));
        Assert.Equal([0, 1, 2], started!.Select(seat => seat.Seat));
        Assert.All(started!, seat => Assert.Matches("^seat_[0-9a-f]{32}$", seat.PublicSeatId));
    }

    [Fact]
    public async Task TurnLease_AllowsOneOwnerAndNeverReplaysCompletedVersion()
    {
        var store = new InMemoryBotTurnLeaseStore();
        var now = DateTime.UnixEpoch;
        var key = new BotTurnKey(CardBotGames.Blackjack, "match", "bot-1", 7);
        var first = await store.TryAcquireAsync(key, "server-a", now, TimeSpan.FromSeconds(10), default);
        var duplicate = await store.TryAcquireAsync(key, "server-b", now, TimeSpan.FromSeconds(10), default);
        Assert.NotNull(first);
        Assert.Null(duplicate);
        Assert.True(await store.CompleteAsync(first!, 8, now.AddSeconds(1), default));
        Assert.Null(await store.TryAcquireAsync(key, "server-b", now.AddMinutes(1), TimeSpan.FromSeconds(10), default));
    }

    [Fact]
    public void BlackjackAgent_OnlyReturnsSuppliedLegalActionsAtEverySkill()
    {
        var agent = new BlackjackBotAgent();
        var observation = new BlackjackBotObservation(
            ["10|clubs", "6|hearts"], "10|spades", [BlackjackActions.Hit, BlackjackActions.Stand]);
        foreach (var skill in new[] { 2, 3, 4 })
        foreach (var version in Enumerable.Range(1, 50))
        {
            var action = agent.Choose(observation, skill, 123, version, Enabled().Blackjack);
            Assert.Contains(action, observation.LegalActions);
        }
    }

    [Fact]
    public void SolitaireAgent_UsesOnlyItsBoardAndEveryChosenCommandIsLegal()
    {
        var agent = new SolitaireBotAgent();
        var game = SolitaireEngine.CreateGame(123);
        foreach (var skill in new[] { 2, 3, 4 })
        {
            var command = agent.Choose(game, 1, skill, 456, Enabled().Solitaire);
            var updated = SolitaireEngine.Apply(game, command);
            Assert.Equal(1, updated.Moves);
        }
    }

    [Fact]
    public void HoldemAgent_IsDeterministicAndOnlyUsesSeatLegalObservation()
    {
        var observation = new TexasHoldemBotObservation(
            ["A|spades", "K|spades"],
            ["Q|spades", "J|spades", "2|clubs"],
            120,
            20,
            900,
            60,
            910,
            [HoldemActions.Fold, HoldemActions.Call, HoldemActions.Raise]);
        var agent = new TexasHoldemBotAgent();
        foreach (var skill in new[] { 2, 3, 4 })
        {
            var first = agent.Choose(observation, skill, 999, 4, Enabled().TexasHoldem);
            var second = agent.Choose(observation, skill, 999, 4, Enabled().TexasHoldem);
            Assert.Equal(first, second);
            Assert.Contains(first.Action, observation.LegalActions);
            if (first.Action == HoldemActions.Raise)
                Assert.InRange(first.RaiseTo!.Value, observation.MinimumRaiseTo, observation.MaximumRaiseTo);
        }
    }

    [Fact]
    public void BlackjackPractice_HidesDealerHoleAndCommandsAreReconnectSafeAndAccountNeutral()
    {
        var service = BlackjackService();
        var now = DateTime.UnixEpoch;
        var joined = service.Join(SessionA, "PlayerA", Join(), now);
        var table = Assert.IsType<BlackjackPracticeTableDto>(joined.Table);
        Assert.True(table.Dealer.Cards[1].Hidden);
        Assert.Contains(table.Seats, seat => seat.Player.DisplayName != "PlayerA");
        Assert.All(table.Seats, seat => Assert.Matches("^seat_[0-9a-f]{32}$", seat.Player.SeatId));
        AssertPublicJsonIsAutomationNeutral(joined);
        Assert.DoesNotContain(typeof(BlackjackPracticeTableDto).GetProperties(), property =>
            property.Name.Contains("Balance", StringComparison.OrdinalIgnoreCase));

        if (table.LegalActions.Count > 0)
        {
            var action = table.LegalActions.Contains(BlackjackActions.Stand)
                ? BlackjackActions.Stand
                : table.LegalActions[0];
            var request = Command(action, table.Version, "blackjack_action_0001");
            var acted = service.Command(SessionA, table.MatchId, request, now.AddSeconds(1));
            var repeated = service.Command(SessionA, table.MatchId, request, now.AddSeconds(2));
            Assert.Equal(acted.Table!.Version, repeated.Table!.Version);
            AssertPublicJsonIsAutomationNeutral(acted);
        }
    }

    [Fact]
    public void SolitairePractice_ProjectsOnlyViewerBoardAndReconnectsAtSameVersion()
    {
        var service = SolitaireService();
        var now = DateTime.UnixEpoch;
        var joined = service.Join(SessionA, "PlayerA", Join(), now);
        var match = Assert.IsType<SolitaireBotPracticeMatchDto>(joined.Match);
        Assert.Contains(match.Seats, seat => seat.DisplayName != "PlayerA");
        AssertPublicJsonIsAutomationNeutral(joined);
        Assert.DoesNotContain(typeof(CardBotSeatDto).GetProperties(), property =>
            property.Name.Contains("Game", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Seed", StringComparison.OrdinalIgnoreCase));

        var reconnected = service.Get(SessionA, now.AddSeconds(1));
        Assert.Equal(match.Version, reconnected.Match!.Version);
        Assert.Equal(JsonSerializer.Serialize(match.Game), JsonSerializer.Serialize(reconnected.Match.Game));

        var draw = Command(SolitaireCommandTypes.Draw, match.Version, "solitaire_draw_0001");
        var acted = service.Command(SessionA, match.MatchId, draw, now.AddSeconds(2));
        var repeated = service.Command(SessionA, match.MatchId, draw, now.AddSeconds(3));
        Assert.Equal(acted.Match!.Version, repeated.Match!.Version);
    }

    [Fact]
    public void HoldemPractice_HidesOpponentHolesAndPreservesPublicActionPotAndIdempotency()
    {
        var service = HoldemService();
        var now = DateTime.UnixEpoch;
        var joined = service.Join(SessionA, "PlayerA", Join(), now);
        var table = Assert.IsType<TexasHoldemPracticeTableDto>(joined.Table);
        var human = table.Seats.Single(seat => seat.Player.DisplayName == "PlayerA");
        var bot = table.Seats.Single(seat => seat.Player.DisplayName != "PlayerA");
        Assert.All(human.HoleCards, card => Assert.False(card.Hidden));
        Assert.All(bot.HoleCards, card => Assert.True(card.Hidden));
        Assert.DoesNotContain(typeof(TexasHoldemPracticeTableDto).GetProperties(), property =>
            property.Name.Contains("Balance", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Ledger", StringComparison.OrdinalIgnoreCase));

        var action = table.LegalActions.Contains(HoldemActions.Call)
            ? HoldemActions.Call
            : table.LegalActions.First(value => value != HoldemActions.Raise);
        var request = Command(action, table.Version, "holdem_action_0001");
        var acted = service.Command(SessionA, table.MatchId, request, now.AddSeconds(1));
        var repeated = service.Command(SessionA, table.MatchId, request, now.AddSeconds(2));
        Assert.Equal(acted.Table!.Version, repeated.Table!.Version);
        Assert.True(acted.Table.Pot >= table.Pot);
        Assert.Contains(acted.Table.Events, item =>
            item.ActorSeatId == human.Player.SeatId &&
            item.ActorDisplayName == "PlayerA" &&
            item.Type == action);
        Assert.All(acted.Table.Seats.Single(seat => seat.Player.DisplayName != "PlayerA").HoleCards,
            card => Assert.True(card.Hidden));
        AssertPublicJsonIsAutomationNeutral(acted);
    }

    [Fact]
    public void EveryQueueResponse_UsesOpaqueSeatsAndContainsNoAutomationMetadata()
    {
        var now = DateTime.UnixEpoch;
        var options = Enabled();
        options.Blackjack.HumanWaitGraceMilliseconds = 60_000;
        options.Solitaire.HumanWaitGraceMilliseconds = 60_000;
        options.TexasHoldem.HumanWaitGraceMilliseconds = 60_000;

        var blackjack = new BlackjackBotPracticeService(
            new BotIdentityFactory(), new BlackjackBotAgent(), new InMemoryBotTurnLeaseStore(), Options.Create(options));
        var solitaire = new SolitaireBotPracticeService(
            new BotIdentityFactory(), new SolitaireBotAgent(), new InMemoryBotTurnLeaseStore(), Options.Create(options));
        var holdem = new TexasHoldemBotPracticeService(
            new BotIdentityFactory(), new TexasHoldemBotAgent(), new InMemoryBotTurnLeaseStore(), Options.Create(options));

        var responses = new object[]
        {
            blackjack.Join(SessionA, "PlayerA", Join(), now),
            solitaire.Join(SessionA, "PlayerA", Join(), now),
            holdem.Join(SessionA, "PlayerA", Join(), now)
        };
        Assert.All(responses, AssertPublicJsonIsAutomationNeutral);
        Assert.All(responses.SelectMany(response => PublicSeatIds(response)), seatId =>
            Assert.Matches("^seat_[0-9a-f]{32}$", seatId));
    }

    [Fact]
    public void SolitaireResultResponse_RemainsAutomationNeutralAndKeepsBoardsPrivate()
    {
        var service = SolitaireService();
        var now = DateTime.UnixEpoch;
        var joined = service.Join(SessionA, "PlayerA", Join(), now);
        Assert.NotNull(joined.Match);

        var result = service.Get(SessionA, now.AddMinutes(11));
        Assert.NotNull(result.Result);
        Assert.Null(result.Match);
        AssertPublicJsonIsAutomationNeutral(result);
    }

    [Fact]
    public async Task CompletedBlackjackAndHoldemResponsesAndEvents_RemainAutomationNeutral()
    {
        var now = DateTime.UnixEpoch;
        var blackjack = BlackjackService();
        var blackjackResponse = blackjack.Join(SessionA, "PlayerA", Join(), now);
        for (var attempt = 0; attempt < 40 && blackjackResponse.Table?.Status != "completed"; attempt++)
        {
            now = now.AddSeconds(2);
            var table = blackjackResponse.Table!;
            if (table.LegalActions.Count > 0)
            {
                blackjackResponse = blackjack.Command(
                    SessionA,
                    table.MatchId,
                    Command(BlackjackActions.Stand, table.Version, $"blackjack_finish_{attempt:D4}"),
                    now);
            }
            else
            {
                await blackjack.SweepAsync(now, default);
                blackjackResponse = blackjack.Get(SessionA, now);
            }
            AssertPublicJsonIsAutomationNeutral(blackjackResponse);
        }
        Assert.Equal("completed", blackjackResponse.Table?.Status);
        Assert.NotEmpty(blackjackResponse.Table!.Events);

        now = DateTime.UnixEpoch;
        var holdem = HoldemService();
        var holdemResponse = holdem.Join(SessionA, "PlayerA", Join(), now);
        for (var attempt = 0; attempt < 20 && holdemResponse.Table?.Status != "completed"; attempt++)
        {
            now = now.AddSeconds(2);
            var table = holdemResponse.Table!;
            if (table.LegalActions.Count > 0)
            {
                var action = table.LegalActions.Contains(HoldemActions.Fold)
                    ? HoldemActions.Fold
                    : table.LegalActions[0];
                holdemResponse = holdem.Command(
                    SessionA,
                    table.MatchId,
                    Command(action, table.Version, $"holdem_finish_key_{attempt:D4}"),
                    now);
            }
            else
            {
                await holdem.SweepAsync(now, default);
                holdemResponse = holdem.Get(SessionA, now);
            }
            AssertPublicJsonIsAutomationNeutral(holdemResponse);
        }
        Assert.Equal("completed", holdemResponse.Table?.Status);
        Assert.NotEmpty(holdemResponse.Table!.Events);
    }

    [Fact]
    public void EveryGameGateDefaultsOff()
    {
        var options = new CardBotPlatformOptions();
        Assert.False(options.Blackjack.Enabled);
        Assert.False(options.Solitaire.Enabled);
        Assert.False(options.TexasHoldem.Enabled);
    }

    [Fact]
    public void FrozenV2Contract_ExposesThreeIsolatedPracticeRoutes()
    {
        Assert.Equal("cards.bot.v2", CardBotContract.Version);
        AssertRoute<BlackjackBotPracticeController>("api/cards/blackjack/bot-practice");
        AssertRoute<SolitaireBotPracticeController>("api/cards/solitaire/bot-practice");
        AssertRoute<TexasHoldemBotPracticeController>("api/cards/texas-holdem/bot-practice");
    }

    [Theory]
    [InlineData("A|spades", "K|spades", "Q|spades", "J|spades", "10|spades", "straight-flush")]
    [InlineData("A|spades", "A|hearts", "A|clubs", "K|spades", "K|hearts", "full-house")]
    public void HoldemEvaluator_SettlesEvaluatedPracticeHands(
        string first,
        string second,
        string third,
        string fourth,
        string fifth,
        string expected) =>
        Assert.Equal(expected, TexasHoldemRules.Evaluate([first, second, third, fourth, fifth]).Name);

    private const string SessionA = "practice_session_a_0001";

    private static CardBotJoinRequest Join() => new(2, 3, "practice_join_key_0001");

    private static CardBotCommandRequest Command(string type, int version, string key) =>
        new(type, version, key);

    private static CardBotPlatformOptions Enabled()
    {
        var options = new CardBotPlatformOptions();
        options.Blackjack.Enabled = true;
        options.Blackjack.HumanWaitGraceMilliseconds = 0;
        options.Blackjack.MaxBotsPerMatch = 5;
        options.Solitaire.Enabled = true;
        options.Solitaire.HumanWaitGraceMilliseconds = 0;
        options.Solitaire.MaxBotsPerMatch = 7;
        options.TexasHoldem.Enabled = true;
        options.TexasHoldem.HumanWaitGraceMilliseconds = 0;
        options.TexasHoldem.MaxBotsPerMatch = 5;
        return options;
    }

    private static BlackjackBotPracticeService BlackjackService() => new(
        new BotIdentityFactory(),
        new BlackjackBotAgent(),
        new InMemoryBotTurnLeaseStore(),
        Options.Create(Enabled()));

    private static SolitaireBotPracticeService SolitaireService() => new(
        new BotIdentityFactory(),
        new SolitaireBotAgent(),
        new InMemoryBotTurnLeaseStore(),
        Options.Create(Enabled()));

    private static TexasHoldemBotPracticeService HoldemService() => new(
        new BotIdentityFactory(),
        new TexasHoldemBotAgent(),
        new InMemoryBotTurnLeaseStore(),
        Options.Create(Enabled()));

    private static void AssertRoute<TController>(string expected)
    {
        var route = Assert.Single(typeof(TController).GetCustomAttributes(typeof(RouteAttribute), false));
        Assert.Equal(expected, Assert.IsType<RouteAttribute>(route).Template);
    }

    private static void AssertPublicJsonIsAutomationNeutral(object value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        Visit(document.RootElement);

        static void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    Assert.DoesNotContain("isbot", property.Name, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("skilllevel", property.Name, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("actorkind", property.Name, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("botskill", property.Name, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("humanGrace", property.Name, StringComparison.OrdinalIgnoreCase);
                    if (property.Name.EndsWith("SeatId", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.String)
                    {
                        Assert.Matches("^seat_[0-9a-f]{32}$", property.Value.GetString()!);
                    }
                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) Visit(item);
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString() ?? string.Empty;
                Assert.False(text.StartsWith("bot-", StringComparison.OrdinalIgnoreCase), text);
                Assert.False(text.StartsWith("bot_", StringComparison.OrdinalIgnoreCase), text);
            }
        }
    }

    private static IEnumerable<string> PublicSeatIds(object value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return Find(document.RootElement).ToArray();

        static IEnumerable<string> Find(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("SeatId") && property.Value.ValueKind == JsonValueKind.String)
                        yield return property.Value.GetString()!;
                    foreach (var found in Find(property.Value)) yield return found;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                foreach (var found in Find(item)) yield return found;
            }
        }
    }
}
