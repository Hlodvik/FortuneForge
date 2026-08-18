using System.Collections.Concurrent;
using FortuneForge.Server.Cards.Bots;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Cards.Blackjack.Bots;

internal sealed class BlackjackBotPracticeService : ICardBotGameRunner
{
    private readonly object gate = new();
    private readonly List<HumanFirstBotQueue> queues = [];
    private readonly ConcurrentDictionary<string, HumanFirstBotQueue> sessionQueues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BlackjackPracticeState> sessionMatches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BlackjackPracticeState> matches = new(StringComparer.Ordinal);
    private readonly BotIdentityFactory identities;
    private readonly BlackjackBotAgent agent;
    private readonly IBotTurnLeaseStore leases;
    private readonly CardBotPlatformOptions platform;
    private readonly string ownerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    public BlackjackBotPracticeService(
        BotIdentityFactory identities,
        BlackjackBotAgent agent,
        IBotTurnLeaseStore leases,
        IOptions<CardBotPlatformOptions> options)
    {
        this.identities = identities;
        this.agent = agent;
        this.leases = leases;
        platform = options.Value;
    }

    public string Game => CardBotGames.Blackjack;

    public BlackjackBotPracticeResponse Join(
        string sessionId,
        string displayName,
        CardBotJoinRequest request,
        DateTime nowUtc)
    {
        EnsureEnabled();
        ValidateSessionId(sessionId);
        CardBotContractValidation.ValidateJoin(request, 2, 6);
        if (sessionMatches.TryGetValue(sessionId, out var current)) return Project(current, sessionId);
        if (sessionQueues.TryGetValue(sessionId, out var existing)) return QueueResponse(existing);

        lock (gate)
        {
            var queue = queues.FirstOrDefault(candidate =>
                candidate.RequiredPlayers == request.PlayerCount &&
                candidate.BotSkillLevel == request.Difficulty &&
                candidate.SeatCount < candidate.RequiredPlayers);
            if (queue is null)
            {
                queue = new HumanFirstBotQueue(
                    Guid.NewGuid().ToString("N"),
                    Game,
                    request.PlayerCount,
                    nowUtc,
                    TimeSpan.FromMilliseconds(platform.Blackjack.HumanWaitGraceMilliseconds),
                    platform.Blackjack.MaxBotsPerMatch,
                    request.Difficulty,
                    CardBotSeed.Create());
                queues.Add(queue);
            }
            queue.AddHuman(sessionId, displayName, nowUtc);
            sessionQueues[sessionId] = queue;
            StartIfReady(queue, nowUtc);
            return sessionMatches.TryGetValue(sessionId, out current)
                ? Project(current, sessionId)
                : QueueResponse(queue);
        }
    }

    public BlackjackBotPracticeResponse Get(string sessionId, DateTime nowUtc)
    {
        EnsureEnabled();
        ValidateSessionId(sessionId);
        if (sessionMatches.TryGetValue(sessionId, out var match)) return Project(match, sessionId);
        if (!sessionQueues.TryGetValue(sessionId, out var queue))
            throw new KeyNotFoundException("No Blackjack bot-practice session was found.");
        StartIfReady(queue, nowUtc);
        return sessionMatches.TryGetValue(sessionId, out match)
            ? Project(match, sessionId)
            : QueueResponse(queue);
    }

    public BlackjackBotPracticeResponse Command(
        string sessionId,
        string matchId,
        CardBotCommandRequest request,
        DateTime nowUtc)
    {
        EnsureHumanOwnsMatch(sessionId, matchId);
        return Submit(sessionId, matchId, request, nowUtc, requireHuman: true);
    }

    public async Task SweepAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (!platform.Blackjack.Enabled) return;
        HumanFirstBotQueue[] queueSnapshot;
        lock (gate) queueSnapshot = queues.ToArray();
        foreach (var queue in queueSnapshot) StartIfReady(queue, nowUtc);

        foreach (var state in matches.Values.Distinct())
        {
            BlackjackPracticePlayer? bot;
            int version;
            lock (state)
            {
                if (state.Status != "active" || state.NextBotActionAtUtc > nowUtc) continue;
                bot = state.Players.ElementAtOrDefault(state.ActiveSeat);
                version = state.Version;
                if (bot?.Seat.IsBot != true) continue;
            }
            var key = new BotTurnKey(Game, state.MatchId, bot.Seat.SeatId, version);
            var lease = await leases.TryAcquireAsync(
                key,
                ownerId,
                nowUtc,
                TimeSpan.FromSeconds(platform.TurnLeaseSeconds),
                cancellationToken);
            if (lease is null) continue;

            string action;
            lock (state)
            {
                var legal = LegalActions(state, bot);
                action = agent.Choose(
                    new BlackjackBotObservation(bot.Cards, state.DealerCards[0], legal),
                    bot.Seat.SkillLevel!.Value,
                    state.Seed,
                    state.Version,
                    platform.Blackjack);
            }
            var request = new CardBotCommandRequest(
                action,
                version,
                $"bot_{state.MatchId}_{bot.Seat.SeatId}_{version}".Replace('-', '_'));
            _ = Submit(bot.Seat.SeatId, state.MatchId, request, nowUtc, requireHuman: false);
            await leases.CompleteAsync(lease, state.Version, nowUtc, cancellationToken);
        }
    }

    private BlackjackBotPracticeResponse Submit(
        string actorSeatId,
        string matchId,
        CardBotCommandRequest request,
        DateTime nowUtc,
        bool requireHuman)
    {
        EnsureEnabled();
        CardBotContractValidation.ValidateCommand(request);
        if (!matches.TryGetValue(matchId, out var state))
            throw new KeyNotFoundException("The Blackjack practice match was not found.");

        lock (state)
        {
            var player = state.Players.SingleOrDefault(value => value.Seat.SeatId == actorSeatId)
                ?? throw new UnauthorizedAccessException("This seat does not belong to the practice match.");
            if (requireHuman && player.Seat.IsBot)
                throw new UnauthorizedAccessException("Bot seats cannot be controlled by a player request.");
            if (state.IdempotencyKeys.Contains(request.IdempotencyKey)) return Project(state, actorSeatId);
            if (request.ExpectedVersion != state.Version)
                throw new InvalidOperationException("The Blackjack table changed; reconnect before acting.");
            if (state.Status != "active" || state.ActiveSeat != player.Seat.Seat)
                throw new InvalidOperationException("It is not this seat's turn.");

            var action = request.Type.Trim().ToLowerInvariant();
            var legal = LegalActions(state, player);
            if (!legal.Contains(action)) throw new ArgumentException("That Blackjack action is not legal now.");
            Apply(state, player, action, nowUtc);
            state.IdempotencyKeys.Add(request.IdempotencyKey);
            state.Version++;
            state.UpdatedAtUtc = nowUtc;
            state.Events.Add(new CardBotDomainEvent(
                CardBotContract.Version,
                Game,
                matchId,
                state.Version,
                action,
                actorSeatId,
                nowUtc,
                new Dictionary<string, string> { ["seat"] = player.Seat.Seat.ToString() }));
            Advance(state, nowUtc);
            return Project(state, actorSeatId);
        }
    }

    private void StartIfReady(HumanFirstBotQueue queue, DateTime nowUtc)
    {
        var seats = queue.TryStart(nowUtc, identities);
        if (seats is null) return;
        lock (gate)
        {
            if (seats.Any(seat => sessionMatches.ContainsKey(seat.SeatId))) return;
            var state = Deal(queue, seats, nowUtc);
            matches[state.MatchId] = state;
            foreach (var seat in seats)
            {
                sessionQueues.TryRemove(seat.SeatId, out _);
                if (!seat.IsBot) sessionMatches[seat.SeatId] = state;
            }
            queues.Remove(queue);
        }
    }

    private BlackjackPracticeState Deal(
        HumanFirstBotQueue queue,
        IReadOnlyList<QueueSeat> seats,
        DateTime nowUtc)
    {
        var deck = CreateDeck(queue.Seed);
        var players = seats.Select(seat => new BlackjackPracticePlayer
        {
            Seat = seat,
            Cards = []
        }).ToList();
        var index = 0;
        for (var round = 0; round < 2; round++)
        {
            foreach (var player in players) player.Cards.Add(deck[index++]);
        }
        var dealer = new List<string> { deck[index++], deck[index++] };
        foreach (var player in players.Where(player => BlackjackRules.Score(player.Cards).Blackjack))
            player.Status = "stood";
        var state = new BlackjackPracticeState
        {
            MatchId = Guid.NewGuid().ToString("N"),
            Seed = queue.Seed,
            Deck = deck,
            NextCardIndex = index,
            Players = players,
            DealerCards = dealer,
            ActiveSeat = -1,
            StartedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            NextBotActionAtUtc = nowUtc,
            Events = [],
            IdempotencyKeys = new HashSet<string>(StringComparer.Ordinal)
        };
        Advance(state, nowUtc);
        return state;
    }

    private void Apply(BlackjackPracticeState state, BlackjackPracticePlayer player, string action, DateTime nowUtc)
    {
        if (action == BlackjackActions.Hit)
        {
            player.Cards.Add(Draw(state));
            var score = BlackjackRules.Score(player.Cards);
            if (score.Bust) player.Status = "bust";
            else if (score.Score == 21) player.Status = "stood";
        }
        else if (action == BlackjackActions.Double)
        {
            player.VirtualWagerUnits *= 2;
            player.Cards.Add(Draw(state));
            player.Status = BlackjackRules.Score(player.Cards).Bust ? "bust" : "stood";
        }
        else player.Status = "stood";
    }

    private void Advance(BlackjackPracticeState state, DateTime nowUtc)
    {
        var next = Enumerable.Range(1, state.Players.Count)
            .Select(offset => (state.ActiveSeat + offset) % state.Players.Count)
            .FirstOrDefault(index => state.Players[index].Status == "playing", -1);
        if (next >= 0)
        {
            state.ActiveSeat = next;
            state.NextBotActionAtUtc = nowUtc.AddMilliseconds(ThinkDelay(state, state.Players[next]));
            return;
        }

        while (BlackjackRules.Score(state.DealerCards).Score < 17) state.DealerCards.Add(Draw(state));
        var dealer = BlackjackRules.Score(state.DealerCards);
        foreach (var player in state.Players)
        {
            var hand = BlackjackRules.Score(player.Cards);
            player.Outcome = hand.Bust ? BlackjackOutcomes.PlayerBust
                : hand.Blackjack && !dealer.Blackjack ? BlackjackOutcomes.PlayerBlackjack
                : dealer.Blackjack && !hand.Blackjack ? BlackjackOutcomes.DealerBlackjack
                : dealer.Bust || hand.Score > dealer.Score ? BlackjackOutcomes.PlayerWin
                : hand.Score == dealer.Score ? BlackjackOutcomes.Push
                : BlackjackOutcomes.DealerWin;
            player.Status = "finished";
        }
        state.Status = "completed";
        state.ActiveSeat = -1;
    }

    private int ThinkDelay(BlackjackPracticeState state, BlackjackPracticePlayer player)
    {
        if (!player.Seat.IsBot) return 0;
        var options = platform.Blackjack;
        var random = new DeterministicBotRandom(state.Seed, $"blackjack-delay:{state.Version}:{player.Seat.SeatId}");
        return options.MinimumThinkDelayMilliseconds +
            random.Next(options.MaximumThinkDelayMilliseconds - options.MinimumThinkDelayMilliseconds + 1);
    }

    private static IReadOnlyList<string> LegalActions(BlackjackPracticeState state, BlackjackPracticePlayer player)
    {
        if (state.Status != "active" || player.Status != "playing") return [];
        return player.Cards.Count == 2
            ? [BlackjackActions.Hit, BlackjackActions.Stand, BlackjackActions.Double]
            : [BlackjackActions.Hit, BlackjackActions.Stand];
    }

    private static string Draw(BlackjackPracticeState state) =>
        state.NextCardIndex >= state.Deck.Count
            ? throw new InvalidOperationException("The Blackjack practice deck ran out of cards.")
            : state.Deck[state.NextCardIndex++];

    private static IReadOnlyList<string> CreateDeck(ulong seed)
    {
        var suits = new[] { "clubs", "diamonds", "hearts", "spades" };
        var ranks = new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
        var deck = suits.SelectMany(suit => ranks.Select(rank => $"{rank}|{suit}")).ToArray();
        var random = new DeterministicBotRandom(seed, "blackjack-deck-v1");
        for (var index = deck.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (deck[index], deck[swap]) = (deck[swap], deck[index]);
        }
        return deck;
    }

    private static BlackjackBotPracticeResponse QueueResponse(HumanFirstBotQueue queue) =>
        new(CardBotContract.Version, BlackjackBotPracticeKinds.Queue, queue.ToDto(), null);

    private static BlackjackBotPracticeResponse Project(BlackjackPracticeState state, string viewerSeatId)
    {
        lock (state)
        {
            var revealDealer = state.Status == "completed";
            var dealerCards = state.DealerCards.Select((card, index) =>
                !revealDealer && index == 1
                    ? new BlackjackCardResponse(null, null, true)
                    : Card(card)).ToArray();
            var dealerValue = revealDealer ? BlackjackRules.Score(state.DealerCards) : null;
            var seats = state.Players.Select(player =>
            {
                var value = BlackjackRules.Score(player.Cards);
                return new BlackjackPracticeSeatDto(
                    CardBotPublicProjection.Seat(player.Seat, player.Status),
                    new BlackjackPracticeHandDto(
                        player.Cards.Select(Card).ToArray(),
                        value.Score,
                        value.Soft,
                        value.Blackjack,
                        value.Bust),
                    player.Outcome,
                    player.VirtualWagerUnits);
            }).ToArray();
            var viewer = state.Players.SingleOrDefault(player => player.Seat.SeatId == viewerSeatId);
            return new BlackjackBotPracticeResponse(
                CardBotContract.Version,
                BlackjackBotPracticeKinds.Match,
                null,
                new BlackjackPracticeTableDto(
                    state.MatchId,
                    state.Status,
                    state.Version,
                    state.ActiveSeat,
                    new BlackjackPracticeHandDto(
                        dealerCards,
                        dealerValue?.Score,
                        dealerValue?.Soft ?? false,
                        dealerValue?.Blackjack ?? false,
                        dealerValue?.Bust ?? false),
                    seats,
                    state.Events.Select(item => CardBotPublicProjection.Event(
                        item,
                        state.Players.Select(value => value.Seat).ToArray())).ToArray(),
                    viewer is not null && viewer.Seat.Seat == state.ActiveSeat
                        ? LegalActions(state, viewer)
                        : [],
                    state.StartedAtUtc,
                    state.UpdatedAtUtc));
        }
    }

    private static BlackjackCardResponse Card(string code)
    {
        var card = BlackjackRules.ParseCard(code);
        return new BlackjackCardResponse(card.Rank, card.Suit, false);
    }

    private void EnsureEnabled()
    {
        if (!platform.Blackjack.Enabled)
            throw new CardBotFeatureDisabledException(Game);
    }

    private void EnsureHumanOwnsMatch(string sessionId, string matchId)
    {
        ValidateSessionId(sessionId);
        if (!sessionMatches.TryGetValue(sessionId, out var state) ||
            !string.Equals(state.MatchId, matchId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("This practice session does not own the requested match.");
        }
    }

    private static void ValidateSessionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 16 or > 128)
            throw new ArgumentException("X-Practice-Session-Id must contain 16 to 128 characters.");
    }
}
