using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.JadeDragon.Bonuses;
using FortuneForge.Games.Slots;
using LegacyAudit = FortuneForge.Games.JadeDragon.Models.SlotSpinEngineAudit;
using LegacySpin = FortuneForge.Games.JadeDragon.Models.SpinResult;

namespace FortuneForge.Games.JadeDragon.Server;

public static class JadeSpinEvidence
{
    private static readonly GameDefinition CascadeGame = JadeDragonCascadeEngine.CreateDefinition();
    private static readonly ReelSetDefinition CascadeReels = JadeDragonCascadeEngine.CreateReelSet();
    private static readonly SymbolSetDefinition CascadeSymbols = JadeDragonCascadeEngine.CreateSymbolSet();
    private static readonly PaytableDefinition CascadePaytable = JadeDragonCascadeEngine.CreatePaytable();

    internal static JadeRoundEngineResult SpinLegacy(IGameRandomSource random, long wagerPoints, string accountId,
        bool boost, int pity, string? mode, long energy)
    {
        var captured = new LegacyDrawRecorder(random);
        var result = new JadeDragonEngine(captured).Spin(wagerPoints, accountId, boost, pity, mode,
            isFreeSpin: true, currentEnergyBalance: energy);
        var audit = result.EngineAudit ?? throw Invalid();
        return new(null, result, audit) { LegacyDrawIndexes = captured.ToArray() };
    }

    internal static void Validate(JadePreparedRound round)
    {
        if (round.AdmissionId == Guid.Empty || round.SpinId == Guid.Empty || round.Before.Pity < 0 || round.Before.Energy < 0 ||
            round.WagerCents != (round.Request.UseFreeSpin ? 0 :
                checked(round.EffectiveWagerPoints * JadeDragonCascadeRules.WagerPointValueInCents)))
            throw Invalid();

        switch (round.EngineVersion)
        {
            case JadeDragonCascadeRules.Version:
                ValidateCascade(round);
                break;
            case JadeDragonEngine.Version:
                ValidateLegacy(round);
                break;
            default:
                throw Invalid();
        }
    }

    private static void ValidateCascade(JadePreparedRound round)
    {
        if (round.Outcome is not { Cascade: { } cascade, Legacy: null, LegacyAudit: null, LegacyDrawIndexes: null } ||
            cascade.SpinId != round.SpinId || !ValidTime(cascade.GeneratedAtUtc, round.StartedAtUtc) ||
            round.Request.UseSpecialBoost || round.Request.UseFreeSpin && round.Before.Carry is not
                { Mode: null, EngineVersion: JadeDragonCascadeRules.Version } ||
            round.Request.UseFreeSpin && round.Before.Carry?.WagerPoints != round.EffectiveWagerPoints)
            throw Invalid();

        var replay = JadeDragonCascadeEngine.Replay(round.EffectiveWagerPoints, round.Request.UseFreeSpin, cascade.DrawIndexes);
        if (!Same(cascade.Outcome, replay) || !Same(round.SettledPayout, new SpinPayout(replay.TotalPoints, [])) ||
            round.PayoutCents != checked(replay.TotalPoints * JadeDragonCascadeRules.PayoutPointValueInCents))
            throw Invalid();
    }

    private static void ValidateLegacy(JadePreparedRound round)
    {
        if (round.Outcome is not { Cascade: null, Legacy: { } result, LegacyAudit: { } audit, LegacyDrawIndexes: { Length: > 0 } draws } ||
            !round.Request.UseFreeSpin || round.Before.Carry is not { EngineVersion: JadeDragonEngine.Version } carry ||
            round.EffectiveWagerPoints != carry.WagerPoints || result.SpinId != round.SpinId ||
            audit.EngineVersion != JadeDragonEngine.Version || !ValidTime(audit.GeneratedAtUtc, round.StartedAtUtc))
            throw Invalid();

        var (replay, replayAudit) = ReplayLegacy(draws, round.EffectiveWagerPoints, round.AccountId,
            round.Request.UseSpecialBoost, round.Before.Pity, carry.Mode, round.Before.Energy, result.SpinId, audit.GeneratedAtUtc);
        if (!Same(result, replay) || !Same(audit, replayAudit) ||
            !Same(round.SettledPayout, replay.Payout) ||
            round.PayoutCents != checked(replay.Payout.TotalPoints * replay.PointValueInCents))
            throw Invalid();
    }

    internal static JadeSpinAudit Capture(JadePreparedRound round)
    {
        if (round.Outcome.Cascade is { } cascade)
            return new(round.EngineVersion, cascade.GeneratedAtUtc, CascadeGame, CascadeGame, CascadeReels,
                CascadeSymbols, CascadePaytable, null, round.Before.Pity, round.Before.Energy, null, 1,
                cascade.Outcome.InitialReels, cascade.Outcome.InitialReels, round.Request.UseFreeSpin)
            { JadeDragonCascadeDraws = cascade.DrawIndexes };

        return ProjectLegacyAudit(round.Outcome.LegacyAudit ?? throw Invalid(), round.Outcome.LegacyDrawIndexes);
    }

    /// <summary>Replays archived package-owned v1 evidence. Earlier receipts continue through the legacy auditor.</summary>
    public static IReadOnlyList<RoundAuditFinding> InspectNativeLegacy(RoundAuditSummary summary, JsonElement data)
    {
        if (!data.TryGetProperty("engineAudit", out var savedAudit) ||
            !savedAudit.TryGetProperty("jadeDragonLegacyDraws", out var savedDraws)) return [];

        try
        {
            var draws = savedDraws.GetBytesFromBase64();
            var audit = savedAudit.Deserialize<JadeSpinAudit>(RoundAuditCapture.Json) ?? throw Invalid();
            var result = data.GetProperty("result").Deserialize<JadeSpinResponse>(RoundAuditCapture.Json) ?? throw Invalid();
            var payout = data.GetProperty("settledPayout").Deserialize<SpinPayout>(RoundAuditCapture.Json) ?? throw Invalid();
            if (summary.GameId != JadeSpinReport.AuditGameId || summary.RoundId != result.SpinId.ToString("N") ||
                summary.Participants is not [{ IsAccountBacked: true, WagerCents: 0 } participant] ||
                result.GameId != JadeDragonEngine.GameId || !result.IsFreeSpin || result.ChargedWagerCents != 0 ||
                audit.EngineVersion != JadeDragonEngine.Version || audit.IsDerivedFreeSpin ||
                !ValidTime(audit.GeneratedAtUtc, summary.StartedAtUtc) || audit.GeneratedAtUtc > summary.CompletedAtUtc ||
                !data.GetProperty("isFreeSpin").GetBoolean() || data.GetProperty("admittedMathModelId").ValueKind != JsonValueKind.Null ||
                data.GetProperty("walletSettlementId").GetString() !=
                    GameWalletSettlementIdentity.CreateId(result.GameId, participant.ActorId, summary.RoundId))
                throw Invalid();

            var (replay, replayAudit) = ReplayLegacy(draws, result.WagerPoints, participant.ActorId, result.SpecialBoostApplied,
                audit.PityMissesBefore, audit.FreeSpinFeatureMode, audit.EnergyBalanceBefore, result.SpinId, audit.GeneratedAtUtc);
            var outcome = new JadeRoundEngineResult(null, replay, replayAudit) { LegacyDrawIndexes = draws };
            var payoutCents = checked(replay.Payout.TotalPoints * replay.PointValueInCents);
            if (!Same(result, JadeSpinResponse.CaptureLegacy(replay)) || !Same(audit, ProjectLegacyAudit(replayAudit, draws)) ||
                !Same(payout, replay.Payout) || participant.PayoutCents != payoutCents || participant.NetCents != payoutCents ||
                data.GetProperty("engineOutcomeSha256").GetString() != JadeRoundPolicy.OutcomeDigest(outcome))
                throw Invalid();
            return [];
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException)
        {
            return [new("slot-jade-legacy-native-proof-mismatch", "error",
                "The Jade Dragon legacy draw evidence or wallet settlement does not match the frozen outcome.")];
        }
    }

    private static JadeSpinAudit ProjectLegacyAudit(LegacyAudit audit, byte[]? draws) =>
        new(audit.EngineVersion, audit.GeneratedAtUtc, audit.BaseGame, audit.EffectiveGame, audit.ReelSet,
            audit.SymbolSet, audit.Paytable, audit.FeatureProfile, audit.PityMissesBefore, audit.EnergyBalanceBefore,
            audit.FreeSpinFeatureMode, audit.GenerationAttempts, audit.RawGeneratedReels, audit.ReelsAfterFreeGameFrequency,
            audit.IsDerivedFreeSpin) { JadeDragonLegacyDraws = draws };

    private static (LegacySpin Result, LegacyAudit Audit) ReplayLegacy(byte[] draws, long wager, string account,
        bool boost, int pity, string? mode, long energy, Guid spinId, DateTime generatedAtUtc)
    {
        var saved = new LegacyDrawReplay(draws);
        var replay = new JadeDragonEngine(saved).Spin(wager, account, boost, pity, mode,
            isFreeSpin: true, currentEnergyBalance: energy);
        if (!saved.Complete || replay.EngineAudit is not { } audit) throw Invalid();
        return (replay with { SpinId = spinId }, audit with { GeneratedAtUtc = generatedAtUtc });
    }

    private static bool ValidTime(DateTime generated, DateTime started) =>
        generated.Kind == DateTimeKind.Utc && started.Kind == DateTimeKind.Utc && generated >= started;

    private static bool Same(object? left, object? right) => JsonNode.DeepEquals(
        JsonSerializer.SerializeToNode(left, RoundAuditCapture.Json), JsonSerializer.SerializeToNode(right, RoundAuditCapture.Json));

    private static InvalidOperationException Invalid() => new("The saved Jade Dragon outcome does not replay from its admitted rules.");

    private sealed class LegacyDrawRecorder(IGameRandomSource random) : IGameRandomSource
    {
        private readonly List<byte> draws = [];
        public byte[] ToArray() => draws.ToArray();

        public int Next(int exclusiveUpperBound)
        {
            var index = random.Next(exclusiveUpperBound);
            if (index < 0 || index >= exclusiveUpperBound || index > byte.MaxValue) throw Invalid();
            draws.Add((byte)index);
            return index;
        }
    }

    private sealed class LegacyDrawReplay(byte[] draws) : IGameRandomSource
    {
        private int position;
        public bool Complete => position == draws.Length;

        public int Next(int exclusiveUpperBound)
        {
            if (position >= draws.Length || draws[position] >= exclusiveUpperBound) throw Invalid();
            return draws[position++];
        }
    }
}

internal sealed record JadeSpinAudit(string EngineVersion, DateTime GeneratedAtUtc,
    GameDefinition BaseGame, GameDefinition EffectiveGame, ReelSetDefinition ReelSet,
    SymbolSetDefinition SymbolSet, PaytableDefinition Paytable, SlotSpecialRoundProfile? FeatureProfile,
    int PityMissesBefore, long EnergyBalanceBefore, string? FreeSpinFeatureMode, int GenerationAttempts,
    IReadOnlyList<IReadOnlyList<string>> RawGeneratedReels, IReadOnlyList<IReadOnlyList<string>> ReelsAfterFreeGameFrequency,
    bool IsDerivedFreeSpin)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? JadeDragonCascadeDraws { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? JadeDragonLegacyDraws { get; init; }
}
