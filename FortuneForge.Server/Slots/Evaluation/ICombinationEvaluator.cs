using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Evaluation;

public interface ICombinationEvaluator
{
    IReadOnlyList<PaylineEvaluation> Evaluate(
        IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game,
        SymbolSetDefinition symbolSet);
}
