using System.Text.Json;
using FortuneForge.Server.Cards.Solitaire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FortuneForge.Server.Tests.Solitaire;

public sealed class SolitaireApiContractTests
{
    [Fact]
    public void Controller_ExposesOnlyAuthoritativeQueueCommandResultAndHistorySurface()
    {
        var controller = typeof(SolitaireController);
        var route = Assert.Single(controller
            .GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>());
        Assert.Equal("api/solitaire", route.Template);

        var actions = controller.GetMethods()
            .Where(method => method.DeclaringType == controller)
            .Select(method => new ActionRoute(
                method.Name,
                method.GetCustomAttributes(false).OfType<HttpMethodAttribute>().SingleOrDefault(),
                method.GetCustomAttributes(false).OfType<EnableRateLimitingAttribute>().SingleOrDefault()))
            .Where(value => value.Http is not null)
            .ToArray();

        Assert.Equal(8, actions.Length);
        AssertRoute(actions, "Session", "session", "GET");
        AssertRoute(actions, "Join", "queue", "POST");
        AssertRoute(actions, "Cancel", "queue/{ticketId}", "DELETE");
        AssertRoute(actions, "Command", "matches/{matchId}/commands", "POST");
        AssertRoute(actions, "Forfeit", "matches/{matchId}/forfeit", "POST");
        AssertRoute(actions, "Dismiss", "matches/{matchId}/dismiss", "POST");
        AssertRoute(actions, "Claim", "matches/{matchId}/claim", "POST");
        AssertRoute(actions, "History", "history", "GET");
        Assert.DoesNotContain(actions, value => value.Name.Contains("Result", StringComparison.Ordinal));
        Assert.All(actions, value => Assert.NotNull(value.Rate));
    }

    [Fact]
    public void CommandDto_CannotSubmitSeedScoreTimeMovesPayoutOrCompletion()
    {
        var properties = typeof(SolitaireCommandRequest).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(
            ["Type", "ExpectedVersion", "From", "StartIndex", "To", "Column"],
            properties);
        Assert.DoesNotContain("Score", properties);
        Assert.DoesNotContain("Moves", properties);
        Assert.DoesNotContain("ElapsedSeconds", properties);
        Assert.DoesNotContain("Seed", properties);
        Assert.DoesNotContain("Payout", properties);
        Assert.DoesNotContain("CompletedAtUtc", properties);
    }

    [Fact]
    public void ActiveSessionAndMutationJson_OmitSeedsAndFaceDownCardIdentity()
    {
        var game = SolitaireEngine.ToResponse(SolitaireEngine.CreateGame(1));
        var startedAt = new DateTime(2026, 8, 14, 20, 0, 0, DateTimeKind.Utc);
        var match = new SolitaireMatchSessionResponse(
            new string('a', 64),
            4,
            5m,
            20m,
            18m,
            startedAt,
            startedAt.AddMinutes(10),
            1,
            game.Score,
            game.Moves,
            600_000,
            game,
            []);

        using var sessionJson = JsonDocument.Parse(JsonSerializer.Serialize(
            match,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var mutationJson = JsonDocument.Parse(JsonSerializer.Serialize(
            new SolitaireMutationResponse(match, 95m),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        AssertActiveMatchIsRedacted(sessionJson.RootElement);
        AssertActiveMatchIsRedacted(mutationJson.RootElement.GetProperty("session"));
        Assert.Equal(95m, mutationJson.RootElement.GetProperty("balanceCredits").GetDecimal());
    }

    [Fact]
    public void HistoryJson_DoesNotExposeADealSeed()
    {
        var history = new SolitaireHistoryItemResponse(
            new string('b', 64),
            4,
            5m,
            20m,
            1,
            725,
            300,
            18m,
            13m,
            new DateTime(2026, 8, 14, 20, 5, 0, DateTimeKind.Utc),
            ["Opponent"]);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            history,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        AssertNoSeedProperties(document.RootElement);
    }

    [Fact]
    public void FeatureGate_IsDisabledByDefaultAndRequiresExplicitTrueConfiguration()
    {
        var disabled = new ConfigurationBuilder().Build();
        var enabled = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:CompetitiveSolitaireEnabled"] = "true"
            })
            .Build();

        Assert.False(SolitaireController.IsEnabled(disabled));
        Assert.True(SolitaireController.IsEnabled(enabled));
    }

    [Fact]
    public void SingleHumanBotFill_IsASeparateExplicitlyDisabledTestingOption()
    {
        var defaults = new CompetitiveSolitaireOptions();
        var disabled = new ConfigurationBuilder().Build();
        var enabled = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cards:CompetitiveSolitaire:AllowSingleHumanBotFill"] = "true"
            })
            .Build();

        Assert.False(defaults.AllowSingleHumanBotFill);
        Assert.False(SolitaireController.IsSingleHumanBotFillEnabled(disabled));
        Assert.True(SolitaireController.IsSingleHumanBotFillEnabled(enabled));
    }

    [Theory]
    [InlineData(4, 5, 18, 2)]
    [InlineData(6, 10, 54, 6)]
    [InlineData(8, 25, 180, 20)]
    public void PrizeRule_PaysNinetyPercentAndAccountsRemainder(
        int players,
        decimal buyIn,
        decimal payout,
        decimal fee)
    {
        var buyInCents = SolitaireMoney.ValidateBuyIn(players, buyIn);
        var poolCents = players * buyInCents;
        var payoutCents = SolitaireMoney.WinnerPayout(players, buyInCents);

        Assert.Equal(payout, SolitaireMoney.ToCredits(payoutCents));
        Assert.Equal(fee, SolitaireMoney.ToCredits(poolCents - payoutCents));
    }

    private static void AssertRoute(
        IEnumerable<ActionRoute> actions,
        string name,
        string template,
        string method)
    {
        var action = Assert.Single(actions, value => value.Name == name);
        Assert.Equal(template, action.Http!.Template);
        Assert.Contains(method, action.Http.HttpMethods);
    }

    private static void AssertActiveMatchIsRedacted(JsonElement match)
    {
        AssertNoSeedProperties(match);
        var game = match.GetProperty("game");
        Assert.Equal(24, game.GetProperty("stock").GetArrayLength());
        Assert.Equal(7, game.GetProperty("tableau").GetArrayLength());

        var cards = game.GetProperty("stock").EnumerateArray()
            .Concat(game.GetProperty("waste").EnumerateArray())
            .Concat(game.GetProperty("foundations").EnumerateArray()
                .SelectMany(pile => pile.EnumerateArray()))
            .Concat(game.GetProperty("tableau").EnumerateArray()
                .SelectMany(pile => pile.EnumerateArray()))
            .ToArray();
        Assert.Equal(52, cards.Length);

        var faceUp = cards.Where(card => card.GetProperty("isFaceUp").GetBoolean()).ToArray();
        var faceDown = cards.Where(card => !card.GetProperty("isFaceUp").GetBoolean()).ToArray();
        Assert.Equal(7, faceUp.Length);
        Assert.Equal(45, faceDown.Length);
        Assert.All(faceUp, card =>
        {
            Assert.False(string.IsNullOrWhiteSpace(card.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(card.GetProperty("suit").GetString()));
            Assert.InRange(card.GetProperty("rank").GetInt32(), 1, 13);
        });
        Assert.All(faceDown, card =>
        {
            Assert.False(card.TryGetProperty("id", out _));
            Assert.False(card.TryGetProperty("suit", out _));
            Assert.False(card.TryGetProperty("rank", out _));
        });
    }

    private static void AssertNoSeedProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert.DoesNotContain(
                    property.Name,
                    ["seed", "dealSeed"],
                    StringComparer.OrdinalIgnoreCase);
                AssertNoSeedProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AssertNoSeedProperties(item);
            }
        }
    }

    private sealed record ActionRoute(
        string Name,
        HttpMethodAttribute? Http,
        EnableRateLimitingAttribute? Rate);
}
