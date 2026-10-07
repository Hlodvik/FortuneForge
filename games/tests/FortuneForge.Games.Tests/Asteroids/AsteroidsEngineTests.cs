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
        Assert.Null(first.AlienShip);
        Assert.Empty(first.EnemyBullets);
        Assert.Equal(270, first.AlienSpawnCooldownTicks);
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

        Assert.Equal(expected.Event, actual.Event);
        Assert.Equal(expected.ScoreGained, actual.ScoreGained);
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal(expected.State.Width, actual.State.Width);
        Assert.Equal(expected.State.Height, actual.State.Height);
        Assert.Equal(expected.State.Seed, actual.State.Seed);
        Assert.Equal(expected.State.RandomState, actual.State.RandomState);
        Assert.Equal(expected.State.Ship, actual.State.Ship);
        Assert.Equal(expected.State.Asteroids.ToArray(), actual.State.Asteroids.ToArray());
        Assert.Equal(expected.State.Bullets.ToArray(), actual.State.Bullets.ToArray());
        Assert.Equal(expected.State.PowerUps.ToArray(), actual.State.PowerUps.ToArray());
        Assert.Equal(expected.State.NextEntityId, actual.State.NextEntityId);
        Assert.Equal(expected.State.FireCooldownTicks, actual.State.FireCooldownTicks);
        Assert.Equal(expected.State.Score, actual.State.Score);
        Assert.Equal(expected.State.BestScore, actual.State.BestScore);
        Assert.Equal(expected.State.Lives, actual.State.Lives);
        Assert.Equal(expected.State.Wave, actual.State.Wave);
        Assert.Equal(expected.State.Tick, actual.State.Tick);
        Assert.Equal(expected.State.Phase, actual.State.Phase);
        Assert.Equal(expected.State.RapidFireTicks, actual.State.RapidFireTicks);
        Assert.Equal(expected.State.AlienShip, actual.State.AlienShip);
        Assert.Equal(expected.State.EnemyBullets.ToArray(), actual.State.EnemyBullets.ToArray());
        Assert.Equal(expected.State.AlienSpawnCooldownTicks, actual.State.AlienSpawnCooldownTicks);
    }

    [Fact]
    public void Tick_wraps_the_ship_across_the_left_and_top_edges_without_stopping_it()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(1, 1), new AsteroidsVector(-3, -3), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Small)]);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(AsteroidsEventType.Ticked, transition.Event);
        Assert.Equal(new AsteroidsVector(798, 598), transition.State.Ship.Position);
        Assert.Equal(new AsteroidsVector(-2.985, -2.985), transition.State.Ship.Velocity);
    }

    [Fact]
    public void Bullets_wrap_at_the_field_edge_while_their_lifetime_remains()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Small)],
            [new AsteroidsBullet(2, new AsteroidsVector(799, 300), new AsteroidsVector(3, 0), 10)]);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        var bullet = Assert.Single(transition.State.Bullets);
        Assert.Equal(new AsteroidsVector(2, 300), bullet.Position);
        Assert.Equal(9, bullet.RemainingTicks);
    }

    [Fact]
    public void Asteroids_wrap_across_the_right_and_bottom_edges_without_reversing()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(799, 599), new AsteroidsVector(3, 4), 13, AsteroidSize.Tiny, 2)]);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        var asteroid = Assert.Single(transition.State.Asteroids);
        Assert.Equal(new AsteroidsVector(2, 3), asteroid.Position);
        Assert.Equal(new AsteroidsVector(3, 4), asteroid.Velocity);
    }

    [Fact]
    public void Power_ups_wrap_at_the_field_edge()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Small)]) with
        {
            PowerUps = ImmutableArray.Create(new AsteroidsPowerUp(
                2,
                new AsteroidsVector(799, 599),
                new AsteroidsVector(3, 4),
                AsteroidsPowerUpType.Shield,
                60)),
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(new AsteroidsVector(2, 3), Assert.Single(transition.State.PowerUps).Position);
    }

    [Fact]
    public void A_bullet_crossing_a_seam_hits_an_asteroid_on_the_opposite_edge()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(1, 300), new AsteroidsVector(0, 0), 13, AsteroidSize.Small, 4)],
            [new AsteroidsBullet(2, new AsteroidsVector(799, 300), new AsteroidsVector(3, 0), 10)]);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(3, Assert.Single(transition.State.Asteroids).HitPoints);
        Assert.Empty(transition.State.Bullets);
    }

    [Fact]
    public void Ship_and_power_up_collisions_are_seam_aware()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(2, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(790, 300), new AsteroidsVector(0, 0), 13, AsteroidSize.Small)],
            lives: 2) with
        {
            PowerUps = ImmutableArray.Create(new AsteroidsPowerUp(
                2,
                new AsteroidsVector(795, 300),
                new AsteroidsVector(0, 0),
                AsteroidsPowerUpType.ExtraLife,
                60)),
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(AsteroidsEventType.Damaged, transition.Event);
        Assert.Empty(transition.State.PowerUps);
        Assert.Equal(2, transition.State.Lives);
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
    public void Player_bullet_sweep_does_not_report_a_hit_when_a_moving_asteroid_stays_clear()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(170, 114.2), new AsteroidsVector(3, -1.4), 13, AsteroidSize.Tiny, 2)],
            [new AsteroidsBullet(2, new AsteroidsVector(170, 100), new AsteroidsVector(17, 0), 10)]);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(2, Assert.Single(transition.State.Asteroids).HitPoints);
        Assert.Single(transition.State.Bullets);
    }

    [Fact]
    public void Player_bullet_sweep_reports_a_hit_that_a_moving_asteroid_cannot_escape()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(170, 112.5), new AsteroidsVector(-3, 1.4), 13, AsteroidSize.Tiny, 2)],
            [new AsteroidsBullet(2, new AsteroidsVector(170, 100), new AsteroidsVector(17, 0), 10)]);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(1, Assert.Single(transition.State.Asteroids).HitPoints);
        Assert.Empty(transition.State.Bullets);
    }

    [Fact]
    public void Clearing_a_wave_spawns_the_next_wave_and_awards_a_bonus()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            []) with
        {
            AlienShip = Alien(AsteroidsAlienShipType.Scout, new AsteroidsVector(100, 100), new AsteroidsVector(2.2, 0)),
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(AsteroidsEventType.WaveCleared, transition.Event);
        Assert.Equal(2, transition.State.Wave);
        Assert.Equal(200, transition.ScoreGained);
        Assert.Equal(6, transition.State.AsteroidCount);
        Assert.NotNull(transition.State.AlienShip);
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
    public void Alien_first_appears_on_tick_270_and_wave_one_always_spawns_a_scout()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)]) with
        {
            AlienSpawnCooldownTicks = 270,
        };

        for (var tick = 0; tick < 269; tick++)
            state = AsteroidsEngine.Apply(state, AsteroidsAction.Tick).State;

        Assert.Null(state.AlienShip);
        Assert.Equal(1, state.AlienSpawnCooldownTicks);

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);
        var alien = Assert.IsType<AsteroidsAlienShip>(transition.State.AlienShip);

        Assert.Equal(AsteroidsAlienShipType.Scout, alien.Type);
        Assert.Equal(20, alien.Radius);
        Assert.Equal(2, alien.HitPoints);
        Assert.Equal(2.2, Math.Abs(alien.Velocity.X), 6);
        Assert.True(alien.Position.X is -20 or 820);
        Assert.InRange(alien.Position.Y, alien.Radius, transition.State.Height - alien.Radius);
        Assert.InRange(alien.FireCooldownTicks, 65, 105);
        Assert.InRange(alien.CourseChangeTicks, 60, 90);
        Assert.Equal(420, alien.RemainingTicks);
        Assert.Equal(0, transition.State.AlienSpawnCooldownTicks);
    }

    [Fact]
    public void Later_waves_can_deterministically_spawn_the_faster_hunter()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)]) with
        {
            Wave = 7,
            AlienSpawnCooldownTicks = 1,
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);
        var alien = Assert.IsType<AsteroidsAlienShip>(transition.State.AlienShip);

        Assert.Equal(AsteroidsAlienShipType.Hunter, alien.Type);
        Assert.Equal(16, alien.Radius);
        Assert.Equal(4, alien.HitPoints);
        Assert.Equal(3, Math.Abs(alien.Velocity.X), 6);
        Assert.InRange(alien.FireCooldownTicks, 38, 64);
        Assert.InRange(alien.CourseChangeTicks, 42, 66);
        Assert.Equal(360, alien.RemainingTicks);
    }

    [Fact]
    public void Alien_crosses_horizontally_without_wrapping_but_wraps_vertically_and_fires_toroidally()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(100, 2), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)]) with
        {
            AlienShip = new AsteroidsAlienShip(
                90,
                new AsteroidsVector(799, 599),
                new AsteroidsVector(3, 3),
                16,
                AsteroidsAlienShipType.Hunter,
                4,
                1,
                2,
                100),
            AlienSpawnCooldownTicks = 0,
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);
        var alien = Assert.IsType<AsteroidsAlienShip>(transition.State.AlienShip);
        var shot = Assert.Single(transition.State.EnemyBullets);

        Assert.Equal(new AsteroidsVector(802, 2), alien.Position);
        Assert.Equal(1, alien.CourseChangeTicks);
        Assert.InRange(alien.FireCooldownTicks, 38, 64);
        Assert.Equal(5.4, shot.Velocity.Length, 6);
        Assert.True(shot.Velocity.X > 0);
        Assert.Equal(120, shot.RemainingTicks);
    }

    [Fact]
    public void Alien_course_and_fire_timers_advance_independently()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)]) with
        {
            AlienShip = new AsteroidsAlienShip(
                90,
                new AsteroidsVector(200, 200),
                new AsteroidsVector(2.2, 0.4),
                20,
                AsteroidsAlienShipType.Scout,
                2,
                10,
                1,
                100),
            AlienSpawnCooldownTicks = 0,
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);
        var alien = Assert.IsType<AsteroidsAlienShip>(transition.State.AlienShip);

        Assert.Equal(9, alien.FireCooldownTicks);
        Assert.InRange(alien.CourseChangeTicks, 60, 90);
        Assert.InRange(alien.Velocity.Y, -0.85, 0.85);
        Assert.Empty(transition.State.EnemyBullets);
    }

    [Fact]
    public void Enemy_bullets_wrap_and_hit_the_ship_across_a_seam()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(2, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)]) with
        {
            EnemyBullets = ImmutableArray.Create(new AsteroidsEnemyBullet(
                91,
                new AsteroidsVector(799, 300),
                new AsteroidsVector(3, 0),
                10)),
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(2, transition.State.Lives);
        Assert.Empty(transition.State.EnemyBullets);
    }

    [Fact]
    public void Enemy_bullet_sweep_does_not_report_a_hit_when_a_moving_ship_stays_clear()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(100, 300), new AsteroidsVector(7, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(400, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)]) with
        {
            EnemyBullets = ImmutableArray.Create(new AsteroidsEnemyBullet(
                91,
                new AsteroidsVector(117, 300),
                new AsteroidsVector(5.4, 0),
                10)),
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(3, transition.State.Lives);
        Assert.Single(transition.State.EnemyBullets);
    }

    [Fact]
    public void Enemy_bullet_sweep_reports_a_hit_that_a_moving_ship_cannot_escape_across_a_wrap()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(798, 300), new AsteroidsVector(7, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(400, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)]) with
        {
            EnemyBullets = ImmutableArray.Create(new AsteroidsEnemyBullet(
                91,
                new AsteroidsVector(787, 300),
                new AsteroidsVector(5.4, 0),
                10)),
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(2, transition.State.Lives);
        Assert.Empty(transition.State.EnemyBullets);
    }

    [Theory]
    [InlineData(AsteroidsAlienShipType.Scout, 20, 300)]
    [InlineData(AsteroidsAlienShipType.Hunter, 16, 750)]
    public void Player_shots_destroy_aliens_and_award_their_distinct_scores(
        AsteroidsAlienShipType type,
        double radius,
        int expectedScore)
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)],
            [new AsteroidsBullet(2, new AsteroidsVector(170, 200), new AsteroidsVector(40, 0), 10)]) with
        {
            AlienShip = new AsteroidsAlienShip(90, new AsteroidsVector(200, 200), new AsteroidsVector(0, 0), radius, type, 1, 10, 10, 100),
            AlienSpawnCooldownTicks = 0,
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(AsteroidsEventType.Hit, transition.Event);
        Assert.Null(transition.State.AlienShip);
        Assert.Empty(transition.State.Bullets);
        Assert.Equal(expectedScore, transition.ScoreGained);
        Assert.Equal(expectedScore, transition.State.Score);
        Assert.InRange(transition.State.AlienSpawnCooldownTicks, 342, 432);
    }

    [Fact]
    public void Player_shots_can_damage_an_alien_without_destroying_it()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)],
            [new AsteroidsBullet(2, new AsteroidsVector(170, 200), new AsteroidsVector(40, 0), 10)]) with
        {
            AlienShip = Alien(AsteroidsAlienShipType.Scout, new AsteroidsVector(200, 200), new AsteroidsVector(0, 0)),
            AlienSpawnCooldownTicks = 0,
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(1, Assert.IsType<AsteroidsAlienShip>(transition.State.AlienShip).HitPoints);
        Assert.Equal(0, transition.ScoreGained);
        Assert.Empty(transition.State.Bullets);
    }

    [Fact]
    public void Player_bullet_sweep_does_not_report_a_hit_when_a_moving_alien_stays_clear()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 400), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)],
            [new AsteroidsBullet(2, new AsteroidsVector(170, 100), new AsteroidsVector(17, 0), 10)]) with
        {
            AlienShip = Alien(AsteroidsAlienShipType.Hunter, new AsteroidsVector(170, 117.2), new AsteroidsVector(3, -1.4)),
            AlienSpawnCooldownTicks = 0,
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(4, Assert.IsType<AsteroidsAlienShip>(transition.State.AlienShip).HitPoints);
        Assert.Single(transition.State.Bullets);
    }

    [Fact]
    public void Player_bullet_sweep_reports_a_hit_that_a_moving_alien_cannot_escape()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 400), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)],
            [new AsteroidsBullet(2, new AsteroidsVector(170, 100), new AsteroidsVector(17, 0), 10)]) with
        {
            AlienShip = Alien(AsteroidsAlienShipType.Hunter, new AsteroidsVector(170, 115.5), new AsteroidsVector(-3, 1.4)),
            AlienSpawnCooldownTicks = 0,
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(3, Assert.IsType<AsteroidsAlienShip>(transition.State.AlienShip).HitPoints);
        Assert.Empty(transition.State.Bullets);
    }

    [Fact]
    public void Alien_exit_starts_an_organic_respawn_cooldown()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)]) with
        {
            AlienShip = Alien(AsteroidsAlienShipType.Scout, new AsteroidsVector(819, 300), new AsteroidsVector(2.2, 0)),
            AlienSpawnCooldownTicks = 0,
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Null(transition.State.AlienShip);
        Assert.InRange(transition.State.AlienSpawnCooldownTicks, 342, 432);
    }

    [Fact]
    public void Enemy_shots_and_an_alien_body_cost_at_most_one_life_in_a_tick()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 0),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)],
            [new AsteroidsBullet(2, new AsteroidsVector(700, 500), new AsteroidsVector(0, 0), 10)]) with
        {
            AlienShip = Alien(AsteroidsAlienShipType.Scout, new AsteroidsVector(400, 300), new AsteroidsVector(0, 0)),
            EnemyBullets = ImmutableArray.Create(
                new AsteroidsEnemyBullet(91, new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), 10),
                new AsteroidsEnemyBullet(92, new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), 10)),
            AlienSpawnCooldownTicks = 0,
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(AsteroidsEventType.Damaged, transition.Event);
        Assert.Equal(2, transition.State.Lives);
        Assert.Null(transition.State.AlienShip);
        Assert.Empty(transition.State.EnemyBullets);
        Assert.Empty(transition.State.Bullets);
    }

    [Fact]
    public void Shielded_ship_consumes_enemy_shots_without_losing_a_life()
    {
        var state = State(
            new AsteroidsShip(new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), -Math.PI / 2, 10),
            [new Asteroid(1, new AsteroidsVector(100, 100), new AsteroidsVector(0, 0), 13, AsteroidSize.Tiny, 2)]) with
        {
            EnemyBullets = ImmutableArray.Create(new AsteroidsEnemyBullet(91, new AsteroidsVector(400, 300), new AsteroidsVector(0, 0), 10)),
        };

        var transition = AsteroidsEngine.Apply(state, AsteroidsAction.Tick);

        Assert.Equal(3, transition.State.Lives);
        Assert.Empty(transition.State.EnemyBullets);
        Assert.Equal(9, transition.State.Ship.InvulnerabilityTicks);
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

    private static AsteroidsAlienShip Alien(
        AsteroidsAlienShipType type,
        AsteroidsVector position,
        AsteroidsVector velocity) => new(
            90,
            position,
            velocity,
            type == AsteroidsAlienShipType.Scout ? 20 : 16,
            type,
            type == AsteroidsAlienShipType.Scout ? 2 : 4,
            10,
            10,
            100);
}
