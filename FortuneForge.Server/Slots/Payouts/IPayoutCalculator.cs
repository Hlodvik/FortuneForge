using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Payouts;

public interface IPayoutCalculator
{
    SpinPayout Calculate(
        IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game,
        PaytableDefinition paytable,
        long wagerPoints);
}
