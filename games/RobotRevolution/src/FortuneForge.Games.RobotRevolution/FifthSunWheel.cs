using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RobotRevolution;

public static class FifthSunWheel
{
    public const string MathVersion = "fifth-sun-wheel-v1";
    private static readonly SunwheelVocabulary Vocabulary = new(2, "fifth-sun", MathVersion,
        "SUNSTONE", "WILD", ["JAGUAR", "EAGLE", "SERPENT"]);

    public static SunstoneEclipseFeature Resolve(IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints, bool isEclipseSpin) => SunstoneEclipse.Resolve(reels, wagerPoints, isEclipseSpin, Vocabulary);

    internal static ClassicFeaturePayout CalculatePayout(IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints, decimal payoutMultiplier, bool isEclipseSpin) =>
        SunstoneEclipse.CalculatePayout(reels, wagerPoints, payoutMultiplier, isEclipseSpin, Vocabulary, false);
}
