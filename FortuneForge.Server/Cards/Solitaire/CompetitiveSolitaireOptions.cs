namespace FortuneForge.Server.Cards.Solitaire;

public sealed class CompetitiveSolitaireOptions
{
    public const string SectionName = "Cards:CompetitiveSolitaire";

    // The same human-first queue used by the other card tables can fill open
    // seats with persisted managed players after its brief grace period.
    public bool AllowSingleHumanBotFill { get; set; }

    internal static readonly TimeSpan HumanQueueGracePeriod = TimeSpan.FromSeconds(5);
}
