using System.Collections.Immutable;

namespace FortuneForge.Games.HorseFlight;

public static class HorseFlightEngine
{
    public const int DefaultWidth = 960;
    public const int DefaultHeight = 540;
    public const int TickMilliseconds = 20;
    public const double HorseX = 336;
    public const double HorseWidth = 48;
    public const double HorseHeight = 40;
    public const double SlideHorseHeight = 20;
    public const double GravityPerTick = 0.9;
    public const double JumpVelocity = -12.5;
    public const double FastFallVelocity = 16;
    public const int MaximumJumps = 2;
    public const int SlideDurationTicks = 16;
    public const double SlideSpeedBonus = 4;
    public const double DistancePerPoint = 10;
    public const int PlatformClearBonus = 100;
    public const int ScoresPerLevel = 500;
    public const int HauntedBiomeTransitionStartTick = 14_250;
    public const int HauntedBiomeStartTick = 15_000;

    private const double InitialPlatformWidth = 900;
    private const double InitialPlatformBottomMargin = 80;
    private const double GenerationLead = 420;
    private const int MinimumPlatformCount = 4;
    private const double MinimumPlatformWidth = 260;
    private const double PlatformWidthRange = 180;
    private const double BaseForwardSpeed = 5;
    private const double SpeedIncreasePerLevel = 0.2;
    private const double MaximumForwardSpeed = 7;
    private const int LevelsPerAdditionalObstacle = 3;
    private const int MaximumObstaclesPerPlatform = 3;
    private const double ObstacleClearance = 18;
    private const double MaximumObstacleWidth = 84;
    private const double HorizontalHitboxInset = 3;
    private const double VerticalHitboxInset = 2;
    private const double FenceSlideClearance = 24;
    private const double PositionTolerance = 0.000_001;

    private static readonly HorseFlightObstacleDefinition[] ObstacleDefinitions =
    [
        new(HorseFlightObstacleKind.Crate, Width: 38, Height: 34),
        new(HorseFlightObstacleKind.CrateCluster, Width: 50, Height: 60),
        new(HorseFlightObstacleKind.Pit, Width: 80, Height: 32),
        new(HorseFlightObstacleKind.Well, Width: 56, Height: 52),
        new(HorseFlightObstacleKind.Fence, Width: 84, Height: 42),
        new(HorseFlightObstacleKind.Carriage, Width: 72, Height: 48),
        new(HorseFlightObstacleKind.OilSpill, Width: 78, Height: 12),
        new(HorseFlightObstacleKind.Boulder, Width: 54, Height: 36),
        new(HorseFlightObstacleKind.FallenLog, Width: 80, Height: 40),
        new(HorseFlightObstacleKind.Dog, Width: 58, Height: 38, AdditionalSpeed: 2.2),
        new(HorseFlightObstacleKind.BatFlock, Width: 64, Height: 42, Elevation: 88, AdditionalSpeed: 1.4),
        new(HorseFlightObstacleKind.LassoThrower, Width: 76, Height: 64, AdditionalSpeed: 0.8),
        new(HorseFlightObstacleKind.NetTower, Width: 92, Height: 108),
        new(HorseFlightObstacleKind.Skeleton, Width: 42, Height: 52, AdditionalSpeed: 1.1),
        new(HorseFlightObstacleKind.RollingBarrel, Width: 42, Height: 42, AdditionalSpeed: 2.6),
    ];

    private static double HorseLeft => HorseX - (HorseWidth / 2);
    private static double HorseRight => HorseX + (HorseWidth / 2);

    public static HorseFlightState Start(
        uint seed,
        int width = DefaultWidth,
        int height = DefaultHeight,
        int bestScore = 0)
    {
        ValidateDimensions(width, height);
        if (bestScore < 0)
            throw new ArgumentOutOfRangeException(nameof(bestScore), "The best score cannot be negative.");

        var random = NormalizeSeed(seed);
        var platformY = height - InitialPlatformBottomMargin;
        var platforms = new List<HorseFlightPlatform>
        {
            new(1, 0, InitialPlatformWidth, platformY, Cleared: false),
        };
        var obstacles = new List<HorseFlightObstacle>();
        var nextPlatformId = 2;
        var nextObstacleId = 1;
        FillWorld(
            platforms,
            obstacles,
            width,
            height,
            level: 1,
            tick: 0,
            ref nextPlatformId,
            ref nextObstacleId,
            ref random);

        return new HorseFlightState(
            width,
            height,
            seed,
            random,
            platformY,
            0,
            IsGrounded: true,
            StandingPlatformId: 1,
            platforms.ToImmutableArray(),
            obstacles.ToImmutableArray(),
            nextPlatformId,
            nextObstacleId,
            0,
            0,
            0,
            bestScore,
            0,
            HorseFlightPhase.Running)
        {
            JumpsRemaining = MaximumJumps,
        };
    }

    public static HorseFlightTransition Step(HorseFlightState state, HorseFlightInput input)
    {
        ValidateState(state);
        if (state.Phase != HorseFlightPhase.Running)
            throw new InvalidOperationException("This Horse Flight run has ended. Start a new run to play again.");
        if (input is not HorseFlightInput.None and not HorseFlightInput.Jump and not HorseFlightInput.RightClick)
            throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown Horse Flight input.");

        var slideStartsThisTick = input == HorseFlightInput.RightClick && state.IsGrounded;
        var isSlidingThisTick = state.SlideTicksRemaining > 0 || slideStartsThisTick;
        var speed = ForwardSpeed(state.Level) + (isSlidingThisTick ? SlideSpeedBonus : 0);
        var newlyCleared = 0;
        var platforms = new List<HorseFlightPlatform>(state.Platforms.Length);
        foreach (var platform in state.Platforms)
        {
            var moved = platform with { X = platform.X - speed };
            if (!moved.Cleared && moved.X + moved.Width < HorseLeft)
            {
                moved = moved with { Cleared = true };
                newlyCleared++;
            }
            if (moved.X + moved.Width > 0)
                platforms.Add(moved);
        }

        var obstacles = state.Obstacles
            .Select(obstacle => obstacle with { X = obstacle.X - speed - obstacle.AdditionalSpeed })
            .Where(obstacle => obstacle.X + obstacle.Width > 0)
            .ToList();
        var random = state.RandomState;
        var nextPlatformId = state.NextPlatformId;
        var nextObstacleId = state.NextObstacleId;
        FillWorld(
            platforms,
            obstacles,
            state.Width,
            state.Height,
            state.Level,
            state.Tick,
            ref nextPlatformId,
            ref nextObstacleId,
            ref random);

        var support = state.IsGrounded
            ? platforms.FirstOrDefault(platform =>
                HorizontallyOverlapsHorse(platform) &&
                Math.Abs(platform.Y - state.HorseY) <= PositionTolerance)
            : null;
        var horseY = state.HorseY;
        var velocity = state.HorseVelocity;
        var grounded = support is not null;
        int? standingPlatformId = support?.Id;
        var jumpsRemaining = grounded ? MaximumJumps : state.JumpsRemaining;
        var slideTicksRemaining = state.SlideTicksRemaining;
        var jumped = input == HorseFlightInput.Jump && jumpsRemaining > 0;
        var sliding = input == HorseFlightInput.RightClick && grounded;
        var fastFalling = input == HorseFlightInput.RightClick && !grounded;
        var landed = false;

        if (jumped)
        {
            grounded = false;
            standingPlatformId = null;
            jumpsRemaining--;
            slideTicksRemaining = 0;
            velocity = JumpVelocity;
            horseY += velocity;
        }
        else if (grounded)
        {
            if (sliding)
                slideTicksRemaining = SlideDurationTicks;
            else if (slideTicksRemaining > 0)
                slideTicksRemaining--;
            velocity = 0;
            horseY = support!.Y;
        }
        else
        {
            slideTicksRemaining = 0;
            velocity = fastFalling
                ? Math.Max(velocity, FastFallVelocity)
                : velocity + GravityPerTick;
            var candidateY = horseY + velocity;
            if (velocity >= 0)
            {
                var landing = platforms
                    .Where(HorizontallyOverlapsHorse)
                    .Where(platform => horseY <= platform.Y + PositionTolerance && candidateY >= platform.Y)
                    .OrderBy(platform => platform.Y)
                    .FirstOrDefault();
                if (landing is not null)
                {
                    candidateY = landing.Y;
                    velocity = 0;
                    grounded = true;
                    standingPlatformId = landing.Id;
                    jumpsRemaining = MaximumJumps;
                    landed = true;
                }
            }
            horseY = candidateY;
        }

        var distance = state.Distance + speed;
        var platformsCleared = checked(state.PlatformsCleared + newlyCleared);
        var score = CalculateScore(distance, platformsCleared);
        var scoreGained = score - state.Score;
        var collisionHorseHeight = isSlidingThisTick && grounded ? SlideHorseHeight : HorseHeight;
        var dashedObstacleId = isSlidingThisTick && grounded
            ? ObstacleHitByDash(horseY, collisionHorseHeight, platforms, obstacles)
            : null;
        if (dashedObstacleId is not null)
        {
            obstacles = obstacles
                .Select(obstacle => obstacle.Id == dashedObstacleId
                    ? obstacle with { KnockedDownAt = state.Tick + 1 }
                    : obstacle)
                .ToList();
        }
        var phase = CollidesWithObstacle(horseY, collisionHorseHeight, platforms, obstacles)
            ? HorseFlightPhase.ObstacleCollision
            : horseY - HorseHeight > state.Height
                ? HorseFlightPhase.Fell
                : HorseFlightPhase.Running;
        var nextState = state with
        {
            RandomState = random,
            HorseY = horseY,
            HorseVelocity = velocity,
            IsGrounded = grounded,
            StandingPlatformId = standingPlatformId,
            JumpsRemaining = jumpsRemaining,
            SlideTicksRemaining = slideTicksRemaining,
            Platforms = platforms.ToImmutableArray(),
            Obstacles = obstacles.ToImmutableArray(),
            NextPlatformId = nextPlatformId,
            NextObstacleId = nextObstacleId,
            Distance = distance,
            PlatformsCleared = platformsCleared,
            Score = score,
            BestScore = Math.Max(state.BestScore, score),
            Tick = checked(state.Tick + 1),
            Phase = phase,
        };
        var eventType = phase switch
        {
            HorseFlightPhase.ObstacleCollision => HorseFlightEventType.ObstacleCollision,
            HorseFlightPhase.Fell => HorseFlightEventType.Fell,
            _ when landed => HorseFlightEventType.Landed,
            _ when sliding => HorseFlightEventType.Slid,
            _ when fastFalling => HorseFlightEventType.FastFell,
            _ when newlyCleared > 0 || scoreGained > 0 => HorseFlightEventType.Progressed,
            _ when jumped => HorseFlightEventType.Jumped,
            _ => HorseFlightEventType.Advanced,
        };
        return new HorseFlightTransition(nextState, eventType, scoreGained, speed);
    }

    public static HorseFlightSnapshot ToSnapshot(HorseFlightState state)
    {
        ValidateState(state);
        var platformYById = state.Platforms.ToDictionary(platform => platform.Id, platform => platform.Y);
        return new HorseFlightSnapshot(
            state.Width,
            state.Height,
            state.Seed,
            state.Tick,
            HorseX,
            state.HorseY,
            state.HorseVelocity,
            state.IsGrounded,
            state.Distance,
            state.PlatformsCleared,
            state.Score,
            state.BestScore,
            state.Level,
            state.Phase,
            state.Platforms
                .Select(platform => new HorseFlightPlatformSnapshot(
                    platform.Id,
                    platform.X,
                    platform.Width,
                    platform.Y))
                .ToImmutableArray(),
            state.Obstacles
                .Select(obstacle => new HorseFlightObstacleSnapshot(
                    obstacle.Id,
                    obstacle.Kind,
                    obstacle.X,
                    obstacle.Width,
                    platformYById[obstacle.PlatformId] - obstacle.Elevation - obstacle.Height,
                    obstacle.Height))
                .ToImmutableArray());
    }

    private static void FillWorld(
        List<HorseFlightPlatform> platforms,
        List<HorseFlightObstacle> obstacles,
        int width,
        int height,
        int level,
        int tick,
        ref int nextPlatformId,
        ref int nextObstacleId,
        ref uint random)
    {
        while (platforms.Count < MinimumPlatformCount ||
               platforms.Max(platform => platform.X + platform.Width) < width + GenerationLead)
        {
            var previous = platforms.Count == 0
                ? null
                : platforms.MaxBy(platform => platform.X + platform.Width);
            var previousRight = previous?.X + previous?.Width ?? 0;
            var obstacleSlots = Math.Min(
                MaximumObstaclesPerPlatform,
                1 + ((level - 1) / LevelsPerAdditionalObstacle));
            var minimumWidthForSlots = 80 + (obstacleSlots * MaximumObstacleWidth) +
                ((obstacleSlots - 1) * ObstacleClearance);
            var platformWidth = Math.Max(
                minimumWidthForSlots,
                MinimumPlatformWidth + (NextUnit(ref random) * PlatformWidthRange));
            var platform = new HorseFlightPlatform(
                nextPlatformId++,
                previousRight,
                platformWidth,
                height - InitialPlatformBottomMargin,
                Cleared: false);
            platforms.Add(platform);

            var usableWidth = platform.Width - 80 - ((obstacleSlots - 1) * ObstacleClearance);
            var slotWidth = usableWidth / obstacleSlots;
            var eligibleDefinitions = tick >= HauntedBiomeStartTick
                ? ObstacleDefinitions
                : ObstacleDefinitions.Where(definition =>
                    definition.Kind is not HorseFlightObstacleKind.BatFlock and not HorseFlightObstacleKind.Skeleton).ToArray();
            for (var slot = 0; slot < obstacleSlots; slot++)
            {
                var definition = eligibleDefinitions[
                    Math.Min(
                        (int)(NextUnit(ref random) * eligibleDefinitions.Length),
                        eligibleDefinitions.Length - 1)];
                var slotStart = platform.X + 40 + (slot * (slotWidth + ObstacleClearance));
                var obstacleX = slotStart + (NextUnit(ref random) * (slotWidth - definition.Width));

                obstacles.Add(new HorseFlightObstacle(
                    nextObstacleId++,
                    definition.Kind,
                    platform.Id,
                    obstacleX,
                    definition.Width,
                    definition.Height,
                    definition.Elevation,
                    definition.AdditionalSpeed));
            }
        }
    }

    private static bool HorizontallyOverlapsHorse(HorseFlightPlatform platform) =>
        platform.X < HorseRight && platform.X + platform.Width > HorseLeft;

    private static bool CollidesWithObstacle(
        double horseY,
        double horseHeight,
        IReadOnlyCollection<HorseFlightPlatform> platforms,
        IEnumerable<HorseFlightObstacle> obstacles)
    {
        var platformYById = platforms.ToDictionary(platform => platform.Id, platform => platform.Y);
        var horseTop = horseY - horseHeight;
        foreach (var obstacle in obstacles)
        {
            if (obstacle.KnockedDownAt is not null)
                continue;
            var obstacleBottom = CollisionBottom(obstacle, platformYById[obstacle.PlatformId]);
            var obstacleLeft = obstacle.X + HorizontalHitboxInset;
            var obstacleRight = obstacle.X + obstacle.Width - HorizontalHitboxInset;
            if (obstacle.Kind == HorseFlightObstacleKind.NetTower && horseHeight <= SlideHorseHeight)
                continue;
            var collisionHeight = obstacle.Kind == HorseFlightObstacleKind.NetTower ? 24 : obstacle.Height;
            var obstacleTop = obstacleBottom - collisionHeight + VerticalHitboxInset;
            var obstacleCollisionBottom = obstacleBottom - VerticalHitboxInset;
            if (obstacleLeft < HorseRight &&
                obstacleRight > HorseLeft &&
                obstacleTop < horseY &&
                obstacleCollisionBottom > horseTop)
            {
                return true;
            }
        }
        return false;
    }

    private static int? ObstacleHitByDash(
        double horseY,
        double horseHeight,
        IReadOnlyCollection<HorseFlightPlatform> platforms,
        IEnumerable<HorseFlightObstacle> obstacles)
    {
        var platformYById = platforms.ToDictionary(platform => platform.Id, platform => platform.Y);
        var horseTop = horseY - horseHeight;
        foreach (var obstacle in obstacles)
        {
            if (obstacle.Kind is not (HorseFlightObstacleKind.LassoThrower or HorseFlightObstacleKind.Dog) || obstacle.KnockedDownAt is not null)
                continue;
            var obstacleBottom = platformYById[obstacle.PlatformId] - obstacle.Elevation;
            var obstacleLeft = obstacle.X + HorizontalHitboxInset;
            var obstacleRight = obstacle.X + obstacle.Width - HorizontalHitboxInset;
            var obstacleTop = obstacleBottom - obstacle.Height + VerticalHitboxInset;
            var obstacleCollisionBottom = obstacleBottom - VerticalHitboxInset;
            if (obstacleLeft < HorseRight &&
                obstacleRight > HorseLeft &&
                obstacleTop < horseY &&
                obstacleCollisionBottom > horseTop)
            {
                return obstacle.Id;
            }
        }
        return null;
    }

    private static double CollisionBottom(HorseFlightObstacle obstacle, double platformY) =>
        platformY - obstacle.Elevation -
        (obstacle.Kind == HorseFlightObstacleKind.Fence ? FenceSlideClearance : 0);

    private static int CalculateScore(double distance, int platformsCleared) => checked(
        (int)Math.Floor(distance / DistancePerPoint) +
        (platformsCleared * PlatformClearBonus));

    private static double ForwardSpeed(int level) =>
        Math.Min(MaximumForwardSpeed, BaseForwardSpeed + ((level - 1) * SpeedIncreasePerLevel));

    private static uint NormalizeSeed(uint seed) => seed == 0 ? 0x9E37_79B9u : seed;

    private static double NextUnit(ref uint random)
    {
        random ^= random << 13;
        random ^= random >> 17;
        random ^= random << 5;
        return random / (uint.MaxValue + 1d);
    }

    private static void ValidateState(HorseFlightState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateDimensions(state.Width, state.Height);
        if (state.RandomState == 0)
            throw new ArgumentException("The Horse Flight random state cannot be zero.", nameof(state));
        if (state.JumpsRemaining is < 0 or > MaximumJumps)
            throw new ArgumentException("The Horse Flight jump counter is invalid.", nameof(state));
        if (state.SlideTicksRemaining is < 0 or > SlideDurationTicks)
            throw new ArgumentException("The Horse Flight slide counter is invalid.", nameof(state));
        if (!double.IsFinite(state.HorseY) || !double.IsFinite(state.HorseVelocity))
            throw new ArgumentException("Horse position and velocity must be finite.", nameof(state));
        if (!double.IsFinite(state.Distance) || state.Distance < 0)
            throw new ArgumentException("Distance must be finite and non-negative.", nameof(state));
        if (state.NextPlatformId < 1 || state.NextObstacleId < 1 || state.Tick < 0 ||
            state.PlatformsCleared < 0 || state.Score < 0 || state.BestScore < state.Score)
        {
            throw new ArgumentException("Horse Flight counters are invalid.", nameof(state));
        }
        if (!Enum.IsDefined(state.Phase))
            throw new ArgumentException("The Horse Flight phase is invalid.", nameof(state));
        if (state.Score != CalculateScore(state.Distance, state.PlatformsCleared))
            throw new ArgumentException("The score does not match distance and cleared-platform progress.", nameof(state));

        var platformIds = new HashSet<int>();
        foreach (var platform in state.Platforms)
        {
            if (platform.Id < 1 || platform.Id >= state.NextPlatformId || !platformIds.Add(platform.Id) ||
                !double.IsFinite(platform.X) || !double.IsFinite(platform.Width) || platform.Width <= 0 ||
                !double.IsFinite(platform.Y) || platform.Y < HorseHeight || platform.Y > state.Height)
            {
                throw new ArgumentException("The Horse Flight platform collection is invalid.", nameof(state));
            }
        }

        var platformById = state.Platforms.ToDictionary(platform => platform.Id);
        var obstacleIds = new HashSet<int>();
        foreach (var obstacle in state.Obstacles)
        {
            if (obstacle.Id < 1 || obstacle.Id >= state.NextObstacleId || !obstacleIds.Add(obstacle.Id) ||
                !Enum.IsDefined(obstacle.Kind) ||
                !platformById.TryGetValue(obstacle.PlatformId, out var platform) ||
                !double.IsFinite(obstacle.X) || !double.IsFinite(obstacle.Width) || obstacle.Width <= 0 ||
                !double.IsFinite(obstacle.Height) || obstacle.Height <= 0 ||
                !double.IsFinite(obstacle.Elevation) || obstacle.Elevation < 0 ||
                !double.IsFinite(obstacle.AdditionalSpeed) || obstacle.AdditionalSpeed < 0 ||
                obstacle.KnockedDownAt is < 0 || obstacle.KnockedDownAt > state.Tick ||
                obstacle.Height + obstacle.Elevation >= platform.Y ||
                (obstacle.AdditionalSpeed <= 0 && obstacle.X < platform.X - PositionTolerance) ||
                obstacle.X + obstacle.Width > platform.X + platform.Width + PositionTolerance)
            {
                throw new ArgumentException("The Horse Flight obstacle collection is invalid.", nameof(state));
            }
        }

        if (state.IsGrounded)
        {
            if (state.StandingPlatformId is not int standingId ||
                state.JumpsRemaining != MaximumJumps ||
                !platformById.TryGetValue(standingId, out var platform) ||
                !HorizontallyOverlapsHorse(platform) ||
                Math.Abs(platform.Y - state.HorseY) > PositionTolerance ||
                Math.Abs(state.HorseVelocity) > PositionTolerance)
            {
                throw new ArgumentException("Grounded state must identify the supporting platform.", nameof(state));
            }
        }
        else if (state.StandingPlatformId is not null)
        {
            throw new ArgumentException("An airborne horse cannot identify a standing platform.", nameof(state));
        }
        else if (state.SlideTicksRemaining != 0)
        {
            throw new ArgumentException("An airborne horse cannot remain in a slide.", nameof(state));
        }
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width is < 480 or > 3840)
            throw new ArgumentOutOfRangeException(nameof(width), "Horse Flight width must be between 480 and 3840.");
        if (height is < 300 or > 2160)
            throw new ArgumentOutOfRangeException(nameof(height), "Horse Flight height must be between 300 and 2160.");
    }

    private readonly record struct HorseFlightObstacleDefinition(
        HorseFlightObstacleKind Kind,
        double Width,
        double Height,
        double Elevation = 0,
        double AdditionalSpeed = 0);
}
