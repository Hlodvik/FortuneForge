using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.RoundAudit;

public sealed record DemoSlotAuditEntry(
    DemoSpinRequest Request,
    SpinResult EngineResult,
    SpinResult ResponseResult,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc);

public interface IDemoSlotAuditStore
{
    Task RecordAsync(DemoSlotAuditEntry entry, CancellationToken cancellationToken);
}

internal static class DemoSlotRoundRecording
{
    internal static RoundAuditCapture Capture(DemoSlotAuditEntry entry)
    {
        var gameId = AcceptedGameActivityReader.SlotIds.TryGetValue(entry.EngineResult.GameId, out var catalogueId)
            ? catalogueId : entry.EngineResult.GameId;
        var instanceId = "demo:" + entry.EngineResult.SpinId.ToString("N");
        var findings = SlotRoundAudit.Check(entry.EngineResult, entry.ResponseResult.Payout).ToList();
        findings.Add(new RoundAuditFinding("demo-nonfinancial", "info",
            "This public demo spin uses client-supplied bonus state and does not move account funds."));
        return RoundAuditCapture.Create(gameId, instanceId, instanceId, 1,
            entry.StartedAtUtc, entry.CompletedAtUtc, new
            {
                mode = "demo",
                financial = false,
                wagerCents = 0,
                payoutCents = 0,
                entry.Request,
                entry.EngineResult,
                engineAudit = entry.EngineResult.EngineAudit,
                entry.ResponseResult
            }, "slot-demo-nonfinancial-v1", SlotRoundAudit.AuditorVersion,
            [new("public-demo", "Public demo", false, 0, 0, 0)], findings);
    }
}
