using System.Collections.Concurrent;
using FortuneForge.Server.Cards.Bots;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Cards.TexasHoldem.Bots;

internal sealed class TexasHoldemBotPracticeService : ICardBotGameRunner
{
    private readonly object gate = new();
    private readonly List<HumanFirstBotQueue> queues = [];
    private readonly ConcurrentDictionary<string, HumanFirstBotQueue> sessionQueues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TexasHoldemState> sessionMatches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TexasHoldemState> matches = new(StringComparer.Ordinal);
    private readonly BotIdentityFactory identities;
    private readonly TexasHoldemBotAgent agent;
    private readonly IBotTurnLeaseStore leases;
    private readonly CardBotPlatformOptions platform;
    private readonly string ownerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    public TexasHoldemBotPracticeService(
        BotIdentityFactory identities,
        TexasHoldemBotAgent agent,
        IBotTurnLeaseStore leases,
        IOptions<CardBotPlatformOptions> options)
    {
        this.identities = identities;
        this.agent = agent;
        this.leases = leases;
        platform = options.Value;
    }

    public string Game => CardBotGames.TexasHoldem;

    public TexasHoldemBotPracticeResponse Join(
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
                    TimeSpan.FromMilliseconds(platform.TexasHoldem.HumanWaitGraceMilliseconds),
                    platform.TexasHoldem.MaxBotsPerMatch,
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

    public TexasHoldemBotPracticeResponse Get(string sessionId, DateTime nowUtc)
    {
        EnsureEnabled();
        ValidateSessionId(sessionId);
        if (sessionMatches.TryGetValue(sessionId, out var match)) return Project(match, sessionId);
        if (!sessionQueues.TryGetValue(sessionId, out var queue))
            throw new KeyNotFoundException("No Hold'em bot-practice session was found.");
        StartIfReady(queue, nowUtc);
        return sessionMatches.TryGetValue(sessionId, out match)
            ? Project(match, sessionId)
            : QueueResponse(queue);
    }

    public TexasHoldemBotPracticeResponse Command(
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
        if (!platform.TexasHoldem.Enabled) return;
        HumanFirstBotQueue[] queueSnapshot;
        lock (gate) queueSnapshot = queues.ToArray();
        foreach (var queue in queueSnapshot) StartIfReady(queue, nowUtc);

        foreach (var state in matches.Values.Distinct())
        {
            TexasHoldemPlayer? bot;
            int version;
            lock (state)
            {
                if (state.Status != "active" || state.NextBotActionAtUtc > nowUtc) continue;
                bot = state.Players.ElementAtOrDefault(state.ActiveSeat);
                version = state.Version;
                if (bot?.Seat.IsBot != true) continue;
            }
            var lease = await leases.TryAcquireAsync(
                new BotTurnKey(Game, state.MatchId, bot.Seat.SeatId, version),
                ownerId,
                nowUtc,
                TimeSpan.FromSeconds(platform.TurnLeaseSeconds),
                cancellationToken);
            if (lease is null) continue;

            TexasHoldemBotDecision decision;
            lock (state)
            {
                var call = Math.Max(0, state.CurrentBet - bot.CommittedRound);
                decision = agent.Choose(
                    new TexasHoldemBotObservation(
                        bot.HoleCards,
                        state.Community,
                        Pot(state),
                        call,
                        bot.Stack,
                        state.CurrentBet + state.MinimumRaise,
                        bot.CommittedRound + bot.Stack,
                        LegalActions(state, bot)),
                    bot.Seat.SkillLevel!.Value,
                    state.Seed,
                    version,
                    platform.TexasHoldem);
            }
            var arguments = decision.RaiseTo is { } amount
                ? new Dictionary<string, string> { ["raiseTo"] = amount.ToString() }
                : null;
            var request = new CardBotCommandRequest(
                decision.Action,
                version,
                $"bot_{state.MatchId}_{bot.Seat.SeatId}_{version}".Replace('-', '_'),
                arguments);
            _ = Submit(bot.Seat.SeatId, state.MatchId, request, nowUtc, requireHuman: false);
            await leases.CompleteAsync(lease, state.Version, nowUtc, cancellationToken);
        }
    }

    private TexasHoldemBotPracticeResponse Submit(
        string actorSeatId,
        string matchId,
        CardBotCommandRequest request,
        DateTime nowUtc,
        bool requireHuman)
    {
        EnsureEnabled();
        CardBotContractValidation.ValidateCommand(request);
        if (!matches.TryGetValue(matchId, out var state))
            throw new KeyNotFoundException("The Hold'em practice match was not found.");
        lock (state)
        {
            var player = state.Players.SingleOrDefault(value => value.Seat.SeatId == actorSeatId)
                ?? throw new UnauthorizedAccessException("This seat does not belong to the practice match.");
            if (requireHuman && player.Seat.IsBot)
                throw new UnauthorizedAccessException("Bot seats cannot be controlled by a player request.");
            var scopedKey = $"{actorSeatId}:{request.IdempotencyKey}";
            if (state.IdempotencyKeys.Contains(scopedKey)) return Project(state, actorSeatId);
            if (request.ExpectedVersion != state.Version)
                throw new InvalidOperationException("The Hold'em table changed; reconnect before acting.");
            if (state.Status != "active" || state.ActiveSeat != player.Seat.Seat)
                throw new InvalidOperationException("It is not this seat's turn.");

            var action = request.Type.Trim().ToLowerInvariant();
            if (!LegalActions(state, player).Contains(action))
                throw new ArgumentException("That Hold'em action is not legal now.");
            var publicData = Apply(state, player, action, request.Arguments, nowUtc);
            state.IdempotencyKeys.Add(scopedKey);
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
                publicData));
            Advance(state, nowUtc);
            return Project(state, actorSeatId);
        }
    }

    private static IReadOnlyDictionary<string, string> Apply(
        TexasHoldemState state,
        TexasHoldemPlayer player,
        string action,
        IReadOnlyDictionary<string, string>? arguments,
        DateTime nowUtc)
    {
        var call = Math.Max(0, state.CurrentBet - player.CommittedRound);
        if (action == HoldemActions.Fold)
        {
            player.Status = "folded";
            player.HasActed = true;
            return new Dictionary<string, string> { ["seat"] = player.Seat.Seat.ToString() };
        }
        if (action == HoldemActions.Check)
        {
            player.HasActed = true;
            return new Dictionary<string, string> { ["seat"] = player.Seat.Seat.ToString() };
        }
        if (action == HoldemActions.Call)
        {
            var paid = Commit(player, Math.Min(call, player.Stack));
            player.HasActed = true;
            return new Dictionary<string, string>
            {
                ["seat"] = player.Seat.Seat.ToString(),
                ["amount"] = paid.ToString()
            };
        }

        if (arguments is null || !arguments.TryGetValue("raiseTo", out var text) || !int.TryParse(text, out var raiseTo))
            throw new ArgumentException("A raise requires an integer raiseTo argument.");
        var maximum = player.CommittedRound + player.Stack;
        var minimum = state.CurrentBet + state.MinimumRaise;
        if (raiseTo <= state.CurrentBet || (raiseTo < minimum && raiseTo != maximum) || raiseTo > maximum)
            throw new ArgumentOutOfRangeException(nameof(arguments), $"RaiseTo must be at least {minimum}, or this seat's all-in {maximum}.");
        var previousBet = state.CurrentBet;
        var paidRaise = Commit(player, raiseTo - player.CommittedRound);
        state.CurrentBet = player.CommittedRound;
        if (state.CurrentBet - previousBet >= state.MinimumRaise) state.MinimumRaise = state.CurrentBet - previousBet;
        foreach (var other in state.Players.Where(other =>
            other != player && other.Status == "active")) other.HasActed = false;
        player.HasActed = true;
        return new Dictionary<string, string>
        {
            ["seat"] = player.Seat.Seat.ToString(),
            ["amount"] = paidRaise.ToString(),
            ["raiseTo"] = state.CurrentBet.ToString()
        };
    }

    private void Advance(TexasHoldemState state, DateTime nowUtc)
    {
        var contenders = state.Players.Where(player => player.Status != "folded").ToArray();
        if (contenders.Length == 1)
        {
            AwardUncontested(state, contenders[0]);
            return;
        }
        if (BettingRoundComplete(state))
        {
            if (state.Street == "river" || contenders.All(player => player.Status == "all-in"))
            {
                while (state.Community.Count < 5) DealNextStreet(state);
                SettleShowdown(state);
                return;
            }
            DealNextStreet(state);
            foreach (var player in state.Players)
            {
                player.CommittedRound = 0;
                player.HasActed = player.Status != "active";
            }
            state.CurrentBet = 0;
            state.MinimumRaise = 20;
            state.ActiveSeat = NextActive(state, state.DealerSeat);
        }
        else state.ActiveSeat = NextActive(state, state.ActiveSeat);
        state.NextBotActionAtUtc = nowUtc.AddMilliseconds(ThinkDelay(state, state.Players[state.ActiveSeat]));
    }

    private static bool BettingRoundComplete(TexasHoldemState state) =>
        state.Players.Where(player => player.Status == "active")
            .All(player => player.HasActed && player.CommittedRound == state.CurrentBet);

    private static int NextActive(TexasHoldemState state, int afterSeat)
    {
        for (var offset = 1; offset <= state.Players.Count; offset++)
        {
            var index = (afterSeat + offset) % state.Players.Count;
            if (state.Players[index].Status == "active") return index;
        }
        throw new InvalidOperationException("No active Hold'em seat remains.");
    }

    private static int Commit(TexasHoldemPlayer player, int amount)
    {
        if (amount < 0 || amount > player.Stack) throw new ArgumentOutOfRangeException(nameof(amount));
        player.Stack -= amount;
        player.CommittedRound += amount;
        player.CommittedHand += amount;
        if (player.Stack == 0) player.Status = "all-in";
        return amount;
    }

    private static void DealNextStreet(TexasHoldemState state)
    {
        state.NextCardIndex++; // burn, kept server-private
        var count = state.Community.Count == 0 ? 3 : 1;
        for (var index = 0; index < count; index++) state.Community.Add(state.Deck[state.NextCardIndex++]);
        state.Street = state.Community.Count switch { 3 => "flop", 4 => "turn", 5 => "river", _ => state.Street };
    }

    private static void AwardUncontested(TexasHoldemState state, TexasHoldemPlayer winner)
    {
        var pot = Pot(state);
        winner.Stack += pot;
        winner.Payout += pot;
        state.Status = "completed";
        state.Street = "settled";
        state.ActiveSeat = -1;
    }

    private static void SettleShowdown(TexasHoldemState state)
    {
        foreach (var player in state.Players.Where(player => player.Status != "folded"))
            player.RevealAtShowdown = true;
        var levels = state.Players.Select(player => player.CommittedHand).Where(value => value > 0).Distinct().Order().ToArray();
        var previous = 0;
        foreach (var level in levels)
        {
            var contributors = state.Players.Where(player => player.CommittedHand >= level).ToArray();
            var pot = (level - previous) * contributors.Length;
            previous = level;
            var eligible = contributors.Where(player => player.Status != "folded").ToArray();
            if (eligible.Length == 0) continue;
            var values = eligible.Select(player => (Player: player, Value: TexasHoldemRules.Evaluate(
                player.HoleCards.Concat(state.Community).ToArray()))).ToArray();
            var best = values.Max(item => item.Value.Score);
            var winners = values.Where(item => item.Value.Score == best).Select(item => item.Player).OrderBy(item => item.Seat.Seat).ToArray();
            var share = pot / winners.Length;
            var remainder = pot % winners.Length;
            for (var index = 0; index < winners.Length; index++)
            {
                var payout = share + (index < remainder ? 1 : 0);
                winners[index].Stack += payout;
                winners[index].Payout += payout;
            }
        }
        state.Status = "completed";
        state.Street = "showdown";
        state.ActiveSeat = -1;
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

    private TexasHoldemState Deal(HumanFirstBotQueue queue, IReadOnlyList<QueueSeat> seats, DateTime nowUtc)
    {
        var deck = TexasHoldemRules.CreateDeck(queue.Seed);
        var players = seats.Select(seat => new TexasHoldemPlayer { Seat = seat, HoleCards = [] }).ToList();
        var index = 0;
        for (var round = 0; round < 2; round++)
            foreach (var player in players) player.HoleCards.Add(deck[index++]);
        var smallBlind = players.Count == 2 ? 0 : 1;
        var bigBlind = players.Count == 2 ? 1 : 2;
        Commit(players[smallBlind], 10);
        Commit(players[bigBlind], 20);
        var active = players.Count == 2 ? 0 : 3 % players.Count;
        var state = new TexasHoldemState
        {
            MatchId = Guid.NewGuid().ToString("N"),
            Seed = queue.Seed,
            Deck = deck,
            NextCardIndex = index,
            Players = players,
            Community = [],
            Events = [],
            IdempotencyKeys = new HashSet<string>(StringComparer.Ordinal),
            DealerSeat = 0,
            ActiveSeat = active,
            CurrentBet = 20,
            StartedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            NextBotActionAtUtc = nowUtc
        };
        state.NextBotActionAtUtc = nowUtc.AddMilliseconds(ThinkDelay(state, players[active]));
        return state;
    }

    private int ThinkDelay(TexasHoldemState state, TexasHoldemPlayer player)
    {
        if (!player.Seat.IsBot) return 0;
        var random = new DeterministicBotRandom(state.Seed, $"holdem-delay:{state.Version}:{player.Seat.SeatId}");
        return platform.TexasHoldem.MinimumThinkDelayMilliseconds + random.Next(
            platform.TexasHoldem.MaximumThinkDelayMilliseconds - platform.TexasHoldem.MinimumThinkDelayMilliseconds + 1);
    }

    private static IReadOnlyList<string> LegalActions(TexasHoldemState state, TexasHoldemPlayer player)
    {
        if (state.Status != "active" || player.Status != "active") return [];
        var call = Math.Max(0, state.CurrentBet - player.CommittedRound);
        var actions = new List<string>();
        if (call > 0)
        {
            actions.Add(HoldemActions.Fold);
            actions.Add(HoldemActions.Call);
        }
        else actions.Add(HoldemActions.Check);
        if (player.Stack > call && player.CommittedRound + player.Stack > state.CurrentBet)
            actions.Add(HoldemActions.Raise);
        return actions;
    }

    private static int Pot(TexasHoldemState state) => state.Players.Sum(player => player.CommittedHand);

    private static TexasHoldemBotPracticeResponse QueueResponse(HumanFirstBotQueue queue) =>
        new(CardBotContract.Version, "queue", queue.ToDto(), null);

    private static TexasHoldemBotPracticeResponse Project(TexasHoldemState state, string viewerSeatId)
    {
        lock (state)
        {
            var viewer = state.Players.SingleOrDefault(player => player.Seat.SeatId == viewerSeatId)
                ?? throw new UnauthorizedAccessException("This seat does not belong to the practice match.");
            var seats = state.Players.Select(player =>
            {
                var reveal = player == viewer || player.RevealAtShowdown;
                var cards = player.HoleCards.Select(card => reveal ? Card(card) : new HoldemCardDto(null, null, true)).ToArray();
                var handName = player.RevealAtShowdown
                    ? TexasHoldemRules.Evaluate(player.HoleCards.Concat(state.Community).ToArray()).Name
                    : null;
                return new TexasHoldemPracticeSeatDto(
                    CardBotPublicProjection.Seat(player.Seat, player.Status),
                    cards,
                    player.Stack,
                    player.CommittedHand,
                    player.Status,
                    handName,
                    player.Payout);
            }).ToArray();
            return new TexasHoldemBotPracticeResponse(
                CardBotContract.Version,
                "match",
                null,
                new TexasHoldemPracticeTableDto(
                    state.MatchId,
                    state.Status,
                    state.Street,
                    state.Version,
                    state.DealerSeat,
                    state.ActiveSeat,
                    Pot(state),
                    state.CurrentBet,
                    state.CurrentBet + state.MinimumRaise,
                    state.Community.Select(Card).ToArray(),
                    seats,
                    state.Events.Select(item => CardBotPublicProjection.Event(
                        item,
                        state.Players.Select(value => value.Seat).ToArray())).ToArray(),
                    viewer.Seat.Seat == state.ActiveSeat ? LegalActions(state, viewer) : [],
                    state.StartedAtUtc,
                    state.UpdatedAtUtc));
        }
    }

    private static HoldemCardDto Card(string card)
    {
        var separator = card.IndexOf('|');
        return new HoldemCardDto(card[..separator], card[(separator + 1)..], false);
    }

    private void EnsureEnabled()
    {
        if (!platform.TexasHoldem.Enabled) throw new CardBotFeatureDisabledException(Game);
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
