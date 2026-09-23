using System.Text.Json;
using FortuneForge.Games.Asteroids;

namespace FortuneForge.Games.Tests.Asteroids;

public sealed class AsteroidsReplayVectorFixtureTests
{
    [Fact]
    public void Shared_replay_vectors_match_the_authoritative_engine()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        var root = fixture.RootElement;
        var tolerance = root.GetProperty("floatTolerance").GetDouble();
        var vectors = root.GetProperty("vectors").EnumerateArray().ToArray();
        Assert.Contains(vectors, vector => vector.GetProperty("name").GetString() == "held-fire-cooldown-scoring");
        Assert.Contains(vectors, vector => vector.GetProperty("name").GetString() == "game-over-no-control");
        Assert.Contains(vectors, vector => vector.GetProperty("name").GetString() == "long-surviving-time-cap");

        foreach (var vector in vectors)
            AssertSnapshot(vector.GetProperty("expected"), Replay(vector), tolerance);
    }

    private static AsteroidsState Replay(JsonElement vector)
    {
        var seed64 = Convert.ToUInt64(vector.GetProperty("seedHex").GetString(), 16);
        var seed = (uint)seed64 ^ (uint)(seed64 >> 32);
        var state = AsteroidsEngine.Start(seed);
        var commands = vector.GetProperty("commands").EnumerateArray().ToArray();
        var totalSteps = vector.GetProperty("totalSteps").GetInt32();
        var commandIndex = 0;
        var control = AsteroidsControl.None;
        for (var step = 0; step < totalSteps && state.Phase == AsteroidsPhase.Playing; step++)
        {
            if (commandIndex < commands.Length && commands[commandIndex].GetProperty("step").GetInt32() == step)
                control = (AsteroidsControl)commands[commandIndex++].GetProperty("control").GetInt32();
            state = AsteroidsEngine.AdvanceFrame(state, control).State;
        }
        return state;
    }

    // JavaScript and .NET both use IEEE-754 doubles, but transcendental functions can differ by a
    // few ulps across runtimes. Discrete simulation outcomes remain exact; geometry uses this bound.
    private static void AssertSnapshot(JsonElement expected, AsteroidsState actual, double tolerance)
    {
        Assert.Equal(expected.GetProperty("score").GetInt32(), actual.Score);
        Assert.Equal(expected.GetProperty("lives").GetInt32(), actual.Lives);
        Assert.Equal(expected.GetProperty("wave").GetInt32(), actual.Wave);
        Assert.Equal(expected.GetProperty("phase").GetString(), actual.Phase == AsteroidsPhase.GameOver ? "game-over" : "playing");
        Assert.Equal(expected.GetProperty("tick").GetInt32(), actual.Tick);
        Assert.Equal(expected.GetProperty("randomState").GetUInt32(), actual.RandomState);
        Assert.Equal(expected.GetProperty("asteroidCount").GetInt32(), actual.Asteroids.Length);
        Assert.Equal(expected.GetProperty("bulletCount").GetInt32(), actual.Bullets.Length);
        Assert.Equal(expected.GetProperty("powerUpCount").GetInt32(), actual.PowerUps.IsDefault ? 0 : actual.PowerUps.Length);
        AssertShip(expected.GetProperty("ship"), actual.Ship, tolerance);
        AssertAsteroid(expected.GetProperty("asteroid"), actual.Asteroids.OrderBy(asteroid => asteroid.Id).FirstOrDefault(), tolerance);
        AssertBullet(expected.GetProperty("bullet"), actual.Bullets.OrderBy(bullet => bullet.Id).FirstOrDefault(), tolerance);
    }

    private static void AssertShip(JsonElement expected, AsteroidsShip actual, double tolerance)
    {
        Close(expected.GetProperty("x").GetDouble(), actual.Position.X, tolerance);
        Close(expected.GetProperty("y").GetDouble(), actual.Position.Y, tolerance);
        Close(expected.GetProperty("velocityX").GetDouble(), actual.Velocity.X, tolerance);
        Close(expected.GetProperty("velocityY").GetDouble(), actual.Velocity.Y, tolerance);
        Close(expected.GetProperty("angle").GetDouble(), actual.Angle, tolerance);
        Assert.Equal(expected.GetProperty("invulnerabilityTicks").GetInt32(), actual.InvulnerabilityTicks);
        Assert.Equal(expected.GetProperty("thrustTicks").GetInt32(), actual.ThrustTicks);
    }

    private static void AssertAsteroid(JsonElement expected, Asteroid? actual, double tolerance)
    {
        if (expected.ValueKind == JsonValueKind.Null) { Assert.Null(actual); return; }
        actual = Assert.IsType<Asteroid>(actual);
        Assert.Equal(expected.GetProperty("id").GetInt32(), actual.Id);
        Assert.Equal(expected.GetProperty("radius").GetDouble(), actual.Radius);
        Assert.Equal(expected.GetProperty("size").GetString(), actual.Size.ToString().ToLowerInvariant());
        Assert.Equal(expected.GetProperty("hitPoints").GetInt32(), actual.HitPoints);
        Assert.Equal(expected.GetProperty("spriteVariant").GetInt32(), actual.SpriteVariant);
        Close(expected.GetProperty("x").GetDouble(), actual.Position.X, tolerance);
        Close(expected.GetProperty("y").GetDouble(), actual.Position.Y, tolerance);
        Close(expected.GetProperty("velocityX").GetDouble(), actual.Velocity.X, tolerance);
        Close(expected.GetProperty("velocityY").GetDouble(), actual.Velocity.Y, tolerance);
    }

    private static void AssertBullet(JsonElement expected, AsteroidsBullet? actual, double tolerance)
    {
        if (expected.ValueKind == JsonValueKind.Null) { Assert.Null(actual); return; }
        actual = Assert.IsType<AsteroidsBullet>(actual);
        Assert.Equal(expected.GetProperty("id").GetInt32(), actual.Id);
        Assert.Equal(expected.GetProperty("remainingTicks").GetInt32(), actual.RemainingTicks);
        Close(expected.GetProperty("x").GetDouble(), actual.Position.X, tolerance);
        Close(expected.GetProperty("y").GetDouble(), actual.Position.Y, tolerance);
        Close(expected.GetProperty("velocityX").GetDouble(), actual.Velocity.X, tolerance);
        Close(expected.GetProperty("velocityY").GetDouble(), actual.Velocity.Y, tolerance);
    }

    private static void Close(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}; actual {actual:R}; tolerance {tolerance:R}.");

    private static string FixturePath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../games/Asteroids/client/FortuneForge.Games.Asteroids.Client/src/asteroidsReplayVectors.json"));
}
