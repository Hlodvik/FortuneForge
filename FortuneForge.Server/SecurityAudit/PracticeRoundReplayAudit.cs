using System.Collections.Immutable;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Craps;
using FortuneForge.Games.Dice;
using FortuneForge.Games.DropMerge;
using FortuneForge.Games.Roulette;
using FortuneForge.Games.TwentyFortyEight;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Recomputes named non-credit outcome checks from saved DB evidence, without trusting capture-time verdicts.</summary>
internal static class PracticeRoundReplayAudit
{
    internal static IReadOnlyList<RoundAuditFinding> Inspect(RoundAuditSummary summary, string payloadJson)
    {
        if (!Supported(summary)) return [];
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            return summary.GameId switch
            {
                "2048" => PracticeOutcomeAudit.Check(JsonSerializer.Deserialize<TwentyFortyEightState>(
                    root.GetProperty("state"), RoundAuditCapture.Json) ?? throw new JsonException("Missing 2048 state.")),
                "drop-merge" => PracticeOutcomeAudit.Check(JsonSerializer.Deserialize<DropMergeState>(
                    root.GetProperty("state"), RoundAuditCapture.Json) ?? throw new JsonException("Missing Drop Merge state."),
                    root.GetProperty("dropColumn").GetInt32(), root.GetProperty("dropRow").GetInt32()),
                "craps" => Craps(root),
                "roulette" => Roulette(root),
                _ => []
            };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
               ArgumentException or FormatException or NotSupportedException or OverflowException)
        {
            return [new("practice-outcome-evidence-invalid", "error",
                "The saved terminal evidence cannot be parsed for independent outcome review.")];
        }
    }

    internal static bool Supported(RoundAuditSummary summary) => (summary.GameId, summary.AuditorVersion) switch
    {
        ("2048", PracticeOutcomeAudit.TwentyFortyEightVersion) => true,
        ("drop-merge", PracticeOutcomeAudit.DropMergeVersion) => true,
        ("craps", PracticeOutcomeAudit.CrapsVersion) => true,
        ("roulette", PracticeOutcomeAudit.RouletteVersion) => true,
        _ => false
    };

    private static IReadOnlyList<RoundAuditFinding> Craps(JsonElement root)
    {
        var value = root.GetProperty("state");
        var bet = value.GetProperty("bet");
        var last = value.GetProperty("lastOutcome");
        var state = new CrapsPassLineState(
            new CrapsPassLineBet(bet.GetProperty("playerId").GetString() ?? "", bet.GetProperty("stake").GetDecimal()),
            (CrapsPassLinePhase)value.GetProperty("phase").GetInt32(),
            NullableInt(value.GetProperty("point")),
            value.GetProperty("rolls").EnumerateArray().Select(Dice).ToImmutableArray(),
            new CrapsRollOutcome(Dice(last.GetProperty("dice")),
                (CrapsRollResult)last.GetProperty("result").GetInt32(),
                last.GetProperty("isTerminal").GetBoolean(), NullableDecimal(last.GetProperty("totalReturn"))));
        var extras = root.GetProperty("extraBets").EnumerateArray().Select(value => new PracticeCrapsExtraBet(
            value.GetProperty("kind").GetString() ?? "", value.GetProperty("stake").GetDecimal(),
            value.GetProperty("resolved").GetBoolean(), value.GetProperty("won").GetBoolean(),
            NullableDecimal(value.GetProperty("totalReturn")))).ToArray();
        return PracticeOutcomeAudit.Check(state, extras);
    }

    private static IReadOnlyList<RoundAuditFinding> Roulette(JsonElement root)
    {
        var value = root.GetProperty("state");
        var bets = value.GetProperty("bets").EnumerateArray().Select(bet => new RouletteBet(
            bet.GetProperty("playerId").GetString() ?? "", (RouletteBetKind)bet.GetProperty("kind").GetInt32(),
            bet.GetProperty("stake").GetDecimal(), NullableInt(bet.GetProperty("number")),
            bet.GetProperty("numbers").EnumerateArray().Select(number => number.GetInt32()).ToImmutableArray())).ToImmutableArray();
        var settlements = value.GetProperty("settlements").EnumerateArray().Select(item => new RouletteSettlement(
            item.GetProperty("playerId").GetString() ?? "", (RouletteBetKind)item.GetProperty("kind").GetInt32(),
            item.GetProperty("stake").GetDecimal(), item.GetProperty("won").GetBoolean(),
            item.GetProperty("totalReturn").GetDecimal())).ToImmutableArray();
        var state = new RouletteRoundState(value.GetProperty("roundId").GetString() ?? "",
            (RouletteRoundPhase)value.GetProperty("phase").GetInt32(), bets,
            new RoulettePocket(value.GetProperty("winningPocket").GetProperty("number").GetInt32()), settlements);
        return PracticeOutcomeAudit.Check(state);
    }

    private static DicePair Dice(JsonElement value) => new(
        new DieValue(value.GetProperty("first").GetProperty("value").GetInt32()),
        new DieValue(value.GetProperty("second").GetProperty("value").GetInt32()));
    private static int? NullableInt(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
    private static decimal? NullableDecimal(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();
}
