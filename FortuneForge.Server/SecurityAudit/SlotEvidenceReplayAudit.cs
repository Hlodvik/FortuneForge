using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Activity;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
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
            var root = document.RootElement;
            var data = demo ? root : root.GetProperty("data");
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

            var findings = SlotRoundAudit.Check(result with { EngineAudit = engineAudit }, settledPayout, isPaid: !demo).ToList();
            var usesOlympusTumble = OlympusTumbleDemo.SupportsEngineVersion(engineAudit.EngineVersion);
            if (usesOlympusTumble && demo && summary.Participants is not
                [{ ActorId: "public-demo", IsAccountBacked: false, WagerCents: 0, PayoutCents: 0, NetCents: 0 }])
                findings.Add(new("slot-olympus-tumble-financial-mismatch", "error",
                    "The recorded tumble demo must be nonfinancial and independent of player accounts."));
            if (!demo && NeonArcadeSlotDefinitions.SupportsSpecialPlays(engineAudit.EngineVersion) &&
                data.GetProperty("isFreeSpin").GetBoolean() != result.IsFreeSpin)
                findings.Add(new("slot-neon-admission-mismatch", "error",
                    "The recorded Neon spin type differs from the accepted admission."));
            if (!demo && NeonArcadeSlotDefinitions.SupportsDiscoCascade(engineAudit.EngineVersion) &&
                (!data.TryGetProperty("isNeonDiscoPlay", out var discoMode) ||
                 discoMode.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                 discoMode.GetBoolean() != (result.NeonDiscoCascade is not null)))
                findings.Add(new("slot-neon-disco-admission-mismatch", "error",
                    "The recorded Disco cascade differs from the admitted paid play."));
            if (!AcceptedGameActivityReader.SlotIds.TryGetValue(result.GameId, out var catalogueGameId) ||
                catalogueGameId != summary.GameId ||
                (demo ? "demo:" : string.Empty) + result.SpinId.ToString("N") != summary.RoundId ||
                ((demo || usesOlympusTumble) && summary.TableId != summary.RoundId))
                findings.Add(new("slot-evidence-identity-mismatch", "error",
                    "The saved spin identity differs from its audit summary."));
            if (engineAudit.EngineVersion == NeonArcadeSlotDefinitions.EngineVersion)
                findings.AddRange(InspectCurrentNeon(root, data, result, response, engineAudit,
                    settledPayout, summary, demo));
            else if (demo)
                findings.AddRange(InspectDemo(data, result, response!, engineAudit));
            else if (usesOlympusTumble)
                findings.AddRange(InspectOlympusSettlement(root, data, result, engineAudit, settledPayout, summary));
            else if (engineAudit.EngineVersion == ArcaneLightningCollector.EngineVersion)
                findings.AddRange(InspectArcaneSettlement(data, result, engineAudit, settledPayout, summary));
            else if (GoldFoundrySlotDefinitions.IsModel(engineAudit.BaseGame))
                findings.AddRange(GoldFoundryAudit.InspectPaidSettlement(data, result, engineAudit, settledPayout, summary));
            else if (result.GameId == SlotSpecialRoundProfiles.DragonHoardGameId &&
                data.TryGetProperty("outcomeSchemaVersion", out var schema) && schema.GetInt64() >= 7)
                findings.AddRange(InspectMidnightAdmission(data, result, engineAudit, settledPayout, summary));
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

    // The host checks receipt identity and money; Neon owns entitlement transitions.
    private static IReadOnlyList<RoundAuditFinding> InspectCurrentNeon(JsonElement root, JsonElement data,
        SpinResult engine, SpinResult? response, SlotSpinEngineAudit audit, SpinPayout settled,
        RoundAuditSummary summary, bool demo)
    {
        var findings = new List<RoundAuditFinding>();
        void Flag(string code, string message) => findings.Add(new(code, "error", message));
        if (demo)
        {
            var request = data.GetProperty("request").Deserialize<DemoSpinRequest>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing Neon demo request.");
            var discoPlay = !request.UseFreeSpin && request.NeonDiscoSpinsRemaining > 0;
            var progress = NeonPlayPolicy.AdvanceAwardedPlays(
                request.FreeSpinsRemaining - (request.UseFreeSpin ? 1 : 0), request.UseFreeSpin,
                request.FreeSpinFeatureMode, engine.FreeSpinsAwarded, engine.WagerPoints, engine.NeonArcade);
            var featureMode = request.UseFreeSpin && NeonPlayPolicy.IsFeatureMode(request.FreeSpinFeatureMode)
                ? request.FreeSpinFeatureMode : null;
            var charged = NeonPlayPolicy.ChargesWager(request.UseFreeSpin, request.FreeSpinFeatureMode)
                ? RandMoney.PointsToCents(engine.WagerPoints, engine.PointValueInCents) : 0;
            if (data.GetProperty("mode").GetString() != "demo" || data.GetProperty("financial").GetBoolean() ||
                data.GetProperty("wagerCents").GetInt64() != 0 || data.GetProperty("payoutCents").GetInt64() != 0 ||
                response!.SlotsCreditsBalance is not null)
                Flag("slot-demo-financial-classification-mismatch", "The Neon demo receipt is not consistently nonfinancial.");
            if (request.GameId != engine.GameId || request.WagerPoints != engine.WagerPoints ||
                request.UseFreeSpin != (request.FreeSpinsRemaining > 0) ||
                engine.IsFreeSpin != request.UseFreeSpin || request.UseFreeSpin && request.FreeSpinWagerPoints != engine.WagerPoints ||
                audit.EnergyBalanceBefore != 0 || audit.FreeSpinFeatureMode != featureMode ||
                !SameEngineOutcome(engine, response!) ||
                response!.ChargedWagerCents != charged || response.IsFreeSpin != request.UseFreeSpin ||
                response.FreeSpinsRemaining != progress.Remaining || response.FreeSpinWagerPoints != progress.LockedWagerPoints ||
                response.FreeSpinFeatureMode != progress.FeatureMode || response.FreeSpinsAwarded != engine.FreeSpinsAwarded ||
                response.CollectionFreeSpinsAwarded != 0 || response.SealCollections.Count != 0 ||
                response.EnergyBalance != 0 || response.EnergyMultiplierApplied || response.PayoutMultiplier != 1m ||
                response.SpecialPointsBalance != 0 ||
                discoPlay != (engine.NeonDiscoCascade is not null) ||
                response.NeonDiscoSpinsRemaining != NeonPlayPolicy.NextDiscoSpins(
                    request.NeonDiscoSpinsRemaining, discoPlay, engine.NeonArcade) ||
                JsonSerializer.Serialize(response.Payout) != JsonSerializer.Serialize(settled) ||
                JsonSerializer.Serialize(engine.NeonArcade) != JsonSerializer.Serialize(response.NeonArcade))
                Flag("slot-neon-demo-progression-mismatch", "The Neon response differs from its captured play and progression rules.");
            return findings;
        }

        var isFreeSpin = data.GetProperty("isFreeSpin").GetBoolean();
        var activeMode = data.GetProperty("activeFreeSpinFeatureMode").GetString();
        var isDisco = data.GetProperty("isNeonDiscoPlay").GetBoolean();
        var wagerCents = NeonPlayPolicy.ChargesWager(isFreeSpin, activeMode)
            ? RandMoney.PointsToCents(engine.WagerPoints, engine.PointValueInCents) : 0;
        var payoutCents = RandMoney.PointsToCents(settled.TotalPoints, engine.PointValueInCents);
        if (isFreeSpin != engine.IsFreeSpin || isDisco != (engine.NeonDiscoCascade is not null) ||
            data.GetProperty("payoutMultiplier").GetDecimal() != 1m ||
            data.GetProperty("neonDiscoSpinsRemaining").GetInt32() != NeonPlayPolicy.NextDiscoSpins(
                data.GetProperty("neonDiscoSpinsBefore").GetInt32(), isDisco, engine.NeonArcade) ||
            root.GetProperty("sourceCollection").GetString() != "slotSpinResults" ||
            root.GetProperty("sourceDocumentId").GetString() != engine.SpinId.ToString("N") ||
            root.GetProperty("wagerCents").GetInt64() != wagerCents || root.GetProperty("payoutCents").GetInt64() != payoutCents ||
            root.GetProperty("netCents").GetInt64() != payoutCents - wagerCents ||
            summary.Participants is not [{ IsAccountBacked: true } participant] ||
            string.IsNullOrWhiteSpace(participant.ActorId) || participant.WagerCents != wagerCents ||
            participant.PayoutCents != payoutCents || participant.NetCents != payoutCents - wagerCents)
            Flag("slot-neon-financial-mismatch", "The Neon admission or replayed amounts differ from the paid receipt.");
        return findings;
    }

    private static IReadOnlyList<RoundAuditFinding> InspectOlympusSettlement(
        JsonElement root, JsonElement data, SpinResult result, SlotSpinEngineAudit audit,
        SpinPayout settled, RoundAuditSummary summary)
    {
        if (data.GetProperty("isFreeSpin").GetBoolean() ||
            data.GetProperty("activeFreeSpinFeatureMode").ValueKind != JsonValueKind.Null ||
            data.GetProperty("admittedMathModelId").GetString() != audit.EngineVersion ||
            data.GetProperty("payoutMultiplier").GetDecimal() != 1m)
            return [new("slot-olympus-tumble-admission-mismatch", "error",
                "The recorded Olympus spin differs from the admitted paid tumble rules.")];

        var wagerCents = RandMoney.PointsToCents(result.WagerPoints, result.PointValueInCents);
        var payoutCents = RandMoney.PointsToCents(settled.TotalPoints, result.PointValueInCents);
        var netCents = checked(payoutCents - wagerCents);
        if (root.GetProperty("sourceCollection").GetString() != "slotSpinResults" ||
            root.GetProperty("sourceDocumentId").GetString() != result.SpinId.ToString("N") ||
            root.GetProperty("wagerCents").GetInt64() != wagerCents ||
            root.GetProperty("payoutCents").GetInt64() != payoutCents ||
            root.GetProperty("netCents").GetInt64() != netCents ||
            summary.Participants is not [{ IsAccountBacked: true } participant] ||
            string.IsNullOrWhiteSpace(participant.ActorId) || participant.WagerCents != wagerCents ||
            participant.PayoutCents != payoutCents || participant.NetCents != netCents)
            return [new("slot-olympus-tumble-financial-mismatch", "error",
                "The Olympus stake and replayed payout differ from the paid settlement receipt.")];
        return [];
    }

    private static IReadOnlyList<RoundAuditFinding> InspectArcaneSettlement(
        JsonElement data, SpinResult result, SlotSpinEngineAudit audit, SpinPayout settled, RoundAuditSummary summary)
    {
        var isFreeSpin = data.GetProperty("isFreeSpin").GetBoolean();
        var wagerCents = isFreeSpin ? 0 : RandMoney.PointsToCents(result.WagerPoints, result.PointValueInCents);
        var payoutCents = RandMoney.PointsToCents(settled.TotalPoints, result.PointValueInCents);
        if (isFreeSpin != result.IsFreeSpin || isFreeSpin != audit.IsDerivedFreeSpin || summary.Participants.Count != 1 ||
            summary.Participants[0].WagerCents != wagerCents || summary.Participants[0].PayoutCents != payoutCents ||
            summary.Participants[0].NetCents != payoutCents - wagerCents)
            return [new("slot-arcane-financial-mismatch", "error",
                "The Arcane Archives stake and payout do not balance with the paid round summary.")];
        return [];
    }

    private static IReadOnlyList<RoundAuditFinding> InspectMidnightAdmission(
        JsonElement data, SpinResult result, SlotSpinEngineAudit audit, SpinPayout settled,
        RoundAuditSummary summary)
    {
        var findings = new List<RoundAuditFinding>();
        var model = data.GetProperty("admittedMathModelId").GetString();
        var free = data.GetProperty("isFreeSpin").GetBoolean();
        if (string.IsNullOrEmpty(model) || model != result.PaytableId ||
            model != audit.BaseGame.Math.PaytableId || model != audit.Paytable.Id ||
            free != result.IsFreeSpin || free != audit.IsDerivedFreeSpin)
            findings.Add(new("slot-midnight-admission-mismatch", "error",
                "The saved payout model or bonus wager differs from the accepted admission."));
        var wagerCents = free ? 0 : RandMoney.PointsToCents(result.WagerPoints, result.PointValueInCents);
        var payoutCents = RandMoney.PointsToCents(settled.TotalPoints, result.PointValueInCents);
        if (summary.Participants.Count != 1 || summary.Participants[0].WagerCents != wagerCents ||
            summary.Participants[0].PayoutCents != payoutCents ||
            summary.Participants[0].NetCents != payoutCents - wagerCents)
            findings.Add(new("slot-midnight-financial-mismatch", "error",
                "The saved stake and payout do not balance with the paid round summary."));
        var bonus = data.GetProperty("midnightBonusEvidence").Deserialize<MidnightVaultBonusEvidence>(RoundAuditCapture.Json)
            ?? throw new JsonException("Missing Midnight Vault round progression.");
        var head = bonus.RoundsBeforeAdmission.FirstOrDefault();
        var consumed = free ? MidnightVaultBonusRounds.Consume(bonus.RoundsBeforeAdmission) : bonus.RoundsBeforeAdmission;
        if (bonus.AdmittedRound != (free ? head : null) ||
            free && (head is null || head.WagerPoints != result.WagerPoints || head.MathModelId != model ||
                head.FeatureMode != audit.FreeSpinFeatureMode) ||
            !free && head is not null || !consumed.SequenceEqual(bonus.RoundsAfterConsumption))
            findings.Add(new("slot-midnight-round-admission-mismatch", "error", "The bonus round consumed by admission differs from its saved queue."));
        var progressed = MidnightVaultBonusRounds.Advance(consumed, bonus.CollectionCountsBefore,
            bonus.CollectionWagerPointsBefore, result.SealsAwarded, result.WagerPoints,
            result.FreeSpinsAwarded, audit.FeatureProfile?.ScatterFeatureMode, model!, result.SpinId);
        if (!progressed.Rounds.SequenceEqual(bonus.RoundsAfterSettlement) ||
            !progressed.AwardedRounds.SequenceEqual(bonus.AwardedRounds) ||
            !SameMap(progressed.CollectionCounts, bonus.CollectionCountsAfter) ||
            !SameMap(progressed.CollectionWagerPoints, bonus.CollectionWagerPointsAfter))
            findings.Add(new("slot-midnight-round-progression-mismatch", "error", "The saved round queue or weighted gem totals do not replay."));
        return findings;
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
            request.FreeSpinsRemaining < 0 ||
            !NeonArcadeSlotDefinitions.UsesExpandedArcadeGrid(audit.EngineVersion) && request.FreeSpinsRemaining > 1_000 ||
            request.NeonDiscoSpinsRemaining is < 0 or > NeonArcadeSlotDefinitions.DiscoPlaySpins ||
            request.GameId != SlotSpecialRoundProfiles.NeonNightsGameId && request.NeonDiscoSpinsRemaining != 0 ||
            request.EnergyBalance is < 0 or > 100 ||
            request.UseFreeSpin && request.FreeSpinsRemaining == 0 ||
            !SameEngineOutcome(engine, response))
            Flag("slot-demo-response-mismatch",
                "The saved demo request or response differs from the captured engine outcome.");

        if (OlympusTumbleDemo.SupportsEngineVersion(audit.EngineVersion) || request.OlympusTumbleShowcase is not null)
        {
            if (audit.OlympusTumble is not { } tumble ||
                request.OlympusTumbleShowcase != tumble.Showcase ||
                request.UseFreeSpin || request.FreeSpinsRemaining != 0 || request.FreeSpinWagerPoints is not null ||
                request.EnergyBalance != 0 || request.FreeSpinFeatureMode is not null ||
                request.MidnightBonusState is not null || request.CrucibleWageredCents != 0 ||
                request.SealCollections is { Count: > 0 } ||
                !SlotRoundAudit.HasOnlyOlympusDemoState(response))
                Flag("slot-olympus-tumble-demo-state-mismatch",
                    "The saved request or response contains bonus carry or differs from the recorded tumble mode.");
            return findings;
        }

        if (engine.GameId == SlotSpecialRoundProfiles.DragonHoardGameId && response.MidnightBonusState is not null)
        {
            findings.AddRange(InspectMidnightDemo(request, engine, response, audit));
            return findings;
        }

        if (GoldFoundrySlotDefinitions.IsModel(audit.BaseGame))
        {
            findings.AddRange(GoldFoundryAudit.InspectDemo(request, engine, response, audit));
            return findings;
        }
        var usesNeonSpecialPlays = NeonArcadeSlotDefinitions.SupportsSpecialPlays(audit.EngineVersion);
        if (usesNeonSpecialPlays &&
            (request.UseFreeSpin != (request.FreeSpinsRemaining > 0) ||
             engine.IsFreeSpin != request.UseFreeSpin ||
             request.UseFreeSpin && request.FreeSpinWagerPoints != request.WagerPoints ||
             JsonSerializer.Serialize(engine.NeonArcade) != JsonSerializer.Serialize(response.NeonArcade)))
            Flag("slot-neon-demo-progression-mismatch",
                "The saved Neon spin type, locked wager, or activators differ from the accepted demo outcome.");
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
            ? usesNeonSpecialPlays ? engine.WagerPoints : request.FreeSpinWagerPoints ?? request.WagerPoints : null;
        var nextFeatureMode = freeSpinsRemaining > 0
            ? specialRounds.FeatureMode ??
                (NeonArcadeSlotDefinitions.UsesExpandedArcadeGrid(audit.EngineVersion) &&
                    !request.UseFreeSpin && engine.FreeSpinsAwarded > 0
                    ? NeonArcadeBonus.AwardedFeatureMode(engine.NeonArcade) : null) ??
                (engine.FreeSpinsAwarded > 0 ? profile?.ScatterFeatureMode : null) ??
                (request.UseFreeSpin ? request.FreeSpinFeatureMode : null)
            : null;
        var expectedChargedWagerCents = request.UseFreeSpin &&
            !(usesNeonSpecialPlays && NeonArcadeBonus.IsPaidArcadeMode(request.FreeSpinFeatureMode))
                ? 0 : RandMoney.PointsToCents(engine.WagerPoints, engine.PointValueInCents);
        if (response.IsFreeSpin != request.UseFreeSpin ||
            (NeonArcadeSlotDefinitions.UsesExpandedArcadeGrid(audit.EngineVersion) &&
                response.ChargedWagerCents != expectedChargedWagerCents) ||
            response.FreeSpinsAwarded != checked(engine.FreeSpinsAwarded + collectionFreeSpinsAwarded) ||
            response.CollectionFreeSpinsAwarded != collectionFreeSpinsAwarded ||
            response.FreeSpinsRemaining != freeSpinsRemaining ||
            response.FreeSpinWagerPoints != freeSpinWagerPoints ||
            response.EnergyBalance != energy.FinalEnergyBalance ||
            response.EnergyMultiplierApplied != energy.MultiplierApplied ||
            response.CrucibleWageredCents is not null || response.CruciblePourValueCents is not null ||
            response.CruciblePoured is not null || response.CruciblePayoutCents is not null ||
            response.PayoutMultiplier != energy.PayoutMultiplier ||
            response.FreeSpinFeatureMode != nextFeatureMode ||
            response.SpecialPointsBalance != 0 ||
            !response.SealCollections.SequenceEqual(specialRounds.Collections))
            Flag("slot-demo-progression-mismatch",
                "The saved demo bonus state does not replay from its request and engine outcome.");
        return findings;
    }

    private static IReadOnlyList<RoundAuditFinding> InspectMidnightDemo(
        DemoSpinRequest request, SpinResult engine, SpinResult response, SlotSpinEngineAudit audit)
    {
        var state = DemoSlotsController.CreateMidnightState(request, engine.PaytableId);
        MidnightVaultBonusRounds.Validate(state.Rounds);
        var head = state.Rounds.FirstOrDefault();
        var consumed = request.UseFreeSpin ? MidnightVaultBonusRounds.Consume(state.Rounds) : state.Rounds;
        var progress = MidnightVaultBonusRounds.Advance(consumed, state.CollectionCounts, state.CollectionWagerPoints,
            engine.SealsAwarded, engine.WagerPoints, engine.FreeSpinsAwarded,
            audit.FeatureProfile?.ScatterFeatureMode, engine.PaytableId, engine.SpinId);
        var next = progress.Rounds.FirstOrDefault();
        var recorded = response.MidnightBonusState!;
        if (request.UseFreeSpin != (head is not null) || request.FreeSpinsRemaining != (head?.RemainingSpins ?? 0) ||
            request.UseFreeSpin && (head!.WagerPoints != engine.WagerPoints || head.MathModelId != engine.PaytableId) ||
            audit.IsDerivedFreeSpin != request.UseFreeSpin || audit.FreeSpinFeatureMode != head?.FeatureMode ||
            response.BonusRound != next || !response.QueuedBonusRounds.SequenceEqual(progress.Rounds.Skip(1)) ||
            !response.AwardedBonusRounds.SequenceEqual(progress.AwardedRounds) ||
            !recorded.Rounds.SequenceEqual(progress.Rounds) ||
            !SameMap(recorded.CollectionCounts, progress.CollectionCounts) ||
            !SameMap(recorded.CollectionWagerPoints, progress.CollectionWagerPoints) ||
            response.FreeSpinsRemaining != (next?.RemainingSpins ?? 0) || response.FreeSpinWagerPoints != next?.WagerPoints ||
            response.FreeSpinFeatureMode != next?.FeatureMode || response.IsFreeSpin != request.UseFreeSpin ||
            response.EnergyBalance != 0 || response.EnergyMultiplierApplied || response.PayoutMultiplier != 1m ||
            response.SpecialPointsBalance != 0 ||
            response.FreeSpinsAwarded != engine.FreeSpinsAwarded ||
            response.CollectionFreeSpinsAwarded != progress.AwardedRounds.Where(round => round.FeatureMode == "rows-paw").Sum(round => round.TotalSpins) ||
            !response.SealCollections.SequenceEqual(DemoSlotsController.CreateMidnightCollections(progress.CollectionCounts, progress.CollectionWagerPoints)))
            return [new("slot-midnight-demo-progression-mismatch", "error", "The demo's locked rounds or weighted gem totals do not replay.")];
        return [];
    }

    private static bool SameMap(IReadOnlyDictionary<string, long> left, IReadOnlyDictionary<string, long> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

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
        SlotRoundAudit.SameLightningCollection(engine.LightningCollection, response.LightningCollection) &&
        (engine.OlympusTumble is null && response.OlympusTumble is null ||
         JsonSerializer.Serialize(engine.OlympusTumble, RoundAuditCapture.Json) ==
             JsonSerializer.Serialize(response.OlympusTumble, RoundAuditCapture.Json)) &&
        JsonSerializer.Serialize(engine.NeonDiscoCascade, RoundAuditCapture.Json) ==
            JsonSerializer.Serialize(response.NeonDiscoCascade, RoundAuditCapture.Json) &&
        engine.SealsAwarded.Count == response.SealsAwarded.Count &&
        engine.SealsAwarded.All(pair => response.SealsAwarded.TryGetValue(pair.Key, out var count) && count == pair.Value);
}
