using FortuneForge.Server.Matchmaking.QueueBotScheduling;

namespace FortuneForge.Server.Cards.Bots;

public sealed class CardBotPlatformOptions
{
    public const string SectionName = "Cards:Bots";

    public CardBotGameOptions Blackjack { get; set; } = new();
    public CardBotGameOptions Solitaire { get; set; } = new();
    public CardBotGameOptions TexasHoldem { get; set; } = new();
    public CardBotQueueSchedulerOptions QueueScheduler { get; set; } = new();
    public int WorkerIntervalMilliseconds { get; set; } = 250;
    public int TurnLeaseSeconds { get; set; } = 15;
}

public sealed class CardBotQueueSchedulerOptions
{
    public int MaximumHumanWaitMilliseconds { get; set; } = 30_000;
    public int ArrivalSampleWindowSeconds { get; set; } = 60;
}

internal static class CardBotOptionValidation
{
    public static void Validate(CardBotPlatformOptions options)
    {
        if (options.WorkerIntervalMilliseconds is < 50 or > 10_000)
            throw new InvalidOperationException("Cards:Bots:WorkerIntervalMilliseconds must be between 50 and 10000.");
        if (options.TurnLeaseSeconds is < 2 or > 120)
            throw new InvalidOperationException("Cards:Bots:TurnLeaseSeconds must be between 2 and 120.");
        if (options.QueueScheduler.MaximumHumanWaitMilliseconds is < 1_000 or > 300_000)
            throw new InvalidOperationException("Cards:Bots:QueueScheduler:MaximumHumanWaitMilliseconds must be between 1000 and 300000.");
        if (options.QueueScheduler.ArrivalSampleWindowSeconds is < 10 or > 1_800)
            throw new InvalidOperationException("Cards:Bots:QueueScheduler:ArrivalSampleWindowSeconds must be between 10 and 1800.");

        ValidateGame(options.Blackjack, "Blackjack", 6, options.QueueScheduler.MaximumHumanWaitMilliseconds);
        ValidateGame(options.Solitaire, "Solitaire", 7, options.QueueScheduler.MaximumHumanWaitMilliseconds);
        ValidateGame(options.TexasHoldem, "TexasHoldem", 5, options.QueueScheduler.MaximumHumanWaitMilliseconds);
    }

    private static void ValidateGame(
        CardBotGameOptions value,
        string name,
        int maximumBots,
        int maximumHumanWaitMilliseconds)
    {
        if (value.MaxBotsPerMatch < 0 || value.MaxBotsPerMatch > maximumBots)
            throw new InvalidOperationException($"Cards:Bots:{name}:MaxBotsPerMatch must be between 0 and {maximumBots}.");
        if (value.HumanWaitGraceMilliseconds is < 0 or > 300_000)
            throw new InvalidOperationException($"Cards:Bots:{name}:HumanWaitGraceMilliseconds must be between 0 and 300000.");
        if (value.HumanWaitGraceMilliseconds > maximumHumanWaitMilliseconds)
            throw new InvalidOperationException($"Cards:Bots:{name}:HumanWaitGraceMilliseconds cannot exceed the queue maximum wait.");
        if (value.MinimumThinkDelayMilliseconds is < 100 or > 30_000 ||
            value.MaximumThinkDelayMilliseconds < value.MinimumThinkDelayMilliseconds ||
            value.MaximumThinkDelayMilliseconds > 60_000)
        {
            throw new InvalidOperationException(
                $"Cards:Bots:{name} think delays must be bounded from 100 through 60000 milliseconds.");
        }
        if (value.ThreeStarErrorRate is < 0 or > 0.5 || value.FourStarImperfectionRate is <= 0 or > 0.15)
            throw new InvalidOperationException($"Cards:Bots:{name} error rates are outside their safe bounds.");
    }
}

internal static class CardBotQueueFillPolicies
{
    public static QueueBotFillPolicy Create(
        CardBotPlatformOptions platform,
        CardBotGameOptions game) => new(
            MinimumHumanPlayers: 1,
            MaximumBots: game.MaxBotsPerMatch,
            InitialHumanOnlyWait: TimeSpan.FromMilliseconds(game.HumanWaitGraceMilliseconds),
            MaximumHumanWait: TimeSpan.FromMilliseconds(platform.QueueScheduler.MaximumHumanWaitMilliseconds),
            ArrivalSampleWindow: TimeSpan.FromSeconds(platform.QueueScheduler.ArrivalSampleWindowSeconds));
}
