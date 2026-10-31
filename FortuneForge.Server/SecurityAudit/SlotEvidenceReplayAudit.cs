using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Activity;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.Accounts.Models;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Rechecks paid and public-demo spins from persisted, frozen rules and outcomes.</summary>
internal static class SlotEvidenceReplayAudit
{
    internal static IReadOnlyList<RoundAuditFinding> Inspect(RoundAuditSummary summary, string payloadJson)
    {
        if (summary.AuditorVersion != SlotRoundAudit.AuditorVersion ||
            !AcceptedGameActivityReader.SlotIds.Values.Contains(summary.GameId, StringComparer.Ordinal))
            return [];

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var demo = summary.EvidenceKind == "slot-demo-nonfinancial-v1";
            var data = demo ? document.RootElement : document.RootElement.GetProperty("data");
            var result = data.GetProperty(demo ? "engineResult" : "result")
                .Deserialize<SpinResult>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing slot result.");
            // SpinResult.EngineAudit is omitted from JSON because it is private engine data;
            // the settlement receipt stores a separate frozen copy beside the result.
            var engineAudit = data.GetProperty("engineAudit").Deserialize<SlotSpinEngineAudit>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing slot engine audit.");
            var response = demo ? data.GetProperty("responseResult").Deserialize<SpinResult>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing demo response.") : null;
            var settledPayout = demo ? response!.Payout :
                data.GetProperty("settledPayout").Deserialize<SpinPayout>(RoundAuditCapture.Json)
                    ?? throw new JsonException("Missing settled slot payout.");

            var findings = SlotRoundAudit.Check(result with { EngineAudit = engineAudit }, settledPayout).ToList();
            if (!AcceptedGameActivityReader.SlotIds.TryGetValue(result.GameId, out var catalogueGameId) ||
                catalogueGameId != summary.GameId ||
                (demo ? "demo:" : string.Empty) + result.SpinId.ToString("N") != summary.RoundId ||
                (demo && summary.TableId != summary.RoundId))
                findings.Add(new("slot-evidence-identity-mismatch", "error",
                    "The saved spin identity differs from its audit summary."));
            if (demo)
                findings.AddRange(InspectDemo(data, result, response!, engineAudit));
            else if (result.GameId == SlotSpecialRoundProfiles.PiratesFortuneGameId &&
                engineAudit.FeatureProfile?.CollectionTarget == PiratesGemBonus.MaximumGems)
                findings.AddRange(InspectPiratesGems(data, result, engineAudit.FeatureProfile));
            return findings;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            ArgumentException or OverflowException or IndexOutOfRangeException or
            KeyNotFoundException or DivideByZeroException or NullReferenceException or
            NotSupportedException or FormatException)
        {
            return [new("slot-evidence-replay-invalid", "error",
                "The saved slot result or frozen engine rules cannot be parsed for independent outcome review.")];
        }
    }

    private static IReadOnlyList<RoundAuditFinding> InspectPiratesGems(
        JsonElement data, SpinResult result, SlotSpecialRoundProfile profile)
    {
        var evidence = data.GetProperty("piratesGemEvidence")
            .Deserialize<PiratesGemSettlementEvidence>(RoundAuditCapture.Json)
            ?? throw new JsonException("Missing Pirates gem settlement evidence.");
        if (evidence.RuleVersion != PiratesGemBonus.RuleVersion)
            throw new JsonException("Unsupported Pirates gem chance rule version.");
        var counts = evidence.CountsBefore;
        var wagerCents = evidence.WagerCentsBefore;
        if (counts is null || wagerCents is null || evidence.CountsAfter is null ||
            evidence.WagerCentsAfter is null ||
            PiratesGemBonus.Modes.Any(mode => !counts.ContainsKey(mode) ||
                !wagerCents.ContainsKey(mode) || !evidence.CountsAfter.ContainsKey(mode) ||
                !evidence.WagerCentsAfter.ContainsKey(mode)))
            throw new JsonException("Incomplete Pirates gem collection state.");

        var awardedByMode = PiratesGemBonus.Modes.ToDictionary(
            mode => mode, _ => 0, StringComparer.Ordinal);
        foreach (var (symbolId, amount) in result.SealsAwarded)
        {
            if (amount > 0 && PiratesGemBonus.ModesBySymbol.TryGetValue(symbolId, out var mode))
                awardedByMode[mode] = checked(awardedByMode[mode] + amount);
        }

        var freeSpinsAwarded = 0;
        string? featureMode = result.FreeSpinsAwarded > 0 ? profile.ScatterFeatureMode : null;
        var freeSpinWagerPoints = 0L;
        var spinWagerCents = RandMoney.PointsToCents(result.WagerPoints, result.PointValueInCents);
        var findings = new List<RoundAuditFinding>();
        foreach (var mode in PiratesGemBonus.Modes)
        {
            var progress = PiratesGemBonus.Advance(result.SpinId, mode,
                checked((int)counts[mode]), wagerCents[mode], awardedByMode[mode], spinWagerCents);
            if (evidence.CountsAfter[mode] != progress.Count ||
                evidence.WagerCentsAfter[mode] != progress.WagerTotal)
                findings.Add(new("slot-pirates-gem-state-mismatch", "error",
                    "The saved Pirates gem pile differs from replayed spin progression."));

            foreach (var award in progress.Awards)
            {
                freeSpinsAwarded = checked(freeSpinsAwarded + profile.CollectionAwardedSpins);
                featureMode = PiratesGemBonus.AddFeatureMode(featureMode, mode);
                if (freeSpinWagerPoints > 0) continue;
                var totalWagerPoints = RandMoney.CentsToPoints(
                    award.WagerTotal, result.PointValueInCents);
                freeSpinWagerPoints = Math.Max(1, checked((long)Math.Round(
                    totalWagerPoints / (decimal)award.GemCount,
                    MidpointRounding.AwayFromZero)));
            }
        }

        if (evidence.FreeSpinsAwarded != freeSpinsAwarded ||
            evidence.FeatureMode != featureMode ||
            evidence.FreeSpinWagerPoints != freeSpinWagerPoints)
            findings.Add(new("slot-pirates-gem-award-mismatch", "error",
                "The saved Pirates free-spin award differs from replayed gem triggers."));
        return findings;
    }

    private static IReadOnlyList<RoundAuditFinding> InspectDemo(JsonElement data, SpinResult engine,
        SpinResult response, SlotSpinEngineAudit audit)
    {
        var findings = new List<RoundAuditFinding>();
        void Flag(string code, string message) => findings.Add(new(code, "error", message));
        var request = data.GetProperty("request").Deserialize<DemoSpinRequest>(RoundAuditCapture.Json)
            ?? throw new JsonException("Missing demo request.");
        if (data.GetProperty("mode").GetString() != "demo" ||
            data.GetProperty("financial").GetBoolean() ||
            data.GetProperty("wagerCents").GetInt64() != 0 ||
            data.GetProperty("payoutCents").GetInt64() != 0 ||
            response.SlotsCreditsBalance is not null)
            Flag("slot-demo-financial-classification-mismatch",
                "The saved demo spin is not consistently marked nonfinancial.");
        if (request.GameId != engine.GameId || request.WagerPoints != engine.WagerPoints ||
            request.FreeSpinsRemaining is < 0 or > 1_000 ||
            request.EnergyBalance is < 0 or > 100 ||
            request.UseFreeSpin && request.FreeSpinsRemaining == 0 ||
            !SameEngineOutcome(engine, response))
            Flag("slot-demo-response-mismatch",
                "The saved demo request or response differs from the captured engine outcome.");

        var profile = audit.FeatureProfile;
        var currentEnergy = profile?.UsesEnergy != false ? request.EnergyBalance : 0;
        var activeFeatureMode = request.UseFreeSpin &&
            SlotSpecialRoundProfiles.IsFeatureMode(request.FreeSpinFeatureMode)
                ? request.FreeSpinFeatureMode : null;
        if (audit.EnergyBalanceBefore != currentEnergy ||
            audit.FreeSpinFeatureMode != activeFeatureMode)
            Flag("slot-demo-engine-input-mismatch",
                "The saved demo engine inputs differ from the accepted request.");

        var energy = EnergyBonus.Settle(currentEnergy, engine.EnergyAwarded, engine.Payout);
        var specialRounds = profile is null
            ? new SlotSpecialRoundProgress(0, null, [])
            : SlotSpecialRoundProfiles.SettleDemo(profile, request.SealCollections ?? [],
                engine.SealsAwarded, engine.WagerPoints, energy.MultiplierApplied,
                engine.SpinId);
        var collectionFreeSpinsAwarded = engine.GameId == SlotSpecialRoundProfiles.PiratesFortuneGameId &&
            profile?.CollectionTarget == PiratesGemBonus.MaximumGems
                ? specialRounds.FreeSpinsAwarded : 0;
        var freeSpinsRemaining = checked(request.FreeSpinsRemaining -
            (request.UseFreeSpin ? 1 : 0) + engine.FreeSpinsAwarded + specialRounds.FreeSpinsAwarded);
        long? freeSpinWagerPoints = freeSpinsRemaining > 0
            ? request.FreeSpinWagerPoints ?? request.WagerPoints : null;
        var nextFeatureMode = freeSpinsRemaining > 0
            ? specialRounds.FeatureMode ??
                (engine.FreeSpinsAwarded > 0 ? profile?.ScatterFeatureMode : null) ??
                (request.UseFreeSpin ? request.FreeSpinFeatureMode : null)
            : null;
        if (response.IsFreeSpin != request.UseFreeSpin ||
            response.FreeSpinsAwarded != checked(engine.FreeSpinsAwarded + collectionFreeSpinsAwarded) ||
            response.CollectionFreeSpinsAwarded != collectionFreeSpinsAwarded ||
            response.FreeSpinsRemaining != freeSpinsRemaining ||
            response.FreeSpinWagerPoints != freeSpinWagerPoints ||
            response.EnergyBalance != energy.FinalEnergyBalance ||
            response.EnergyMultiplierApplied != energy.MultiplierApplied ||
            response.PayoutMultiplier != energy.PayoutMultiplier ||
            response.FreeSpinFeatureMode != nextFeatureMode ||
            response.SpecialPointsBalance != 0 ||
            !response.SealCollections.SequenceEqual(specialRounds.Collections))
            Flag("slot-demo-progression-mismatch",
                "The saved demo bonus state does not replay from its request and engine outcome.");
        return findings;
    }

    private static bool SameEngineOutcome(SpinResult engine, SpinResult response) =>
        engine.SpinId == response.SpinId && engine.GameId == response.GameId &&
        engine.ReelSetId == response.ReelSetId && engine.SymbolSetId == response.SymbolSetId &&
        engine.PaytableId == response.PaytableId && engine.WagerPoints == response.WagerPoints &&
        engine.PointValueInCents == response.PointValueInCents &&
        engine.ReelStops.SequenceEqual(response.ReelStops) &&
        engine.Reels.Count == response.Reels.Count &&
        engine.Reels.Zip(response.Reels).All(pair => pair.First.SequenceEqual(pair.Second)) &&
        engine.ConsecutiveFiveMisses == response.ConsecutiveFiveMisses &&
        engine.FiveMatchPityTriggered == response.FiveMatchPityTriggered &&
        engine.SpecialPointsAwarded == response.SpecialPointsAwarded &&
        engine.EnergyAwarded == response.EnergyAwarded &&
        engine.SpecialBoostApplied == response.SpecialBoostApplied &&
        engine.MonkeyPawCount == response.MonkeyPawCount &&
        engine.MoneyGrabPoints == response.MoneyGrabPoints &&
        engine.BananaBonusPoints == response.BananaBonusPoints &&
        engine.SealsAwarded.Count == response.SealsAwarded.Count &&
        engine.SealsAwarded.All(pair => response.SealsAwarded.TryGetValue(pair.Key, out var count) && count == pair.Value);
}
