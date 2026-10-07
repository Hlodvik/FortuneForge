using System.Collections.Immutable;

namespace FortuneForge.Games.Asteroids;

public enum AsteroidsAction
{
    Tick,
    RotateLeft,
    RotateRight,
    Thrust,
    Fire,
}

[Flags]
public enum AsteroidsControl
{
    None = 0,
    Thrust = 1,
    TurnLeft = 2,
    TurnRight = 4,
    Fire = 8,
}

public enum AsteroidsPhase
{
    Playing,
    GameOver,
}

public enum AsteroidsEventType
{
    Started,
    Ticked,
    Rotated,
    Thrusted,
    Fired,
    Hit,
    Damaged,
    WaveCleared,
    PowerUpCollected,
    NoOp,
    GameOver,
}

public enum AsteroidSize
{
    Tiny,
    Small,
    Medium,
    Large,
    Huge,
}

public enum AsteroidsPowerUpType
{
    Shield,
    RapidFire,
    ExtraLife,
}

public readonly record struct AsteroidsVector(double X, double Y)
{
    public double Length => Math.Sqrt((X * X) + (Y * Y));

    public static AsteroidsVector operator +(AsteroidsVector left, AsteroidsVector right) => new(left.X + right.X, left.Y + right.Y);
    public static AsteroidsVector operator -(AsteroidsVector left, AsteroidsVector right) => new(left.X - right.X, left.Y - right.Y);
    public static AsteroidsVector operator *(AsteroidsVector value, double factor) => new(value.X * factor, value.Y * factor);
}

public sealed record AsteroidsShip(
    AsteroidsVector Position,
    AsteroidsVector Velocity,
    double Angle,
    int InvulnerabilityTicks,
    int ThrustTicks = 0);

public sealed record Asteroid(
    int Id,
    AsteroidsVector Position,
    AsteroidsVector Velocity,
    double Radius,
    AsteroidSize Size,
    int HitPoints = 1,
    int SpriteVariant = 0);

public sealed record AsteroidsBullet(
    int Id,
    AsteroidsVector Position,
    AsteroidsVector Velocity,
    int RemainingTicks);

public sealed record AsteroidsPowerUp(
    int Id,
    AsteroidsVector Position,
    AsteroidsVector Velocity,
    AsteroidsPowerUpType Type,
    int RemainingTicks);

public sealed record AsteroidsState(
    int Width,
    int Height,
    uint Seed,
    uint RandomState,
    AsteroidsShip Ship,
    ImmutableArray<Asteroid> Asteroids,
    ImmutableArray<AsteroidsBullet> Bullets,
    int NextEntityId,
    int FireCooldownTicks,
    int Score,
    int BestScore,
    int Lives,
    int Wave,
    int Tick,
    AsteroidsPhase Phase,
    ImmutableArray<AsteroidsPowerUp> PowerUps = default,
    int RapidFireTicks = 0)
{
    public int AsteroidCount => Asteroids.Length;
}

public sealed record AsteroidsTransition(
    AsteroidsState State,
    AsteroidsEventType Event,
    int ScoreGained,
    string Message);
