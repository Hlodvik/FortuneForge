using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.ArcaneArchives.Server;

/// <summary>Arcane owns replay of its admitted outcome, feature progression and settlement projection.</summary>
public static class ArcaneSpinEvidence
{
    public const string EvidenceKind = "arcane-native-paid-round-v1";
    public const string AuditorVersion = "arcane-native-outcome-auditor-v1";
    public const string GameId = ArcaneArchivesEngine.GameId;

    public static bool Owns(string gameId) => gameId == GameId;

    public static IReadOnlyList<RoundAuditFinding> InspectNative(RoundAuditSummary summary, JsonElement data)
    {
        if (summary.AuditorVersion != AuditorVersion || summary.EvidenceKind != EvidenceKind ||
            !data.TryGetProperty("nativeArcaneRound", out var native))
            return [new("slot-owned-evidence-unsupported", "warning",
                "This Arcane receipt predates package-owned native audit evidence and cannot be replayed by the host.")];

        try
        {
            var round = native.Deserialize<ArcanePreparedRound>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing native Arcane admission.");
            var output = round.Outcome;
            if (round.Features is not { Tome: not null, Potions: not null })
                throw new JsonException("Missing admitted Arcane workbench.");
            var beforeTome = round.Features.Tome;
            var beforePotions = round.Features.Potions;
            var potion = ArcanePotions.ForSpin(beforePotions, output.IsFreeSpin);
            var findings = new List<RoundAuditFinding>();
            void Flag(string code, string detail) => findings.Add(new(code, "error", detail));

            var spinId = output.SpinId.ToString("N");
            var wagerCents = output.IsFreeSpin ? 0 : checked((long)(output.WagerPoints * output.PointValueInCents));
            var payoutCents = checked((long)(output.Payout.TotalPoints * output.PointValueInCents));
            if (summary.GameId != ArcaneFeatureEndpoints.AuditGameId || summary.RoundId != spinId || summary.TableId != spinId ||
                summary.StartedAtUtc != round.StartedAtUtc || summary.CompletedAtUtc < round.StartedAtUtc ||
                data.GetProperty("admissionId").GetGuid() != round.AdmissionId ||
                round.Request.UseFreeSpin != output.IsFreeSpin ||
                !output.IsFreeSpin && round.Request.WagerPoints != output.WagerPoints ||
                data.GetProperty("isFreeSpin").GetBoolean() != output.IsFreeSpin ||
                summary.Participants is not [{ IsAccountBacked: true } participant] || participant.ActorId != round.AccountId ||
                participant.WagerCents != wagerCents || participant.PayoutCents != payoutCents ||
                participant.NetCents != payoutCents - wagerCents || round.WagerCents != wagerCents || round.PayoutCents != payoutCents)
                Flag("slot-arcane-financial-mismatch", "The native Arcane admission and wallet amounts differ from the audit summary.");

            var outcomeHash = GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(output, RoundAuditCapture.Json));
            if (outcomeHash != round.OutcomeSha256 || outcomeHash != data.GetProperty("engineOutcomeSha256").GetString())
                Flag("slot-arcane-outcome-mismatch", "The saved native Arcane outcome differs from its admitted digest.");

            ArcaneNativeReplay.Validate(output, round.AccountId, output.WagerPoints, output.IsFreeSpin,
                output.Audit.EngineVersion, beforeTome, potion, round.PityMissesBefore, output.Audit.EnergyBalanceBefore);
            var recordedPayout = data.GetProperty("settledPayout").Deserialize<SpinPayout>(RoundAuditCapture.Json);
            if (!Same(output.Payout, recordedPayout))
                Flag("slot-settlement-mismatch", "The recorded Arcane settlement differs from its replayed native payout.");
            var recordedResponse = data.GetProperty("result").Deserialize<ArcaneSpinResponse>(RoundAuditCapture.Json);
            if (!Same(ArcaneSpinResponse.CaptureOutcome(round), recordedResponse))
                Flag("slot-arcane-response-mismatch", "The public Arcane result differs from the native admitted outcome.");

            var tome = data.GetProperty("arcaneTome");
            var recordedBeforeTome = tome.GetProperty("before").Deserialize<ArcaneTomeState>(RoundAuditCapture.Json);
            var progress = tome.GetProperty("progress").Deserialize<ArcaneTomeProgress>(RoundAuditCapture.Json);
            var expectedTome = ArcaneTome.CompleteSpin(beforeTome, output.Reels, output.IsFreeSpin);
            if (!Same(beforeTome, recordedBeforeTome) || !Same(expectedTome, progress))
                Flag("slot-arcane-tome-mismatch", "The saved Tome progression differs from the admitted page and native result.");

            var potions = data.GetProperty("arcanePotions");
            var recordedBeforePotions = potions.GetProperty("before").Deserialize<ArcanePotionState>(RoundAuditCapture.Json);
            var afterPotions = potions.GetProperty("after").Deserialize<ArcanePotionState>(RoundAuditCapture.Json);
            if (ArcanePotions.LockedWagerForSpin(beforePotions, output.IsFreeSpin) is { } lockedWager && lockedWager != output.WagerPoints)
                Flag("slot-arcane-potion-wager-mismatch", "The native Arcane round differs from the potion's earned wager.");
            var expectedPotions = ArcanePotions.CompleteSpin(beforePotions,
                ArcaneTome.OrderedIngredientUnits(beforeTome, output.Reels, output.IsFreeSpin), output.IsFreeSpin,
                output.WagerPoints, allowRareRecipes: ArcaneArchivesEngine.SupportsRarePotions(output.Audit.EngineVersion));
            if (!Same(beforePotions, recordedBeforePotions) || !Same(expectedPotions, afterPotions))
                Flag("slot-arcane-potion-mismatch", "The saved potion progression differs from the native admitted workbench.");
            return findings;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or
            OverflowException or KeyNotFoundException or NullReferenceException or FormatException)
        {
            return [new("slot-arcane-native-proof-mismatch", "error", "The native Arcane round or its captured evidence cannot be replayed.")];
        }
    }

    private static bool Same<T>(T expected, T actual) =>
        JsonSerializer.Serialize(expected, RoundAuditCapture.Json) == JsonSerializer.Serialize(actual, RoundAuditCapture.Json);
}
