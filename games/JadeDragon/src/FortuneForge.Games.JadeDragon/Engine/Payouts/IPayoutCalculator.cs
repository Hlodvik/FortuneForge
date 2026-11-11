using FortuneForge.Games.JadeDragon.Models;

namespace FortuneForge.Games.JadeDragon.Payouts;

public interface IPayoutCalculator
{
    SpinPayout Calculate(
        IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game,
        PaytableDefinition paytable,
        long wagerPoints);
}
