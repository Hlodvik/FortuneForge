using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.RobotRevolution.Server;

public sealed record FifthSunPaidMode(string Mode, int Remaining, long WagerPoints);

/// <summary>The game owns its paid Eclipse state; the neutral store transports its opaque versioned document.</summary>
internal static class FifthSunPaidModeState
{
    private sealed record Document(int ContractVersion, FifthSunPaidMode PaidMode);

    internal static FifthSunPaidMode? Read(ClassicSlotRoundState state)
    {
        var mode = Decode(state.NativeStateJson);
        if (mode is not null && (state.Carry is not null || state.NativeRemaining != 0 || state.LegacyRemaining != 0))
            throw new InvalidOperationException("A paid Eclipse cannot overlap a historical free-game bank.");
        return mode;
    }

    internal static void Validate(string value) => _ = Decode(value);

    internal static string? Write(FifthSunPaidMode? mode)
    {
        if (mode is null) return null;
        var value = JsonSerializer.Serialize(new Document(1, mode), RoundAuditCapture.Json);
        Validate(value);
        return value;
    }

    private static FifthSunPaidMode? Decode(string? value)
    {
        if (value is null) return null;
        try
        {
            if (value.Length > 512) throw Invalid();
            using var json = JsonDocument.Parse(value);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                root.GetProperty("contractVersion").GetInt32() != 1) throw Invalid();
            var native = root.GetProperty("paidMode");
            if (native.ValueKind != JsonValueKind.Object || native.EnumerateObject().Count() != 3) throw Invalid();
            var mode = native.Deserialize<FifthSunPaidMode>(RoundAuditCapture.Json) ?? throw Invalid();
            if (!FifthSunTotalityDefinitions.IsMode(mode.Mode) || mode.Remaining is < 1 or > FifthSunTotalityDefinitions.EclipseRounds)
                throw Invalid();
            RobotRevolutionEngine.ValidateWager(mode.WagerPoints, false);
            return mode;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidOperationException("The saved Fifth Sun Eclipse state is invalid.", error);
        }
    }

    private static InvalidOperationException Invalid() => new("The saved Fifth Sun Eclipse state is invalid.");
}
