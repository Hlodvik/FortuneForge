using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Dice;
using FortuneForge.Games.LiarsDice;

namespace FortuneForge.Server.Games.LiarsDice;

/// <summary>Owns account-isolated Liar's Dice matches against three local bots.</summary>
public sealed class LiarsDiceGameService
{
    private const string HumanId = "you";
    private static readonly IReadOnlySet<string> BotIds = new HashSet<string>(["bot-1", "bot-2", "bot-3"], StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, LiarsDiceSession> matches = new();
    private readonly LiarsDiceBotAgent bot = new();
    private readonly CardBotGameOptions botOptions = new() { Enabled = true };

    public LiarsDiceStatusResponse Status() => new(true, LiarsDiceMatchEngine.DefaultDicePerPlayer, "free-play-bots");

    public LiarsDiceMatchResponse Start(string userId, StartLiarsDiceMatchRequest request)
    {
        var dicePerPlayer = request.DicePerPlayer ?? LiarsDiceMatchEngine.DefaultDicePerPlayer;
        var seed = request.Seed ?? NextSeed();
        var session = new LiarsDiceSession(userId, LiarsDiceMatchEngine.Start(seed, null, dicePerPlayer), CardBotSeed.Create());
        if (!matches.TryAdd(session.Id, session)) throw new InvalidOperationException("Could not open a Liar's Dice table.");
        AdvanceBots(session);
        return ToResponse(session);
    }

    public LiarsDiceMatchResponse Get(string userId, Guid matchId) => Read(userId, matchId, ToResponse);

    public LiarsDiceMatchResponse Bid(string userId, Guid matchId, PlaceLiarsDiceBidRequest request) =>
        Change(userId, matchId, session => ApplyHumanAction(session, new PlaceLiarsDiceBid(HumanId, new LiarsDiceBid(request.Quantity, new DieValue(request.Face)))));

    public LiarsDiceMatchResponse Challenge(string userId, Guid matchId) =>
        Change(userId, matchId, session => ApplyHumanAction(session, new ChallengeLiarsDiceBid(HumanId)));

    public LiarsDiceMatchResponse SpotOn(string userId, Guid matchId) =>
        Change(userId, matchId, session => ApplyHumanAction(session, new SpotOnLiarsDiceBid(HumanId)));

    public LiarsDiceMatchResponse Advance(string userId, Guid matchId) => Change(userId, matchId, AdvanceBots);

    public LiarsDiceMatchResponse NextRound(string userId, Guid matchId, uint? seed) => Change(userId, matchId, session =>
    {
        session.State = LiarsDiceMatchEngine.StartNextRound(session.State, seed ?? NextSeed());
        session.Message = $"Round {session.State.RoundNumber} begins. {DisplayName(session.State.CurrentPlayerId)} acts first.";
        AdvanceBots(session);
    });

    private LiarsDiceMatchResponse Change(string userId, Guid matchId, Action<LiarsDiceSession> action) =>
        Read(userId, matchId, session => { action(session); return ToResponse(session); });

    private T Read<T>(string userId, Guid matchId, Func<LiarsDiceSession, T> action)
    {
        if (!matches.TryGetValue(matchId, out var session) || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
            throw new LiarsDiceAccessException();
        lock (session.SyncRoot) return action(session);
    }

    private void ApplyHumanAction(LiarsDiceSession session, LiarsDiceCommand command)
    {
        if (!string.Equals(session.State.CurrentPlayerId, HumanId, StringComparison.Ordinal))
            throw new LiarsDiceRuleException($"It is {DisplayName(session.State.CurrentPlayerId)}'s turn. The bots are still acting.");
        var transition = LiarsDiceMatchEngine.Apply(session.State, command);
        session.State = transition.State;
        session.Message = transition.Message;
        AdvanceBots(session);
    }

    private void AdvanceBots(LiarsDiceSession session)
    {
        string? lastMessage = null;
        for (var action = 0; action < 256 && session.State.Winner is null && session.State.Round.Phase != LiarsDiceRoundPhase.Resolved && BotIds.Contains(session.State.CurrentPlayerId); action++)
        {
            var transition = LiarsDiceMatchEngine.AdvanceBotTurn(session.State, BotIds, bot, CardBotSkillLevels.Strong,
                session.BotSeed, session.BotActionVersion++, botOptions);
            session.State = transition.State;
            lastMessage = transition.Message;
        }
        if (lastMessage is not null) session.Message = lastMessage;
    }

    private static LiarsDiceMatchResponse ToResponse(LiarsDiceSession session)
    {
        var state = session.State;
        var round = state.Round;
        var hand = round.Hands.TryGetValue(HumanId, out var humanHand) ? humanHand : [];
        return new LiarsDiceMatchResponse(
            session.Id, round.Phase == LiarsDiceRoundPhase.Bidding ? "bidding" : "resolved", state.RoundNumber,
            round.CurrentPlayerId, round.CurrentBid is { } bid ? new LiarsDiceBidResponse(bid.Quantity, bid.Face.Value) : null,
            round.CurrentBidderId, round.Hands.Values.Sum(values => values.Length), hand.Select(die => die.Value).ToArray(),
            state.AllPlayerIds.Select(player => new LiarsDicePlayerResponse(player, DisplayName(player), state.DiceCounts[player],
                state.DiceCounts[player] > 0, player == HumanId)).ToArray(),
            state.LastOutcome is { } outcome ? new LiarsDiceOutcomeResponse(outcome.ChallengerId, outcome.BidderId, outcome.LoserId,
                outcome.Bid.Quantity, outcome.Bid.Face.Value, outcome.MatchingDice, outcome.IsSpotOn ? "spot-on" : "liar") : null,
            state.Winner, session.Message);
    }

    private static uint NextSeed() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
    private static string DisplayName(string id) => id switch { "you" => "You", "bot-1" => "Amber Badger", "bot-2" => "Copper Finch", "bot-3" => "Silver Otter", _ => id };

    private sealed class LiarsDiceSession(string userId, LiarsDiceMatchState state, ulong botSeed)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public object SyncRoot { get; } = new();
        public string UserId { get; } = userId;
        public LiarsDiceMatchState State { get; set; } = state;
        public ulong BotSeed { get; } = botSeed;
        public int BotActionVersion { get; set; }
        public string Message { get; set; } = "Place a bid or wait for the opening bot turn.";
    }
}

public sealed class LiarsDiceAccessException() : InvalidOperationException("That Liar's Dice table is not available.");
