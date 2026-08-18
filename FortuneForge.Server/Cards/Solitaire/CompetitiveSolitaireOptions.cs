namespace FortuneForge.Server.Cards.Solitaire;

public sealed class CompetitiveSolitaireOptions
{
    public const string SectionName = "Cards:CompetitiveSolitaire";

    // Testing-only. Production remains all-human unless this is explicitly enabled.
    public bool AllowSingleHumanBotFill { get; set; }
}
