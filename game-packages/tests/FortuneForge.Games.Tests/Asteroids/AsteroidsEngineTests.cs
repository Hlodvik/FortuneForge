using System.Collections.Immutable;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Asteroids;

namespace FortuneForge.Games.Tests.Asteroids;

public sealed class AsteroidsEngineTests
{
    [Fact]
    public void Start_is_seeded_and_creates_a_wave_with_three_lives()
    {
        var first = AsteroidsEngine.Start(42);
        var second = AsteroidsEngine.Start(42);

        Assert.Equal(first.RandomState, second.RandomState);
        Assert.Equal(first.Ship, second.Ship);
        Assert.Equal(first.Asteroids.ToArray(), second.Asteroids.ToArray());
        Assert.Equal(5, first.AsteroidCount);
        Assert.Equal(3, first.Lives);
        Assert.Equal(1, first.Wave);
        Assert.Equal(5, first.Asteroids.Select(asteroid => asteroid.Size).Distinct().Count());
        Assert.Equal(5, first.Asteroids.Select(asteroid => asteroid.SpriteVariant).Distinct().Count());
        Assert.True(first.Asteroids.Max(asteroid => asteroid.Velocity.Length) > first.Asteroids.Min(asteroid => asteroid.Velocity.Length));
        Assert.All(first.Asteroids, asteroid => Assert.InRange(asteroid.Velocity.Length, 0, AsteroidsEngine.MaxAsteroidSpeed));
        Assert.Equal(10, first.Asteroids.First(asteroid => asteroid.Size == AsteroidSize.Huge).HitPoints);
        Assert.Equal(8, first.Asteroids.First(asteroid => asteroid.Size == AsteroidSize.Large).HitPoints);
        Assert.Equal(6, first.Asteroids.First(asteroid => asteroid.Size == AsteroidSize.Medium).HitPoints);
        Assert.Equal(4, first.Asteroids.First(asteroid => asteroid.Size == AsteroidSize.Small).HitPoints);
        Assert.Equal(2, first.Asteroids.First(asteroid => asteroid.Size == AsteroidSize.Tiny).HitPoints);
        Assert.All(first.Asteroids, asteroid => Assert.Equal(AsteroidKind.Drifter, asteroid.Kind));
    }

    [Fact]
    public void Rotate_thrust_and_fire_update_the_ship_and_weapon_state()
    {
        var state = AsteroidsEngine.Start(7);
        var rotated = AsteroidsEngine.Apply(state, AsteroidsAction.RotateRight);
        var thrust = AsteroidsEngine.Apply(rotated.State, AsteroidsAction.Thrust);
        var fired = AsteroidsEngine.Apply(thrust.State, AsteroidsAction.Fire);

        Assert.Equal(AsteroidsEventType.Rotated, rotated.Event);
        Assert.Equal(0.10, rotated.State.Ship.Angle - state.Ship.Angle, 4);
        Assert.Equal(AsteroidsEventType.Thrusted, thrust.Event);
        Assert.True(thrust.State.Ship.Velocity.Length > 0);
        Assert.True(thrust.State.Ship.ThrustTicks > 0);
        Assert.Equal(AsteroidsEventType.Fired, fired.Event);
        Assert.Single(fired.State.Bullets);
        Assert.True(fired.State.FireCooldownTicks > 0);
    }

    [Fact]
    public void Frame_controls_none_still_ticks_the_full_engine()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(2, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Small)]);

        var transition = AsteroidsEngine.AdvanceFrame(state, AsteroidsControl.None);

        Assert.Equal(AsteroidsEventType.Ticked, transition.Event);
        Assert.Equal(1, transition.State.Tick);
        Assert.True(transition.State.Ship.Position.X > state.Ship.Position.X);
    }

    [Fact]
    public void Held_frame_controls_repeat_on_every_frame()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Small)]);
        var controls = AsteroidsControl.TurnRight | AsteroidsControl.Thrust;

        var first = AsteroidsEngine.AdvanceFrame(state, controls);
        var second = AsteroidsEngine.AdvanceFrame(first.State, controls);

        Assert.Equal(state.Ship.Angle + 0.20, second.State.Ship.Angle, 4);
        Assert.Equal(2, second.State.Tick);
        Assert.True(second.State.Ship.Velocity.Length > first.State.Ship.Velocity.Length);
        Assert.True(second.State.Ship.ThrustTicks > 0);
    }

    [Fact]
    public void Frame_controls_reject_unknown_or_contradictory_turn_bits()
    {
        var state = AsteroidsEngine.Start(7);

        Assert.Throws<ArgumentException>(() => AsteroidsEngine.AdvanceFrame(state, AsteroidsControl.TurnLeft | AsteroidsControl.TurnRight));
        Assert.Throws<ArgumentOutOfRangeException>(() => AsteroidsEngine.AdvanceFrame(state, (AsteroidsControl)16));
    }

    [Fact]
    public void Held_fire_uses_the_existing_weapon_cooldown()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Small)]);

        var first = AsteroidsEngine.AdvanceFrame(state, AsteroidsControl.Fire);
        var second = AsteroidsEngine.AdvanceFrame(first.State, AsteroidsControl.Fire);

        Assert.Single(first.State.Bullets);
        Assert.Single(second.State.Bullets);
        Assert.Equal(first.State.FireCooldownTicks - 1, second.State.FireCooldownTicks);
    }

    [Fact]
    public void Frame_controls_match_the_equivalent_explicit_action_sequence()
    {
        var state = AsteroidsEngine.Start(7);
        var controls = AsteroidsControl.TurnRight | AsteroidsControl.Thrust | AsteroidsControl.Fire;

        var expected = AsteroidsEngine.Apply(
            AsteroidsEngine.Apply(
                AsteroidsEngine.Apply(
                    AsteroidsEngine.Apply(state, AsteroidsAction.RotateRight).State,
                    AsteroidsAction.Thrust).State,
                AsteroidsAction.Fire).State,
            AsteroidsAction.Tick);
        var actual = AsteroidsEngine.AdvanceFrame(state, controls);

        Assert.Equivalent(expected, actual, strict: true);
    }

    [Fact]
    public void Tick_keeps_the_ship_inside_the_field()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(799, 300), new AsteroidsVector(3, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Small)]);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(AsteroidsEventType.Ticked, transition.Event);
        Assert.Equal(788, transition.State.Ship.Position.X);
        Assert.Equal(300, transition.State.Ship.Position.Y);
        Assert.Equal(0, transition.State.Ship.Velocity.X);
    }

    [Fact]
    public void Bullets_leave_the_field_instead_of_wrapping()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Small)],
            [new AsteroidsBullet(2, new AsteroidsVector(799, 300), new AsteroidsVector(3, 0), 10)]);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Empty(transition.State.Bullets);
    }

    [Fact]
    public void Asteroids_bounce_inside_the_field_instead_of_wrapping()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(787, 300), new AsteroidsVector(3, 0), 13, AsteroidSize.Tiny, 2)]);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(787, transition.State.Asteroids.Single().Position.X);
        Assert.Equal(-3, transition.State.Asteroids.Single().Velocity.X);
    }

    [Fact]
    public void Hunter_asteroids_accelerate_toward_the_ship_with_a_bounded_speed()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(200, 100), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 1), 20, AsteroidSize.Small, 4, 0, AsteroidKind.Hunter)]);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);
        var hunter = Assert.Single(transition.State.Asteroids);

        Assert.Equal(100.035, hunter.Position.X, 10);
        Assert.Equal(101, hunter.Position.Y, 10);
        Assert.Equal(0.035, hunter.Velocity.X, 10);
        Assert.Equal(1, hunter.Velocity.Y, 10);
        Assert.InRange(hunter.Velocity.Length, 0, AsteroidsEngine.HunterMaxSpeed);
    }

    [Fact]
    public void Shooting_a_large_asteroid_requires_eight_hits_before_it_splits()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 40, AsteroidSize.Large, 8)],
            [
                new AsteroidsBullet(2, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 10),
                new AsteroidsBullet(3, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 10),
                new AsteroidsBullet(4, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 10),
                new AsteroidsBullet(5, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 10),
                new AsteroidsBullet(6, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 10),
                new AsteroidsBullet(7, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 10),
                new AsteroidsBullet(8, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 10),
                new AsteroidsBullet(9, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 10),
            ]);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(AsteroidsEventType.Hit, transition.Event);
        Assert.Equal(35, transition.ScoreGained);
        Assert.Equal(35, transition.State.Score);
        Assert.Equal(2, transition.State.AsteroidCount);
        Assert.All(transition.State.Asteroids, asteroid => Assert.Equal(AsteroidSize.Medium, asteroid.Size));
        Assert.Empty(transition.State.Bullets);
    }

    [Fact]
    public void Destroying_a_hunter_awards_bonus_points_and_its_fragments_become_drifters()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 20, AsteroidSize.Small, 1, 0, AsteroidKind.Hunter)],
            [new AsteroidsBullet(2, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 10)]);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(155, transition.ScoreGained);
        Assert.Equal(155, transition.State.Score);
        Assert.Equal(2, transition.State.AsteroidCount);
        Assert.All(transition.State.Asteroids, asteroid => Assert.Equal(AsteroidKind.Drifter, asteroid.Kind));
    }

    [Fact]
    public void Asteroid_split_groups_are_unique_and_the_rounded_boulder_breaks_into_matching_halves()
    {
        var rounded = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0.5, 0), 40, AsteroidSize.Large, 1, 1)],
            [new AsteroidsBullet(2, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 10)]);
        var angular = rounded with
        {
            Asteroids = ImmutableArray.Create(new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0.5, 0), 40, AsteroidSize.Large, 1, 0)),
        };

        var roundedSplit = AsteroidsEngine.Apply(rounded, AsteroidsAction.Tick);
        var angularSplit = AsteroidsEngine.Apply(angular, AsteroidsAction.Tick);

        Assert.Equal([5, 6], roundedSplit.State.Asteroids.Select(asteroid => asteroid.SpriteVariant).Order().ToArray());
        Assert.Equal([2, 4], angularSplit.State.Asteroids.Select(asteroid => asteroid.SpriteVariant).Order().ToArray());
        Assert.All(roundedSplit.State.Asteroids.Concat(angularSplit.State.Asteroids), asteroid => Assert.InRange(asteroid.Velocity.Length, 0, AsteroidsEngine.MaxAsteroidSpeed));
    }

    [Fact]
    public void A_bullet_is_consumed_when_it_damages_an_asteroid()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 40, AsteroidSize.Large, 8)],
            [new AsteroidsBullet(2, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 10)]);

        var firstTick = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);
        Assert.Equal(7, firstTick.State.Asteroids.Single().HitPoints);
        Assert.Empty(firstTick.State.Bullets);
    }

    [Fact]
    public void A_fast_bullet_hits_an_asteroid_along_its_travel_path()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Small, 4)],
            [new AsteroidsBullet(2, new AsteroidsVector(80, 100), new AsteroidsVector(40, 0), 10)]);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(3, transition.State.Asteroids.Single().HitPoints);
        Assert.Empty(transition.State.Bullets);
    }

    [Fact]
    public void Clearing_a_wave_spawns_the_next_wave_and_awards_a_bonus()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            []);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(AsteroidsEventType.WaveCleared, transition.Event);
        Assert.Equal(2, transition.State.Wave);
        Assert.Equal(200, transition.ScoreGained);
        Assert.Equal(6, transition.State.AsteroidCount);
        Assert.Equal(1, transition.State.Asteroids.Count(asteroid => asteroid.Kind == AsteroidKind.Hunter));
        Assert.Contains("1 hunter tracking", transition.Message);
    }

    [Fact]
    public void Ship_collision_uses_a_life_and_final_collision_ends_the_game()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), 13, AsteroidSize.Small)],
            lives: 1);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(AsteroidsEventType.GameOver, transition.Event);
        Assert.Equal(AsteroidsPhase.GameOver, transition.State.Phase);
        Assert.Equal(0, transition.State.Lives);
    }

    [Fact]
    public void Ship_collision_preserves_its_current_position_instead_of_resetting_to_center()
    {
        var position = new AsteroidsVector(240, 180);
        var state = State(
            new AsteroidsShip(position, new AsteroidsVector(0, 0), 0.75, 0),
            [new Asteroid(1, position, new AsteroidsVector(0, 0), 13, AsteroidSize.Small)],
            lives: 2);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(AsteroidsEventType.Damaged, transition.Event);
        Assert.Equal(position, transition.State.Ship.Position);
        Assert.Equal(0, transition.State.Ship.Velocity.Length);
        Assert.Equal(0.75, transition.State.Ship.Angle);
        Assert.True(transition.State.Ship.InvulnerabilityTicks > 0);
    }

    [Fact]
    public void Collecting_power_ups_applies_shield_rapid_fire_and_extra_life()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Small)],
            lives: 2) with
        {
            PowerUps = ImmutableArray.Create(
                new AsteroidsPowerUp(2, new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), AsteroidsPowerUpType.Shield, 60),
                new AsteroidsPowerUp(3, new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), AsteroidsPowerUpType.RapidFire, 60),
                new AsteroidsPowerUp(4, new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), AsteroidsPowerUpType.ExtraLife, 60)),
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(AsteroidsEventType.PowerUpCollected, transition.Event);
        Assert.Empty(transition.State.PowerUps);
        Assert.True(transition.State.Ship.InvulnerabilityTicks >= 240);
        Assert.True(transition.State.RapidFireTicks >= 300);
        Assert.Equal(3, transition.State.Lives);
    }

    [Fact]
    public void Descriptor_is_an_arcade_free_play_game()
    {
        var descriptor = AsteroidsModule.Descriptor;

        Assert.Equal("asteroids", descriptor.Id);
        Assert.Equal(GameCategory.Arcade, descriptor.Category);
        Assert.Equal(GameCapability.FreePlay, descriptor.Capabilities);
        descriptor.Validate();
    }

    private static AsteroidsState State(
        AsteroidsShip ship,
        Asteroid[] asteroids,
        AsteroidsBullet[]? bullets = null,
        int lives = 3) => new(
            800,
            600,
            7,
            123456789,
            ship,
            asteroids.ToImmutableArray(),
            (bullets ?? []).ToImmutableArray(),
            100,
            0,
            0,
            0,
            lives,
            1,
            0,
            AsteroidsPhase.Playing);
}
