using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Snake;
namespace FortuneForge.Server.Games.Snake;
internal static class SnakeReplayAudit
{
    internal const string Version = FortuneForge.Games.Snake.SnakeReplayAudit.Version;
    internal static IReadOnlyList<RoundAuditFinding> Inspect(RoundAuditSummary summary, string payloadJson) =>
        FortuneForge.Games.Snake.SnakeReplayAudit.Inspect(summary, payloadJson);
    internal static IReadOnlyList<RoundAuditFinding> Check(SnakeState recorded, int initialBest, IReadOnlyList<string> actions, bool abandoned) =>
        FortuneForge.Games.Snake.SnakeReplayAudit.Check(recorded, initialBest, actions, abandoned);
}
