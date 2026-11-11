using FortuneForge.Games.JadeDragon;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Reels;

namespace FortuneForge.Server.Slots.Games.JadeDragon;

internal sealed class JadeDragonCascadeAdapter(IRandomIndexSource random)
{
    private readonly JadeDragonCascadeEngine engine = new(random);

    internal SpinResult Spin(SlotSpinAdmission admission)
    {
        if (admission.MathModelId != JadeDragonCascadeRules.Version ||
            admission.SpecialBoostApplied || admission.IsNeonDiscoPlay ||
            admission.FreeSpinFeatureMode is not null)
            throw new ArgumentException("A Jade Dragon cascade admission is required.", nameof(admission));

        var spin = engine.Spin(admission.WagerPoints, admission.IsFreeSpin);
        var outcome = spin.Outcome;
        var rules = JadeDragonCascadeEngine.CreateDefinition();
        return new SpinResult(spin.SpinId, rules.Id, rules.Math.ReelSetId,
            rules.Symbols.SymbolSetId, rules.Math.PaytableId, admission.WagerPoints,
            rules.Wagering.PointValueInCents, [], outcome.InitialReels,
            new SpinPayout(outcome.TotalPoints, []), admission.PityMissesBefore, false,
            outcome.FreeSpinsAwarded, 0, 0, false)
        {
            IsFreeSpin = admission.IsFreeSpin,
            ChargedWagerCents = admission.ChargedWagerCents,
            JadeDragonCascade = outcome,
            PayoutPointValueInCents = JadeDragonCascadeRules.PayoutPointValueInCents,
            EngineAudit = new SlotSpinEngineAudit(JadeDragonCascadeRules.Version, spin.GeneratedAtUtc,
                rules, rules, JadeDragonCascadeEngine.CreateReelSet(), JadeDragonCascadeEngine.CreateSymbolSet(),
                JadeDragonCascadeEngine.CreatePaytable(), null, admission.PityMissesBefore,
                admission.EnergyBalance, null, 1, outcome.InitialReels, outcome.InitialReels)
            {
                IsDerivedFreeSpin = admission.IsFreeSpin,
                JadeDragonCascadeDraws = spin.DrawIndexes
            }
        };
    }
}
