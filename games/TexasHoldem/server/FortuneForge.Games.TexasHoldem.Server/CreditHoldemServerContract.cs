using Microsoft.Extensions.Configuration;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

public sealed class CreditHoldemOptions
{
    public const string SectionName = "Cards:CreditTexasHoldem";
    public bool AllowSingleHumanBotFill { get; set; }
}

internal static class CreditHoldemServerContract
{
internal static CreditHoldemStatusResponse Status(IConfiguration configuration) => new(
        true,
        CreditHoldemMoney.MinimumStartPlayers,
        CreditHoldemMoney.MaximumSeats,
        configuration.GetValue($"{CreditHoldemOptions.SectionName}:AllowSingleHumanBotFill", false) ? 1 : 2,
        CreditHoldemMoney.ToCredits(CreditHoldemMoney.SmallBlindCents),
        CreditHoldemMoney.ToCredits(CreditHoldemMoney.BigBlindCents),
        checked((int)CreditHoldemEngine.ActionDuration.TotalSeconds),
        checked((int)CreditHoldemEngine.MatchDuration.TotalSeconds),
        CreditHoldemTableRules.All.Select(rule => rule.Public).ToArray());
}
