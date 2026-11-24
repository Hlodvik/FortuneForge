using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PharaohsAscent.Server;

public sealed record SealedTombDraw(int Bound, int Value);
public sealed record SealedTombActionEvidence(Guid EventId, DateTime GeneratedAtUtc,
    SealedTombState After, long PayoutPoints, IReadOnlyList<SealedTombDraw> Draws);

internal static class SealedTombRoundPolicy
{
    internal static PharaohsAscentPreparedRound Prepare(string account, PharaohsAscentRoundRequest request,
        ClassicSlotRoundState before, DateTime started, GameRequestObservation? observation, IGameRandomSource random)
    {
        Terms(request, before);
        var tape = new TombTape(random);
        var outcome = SealedTombRules.Act(SealedTombState.Parse(before.NativeStateJson), request.TombAction!, tape);
        var evidence = new SealedTombActionEvidence(Guid.NewGuid(), DateTime.UtcNow, outcome.After, outcome.PayoutPoints, tape.Draws);
        var progress = Progress(before, evidence);
        var round = new PharaohsAscentPreparedRound(Guid.NewGuid(), account, request, before, null, progress,
            Digest(request.TombAction!, before.NativeStateJson, evidence), 0,
            PharaohsAscentRoundPolicy.ToCents(outcome.PayoutPoints, PharaohsAscentEngine.WagerPointValueInCents), false,
            started, observation) { TombEvidence = evidence };
        Validate(round);
        return round;
    }

    internal static void Validate(PharaohsAscentPreparedRound round)
    {
        Terms(round.Request, round.Before);
        var evidence = round.TombEvidence ?? throw new InvalidOperationException("The tomb action has no recorded evidence.");
        var replay = new TombTape(evidence.Draws);
        var reproduced = SealedTombRules.Act(SealedTombState.Parse(round.Before.NativeStateJson), round.Request.TombAction!, replay);
        if (!replay.Finished || round.Engine is not null || round.SpendsLegacy || round.WagerCents != 0 ||
            round.AdmissionId == Guid.Empty || evidence.EventId == Guid.Empty || evidence.GeneratedAtUtc.Kind != DateTimeKind.Utc ||
            evidence.GeneratedAtUtc < round.StartedAtUtc ||
            SealedTombState.Serialize(reproduced.After) != SealedTombState.Serialize(evidence.After) ||
            reproduced.PayoutPoints != evidence.PayoutPoints ||
            round.PayoutCents != PharaohsAscentRoundPolicy.ToCents(evidence.PayoutPoints, PharaohsAscentEngine.WagerPointValueInCents) ||
            round.OutcomeSha256 != Digest(round.Request.TombAction!, round.Before.NativeStateJson, evidence) ||
            JsonSerializer.Serialize(round.Progress, RoundAuditCapture.Json) != JsonSerializer.Serialize(Progress(round.Before, evidence), RoundAuditCapture.Json))
            throw new InvalidOperationException("The recorded Sealed Tomb action does not reproduce its funded settlement.");
        _ = PharaohsAscentRoundStateCodec.Storage.Write(round.Progress.After);
    }

    private static void Terms(PharaohsAscentRoundRequest request, ClassicSlotRoundState before)
    {
        if (request.TombAction is null || request.WagerPoints != 0 || request.UseFreeSpin || request.UseSpecialBoost)
            throw new ArgumentException("A Sealed Tomb action does not place another wager or use reel-spin options.");
        if (request.TombAction.Action == "enter" && before.Carry is not null)
            throw new SealedTombConflictException("tomb-reel-bonus-active", "Finish the earned reel spins before entering The Sealed Tomb.");
    }

    private static ClassicSlotActiveRoundProgress Progress(ClassicSlotRoundState before, SealedTombActionEvidence evidence) =>
        new(before with { NativeStateJson = SealedTombState.Serialize(evidence.After) },
            new(new(evidence.PayoutPoints, []), before.Energy, before.Energy, 0, false, 1m), 0, null, 0);

    private static string Digest(SealedTombAction action, string? before, SealedTombActionEvidence evidence) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(new { action, before, evidence }, RoundAuditCapture.Json));

    private sealed class TombTape : IGameRandomSource
    {
        private readonly IGameRandomSource? source;
        private readonly IReadOnlyList<SealedTombDraw>? recorded;
        private readonly List<SealedTombDraw> draws = [];
        private int cursor;
        internal TombTape(IGameRandomSource source) => this.source = source;
        internal TombTape(IReadOnlyList<SealedTombDraw> recorded) => this.recorded = recorded;
        internal IReadOnlyList<SealedTombDraw> Draws => draws.ToArray();
        internal bool Finished => recorded is not null && cursor == recorded.Count;
        public int Next(int bound)
        {
            if (source is not null)
            {
                var value = source.Next(bound);
                if (value < 0 || value >= bound) throw new InvalidOperationException("The tomb draw is invalid.");
                draws.Add(new(bound, value));
                return value;
            }
            if (recorded is null || cursor >= recorded.Count || recorded[cursor] is not { } draw || draw.Bound != bound ||
                draw.Value < 0 || draw.Value >= bound) throw new InvalidOperationException("The tomb draw tape is invalid.");
            cursor++;
            return draw.Value;
        }
    }
}
