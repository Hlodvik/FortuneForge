using System.Text.Json;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Cards.HouseHoldem;
using FortuneForge.Server.Cards.ThreeCardPoker.Table;
using FortuneForge.Server.Dice.SicBo;
using FortuneForge.Server.Games.Craps;
using FortuneForge.Server.Games.DropMerge;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.Games.LiarsDice;
using FortuneForge.Server.Games.Roulette;
using FortuneForge.Server.Games.TwentyFortyEight;
using FortuneForge.Server.Numbers.Keno;
using Xunit;

namespace FortuneForge.Server.Tests.Activity;

public sealed class AcceptedGameActivityTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static AcceptedGameActivity Read(string path, object response, string? requestKey = "accepted-request") =>
        Assert.IsType<AcceptedGameActivity>(AcceptedGameActivityReader.Read(
            Assert.IsType<string>(AcceptedGameActivityReader.GameFor("POST", path)), JsonSerializer.Serialize(response, Json), requestKey));

    [Fact]
    public void Legacy_paid_blackjack_uses_its_actual_game_identity_for_start_and_actions()
    {
        var hand = new BlackjackHandResponse([], 20, false, false, false);
        var response = new BlackjackGameResponse("legacy-one", "complete", "win", "Win", 1, 1, 2, 10,
            hand, hand, false, false, false, 2, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        foreach (var path in new[] { "/api/cards/blackjack/games", "/api/cards/blackjack/games/legacy-one/actions" })
        {
            var activity = Read(path, response);
            Assert.Equal("blackjack", activity.GameId); Assert.Equal("legacy:legacy-one", activity.RoundKey); Assert.True(activity.IsTerminal);
        }
    }

    [Theory]
    [InlineData("GET", "/api/slots/spins")]
    [InlineData("POST", "/api/slots/demo/spins")]
    [InlineData("POST", "/api/games/roulette/rounds")]
    [InlineData("POST", "/api/games/roulette/rounds/table/bets")]
    [InlineData("POST", "/api/cards/texas-holdem/credit/queue")]
    [InlineData("POST", "/api/cards/blackjack/table/queue")]
    [InlineData("POST", "/api/solitaire/queue")]
    [InlineData("POST", "/api/solitaire/matches/m/claim")]
    [InlineData("POST", "/api/solitaire/free/runs")]
    [InlineData("POST", "/api/solitaire/free/runs/m/resume")]
    [InlineData("POST", "/api/games/2048/games")]
    [InlineData("POST", "/api/games/2048/games/m/reset")]
    [InlineData("POST", "/api/games/hearts/paid/join")]
    [InlineData("POST", "/api/games/hearts/matches/m/advance")]
    public void Navigation_queue_management_and_uncommitted_bets_are_not_activity(string method, string path) =>
        Assert.Null(AcceptedGameActivityReader.GameFor(method, path));

    [Fact]
    public void Remaining_ten_non_slot_contracts_have_server_authoritative_play_identifiers()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var three = new ThreeCardPokerTableMutationResponse(new ThreeCardPokerTablePlaySessionResponse(
            new ThreeCardPokerTableResponse("table", "playing", 2, new ThreeCardPokerTableHandResponse([], null), [], null, [], now, now,
                null, null, null, null, 0, 0, 0), 2), 100);
        Assert.Equal("table:2", Read("/api/cards/three-card-poker/table/tables/table/wagers", three).RoundKey);
        var house = new HouseHoldemRoundResponse("house-round", 100, "preflop", 1, 1, 0, 0, 2, 0, [], null, [], null, null, null, [], false, null);
        Assert.Equal("house-round", Read("/api/games/house-holdem/rounds", house).RoundKey);
        Assert.Equal("keno-round", Read("/api/games/keno/rounds", new KenoRoundResponse("keno-round", 100, "settled", new([1]), new([1]), 1, 1, 1, 0, "win")).RoundKey);
        Assert.Equal("sic-round", Read("/api/games/sic-bo/rounds", new SicBoRoundResponse("sic-round", 100, "settled", [1, 2, 3], 6, false, 1, 2, 1, [])).RoundKey);
        Assert.Equal(id.ToString(), Read($"/api/games/craps/rounds/{id}/roll", new CrapsRoundResponse(id, 1, "point", 6, [new(1, 2, 4, 6, null)], null, [])).RoundKey);
        var roulette = new RouletteRoundResponse(id, 100, "settled", [], 12, [], "roulette-table", 2, []);
        Assert.Equal("roulette-table:2", Read($"/api/games/roulette/rounds/{id}/spin", roulette).RoundKey);
        Assert.NotEqual(Read($"/api/games/roulette/rounds/{id}/spin", roulette).RoundKey,
            Read($"/api/games/roulette/rounds/{id}/spin", roulette with { RoundNumber = 3 }).RoundKey);
        var hearts = JsonSerializer.Deserialize<HeartsMatchResponse>($"{{\"matchId\":\"{id}\",\"phase\":\"playing\"}}", Json)!;
        Assert.Equal(hearts.MatchId.ToString(), Read($"/api/games/hearts/matches/{hearts.MatchId}/card", hearts).RoundKey);
        var liars = new LiarsDiceMatchResponse(id, "bidding", 1, "player", new(1, 2), "player", 10, [2], [], null, null, false, "Your bid");
        Assert.Equal(id.ToString(), Read($"/api/games/liars-dice/matches/{id}/bid", liars).RoundKey);
        var twenty = new TwentyFortyEightGameService(); var board = twenty.Start("player", 123);
        var moved = twenty.Move("player", board.GameId, "left");
        if (moved.LastEvent == "no-move") moved = twenty.Move("player", board.GameId, "right");
        Assert.Equal(board.RunId.ToString(), Read($"/api/games/2048/games/{board.GameId}/move", moved).RoundKey);
        var drops = new DropMergeGameService(); var drop = drops.Start("player", 123);
        Assert.Equal(drop.RunId.ToString(), Read($"/api/games/drop-merge/games/{drop.GameId}/drop", drops.Drop("player", drop.GameId, 1)).RoundKey);
    }

    [Fact]
    public void Arcade_resets_have_fresh_run_ids_without_changing_the_game_session()
    {
        var twenty = new TwentyFortyEightGameService(); var first = twenty.Start("player", 1);
        var second = twenty.Reset("player", first.GameId, 1);
        Assert.Equal(first.GameId, second.GameId); Assert.NotEqual(first.RunId, second.RunId);
        var drops = new DropMergeGameService(); var drop = drops.Start("player", 1);
        var reset = drops.Reset("player", drop.GameId, 1);
        Assert.Equal(drop.GameId, reset.GameId); Assert.NotEqual(drop.RunId, reset.RunId);
        Assert.Null(AcceptedGameActivityReader.Read("2048", JsonSerializer.Serialize(second, Json), null));
        Assert.Null(AcceptedGameActivityReader.Read("drop-merge", JsonSerializer.Serialize(reset, Json), null));
        Assert.Null(AcceptedGameActivityReader.Read("2048", JsonSerializer.Serialize(second with { Moves = 5, LastEvent = "no-move" }, Json), null));
    }

    [Fact]
    public void Free_solitaire_replay_is_distinct_from_legacy_competitive_activity()
    {
        const string path = "/api/solitaire/free/runs/solitaire_free_run/replay";
        var free = Read(path, new { runId = "solitaire_free_run", gameId = "free-solitaire", moves = 1 });
        Assert.Equal("free-solitaire", free.GameId);
        Assert.Equal("solitaire_free_run", free.RoundKey);
        Assert.Equal("solitaire", Read(path, new { runId = "solitaire_free_old", moves = 1 }).GameId);
        Assert.Null(AcceptedGameActivityReader.Read("solitaire",
            "{\"runId\":\"solitaire_free_empty\",\"gameId\":\"free-solitaire\",\"moves\":0}", null));
        Assert.Equal("solitaire", AcceptedGameActivityReader.Read("solitaire",
            "{\"matchId\":\"paid_match\",\"gameId\":\"free-solitaire\",\"moves\":1}", null)!.GameId);
    }

    [Fact]
    public void Untrusted_or_incomplete_responses_cannot_manufacture_activity()
    {
        Assert.Null(AcceptedGameActivityReader.Read("baccarat", "{\"error\":\"failed\",\"roundId\":\"bad\"}", null));
        Assert.Null(AcceptedGameActivityReader.Read("blackjack", "{\"session\":{\"kind\":\"queued\"}}", null));
        Assert.Null(AcceptedGameActivityReader.Read("slots", "{\"gameId\":\"invented-slot\",\"spinId\":\"spin\"}", null));
        Assert.Null(AcceptedGameActivityReader.Read("hearts", "{\"pastMatches\":[{\"matchId\":\"old\"}]}", null));
    }

    [Fact]
    public void Idempotent_receipts_remain_stable_despite_changing_response_counters()
    {
        var first = AcceptedGameActivityReader.Read("blackjack", "{\"tableId\":\"t\",\"round\":1,\"remaining\":100}", "same-request");
        var repeated = AcceptedGameActivityReader.Read("blackjack", "{\"tableId\":\"t\",\"round\":1,\"remaining\":90}", "same-request");
        Assert.Equal(first, repeated);
        Assert.NotEqual(first!.EventKey, AcceptedGameActivityReader.Read("blackjack", "{\"tableId\":\"t\",\"round\":1}", "new-request")!.EventKey);
    }
}
