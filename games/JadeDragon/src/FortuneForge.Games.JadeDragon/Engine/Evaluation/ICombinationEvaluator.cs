using FortuneForge.Games.JadeDragon.Models;

namespace FortuneForge.Games.JadeDragon.Evaluation;

public interface ICombinationEvaluator
{
    IReadOnlyList<PaylineEvaluation> Evaluate(
        IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game,
        SymbolSetDefinition symbolSet);
}
