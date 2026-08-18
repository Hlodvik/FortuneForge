using System.Collections.Concurrent;
using FortuneForge.Server.Cards.Bots;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Cards.Solitaire.Bots;

internal sealed class SolitaireBotPracticeService : ICardBotGameRunner
{
    private readonly object gate = new();
    private readonly List<HumanFirstBotQueue> queues = [];
    private readonly ConcurrentDictionary<string, HumanFirstBotQueue> sessionQueues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SolitaireBotMatchState> sessionMatches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SolitaireBotMatchState> matches = new(StringComparer.Ordinal);
    private readonly BotIdentityFactory identities;
    private readonly SolitaireBotAgent agent;
    private readonly IBotTurnLeaseStore leases;
    private readonly CardBotPlatformOptions platform;
    private readonly string ownerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    public SolitaireBotPracticeService(
        BotIdentityFactory identities,
        SolitaireBotAgent agent,
        IBotTurnLeaseStore leases,
        IOptions<CardBotPlatformOptions> options)
    {
        this.identities = identities;
        this.agent = agent;
        this.leases = leases;
        platform = options.Value;
    }

    public string Game => CardBotGames.Solitaire;

    public SolitaireBotPracticeResponse Join(
        string sessionId,
        string displayName,
        CardBotJoinRequest request,
        DateTime nowUtc)
    {
        EnsureEnabled();
        CardBotContractValidation.ValidateJoin(request, 2, 8);
        ValidateSessionId(sessionId);
        if (sessionMatches.TryGetValue(sessionId, out var current)) return Project(current, sessionId, nowUtc);
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
                    TimeSpan.FromMilliseconds(platform.Solitaire.HumanWaitGraceMilliseconds),
                    platform.Solitaire.MaxBotsPerMatch,
                    request.Difficulty,
                    CardBotSeed.Create());
                queues.Add(queue);
            }
            queue.AddHuman(sessionId, displayName, nowUtc);
            sessionQueues[sessionId] = queue;
            StartIfReady(queue, nowUtc);
            return sessionMatches.TryGetValue(sessionId, out current)
                ? Project(current, sessionId, nowUtc)
                : QueueResponse(queue);
        }
    }

    public SolitaireBotPracticeResponse Get(string sessionId, DateTime nowUtc)
    {
        EnsureEnabled();
        ValidateSessionId(sessionId);
        if (sessionMatches.TryGetValue(sessionId, out var match))
        {
            Expire(match, nowUtc);
            return Project(match, sessionId, nowUtc);
        }
        if (!sessionQueues.TryGetValue(sessionId, out var queue))
            throw new KeyNotFoundException("No Solitaire bot-practice session was found.");
        StartIfReady(queue, nowUtc);
        return sessionMatches.TryGetValue(sessionId, out match)
            ? Project(match, sessionId, nowUtc)
            : QueueResponse(queue);
    }

    public SolitaireBotPracticeResponse Command(
        string sessionId,
        string matchId,
        CardBotCommandRequest request,
        DateTime nowUtc)
    {
        EnsureHumanOwnsMatch(sessionId, matchId);
        CardBotContractValidation.ValidateCommand(request);
        var command = ToSolitaireCommand(request);
        return Submit(sessionId, matchId, command, request.IdempotencyKey, nowUtc, requireHuman: true);
    }

    public async Task SweepAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (!platform.Solitaire.Enabled) return;
        HumanFirstBotQueue[] queueSnapshot;
        lock (gate) queueSnapshot = queues.ToArray();
        foreach (var queue in queueSnapshot) StartIfReady(queue, nowUtc);

        foreach (var match in matches.Values.Distinct())
        {
            Expire(match, nowUtc);
            SolitaireBotPlayerState[] bots;
            lock (match)
            {
                if (match.CompletedAtUtc is not null) continue;
                bots = match.Players.Where(player =>
                    player.Seat.IsBot && player.Status == "playing" && player.NextActionAtUtc <= nowUtc).ToArray();
            }
            foreach (var bot in bots)
            {
                var version = bot.Version;
                var lease = await leases.TryAcquireAsync(
                    new BotTurnKey(Game, match.MatchId, bot.Seat.SeatId, version),
                    ownerId,
                    nowUtc,
                    TimeSpan.FromSeconds(platform.TurnLeaseSeconds),
                    cancellationToken);
                if (lease is null) continue;
                SolitaireCommandRequest command;
                lock (match)
                {
                    if (bot.Version != version || bot.Status != "playing") continue;
                    command = agent.Choose(
                        bot.Game,
                        bot.Version,
                        bot.Seat.SkillLevel!.Value,
                        match.DecisionSeed,
                        platform.Solitaire);
                }
                var key = $"bot_{match.MatchId}_{bot.Seat.SeatId}_{version}".Replace('-', '_');
                _ = Submit(bot.Seat.SeatId, match.MatchId, command, key, nowUtc, requireHuman: false);
                await leases.CompleteAsync(lease, bot.Version, nowUtc, cancellationToken);
            }
        }
    }

    private SolitaireBotPracticeResponse Submit(
        string actorSeatId,
        string matchId,
        SolitaireCommandRequest command,
        string idempotencyKey,
        DateTime nowUtc,
        bool requireHuman)
    {
        EnsureEnabled();
        if (!matches.TryGetValue(matchId, out var match))
            throw new KeyNotFoundException("The Solitaire practice match was not found.");
        lock (match)
        {
            Expire(match, nowUtc);
            var player = match.Players.SingleOrDefault(value => value.Seat.SeatId == actorSeatId)
                ?? throw new UnauthorizedAccessException("This seat does not belong to the practice match.");
            if (requireHuman && player.Seat.IsBot)
                throw new UnauthorizedAccessException("Bot boards cannot be controlled or requested by another seat.");
            if (player.IdempotencyKeys.Contains(idempotencyKey)) return Project(match, actorSeatId, nowUtc);
            if (player.Status != "playing") throw new InvalidOperationException("This Solitaire board is finished.");
            if (command.ExpectedVersion != player.Version)
                throw new InvalidOperationException("The Solitaire board changed; reconnect before acting.");

            player.Game = SolitaireEngine.Apply(player.Game, command);
            player.Version++;
            player.IdempotencyKeys.Add(idempotencyKey);
            if (SolitaireEngine.IsWon(player.Game)) player.Status = "finished";
            player.NextActionAtUtc = nowUtc.AddMilliseconds(ThinkDelay(match, player));
            CompleteIfAllFinished(match, nowUtc);
            return Project(match, actorSeatId, nowUtc);
        }
    }

    private void StartIfReady(HumanFirstBotQueue queue, DateTime nowUtc)
    {
        var seats = queue.TryStart(nowUtc, identities);
        if (seats is null) return;
        lock (gate)
        {
            if (seats.Any(seat => sessionMatches.ContainsKey(seat.SeatId))) return;
            var dealSeed = unchecked((uint)queue.Seed);
            var match = new SolitaireBotMatchState
            {
                MatchId = Guid.NewGuid().ToString("N"),
                DecisionSeed = queue.Seed,
                StartedAtUtc = nowUtc,
                DeadlineAtUtc = nowUtc.Add(SolitaireCompetitionRules.MatchDuration),
                Players = seats.Select(seat => new SolitaireBotPlayerState
                {
                    Seat = seat,
                    Game = SolitaireEngine.CreateGame(dealSeed),
                    NextActionAtUtc = nowUtc.AddMilliseconds(InitialThinkDelay(queue.Seed, seat)),
                    IdempotencyKeys = new HashSet<string>(StringComparer.Ordinal)
                }).ToList()
            };
            matches[match.MatchId] = match;
            foreach (var seat in seats)
            {
                sessionQueues.TryRemove(seat.SeatId, out _);
                if (!seat.IsBot) sessionMatches[seat.SeatId] = match;
            }
            queues.Remove(queue);
        }
    }

    private void Expire(SolitaireBotMatchState match, DateTime nowUtc)
    {
        lock (match)
        {
            if (match.CompletedAtUtc is not null || nowUtc < match.DeadlineAtUtc) return;
            foreach (var player in match.Players.Where(player => player.Status == "playing"))
                player.Status = "expired";
            match.CompletedAtUtc = match.DeadlineAtUtc;
        }
    }

    private static void CompleteIfAllFinished(SolitaireBotMatchState match, DateTime nowUtc)
    {
        if (match.Players.All(player => player.Status != "playing")) match.CompletedAtUtc = nowUtc;
    }

    private int InitialThinkDelay(ulong seed, QueueSeat seat)
    {
        if (!seat.IsBot) return 0;
        var random = new DeterministicBotRandom(seed, $"solitaire-initial-delay:{seat.SeatId}");
        return platform.Solitaire.MinimumThinkDelayMilliseconds + random.Next(
            platform.Solitaire.MaximumThinkDelayMilliseconds - platform.Solitaire.MinimumThinkDelayMilliseconds + 1);
    }

    private int ThinkDelay(SolitaireBotMatchState match, SolitaireBotPlayerState player)
    {
        if (!player.Seat.IsBot) return 0;
        var random = new DeterministicBotRandom(
            match.DecisionSeed,
            $"solitaire-delay:{player.Seat.SeatId}:{player.Version}");
        return platform.Solitaire.MinimumThinkDelayMilliseconds + random.Next(
            platform.Solitaire.MaximumThinkDelayMilliseconds - platform.Solitaire.MinimumThinkDelayMilliseconds + 1);
    }

    private static SolitaireCommandRequest ToSolitaireCommand(CardBotCommandRequest request)
    {
        var arguments = request.Arguments ?? new Dictionary<string, string>();
        int? Number(string key) => arguments.TryGetValue(key, out var value) && int.TryParse(value, out var number)
            ? number
            : null;
        SolitairePileReference? Pile(string prefix) =>
            arguments.TryGetValue($"{prefix}Zone", out var zone) && Number($"{prefix}Index") is { } index
                ? new SolitairePileReference(zone, index)
                : null;
        return new SolitaireCommandRequest(
            request.Type.Trim().ToLowerInvariant(),
            request.ExpectedVersion,
            Pile("from"),
            Number("startIndex"),
            Pile("to"),
            Number("column"));
    }

    private static SolitaireBotPracticeResponse QueueResponse(HumanFirstBotQueue queue) =>
        new(CardBotContract.Version, SolitaireSessionKinds.Queued, queue.ToDto(), null, null);

    private static SolitaireBotPracticeResponse Project(
        SolitaireBotMatchState match,
        string viewerSeatId,
        DateTime nowUtc)
    {
        lock (match)
        {
            var viewer = match.Players.SingleOrDefault(player => player.Seat.SeatId == viewerSeatId)
                ?? throw new UnauthorizedAccessException("This seat does not belong to the practice match.");
            if (viewer.Seat.IsBot && !viewerSeatId.StartsWith("bot-", StringComparison.Ordinal))
                throw new UnauthorizedAccessException("A bot board is private.");
            if (match.CompletedAtUtc is { } completed)
            {
                var ranked = match.Players
                    .OrderByDescending(player => player.Game.Score)
                    .ThenBy(player => player.Game.Moves)
                    .ThenBy(player => player.Seat.SeatId, StringComparer.Ordinal)
                    .Select((player, index) => new SolitaireBotPracticeStandingDto(
                        index + 1,
                        Seat(player),
                        player.Game.Score,
                        player.Game.Moves,
                        player.Status))
                    .ToArray();
                return new SolitaireBotPracticeResponse(
                    CardBotContract.Version,
                    SolitaireSessionKinds.Result,
                    null,
                    null,
                    new SolitaireBotPracticeResultDto(match.MatchId, match.StartedAtUtc, completed, ranked));
            }

            return new SolitaireBotPracticeResponse(
                CardBotContract.Version,
                SolitaireSessionKinds.Match,
                null,
                new SolitaireBotPracticeMatchDto(
                    match.MatchId,
                    viewer.Version,
                    match.StartedAtUtc,
                    match.DeadlineAtUtc,
                    Math.Max(0, (long)(match.DeadlineAtUtc - nowUtc).TotalMilliseconds),
                    SolitaireEngine.ToResponse(viewer.Game),
                    match.Players.Select(Seat).ToArray()),
                null);
        }
    }

    private static CardBotSeatDto Seat(SolitaireBotPlayerState player) =>
        CardBotPublicProjection.Seat(player.Seat, player.Status);

    private void EnsureEnabled()
    {
        if (!platform.Solitaire.Enabled) throw new CardBotFeatureDisabledException(Game);
    }

    private void EnsureHumanOwnsMatch(string sessionId, string matchId)
    {
        ValidateSessionId(sessionId);
        if (!sessionMatches.TryGetValue(sessionId, out var match) ||
            !string.Equals(match.MatchId, matchId, StringComparison.Ordinal))
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
