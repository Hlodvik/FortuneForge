namespace FortuneForge.Server.Cards.Bots;

internal sealed class HumanFirstBotQueue(
    string queueId,
    string game,
    int requiredPlayers,
    DateTime createdAtUtc,
    TimeSpan humanGrace,
    int maxBots,
    int botSkillLevel,
    ulong seed)
{
    private readonly List<QueueSeat> humans = [];
    private readonly List<QueueSeat> pendingBots = [];
    private bool started;

    public string QueueId { get; } = queueId;
    public string Game { get; } = game;
    public int RequiredPlayers { get; } = requiredPlayers;
    public int BotSkillLevel { get; } = botSkillLevel;
    public DateTime GraceEndsAtUtc { get; } = createdAtUtc.Add(humanGrace);
    public ulong Seed { get; } = seed;

    public QueueSeat AddHuman(string sessionId, string displayName, DateTime nowUtc)
    {
        lock (humans)
        {
            if (started) throw new InvalidOperationException("The match has already started.");
            var existing = humans.SingleOrDefault(seat => seat.SeatId == sessionId);
            if (existing is not null) return existing;
            if (humans.Count >= RequiredPlayers) throw new InvalidOperationException("The queue is full.");

            // Pending bot seats are reservations only. A human always claims capacity first.
            if (pendingBots.Count > 0) pendingBots.RemoveAt(pendingBots.Count - 1);
            var seat = new QueueSeat(
                sessionId,
                CardBotPublicIds.NewSeatId(),
                displayName,
                false,
                null,
                humans.Count,
                nowUtc);
            humans.Add(seat);
            for (var index = 0; index < pendingBots.Count; index++)
                pendingBots[index] = pendingBots[index] with { Seat = humans.Count + index };
            return seat;
        }
    }

    public IReadOnlyList<QueueSeat> ReserveBots(DateTime nowUtc, BotIdentityFactory identities)
    {
        lock (humans)
        {
            if (started || nowUtc < GraceEndsAtUtc || humans.Count >= RequiredPlayers)
                return Seats();

            var needed = Math.Min(RequiredPlayers - humans.Count, maxBots);
            var bots = identities.Create(Seed, needed, BotSkillLevel);
            pendingBots.Clear();
            pendingBots.AddRange(bots.Select((bot, index) => new QueueSeat(
                bot.SeatId,
                CardBotPublicIds.NewSeatId(),
                bot.DisplayName,
                true,
                bot.SkillLevel,
                humans.Count + index,
                nowUtc)));
            return Seats();
        }
    }

    public IReadOnlyList<QueueSeat>? TryStart(DateTime nowUtc, BotIdentityFactory identities)
    {
        lock (humans)
        {
            if (started) return null;
            if (humans.Count < RequiredPlayers && nowUtc < GraceEndsAtUtc) return null;
            _ = ReserveBots(nowUtc, identities);
            if (humans.Count + pendingBots.Count != RequiredPlayers) return null;

            // Start and seat assignment happen under the same lock, so no bot can displace
            // a human between reservation and the atomic transition.
            started = true;
            return Seats().Select((seat, index) => seat with { Seat = index }).ToArray();
        }
    }

    public CardBotQueueDto ToDto()
    {
        lock (humans)
        {
            return new CardBotQueueDto(
                QueueId,
                Game,
                RequiredPlayers,
                Seats().Select(seat => CardBotPublicProjection.Seat(
                    seat,
                    started ? "playing" : "queued")).ToArray());
        }
    }

    public int SeatCount
    {
        get
        {
            lock (humans) return humans.Count + pendingBots.Count;
        }
    }

    private IReadOnlyList<QueueSeat> Seats() => [.. humans, .. pendingBots];
}

internal static class CardBotPublicIds
{
    public static string NewSeatId() => $"seat_{Guid.NewGuid():N}";
}

internal static class CardBotPublicProjection
{
    public static CardBotSeatDto Seat(QueueSeat seat, string status) => new(
        seat.PublicSeatId,
        seat.DisplayName,
        seat.Seat,
        status);

    public static CardBotPublicEventDto Event(
        CardBotDomainEvent domainEvent,
        IReadOnlyCollection<QueueSeat> seats)
    {
        var actor = seats.Single(seat => seat.SeatId == domainEvent.ActorSeatId);
        return new CardBotPublicEventDto(
            domainEvent.ContractVersion,
            domainEvent.Game,
            domainEvent.MatchId,
            domainEvent.Version,
            domainEvent.Type,
            actor.PublicSeatId,
            actor.DisplayName,
            domainEvent.OccurredAtUtc,
            domainEvent.PublicData);
    }
}

internal sealed record QueueSeat(
    string SeatId,
    string PublicSeatId,
    string DisplayName,
    bool IsBot,
    int? SkillLevel,
    int Seat,
    DateTime JoinedAtUtc);
