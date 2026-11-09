using FortuneForge.Games.EmptySlots.Models;

namespace FortuneForge.Games.EmptySlots.Payouts;

public interface IPayoutCalculator
{
    SpinPayout Calculate(
        IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game,
        PaytableDefinition paytable,
        long wagerPoints);
}
