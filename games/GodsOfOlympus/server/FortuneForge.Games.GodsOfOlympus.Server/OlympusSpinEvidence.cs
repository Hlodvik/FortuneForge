using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.GodsOfOlympus.Server;

internal sealed record OlympusTumbleDrawEvidence(bool Showcase, IReadOnlyList<int> DrawIndexes);

internal sealed record OlympusRecordedTumbleAudit(string EngineVersion, DateTime GeneratedAtUtc, GameDefinition BaseGame,
    ReelSetDefinition ReelSet, SymbolSetDefinition SymbolSet, PaytableDefinition Paytable, int PityMissesBefore,
    long EnergyBalanceBefore, IReadOnlyList<IReadOnlyList<string>> RawGeneratedReels, OlympusTumbleDrawEvidence OlympusTumble)
{
    public GameDefinition EffectiveGame => BaseGame;
    public SlotProfile? FeatureProfile => null;
    public string? FreeSpinFeatureMode => null;
    public int GenerationAttempts => 1;
    public IReadOnlyList<IReadOnlyList<string>> ReelsAfterFreeGameFrequency => RawGeneratedReels;
    public bool IsDerivedFreeSpin => false;
}

internal sealed record OlympusNativeProgressEvidence(string OperationId, ClassicSlotRoundState Before, ClassicSlotRoundState After,
    bool SpendsLegacy, long? WalletLegacyRemaining, string? LegacyFreeGameTransactionId, int CollectionFreeSpinsAwarded,
    string? AwardedFeatureMode, long CollectionFreeSpinWagerPoints);

public static partial class OlympusSpinEvidence
{
    /// <summary>Identifies the package proof and leaves prior paid and demo evidence on their existing replay paths.</summary>
    public static bool IsNative(JsonElement data) => data.TryGetProperty("olympusProgress", out _) ||
        data.TryGetProperty("olympusClassicDraws", out _) || data.TryGetProperty("engineOutcomeSha256", out _);

    /// <summary>Replays the exact saved tumble or classic outcome and reconciles the signed wallet.</summary>
    public static IReadOnlyList<RoundAuditFinding> InspectNative(RoundAuditSummary summary, JsonElement root, JsonElement data)
    {
        try
        {
            if (!IsNative(data)) return [];

            var rawResult = data.GetProperty("result");
            var rawAudit = data.GetProperty("engineAudit");
            var result = rawResult.Deserialize<OlympusSpinResponse>(RoundAuditCapture.Json) ?? throw InvalidEvidence();
            var engine = ReadEngine(result, rawAudit, data.GetProperty("olympusClassicDraws"));
            var settled = data.GetProperty("settledPayout").Deserialize<SpinPayout>(RoundAuditCapture.Json) ?? throw InvalidEvidence();
            var source = data.GetProperty("olympusProgress").Deserialize<OlympusNativeProgressEvidence>(RoundAuditCapture.Json) ?? throw InvalidEvidence();
            var started = data.GetProperty("startedAtUtc").GetDateTime();
            var free = data.GetProperty("isFreeSpin").GetBoolean();
            var wagerCents = free ? 0 : OlympusRoundPolicy.ToCents(result.WagerPoints, result.PointValueInCents);
            var payoutCents = OlympusRoundPolicy.ToCents(settled.TotalPoints, result.PointValueInCents);
            var netCents = checked(payoutCents - wagerCents);
            if (summary.GameId != OlympusSpinReport.AuditGameId || summary.RoundId != result.SpinId.ToString("N") ||
                summary.TableId != summary.RoundId || summary.Participants is not [{ IsAccountBacked: true } participant] ||
                Microseconds(summary.StartedAtUtc) != Microseconds(started) ||
                Microseconds(summary.CompletedAtUtc) < Microseconds(engine.GeneratedAtUtc) ||
                result.ChargedWagerCents != wagerCents || participant.WagerCents != wagerCents ||
                participant.PayoutCents != payoutCents || participant.NetCents != netCents ||
                root.GetProperty("sourceCollection").GetString() != OlympusSpinReport.ResultCollection ||
                root.GetProperty("sourceDocumentId").GetString() != summary.RoundId ||
                root.GetProperty("wagerCents").GetInt64() != wagerCents || root.GetProperty("payoutCents").GetInt64() != payoutCents ||
                root.GetProperty("netCents").GetInt64() != netCents ||
                data.GetProperty("activeFreeSpinFeatureMode").GetString() != engine.Classic?.Audit.FeatureMode ||
                data.GetProperty("admittedMathModelId").GetString() != engine.Tumble?.EngineVersion ||
                data.GetProperty("payoutMultiplier").GetDecimal() != 1m ||
                !Same(result.Payout, settled) ||
                data.GetProperty("walletSettlementId").GetString() !=
                    GameWalletSettlementIdentity.CreateId(GodsOfOlympusEngine.GameId, participant.ActorId, summary.RoundId) ||
                source.WalletLegacyRemaining != source.After.LegacyRemaining ||
                source.LegacyFreeGameTransactionId != (source.SpendsLegacy ? $"{summary.RoundId}-free-game-use" : null) ||
                !OlympusRoundRequest.IsValidOperationId(source.OperationId))
                throw InvalidEvidence();

            _ = OlympusRoundStateCodec.Storage.Write(source.Before);
            var request = new OlympusRoundRequest(source.OperationId, result.WagerPoints, free, result.SpecialBoostApplied);
            var progress = new OlympusRoundProgress(source.After, source.CollectionFreeSpinsAwarded,
                source.AwardedFeatureMode, source.CollectionFreeSpinWagerPoints);
            var round = new OlympusPreparedRound(data.GetProperty("admissionId").GetGuid(), participant.ActorId,
                request, source.Before, engine, progress, data.GetProperty("engineOutcomeSha256").GetString() ?? throw InvalidEvidence(),
                wagerCents, payoutCents, source.SpendsLegacy, started, null);
            OlympusRoundPolicy.Validate(round);

            var expectedAudit = CaptureAudit(round);
            if (!JsonElement.DeepEquals(rawResult, JsonSerializer.SerializeToElement(OlympusSpinResponse.Capture(round), RoundAuditCapture.Json)) ||
                !JsonElement.DeepEquals(rawAudit, JsonSerializer.SerializeToElement(expectedAudit, RoundAuditCapture.Json)) ||
                data.GetProperty("rulesSha256").GetString() != RulesDigest(expectedAudit))
                throw InvalidEvidence();
            return [];
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or NullReferenceException or GameWalletUnavailableException)
        {
            return [new("slot-olympus-native-proof-mismatch", "error",
                "The Olympus board, tumble or bonus progression, or signed wallet evidence does not match the paid outcome.")];
        }
    }

    internal static object CaptureAudit(OlympusPreparedRound round)
    {
        if (round.Engine.Classic is { } classic) return ClassicSlotRecordedAudit.From(classic.Audit);
        var tumble = round.Engine.Tumble ?? throw InvalidEvidence();
        return new OlympusRecordedTumbleAudit(tumble.EngineVersion, round.Engine.GeneratedAtUtc, tumble.BaseGame,
            GodsOfOlympusEngine.CreateReelSet(), GodsOfOlympusEngine.CreateSymbolSet(), GodsOfOlympusEngine.CreatePaytable(),
            round.Before.Pity, round.Before.Energy, tumble.Outcome.InitialReels, new(false, tumble.DrawIndexes));
    }

    internal static string RulesDigest(object recorded)
    {
        (GameDefinition BaseGame, GameDefinition EffectiveGame, ReelSetDefinition ReelSet,
            SymbolSetDefinition SymbolSet, PaytableDefinition Paytable, SlotProfile? FeatureProfile) rules = recorded switch
        {
            ClassicSlotRecordedAudit audit => (audit.BaseGame, audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable, audit.FeatureProfile),
            OlympusRecordedTumbleAudit audit => (audit.BaseGame, audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable, audit.FeatureProfile),
            _ => throw InvalidEvidence()
        };
        return GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(new
        {
            rules.BaseGame, rules.EffectiveGame, rules.ReelSet, rules.SymbolSet, rules.Paytable, rules.FeatureProfile
        }));
    }

    private static OlympusRoundEngineResult ReadEngine(OlympusSpinResponse result, JsonElement rawAudit, JsonElement classicDraws)
    {
        if (result.OlympusTumble is { } outcome)
        {
            var audit = rawAudit.Deserialize<OlympusRecordedTumbleAudit>(RoundAuditCapture.Json) ?? throw InvalidEvidence();
            if (audit.OlympusTumble.Showcase || classicDraws.ValueKind != JsonValueKind.Null) throw InvalidEvidence();
            var tumble = new GodsOfOlympusTumbleSpinOutcome(result.SpinId, result.GameId, audit.EngineVersion,
                result.WagerPoints, result.PointValueInCents, outcome, audit.OlympusTumble.DrawIndexes, audit.BaseGame);
            return new(tumble, null, null, audit.GeneratedAtUtc);
        }

        var classic = rawAudit.Deserialize<ClassicSlotRecordedAudit>(RoundAuditCapture.Json) ?? throw InvalidEvidence();
        return new(null, result.ToClassic(classic.ToEngineAudit()), classicDraws.GetBytesFromBase64(), classic.GeneratedAtUtc);
    }

    private static bool Same<T>(T first, T second) => JsonElement.DeepEquals(
        JsonSerializer.SerializeToElement(first, RoundAuditCapture.Json), JsonSerializer.SerializeToElement(second, RoundAuditCapture.Json));

    private static long Microseconds(DateTime value) => value.Ticks / TimeSpan.TicksPerMicrosecond;

    private static InvalidOperationException InvalidEvidence() => new("The archived Olympus round cannot be replayed.");
}
