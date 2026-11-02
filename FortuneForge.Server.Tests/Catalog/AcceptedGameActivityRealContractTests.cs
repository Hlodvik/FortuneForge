using System.Reflection;
using System.Text.Json;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.HiLo.Server;
using FortuneForge.Games.Mines.Server;
using FortuneForge.Games.Solitaire;
using FortuneForge.Games.TexasHoldem;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Admin.Management;
using FortuneForge.Server.Cards.Baccarat;
using FortuneForge.Server.Cards.Blackjack.Table;
using FortuneForge.Server.Cards.CasinoWar;
using FortuneForge.Server.Cards.Solitaire;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using FortuneForge.Server.Cards.VideoPoker;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FortuneForge.Server.Tests.Catalog;

// Serialize the real endpoint DTOs, including polymorphic session envelopes, so
// renamed contract fields cannot silently invalidate the activity projection.
public sealed class AcceptedGameActivityRealContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void EveryRuntimeSlotIdMapsToExactlyOneCatalogueSlotUsingTheRealSpinContract()
    {
        var runtimeIds = typeof(SlotSpecialRoundProfiles).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) && field.Name.EndsWith("GameId"))
            .Select(field => (string)field.GetRawConstantValue()!).ToArray();
        Assert.Equal(20, runtimeIds.Length);
        var canonicalIds = new HashSet<string>();
        var path = ActionPath<SlotsController>(nameof(SlotsController.Spin));
        foreach (var runtimeId in runtimeIds)
        {
            var spinId = Guid.NewGuid();
            var result = Shape<SpinResult>(new { spinId, gameId = runtimeId });
            var activity = Read(path, result);
            Assert.Equal(spinId.ToString(), activity.RoundKey);
            Assert.Equal(activity.RoundKey, activity.EventKey);
            Assert.True(canonicalIds.Add(activity.GameId));
        }
        Assert.Equal(AdminManagementCatalog.Games.Where(game => game.Category == "slots").Select(game => game.Id).Order(), canonicalIds.Order());
    }

    [Fact]
    public void BlackjackReadsRoundFromTheRealPolymorphicMutationEnvelope()
    {
        var table = Shape<BlackjackTableResponse>(new { tableId = "blackjack-table", round = 7, phase = "playing" });
        var response = new BlackjackTableMutationResponse(new BlackjackTablePlaySessionResponse(table, 12), 50m);
        var activity = Read(ActionPath<BlackjackTableController>(nameof(BlackjackTableController.Wager)), response);
        Assert.Equal("blackjack", activity.GameId);
        Assert.Equal("blackjack-table:7", activity.RoundKey);
        Assert.Equal(activity.RoundKey, Read(ActionPath<BlackjackTableController>(nameof(BlackjackTableController.Action)), response).RoundKey);
    }

    [Fact]
    public void HoldemReadsBothPlayingAndFinalHandMutationContracts()
    {
        var table = Shape<CreditHoldemTableResponse>(new { matchId = "holdem-match", handNumber = 3 });
        var path = ActionPath<CreditHoldemController>(nameof(CreditHoldemController.Action));
        var active = new CreditHoldemMutationResponse(new CreditHoldemMatchSessionResponse(table, 8), 50m);
        Assert.Equal("holdem-match:3", Read(path, active).RoundKey);
        var final = Shape<CreditHoldemResultSessionResponse>(new { matchId = "holdem-match", handNumber = 3, finalTable = table });
        Assert.Equal("holdem-match:3", Read(path, new CreditHoldemMutationResponse(final, 60m)).RoundKey);
    }

    [Fact]
    public void SolitaireReadsCompetitiveMovesAndValidatedFreeRunCompletion()
    {
        var session = Shape<SolitaireMatchSessionResponse>(new { matchId = "solitaire-match", moves = 2 });
        var active = new SolitaireMutationResponse(session, 50m);
        Assert.Equal("solitaire-match", Read(ActionPath<SolitaireController>(nameof(SolitaireController.Command)), active).RoundKey);
        var complete = new SolitaireFreeRunCompletionResult("solitaire_free_run", 1, 1, 100, 20, 50000, "won", DateTimeOffset.UtcNow, false);
        Assert.Equal("solitaire_free_run", Read(ActionPath<SolitaireController>(nameof(SolitaireController.CompleteFreeRun)), complete).RoundKey);
        var noMoves = new SolitaireMutationResponse(session with { Moves = 0 }, 50m);
        Assert.Null(AcceptedGameActivityReader.Read("solitaire", JsonSerializer.Serialize(noMoves, Json), null));
    }

    [Fact]
    public void InstantCardGamesReadTheirActualRoundResponseContracts()
    {
        Assert.Equal("baccarat-round", Read(ActionPath<BaccaratController>(nameof(BaccaratController.Start)),
            Shape<BaccaratRoundResponse>(new { roundId = "baccarat-round" })).RoundKey);
        Assert.Equal("video-poker-round", Read(ActionPath<VideoPokerController>(nameof(VideoPokerController.Start)),
            Shape<VideoPokerRoundResponse>(new { roundId = "video-poker-round" })).RoundKey);
        Assert.Equal("casino-war-round", Read(ActionPath<CasinoWarController>(nameof(CasinoWarController.Start)),
            Shape<CasinoWarRoundResponse>(new { roundId = "casino-war-round" })).RoundKey);
        Assert.Equal("dragon-round", Read("/api/games/dragon-tiger/rounds",
            Shape<DragonTigerRoundResponse>(new { roundId = "dragon-round" })).RoundKey);
        Assert.Equal("andar-round", Read("/api/games/andar-bahar/rounds",
            Shape<AndarBaharRoundResponse>(new { roundId = "andar-round" })).RoundKey);
    }

    [Fact]
    public void HiLoAndMinesReadTheirNestedRoundContractsAndKeepOneRoundAcrossActions()
    {
        var hiLo = new HiLoSessionResponse(false, 1000, new HiLoLimits(), Shape<HiLoRoundResponse>(new { roundId = "hi-lo-round" }));
        Assert.Equal("hi-lo-round", Read("/api/games/hi-lo/rounds", hiLo).RoundKey);
        Assert.Equal("hi-lo-round", Read("/api/games/hi-lo/rounds/hi-lo-round/actions", hiLo).RoundKey);
        var mines = new MinesSessionResponse(1000, false, Shape<MinesRoundResponse>(new { roundId = "mines-round" }));
        Assert.Equal("mines-round", Read("/api/games/mines/rounds", mines).RoundKey);
        Assert.Equal("mines-round", Read("/api/games/mines/rounds/mines-round/reveal", mines).RoundKey);
    }

    [Theory]
    [InlineData("/api/cards/blackjack/table/queue")]
    [InlineData("/api/cards/blackjack/table/tables/table/leave")]
    [InlineData("/api/cards/texas-holdem/credit/queue")]
    [InlineData("/api/cards/texas-holdem/credit/matches/match/leave")]
    [InlineData("/api/solitaire/queue")]
    [InlineData("/api/solitaire/matches/match/claim")]
    [InlineData("/api/solitaire/free/runs/run/resume")]
    public void NavigationQueueAndClaimDoNotBecomePlays(string path) => Assert.Null(AcceptedGameActivityReader.GameFor("POST", path));

    private static AcceptedGameActivity Read<T>(string path, T response)
    {
        Assert.Null(AcceptedGameActivityReader.GameFor("GET", path));
        var game = AcceptedGameActivityReader.GameFor("POST", path);
        Assert.NotNull(game);
        return Assert.IsType<AcceptedGameActivity>(AcceptedGameActivityReader.Read(game, JsonSerializer.Serialize(response, Json), "accepted-operation"));
    }

    // Irrelevant card/balance fields use DTO defaults; identifier/turn properties
    // must survive the real serializer before the reader sees any JSON.
    private static T Shape<T>(object fields) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(fields, Json), Json)!;

    private static string ActionPath<T>(string method)
    {
        var route = typeof(T).GetCustomAttribute<RouteAttribute>()!.Template;
        var action = typeof(T).GetMethod(method)!.GetCustomAttribute<HttpPostAttribute>()!.Template;
        return "/" + route + "/" + System.Text.RegularExpressions.Regex.Replace(action!, "\\{[^}]+\\}", "server-id");
    }
}
