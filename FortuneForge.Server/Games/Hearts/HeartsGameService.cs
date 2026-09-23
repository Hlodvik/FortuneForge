using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Server.Games.Hearts;

/// <summary>Owns short-lived, no-credit Hearts tables. Domain rules stay in FortuneForge.Games.</summary>
public sealed class HeartsGameService
{
    private const PlayerSeat HumanSeat = PlayerSeat.North;
    private static readonly IReadOnlySet<PlayerSeat> BotSeats = Enum.GetValues<PlayerSeat>()
        .Where(seat => seat != HumanSeat)
        .ToHashSet();
    private readonly ConcurrentDictionary<Guid, HeartsSession> sessions = new();
    private readonly HeartsBotAgent bot = new();
    private readonly CardBotGameOptions botOptions = new() { Enabled = true };

    public HeartsMatchResponse Start(string userId, StartHeartsMatchRequest request)
    {
        var targetScore = request.TargetScore ?? HeartsMatchEngine.DefaultTargetScore;
        var seed = request.Seed ?? NextSeed();
        var skillLevel = ParseDifficulty(request.Difficulty);
        var session = new HeartsSession(userId, HeartsMatchEngine.Start(seed, targetScore), CardBotSeed.Create(), skillLevel);
        if (!sessions.TryAdd(session.Id, session)) throw new InvalidOperationException("Could not open a Hearts table.");
        return ToResponse(session);
    }

    public HeartsMatchResponse Get(string userId, Guid matchId) => Read(userId, matchId, session => ToResponse(session));

    public HeartsMatchResponse Pass(string userId, Guid matchId, IReadOnlyList<string> cards) =>
        Change(userId, matchId, session =>
        {
            var parsed = cards.Select(CardCode.Parse).ToArray();
            ApplyHumanAction(session, new PassHeartsCards(HumanSeat, parsed));
        });

    public HeartsMatchResponse PlayCard(string userId, Guid matchId, string card) =>
        Change(userId, matchId, session => ApplyHumanAction(session, new PlayHeartsCard(HumanSeat, CardCode.Parse(card))));

    public HeartsMatchResponse Advance(string userId, Guid matchId) =>
        Change(userId, matchId, AdvanceBotTurn);

    public HeartsMatchResponse NextRound(string userId, Guid matchId, uint? seed) =>
        Change(userId, matchId, session =>
        {
            session.State = HeartsMatchEngine.StartNextRound(session.State, seed ?? NextSeed());
            session.LastCompletedTrick = null;
            session.Message = $"Round {session.State.RoundNumber} is ready. Pass three cards {DirectionName(session.State.PassDirection)}.";
            PrepareBotTurn(session);
        });

    private HeartsMatchResponse Change(string userId, Guid matchId, Action<HeartsSession> action) =>
        Read(userId, matchId, session =>
        {
            action(session);
            return ToResponse(session);
        });

    private T Read<T>(string userId, Guid matchId, Func<HeartsSession, T> action)
    {
        if (!sessions.TryGetValue(matchId, out var session) || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
            throw new HeartsAccessException();
        lock (session.SyncRoot) return action(session);
    }

    private void ApplyHumanAction(HeartsSession session, HeartsCommand command)
    {
        if (session.State.Round.Phase == HeartsPhase.Playing && session.State.Round.Turn != HumanSeat)
            throw new HeartsRuleException($"It is {session.State.Round.Turn}'s turn. The bots are still moving.");
        var completedBefore = session.State.Round.CompletedTricks.Count;
        var transition = HeartsMatchEngine.Apply(session.State, command);
        session.State = transition.State;
        RememberCompletedTrick(session, completedBefore);
        session.Message = transition.Message;
        PrepareBotTurn(session);
    }

    private void AdvanceBotTurn(HeartsSession session)
    {
        if (!session.BotThinking) return;
        var round = session.State.Round;
        if (round.Phase == HeartsPhase.Passing && !round.SubmittedPasses.Any(pass => pass.Seat == HumanSeat))
        {
            session.BotThinking = false;
            return;
        }
        if (round.Phase == HeartsPhase.Playing && !BotSeats.Contains(round.Turn))
        {
            session.BotThinking = false;
            return;
        }
        var completedBefore = round.CompletedTricks.Count;
        var transition = HeartsMatchEngine.AdvanceBotTurn(
            session.State, BotSeats, bot, session.SkillLevel, session.BotSeed,
            session.BotActionVersion++, botOptions, HumanSeat);
        session.State = transition.State;
        RememberCompletedTrick(session, completedBefore);
        session.Message = transition.Message;
        session.BotThinking = false;
        PrepareBotTurn(session);
    }

    private static void PrepareBotTurn(HeartsSession session)
    {
        var round = session.State.Round;
        if (session.State.Winner is not null || round.Phase == HeartsPhase.Complete)
        {
            session.BotThinking = false;
            return;
        }
        if (round.Phase == HeartsPhase.Passing)
        {
            session.BotThinking = round.SubmittedPasses.Any(pass => pass.Seat == HumanSeat) &&
                round.SubmittedPasses.Count < TrickTakingRules.PlayerCount;
        }
        else
        {
            session.BotThinking = BotSeats.Contains(round.Turn);
        }
        if (session.BotThinking)
        {
            var seat = round.Phase == HeartsPhase.Playing ? round.Turn : NextPassingBot(round);
            session.Message = $"{SeatName(seat)} is thinking…";
        }
    }

    private static PlayerSeat NextPassingBot(HeartsState round) => Enum.GetValues<PlayerSeat>()
        .First(seat => BotSeats.Contains(seat) && !round.SubmittedPasses.Any(pass => pass.Seat == seat));

    private static void RememberCompletedTrick(HeartsSession session, int completedBefore)
    {
        if (session.State.Round.CompletedTricks.Count > completedBefore)
            session.LastCompletedTrick = session.State.Round.CompletedTricks[^1];
    }

    private static HeartsMatchResponse ToResponse(HeartsSession session)
    {
        var state = session.State;
        var round = state.Round;
        var scores = round.Scores.ToDictionary(score => score.Seat, score => score.Points);
        var legal = round.Phase == HeartsPhase.Playing && round.Turn == HumanSeat
            ? HeartsEngine.LegalCards(round, HumanSeat)
            : [];
        return new HeartsMatchResponse(
            session.Id, state.TargetScore, state.RoundNumber, PhaseName(round.Phase),
            DirectionName(state.PassDirection), SeatName(round.Turn), SeatName(HumanSeat),
            round.Turn == HumanSeat && state.Winner is null, session.BotThinking, round.HeartsBroken,
            round.SubmittedPasses.Select(pass => SeatName(pass.Seat)).ToArray(),
            Enum.GetValues<PlayerSeat>().Select(seat => new HeartsPlayerResponse(
                SeatName(seat), round.HandFor(seat).Count, scores[seat], state.Score.For(seat),
                round.SubmittedPasses.Any(pass => pass.Seat == seat))).ToArray(),
            round.HandFor(HumanSeat).Select(ToCard).ToArray(), legal.Select(ToCard).ToArray(),
            new HeartsTrickResponse(SeatName(round.CurrentTrick.Leader), round.CurrentTrick.Plays.Select(play =>
                new HeartsTrickPlayResponse(SeatName(play.Seat), ToCard(play.Card))).ToArray()),
            session.LastCompletedTrick is { } recent ? new HeartsRecentTrickResponse(
                recent.Number, SeatName(recent.Leader), SeatName(recent.Winner),
                recent.Plays.Sum(play => HeartsRules.PointValue(play.Card)), recent.Plays.Select(play =>
                    new HeartsTrickPlayResponse(SeatName(play.Seat), ToCard(play.Card))).ToArray()) : null,
            round.CompletedTricks.Select(trick => new HeartsCompletedTrickResponse(
                trick.Number, SeatName(trick.Leader), SeatName(trick.Winner),
                trick.Plays.Sum(play => HeartsRules.PointValue(play.Card)))).ToArray(),
            new HeartsScoreResponse(state.Score.North, state.Score.East, state.Score.South, state.Score.West),
            new HeartsScoreResponse(scores[PlayerSeat.North], scores[PlayerSeat.East], scores[PlayerSeat.South], scores[PlayerSeat.West]),
            DifficultyName(session.SkillLevel), state.Winner is { } winner ? SeatName(winner) : null, session.Message);
    }

    private static HeartsCardResponse ToCard(PlayingCard card) => new(card.Code, RankName(card.Rank), SuitName(card.Suit), CardLabel(card));
    private static uint NextSeed() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
    private static int ParseDifficulty(string? difficulty) => difficulty?.Trim().ToLowerInvariant() switch
    {
        null or "" or "standard" => CardBotSkillLevels.Average,
        "relaxed" => CardBotSkillLevels.Poor,
        "sharp" => CardBotSkillLevels.Strong,
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty), "Hearts difficulty must be relaxed, standard, or sharp."),
    };
    private static string DifficultyName(int skillLevel) => skillLevel switch
    {
        CardBotSkillLevels.Poor => "relaxed",
        CardBotSkillLevels.Average => "standard",
        CardBotSkillLevels.Strong => "sharp",
        _ => throw new ArgumentOutOfRangeException(nameof(skillLevel)),
    };
    private static string PhaseName(HeartsPhase phase) => phase switch { HeartsPhase.Passing => "passing", HeartsPhase.Playing => "playing", HeartsPhase.Complete => "complete", _ => throw new ArgumentOutOfRangeException(nameof(phase)) };
    private static string DirectionName(HeartsPassDirection value) => value switch { HeartsPassDirection.Left => "left", HeartsPassDirection.Right => "right", HeartsPassDirection.Across => "across", HeartsPassDirection.Hold => "hold", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string SeatName(PlayerSeat seat) => seat.ToString().ToLowerInvariant();
    private static string RankName(CardRank rank) => rank switch { CardRank.Ace => "ace", CardRank.Jack => "jack", CardRank.Queen => "queen", CardRank.King => "king", _ => ((int)rank).ToString(System.Globalization.CultureInfo.InvariantCulture) };
    private static string SuitName(CardSuit suit) => suit.ToString().ToLowerInvariant();
    private static string CardLabel(PlayingCard card) => $"{card.Code.Split('|')[0]}{card.Suit switch { CardSuit.Clubs => "♣", CardSuit.Diamonds => "♦", CardSuit.Hearts => "♥", CardSuit.Spades => "♠", _ => "?" }}";

    private sealed class HeartsSession(string userId, HeartsMatchState state, ulong botSeed, int skillLevel)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public object SyncRoot { get; } = new();
        public string UserId { get; } = userId;
        public HeartsMatchState State { get; set; } = state;
        public ulong BotSeed { get; } = botSeed;
        public int SkillLevel { get; } = skillLevel;
        public int BotActionVersion { get; set; }
        public bool BotThinking { get; set; }
        public CompletedTrick? LastCompletedTrick { get; set; }
        public string Message { get; set; } = "Pass three cards to the left.";
    }
}

public sealed class HeartsAccessException() : InvalidOperationException("That Hearts table is not available.");
