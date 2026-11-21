using System.Security.Cryptography;

namespace FortuneForge.Games.Abstractions;

public interface IPuzzleSession<out TState>
{
    Guid Id { get; }
    Guid RunId { get; }
    string UserId { get; }
    TState State { get; }
    int UndoDepth { get; }
    int CompletionEpisode { get; }
    long EventVersion { get; }
    DateTime StartedAtUtc { get; }
}

public static class PuzzleSessionIdentity
{
    public static Guid CreateDurableId(DateTime nowUtc) => Guid.CreateVersion7(new DateTimeOffset(nowUtc));
    public static bool IsDurableId(Guid id) => id.Version == 7;
    public static uint SelectSeed(uint? seed) => seed is > 0 ? seed.Value : (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    public static string EpisodeId(Guid runId, int episode) => $"{runId:N}-episode-{episode}";
}
