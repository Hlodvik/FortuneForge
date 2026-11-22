using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RobotRevolution.Server;

/// <summary>Nonfinancial demo admission reuses the game's paid mode rules and response contract.</summary>
public static class FifthSunDemo
{
    public static RobotRevolutionSpinResponse Spin(long wagerPoints, JsonElement? paidMode = null,
        IGameRandomSource? random = null)
    {
        string? nativeState = null;
        if (paidMode is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } carry)
        {
            try
            {
                nativeState = JsonSerializer.Serialize(new { contractVersion = 1, paidMode = carry }, RoundAuditCapture.Json);
                FifthSunPaidModeState.Validate(nativeState);
            }
            catch (InvalidOperationException exception)
            {
                throw new ArgumentException("The demo Eclipse state is invalid.", nameof(paidMode), exception);
            }
        }

        var before = new ClassicSlotRoundState(0, 0, null, 0, 0, 0,
            new Dictionary<string, long>(), new Dictionary<string, long>()) { NativeStateJson = nativeState };
        try
        {
            var round = RobotRevolutionRoundPolicy.Prepare("public-demo",
                new("demo-" + Guid.NewGuid().ToString("N"), wagerPoints, false), before, DateTime.UtcNow,
                null, random ?? new CryptographicSlotRandom());
            return RobotRevolutionSpinResponse.Capture(round) with
            {
                SlotsCreditsBalance = null,
                FreeSpinsRemaining = 0,
                FreeSpinWagerPoints = null,
                FreeSpinFeatureMode = null,
                SpecialPointsBalance = round.Progress.After.SpecialPoints
            };
        }
        catch (FifthSunPaidModeWagerLockedException exception)
        {
            throw new ArgumentException(exception.Message, nameof(wagerPoints), exception);
        }
    }
}
