using FortuneForge.Games.EmptySlots.Models;

namespace FortuneForge.Games.EmptySlots.Evaluation;

public interface ICombinationEvaluator
{
    IReadOnlyList<PaylineEvaluation> Evaluate(
        IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game,
        SymbolSetDefinition symbolSet);
}
