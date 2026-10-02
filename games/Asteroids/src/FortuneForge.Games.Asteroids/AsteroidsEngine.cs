using System.Collections.Immutable;

namespace FortuneForge.Games.Asteroids;

public static class AsteroidsEngine
{
    public const int DefaultWidth = 800;
    public const int DefaultHeight = 600;
    public const int DefaultLives = 3;
    public const int TickMilliseconds = 33;
    public const int ShipInvulnerabilityTicks = 120;
    public const double ShipRadius = 12;
    public const double MaxAsteroidSpeed = 3.08;
    public const double HunterAcceleration = 0.035;
    public const double HunterMaxSpeed = 2.6;
    public const int HunterScoreBonus = 75;

    private const double MaxShipSpeed = 7;
    private const double ThrustPower = 0.22;
    private const double BulletSpeed = 10;
    private const int BulletLifetime = 80;
    private const int FireCooldown = 8;
    private const int RapidFireCooldown = 2;
    private const int RapidFireTicks = 300;
    private const int ShieldTicks = 240;
    private const int PowerUpLifetime = int.MaxValue;
    private const double PowerUpRadius = 18;
    private const int ThrustVisualTicks = 3;
    private const int AsteroidSpriteVariantCount = 7;

    public static AsteroidsState Start(
        uint seed,
        int width = DefaultWidth,
        int height = DefaultHeight,
        int bestScore = 0)
    {
        ValidateDimensions(width, height);
        if (bestScore < 0)
            throw new ArgumentOutOfRangeException(nameof(bestScore), "The best score cannot be negative.");

        var random = NormalizeSeed(seed);
        var ship = NewShip(width, height);
        var nextEntityId = 1;
        var asteroids = SpawnWave(1, ship.Position, width, height, ref random, ref nextEntityId);
        return new AsteroidsState(
            width,
            height,
            seed,
            random,
            ship,
            asteroids,
            ImmutableArray<AsteroidsBullet>.Empty,
            nextEntityId,
            0,
            0,
            bestScore,
            DefaultLives,
            1,
            0,
            AsteroidsPhase.Playing,
            ImmutableArray<AsteroidsPowerUp>.Empty,
            0);
    }

    public static AsteroidsTransition Apply(AsteroidsState game, AsteroidsAction action)
    {
        ValidateState(game);
        if (game.Phase is AsteroidsPhase.GameOver)
            throw new InvalidOperationException("This Asteroids game is over. Start a new game to play again.");

        return action switch
        {
            AsteroidsAction.Tick => Tick(game),
            AsteroidsAction.RotateLeft => Rotate(game, -0.10, "Rotated left."),
            AsteroidsAction.RotateRight => Rotate(game, 0.10, "Rotated right."),
            AsteroidsAction.Thrust => Thrust(game),
            AsteroidsAction.Fire => Fire(game),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown Asteroids action."),
        };
    }

    /// <summary>
    /// Advances one gameplay frame. Controls are applied in this order: one turn, thrust, fire, then one tick.
    /// </summary>
    public static AsteroidsTransition AdvanceFrame(AsteroidsState game, AsteroidsControl controls)
    {
        ValidateControls(controls);

        var state = game;
        if ((controls & AsteroidsControl.TurnLeft) != 0)
            state = Apply(state, AsteroidsAction.RotateLeft).State;
        else if ((controls & AsteroidsControl.TurnRight) != 0)
            state = Apply(state, AsteroidsAction.RotateRight).State;

        if ((controls & AsteroidsControl.Thrust) != 0)
            state = Apply(state, AsteroidsAction.Thrust).State;

        if ((controls & AsteroidsControl.Fire) != 0)
            state = Apply(state, AsteroidsAction.Fire).State;

        return Apply(state, AsteroidsAction.Tick);
    }

    private static AsteroidsTransition Rotate(AsteroidsState game, double amount, string message) =>
        new(game with { Ship = game.Ship with { Angle = game.Ship.Angle + amount } }, AsteroidsEventType.Rotated, 0, message);

    private static AsteroidsTransition Thrust(AsteroidsState game)
    {
        var direction = Forward(game.Ship.Angle);
        var velocity = ClampSpeed(game.Ship.Velocity + direction * ThrustPower, MaxShipSpeed);
        return new AsteroidsTransition(
            game with { Ship = game.Ship with { Velocity = velocity, ThrustTicks = ThrustVisualTicks } },
            AsteroidsEventType.Thrusted,
            0,
            "Thrusters engaged.");
    }

    private static AsteroidsTransition Fire(AsteroidsState game)
    {
        if (game.FireCooldownTicks > 0)
            return new AsteroidsTransition(game, AsteroidsEventType.NoOp, 0, "Weapons are cooling down.");

        var direction = Forward(game.Ship.Angle);
        var bullet = new AsteroidsBullet(
            game.NextEntityId,
            ClampToField(game.Ship.Position + direction * 18, game.Width, game.Height, 0),
            game.Ship.Velocity + direction * BulletSpeed,
            BulletLifetime);
        return new AsteroidsTransition(
            game with
            {
                Bullets = game.Bullets.Add(bullet),
                NextEntityId = checked(game.NextEntityId + 1),
                FireCooldownTicks = game.RapidFireTicks > 0 ? RapidFireCooldown : FireCooldown,
            },
            AsteroidsEventType.Fired,
            0,
            "Photon torpedo fired.");
    }

    private static AsteroidsTransition Tick(AsteroidsState game)
    {
        var requestedShipPosition = game.Ship.Position + game.Ship.Velocity;
        var shipPosition = ClampToField(requestedShipPosition, game.Width, game.Height, ShipRadius);
        var deceleratedVelocity = game.Ship.Velocity * 0.995;
        var ship = game.Ship with
        {
            Position = shipPosition,
            Velocity = new AsteroidsVector(
                shipPosition.X == requestedShipPosition.X ? deceleratedVelocity.X : 0,
                shipPosition.Y == requestedShipPosition.Y ? deceleratedVelocity.Y : 0),
            InvulnerabilityTicks = Math.Max(0, game.Ship.InvulnerabilityTicks - 1),
            ThrustTicks = Math.Max(0, game.Ship.ThrustTicks - 1),
        };
        var asteroids = game.Asteroids
            .Select(asteroid => MoveAsteroid(asteroid, ship.Position, game.Width, game.Height))
            .ToList();
        var bullets = game.Bullets
            .Select(bullet => new MovingBullet(
                bullet.Position,
                bullet with
                {
                    Position = bullet.Position + bullet.Velocity,
                    RemainingTicks = bullet.RemainingTicks - 1,
                }))
            .Where(bullet => bullet.Value.RemainingTicks > 0 && IsInsideField(bullet.Value.Position, game.Width, game.Height))
            .ToList();
        var powerUps = (game.PowerUps.IsDefault ? ImmutableArray<AsteroidsPowerUp>.Empty : game.PowerUps)
            .Select(powerUp => powerUp with
            {
                Position = powerUp.Position + powerUp.Velocity,
            })
            .Where(powerUp => IsInsideField(powerUp.Position, game.Width, game.Height))
            .ToList();

        var random = game.RandomState;
        var nextEntityId = game.NextEntityId;
        var scoreGained = 0;
        var hitCount = 0;
        var destroyedCount = 0;
        var survivingBullets = new List<AsteroidsBullet>(bullets.Count);
        foreach (var bullet in bullets)
        {
            var hitIndex = asteroids.FindIndex(asteroid => SegmentIntersectsCircle(bullet.PreviousPosition, bullet.Value.Position, asteroid.Position, asteroid.Radius));
            if (hitIndex < 0)
            {
                survivingBullets.Add(bullet.Value);
                continue;
            }

            var hit = asteroids[hitIndex];
            hitCount++;
            if (hit.HitPoints > 1)
            {
                asteroids[hitIndex] = hit with { HitPoints = hit.HitPoints - 1 };
            }
            else
            {
                asteroids.RemoveAt(hitIndex);
                scoreGained = checked(scoreGained + ScoreFor(hit.Size) + (hit.Kind == AsteroidKind.Hunter ? HunterScoreBonus : 0));
                destroyedCount++;
                Split(hit, ref random, ref nextEntityId, asteroids);
                TrySpawnPowerUp(hit.Position, ref random, ref nextEntityId, powerUps);
            }
        }

        var lives = game.Lives;
        var phase = AsteroidsPhase.Playing;
        var rapidFireTicks = Math.Max(0, game.RapidFireTicks - 1);
        var eventType = hitCount > 0 ? AsteroidsEventType.Hit : AsteroidsEventType.Ticked;
        var message = destroyedCount > 0
            ? $"Destroyed {destroyedCount} asteroid{(destroyedCount == 1 ? string.Empty : "s")}."
            : hitCount > 0 ? $"Damaged {hitCount} asteroid{(hitCount == 1 ? string.Empty : "s")}." : string.Empty;
        var collectedPowerUps = powerUps.Where(powerUp => Distance(ship.Position, powerUp.Position) <= PowerUpRadius + ShipRadius).ToArray();
        if (collectedPowerUps.Length > 0)
        {
            foreach (var powerUp in collectedPowerUps)
            {
                switch (powerUp.Type)
                {
                    case AsteroidsPowerUpType.Shield:
                        ship = ship with { InvulnerabilityTicks = Math.Max(ship.InvulnerabilityTicks, ShieldTicks) };
                        break;
                    case AsteroidsPowerUpType.RapidFire:
                        rapidFireTicks = Math.Max(rapidFireTicks, RapidFireTicks);
                        break;
                    case AsteroidsPowerUpType.ExtraLife:
                        lives = Math.Min(DefaultLives + 2, lives + 1);
                        break;
                }
            }
            powerUps.RemoveAll(powerUp => collectedPowerUps.Contains(powerUp));
            eventType = AsteroidsEventType.PowerUpCollected;
            message = PowerUpMessage(collectedPowerUps[^1].Type);
        }
        if (ship.InvulnerabilityTicks == 0 && asteroids.Any(asteroid => Distance(ship.Position, asteroid.Position) <= asteroid.Radius + ShipRadius))
        {
            lives = Math.Max(0, lives - 1);
            ship = ship with
            {
                Velocity = new AsteroidsVector(0, 0),
                InvulnerabilityTicks = ShipInvulnerabilityTicks,
                ThrustTicks = 0,
            };
            survivingBullets.Clear();
            if (lives == 0)
            {
                phase = AsteroidsPhase.GameOver;
                eventType = AsteroidsEventType.GameOver;
                message = "The ship was destroyed. Game over.";
            }
            else
            {
                eventType = AsteroidsEventType.Damaged;
                message = $"Ship damaged. {lives} {(lives == 1 ? "life" : "lives")} remaining.";
            }
        }

        var wave = game.Wave;
        if (phase == AsteroidsPhase.Playing && asteroids.Count == 0)
        {
            wave = checked(wave + 1);
            asteroids.AddRange(SpawnWave(wave, ship.Position, game.Width, game.Height, ref random, ref nextEntityId));
            var waveBonus = checked(wave * 100);
            scoreGained = checked(scoreGained + waveBonus);
            eventType = AsteroidsEventType.WaveCleared;
            var hunterCount = HunterCountFor(wave);
            message = hunterCount > 0
                ? $"Wave {wave - 1} cleared. Wave {wave} incoming · {hunterCount} hunter{(hunterCount == 1 ? string.Empty : "s")} tracking · +{waveBonus} points."
                : $"Wave {wave - 1} cleared. Wave {wave} incoming · +{waveBonus} points.";
        }

        var score = checked(game.Score + scoreGained);
        return new AsteroidsTransition(
            game with
            {
                RandomState = random,
                Ship = ship,
                Asteroids = asteroids.ToImmutableArray(),
                Bullets = survivingBullets.ToImmutableArray(),
                PowerUps = powerUps.ToImmutableArray(),
                NextEntityId = nextEntityId,
                FireCooldownTicks = Math.Max(0, game.FireCooldownTicks - 1),
                RapidFireTicks = rapidFireTicks,
                Score = score,
                BestScore = Math.Max(game.BestScore, score),
                Lives = lives,
                Wave = wave,
                Tick = checked(game.Tick + 1),
                Phase = phase,
            },
            eventType,
            scoreGained,
            message);
    }

    private static void Split(
        Asteroid asteroid,
        ref uint random,
        ref int nextEntityId,
        List<Asteroid> destination)
    {
        if (asteroid.Size == AsteroidSize.Tiny)
            return;

        var childSize = asteroid.Size switch
        {
            AsteroidSize.Huge => AsteroidSize.Large,
            AsteroidSize.Large => AsteroidSize.Medium,
            AsteroidSize.Medium => AsteroidSize.Small,
            AsteroidSize.Small => AsteroidSize.Tiny,
            _ => throw new ArgumentOutOfRangeException(nameof(asteroid.Size), asteroid.Size, "Unknown asteroid size."),
        };
        var childRadius = RadiusFor(childSize);
        var baseAngle = Math.Atan2(asteroid.Velocity.Y, asteroid.Velocity.X);
        var childVariants = SplitVariantsFor(asteroid.SpriteVariant);
        for (var index = 0; index < 2; index++)
        {
            var angle = baseAngle + (index == 0 ? -0.65 : 0.65) + RandomRange(ref random, -0.18, 0.18);
            var childSpeed = Math.Min(MaxAsteroidSpeed, Math.Max(0.45, asteroid.Velocity.Length + RandomRange(ref random, 0.25, 0.85)));
            var velocity = new AsteroidsVector(Math.Cos(angle), Math.Sin(angle)) * childSpeed;
            destination.Add(new Asteroid(
                nextEntityId++,
                asteroid.Position,
                velocity,
                childRadius,
                childSize,
                HitPointsFor(childSize),
                childVariants[index]));
        }
    }

    private static void TrySpawnPowerUp(
        AsteroidsVector position,
        ref uint random,
        ref int nextEntityId,
        List<AsteroidsPowerUp> destination)
    {
        if (RandomRange(ref random, 0, 1) > 0.18)
            return;

        var type = RandomRange(ref random, 0, 1) switch
        {
            < 0.4 => AsteroidsPowerUpType.Shield,
            < 0.75 => AsteroidsPowerUpType.RapidFire,
            _ => AsteroidsPowerUpType.ExtraLife,
        };
        var angle = RandomRange(ref random, 0, Math.PI * 2);
        destination.Add(new AsteroidsPowerUp(
            nextEntityId++,
            position,
            new AsteroidsVector(Math.Cos(angle), Math.Sin(angle)) * 0.8,
            type,
            PowerUpLifetime));
    }

    private static ImmutableArray<Asteroid> SpawnWave(
        int wave,
        AsteroidsVector shipPosition,
        int width,
        int height,
        ref uint random,
        ref int nextEntityId)
    {
        var count = Math.Min(18, 4 + wave);
        var hunterCount = HunterCountFor(wave);
        var asteroids = new List<Asteroid>(count);
        for (var index = 0; index < count; index++)
        {
            var size = InitialSizeFor(wave, index);
            var radius = RadiusFor(size);
            AsteroidsVector position;
            for (var attempt = 0; ; attempt++)
            {
                position = new AsteroidsVector(RandomRange(ref random, radius, width - radius), RandomRange(ref random, radius, height - radius));
                if (Distance(position, shipPosition) >= 150 || attempt >= 64)
                    break;
            }
            var angle = RandomRange(ref random, 0, Math.PI * 2);
            var baseSpeed = size switch
            {
                AsteroidSize.Huge => RandomRange(ref random, 0.12, 0.55),
                AsteroidSize.Large => RandomRange(ref random, 0.25, 1.05),
                AsteroidSize.Medium => RandomRange(ref random, 0.5, 1.65),
                AsteroidSize.Small => RandomRange(ref random, 0.9, 2.35),
                AsteroidSize.Tiny => RandomRange(ref random, 1.3, 3.02),
                _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown asteroid size."),
            };
            var speed = Math.Min(MaxAsteroidSpeed, baseSpeed + Math.Min(0.6, Math.Max(0, wave - 1) * 0.06));
            asteroids.Add(new Asteroid(
                nextEntityId++,
                position,
                new AsteroidsVector(Math.Cos(angle), Math.Sin(angle)) * speed,
                radius,
                size,
                HitPointsFor(size),
                InitialSpriteVariantFor(wave, index),
                index < hunterCount ? AsteroidKind.Hunter : AsteroidKind.Drifter));
        }
        return asteroids.ToImmutableArray();
    }

    private static AsteroidsShip NewShip(int width, int height) =>
        new(new AsteroidsVector(width / 2.0, height / 2.0), new AsteroidsVector(0, 0), -Math.PI / 2, 0);

    private static AsteroidSize InitialSizeFor(int wave, int index) => ((wave + index) % 5) switch
    {
        0 => AsteroidSize.Huge,
        1 => AsteroidSize.Large,
        2 => AsteroidSize.Medium,
        3 => AsteroidSize.Small,
        _ => AsteroidSize.Tiny,
    };

    private static int InitialSpriteVariantFor(int wave, int index) => (wave * 3 + index * 2) % 5;

    private static int[] SplitVariantsFor(int spriteVariant) => spriteVariant switch
    {
        0 => [4, 2],
        1 => [5, 6],
        2 => [3, 0],
        3 => [2, 4],
        4 => [0, 3],
        5 => [2, 4],
        6 => [4, 3],
        _ => throw new ArgumentOutOfRangeException(nameof(spriteVariant), spriteVariant, "Unknown asteroid sprite variant."),
    };

    private static AsteroidsVector Forward(double angle) => new(Math.Cos(angle), Math.Sin(angle));

    private static int HunterCountFor(int wave) => wave < 2 ? 0 : Math.Min(3, wave / 2);

    private static Asteroid MoveAsteroid(Asteroid asteroid, AsteroidsVector shipPosition, int width, int height)
    {
        var velocity = asteroid.Velocity;
        if (asteroid.Kind == AsteroidKind.Hunter)
        {
            var pursuit = shipPosition - asteroid.Position;
            if (pursuit.Length > 0)
                velocity = ClampSpeed(velocity + pursuit * (HunterAcceleration / pursuit.Length), HunterMaxSpeed);
        }
        var requestedPosition = asteroid.Position + velocity;
        var position = ClampToField(requestedPosition, width, height, asteroid.Radius);
        return asteroid with
        {
            Position = position,
            Velocity = new AsteroidsVector(
                position.X == requestedPosition.X ? velocity.X : -velocity.X,
                position.Y == requestedPosition.Y ? velocity.Y : -velocity.Y),
        };
    }

    private static AsteroidsVector ClampSpeed(AsteroidsVector velocity, double maximum)
    {
        if (velocity.Length <= maximum || velocity.Length == 0)
            return velocity;
        return velocity * (maximum / velocity.Length);
    }

    private static AsteroidsVector ClampToField(AsteroidsVector position, int width, int height, double padding) =>
        new(Math.Clamp(position.X, padding, width - padding), Math.Clamp(position.Y, padding, height - padding));

    private static bool IsInsideField(AsteroidsVector position, int width, int height) =>
        position.X >= 0 && position.X <= width && position.Y >= 0 && position.Y <= height;

    private static bool SegmentIntersectsCircle(AsteroidsVector start, AsteroidsVector end, AsteroidsVector center, double radius)
    {
        var segment = end - start;
        var lengthSquared = (segment.X * segment.X) + (segment.Y * segment.Y);
        if (lengthSquared == 0)
            return Distance(start, center) <= radius;
        var toCenter = center - start;
        var projection = Math.Clamp(((toCenter.X * segment.X) + (toCenter.Y * segment.Y)) / lengthSquared, 0, 1);
        return Distance(start + (segment * projection), center) <= radius;
    }

    private readonly record struct MovingBullet(AsteroidsVector PreviousPosition, AsteroidsBullet Value);

    private static double Distance(AsteroidsVector first, AsteroidsVector second) => (first - second).Length;
    private static double RandomRange(ref uint random, double minimum, double maximum) => minimum + ((NextRandom(ref random) / (double)uint.MaxValue) * (maximum - minimum));
    private static int ScoreFor(AsteroidSize size) => size switch { AsteroidSize.Huge => 20, AsteroidSize.Large => 35, AsteroidSize.Medium => 55, AsteroidSize.Small => 80, AsteroidSize.Tiny => 110, _ => 0 };
    private static double RadiusFor(AsteroidSize size) => size switch { AsteroidSize.Huge => 52, AsteroidSize.Large => 40, AsteroidSize.Medium => 29, AsteroidSize.Small => 20, AsteroidSize.Tiny => 13, _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown asteroid size.") };
    private static int HitPointsFor(AsteroidSize size) => size switch { AsteroidSize.Huge => 10, AsteroidSize.Large => 8, AsteroidSize.Medium => 6, AsteroidSize.Small => 4, AsteroidSize.Tiny => 2, _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown asteroid size.") };
    private static string PowerUpMessage(AsteroidsPowerUpType type) => type switch
    {
        AsteroidsPowerUpType.Shield => "Shield activated.",
        AsteroidsPowerUpType.RapidFire => "Rapid fire online.",
        AsteroidsPowerUpType.ExtraLife => "Extra life secured.",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown power-up."),
    };

    private static uint NormalizeSeed(uint seed) => seed == 0 ? 0xA341316Cu : seed;

    private static uint NextRandom(ref uint value)
    {
        unchecked
        {
            if (value == 0)
                value = 0xA341316Cu;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            return value;
        }
    }

    private static void ValidateState(AsteroidsState game)
    {
        ArgumentNullException.ThrowIfNull(game);
        ValidateDimensions(game.Width, game.Height);
        if (game.Score < 0 || game.BestScore < game.Score || game.Lives < 0 || game.Wave < 1 || game.Tick < 0 || game.NextEntityId < 1 || game.FireCooldownTicks < 0 || game.RapidFireTicks < 0 || game.Ship.ThrustTicks < 0)
            throw new ArgumentException("Asteroids scores and counters are invalid.", nameof(game));
        if (game.Asteroids.Any(asteroid => asteroid.Radius <= 0 || asteroid.Id < 1 || asteroid.HitPoints <= 0 || asteroid.SpriteVariant is < 0 or >= AsteroidSpriteVariantCount || !Enum.IsDefined(asteroid.Kind)) || game.Bullets.Any(bullet => bullet.Id < 1 || bullet.RemainingTicks <= 0) || (!game.PowerUps.IsDefault && game.PowerUps.Any(powerUp => powerUp.Id < 1 || powerUp.RemainingTicks <= 0)))
            throw new ArgumentException("Asteroids entities are invalid.", nameof(game));
    }

    private static void ValidateControls(AsteroidsControl controls)
    {
        const AsteroidsControl known = AsteroidsControl.Thrust | AsteroidsControl.TurnLeft | AsteroidsControl.TurnRight | AsteroidsControl.Fire;
        if ((((int)controls) & ~((int)known)) != 0)
            throw new ArgumentOutOfRangeException(nameof(controls), controls, "Unknown Asteroids control bits.");
        if ((controls & (AsteroidsControl.TurnLeft | AsteroidsControl.TurnRight)) == (AsteroidsControl.TurnLeft | AsteroidsControl.TurnRight))
            throw new ArgumentException("Asteroids controls cannot turn left and right in the same frame.", nameof(controls));
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width is < 400 or > 1600 || height is < 300 or > 1200)
            throw new ArgumentOutOfRangeException(nameof(width), "An Asteroids field must be between 400×300 and 1600×1200.");
    }
}
