using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Games.MidnightVault;

internal static class MidnightVaultAudit
{
    internal static IReadOnlyList<RoundAuditFinding> Check(SpinResult result, SpinPayout settledPayout)
    {
        if (result.EngineAudit is not { } audit)
            return [new("slot-midnight-engine-evidence-missing", "error", "Midnight Vault engine evidence is missing.")];
        var findings = MidnightVaultOutcomeReplay.Check(MidnightVaultEngine.ToPackage(result, audit), settledPayout)
            .Select(finding => new RoundAuditFinding(finding.Code, "error", finding.Message)).ToList();
        if (audit.FeatureProfile?.PaylinePatternIds is not null)
            findings.Add(new("slot-midnight-profile-mismatch", "error", "Midnight Vault evidence contains foreign payline selection state."));
        if (audit.WukongPawPrototype is not null || audit.NeonArcade is not null || audit.NeonDiscoCascade is not null ||
            audit.OlympusTumble is not null || audit.CrucibleRuleVersion is not null || result.NeonArcade is not null ||
            result.NeonDiscoCascade is not null || result.OlympusTumble is not null || result.LightningCollection is not null ||
            result.MonkeyPawCount != 0 || result.MoneyGrabPoints != 0 || result.CrucibleWageredCents is not null ||
            result.CruciblePourValueCents is not null || result.CruciblePoured is not null || result.CruciblePayoutCents is not null)
            findings.Add(new("slot-midnight-foreign-state", "error", "The Midnight Vault outcome includes another game's state."));
        return findings;
    }
}
