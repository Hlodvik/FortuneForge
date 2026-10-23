using System.Collections.Immutable;

namespace FortuneForge.Games.HorseFlight;

public enum HorseFlightInput
{
    None,
    Jump,
    RightClick,
}

public enum HorseFlightPhase
{
    Running,
    ObstacleCollision,
    Fell,
}

public enum HorseFlightEventType
{
    Advanced,
    Jumped,
    Landed,
    Progressed,
    Slid,
    FastFell,
    ObstacleCollision,
    Fell,
}

public enum HorseFlightObstacleKind
{
    Crate,
    CrateCluster,
    Pit,
    Well,
    Fence,
    Carriage,
    OilSpill,
    Boulder,
    FallenLog,
    Dog,
    BatFlock,
    LassoThrower,
    NetTower,
    Skeleton,
    RollingBarrel,
    MarketCart,
    HangingSign,
    ThornBramble,
    SpikeBarricade,
    Cactus,
    StormCloud,
}

public enum HorseFlightBiome
{
    Mountain,
    CastleTown,
    DarkForest,
    Battleground,
    Haunted,
    Desert,
    Sky,
}

public enum HorseFlightBiomePhase
{
    Main,
    Transition,
}

public readonly record struct HorseFlightBiomeSection(
    HorseFlightBiomePhase Phase,
    HorseFlightBiome Biome,
    HorseFlightBiome From,
    HorseFlightBiome To,
    double Blend);

public sealed record HorseFlightPlatform(
    int Id,
    double X,
    double Width,
    double Y,
    bool Cleared);

public sealed record HorseFlightObstacle(
    int Id,
    HorseFlightObstacleKind Kind,
    int PlatformId,
    double X,
    double Width,
    double Height,
    double Elevation = 0,
    double AdditionalSpeed = 0,
    int? KnockedDownAt = null);

public sealed record HorseFlightState(
    int Width,
    int Height,
    uint Seed,
    uint RandomState,
    double HorseY,
    double HorseVelocity,
    bool IsGrounded,
    int? StandingPlatformId,
    ImmutableArray<HorseFlightPlatform> Platforms,
    ImmutableArray<HorseFlightObstacle> Obstacles,
    int NextPlatformId,
    int NextObstacleId,
    double Distance,
    int PlatformsCleared,
    int Score,
    int BestScore,
    int Tick,
    HorseFlightPhase Phase)
{
    public int Level => 1 + (Score / HorseFlightEngine.ScoresPerLevel);

    /// <summary>How many jumps the horse may still make before landing.</summary>
    public int JumpsRemaining { get; init; } = HorseFlightEngine.MaximumJumps;

    /// <summary>How many ticks remain in the low slide dash.</summary>
    public int SlideTicksRemaining { get; init; }

    /// <summary>The obstacle that ended the run, or <see langword="null"/> while running or after a fall.</summary>
    public HorseFlightObstacleKind? TerminalObstacleKind { get; init; }

    /// <summary>Generation rules used for this run. Version 1 preserves the original mountain-to-haunted route.</summary>
    public int RulesVersion { get; init; } = HorseFlightEngine.CurrentRulesVersion;
}

public sealed record HorseFlightTransition(
    HorseFlightState State,
    HorseFlightEventType Event,
    int ScoreGained,
    double DistanceGained);

public sealed record HorseFlightPlatformSnapshot(
    int Id,
    double X,
    double Width,
    double Y);

public sealed record HorseFlightObstacleSnapshot(
    int Id,
    HorseFlightObstacleKind Kind,
    double X,
    double Width,
    double Y,
    double Height);

public sealed record HorseFlightSnapshot(
    int Width,
    int Height,
    uint Seed,
    int Tick,
    double HorseX,
    double HorseY,
    double HorseVelocity,
    bool IsGrounded,
    double Distance,
    int PlatformsCleared,
    int Score,
    int BestScore,
    int Level,
    HorseFlightPhase Phase,
    ImmutableArray<HorseFlightPlatformSnapshot> Platforms,
    ImmutableArray<HorseFlightObstacleSnapshot> Obstacles)
{
    /// <summary>The obstacle that ended the run, or <see langword="null"/> when no obstacle caused the terminal state.</summary>
    public HorseFlightObstacleKind? TerminalObstacleKind { get; init; }
}

/// <summary>
/// A compact, deterministic terminal-run replay. Input ticks are zero-based step indexes.
/// </summary>
public sealed record HorseFlightReplay(
    int TotalTicks,
    ImmutableArray<int> JumpTicks)
{
    public ImmutableArray<int> RightClickTicks { get; init; } = ImmutableArray<int>.Empty;
    public int RulesVersion { get; init; } = HorseFlightEngine.CurrentRulesVersion;
}

public sealed record HorseFlightReplayResult(
    HorseFlightSnapshot Snapshot,
    ImmutableArray<int> JumpTicks)
{
    public ImmutableArray<int> RightClickTicks { get; init; } = ImmutableArray<int>.Empty;
}
