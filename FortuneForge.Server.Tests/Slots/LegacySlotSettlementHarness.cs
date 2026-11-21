using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Spins;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

/// <summary>Exercises historical admission and settlement without exposing host admission through the public API.</summary>
internal sealed class LegacySlotSettlementHarness(FirestoreAccountStore store, SpinService engine, string userId, string gameId)
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromMilliseconds(500);

    internal Task<SlotSpinAdmission> AdmitAsync(long wager, bool freeSpin, bool boost, CancellationToken token = default,
        DateTime? startedAtUtc = null)
    {
        engine.ValidateRequest(gameId, wager, freeSpin);
        var pointValue = engine.GetPointValueInCents(gameId);
        var boostCost = boost ? engine.GetSpecialBoostCost(gameId) : 0;
        return store.BeginSlotSpinAsync(userId, gameId, wager, pointValue, freeSpin, boost, boostCost, startedAtUtc ?? DateTime.UtcNow,
            Cooldown, token);
    }

    internal async Task<(SpinResult Outcome, SlotSpinSettlement Settlement)> SpinAsync(
        long wager, bool freeSpin, bool boost, CancellationToken token = default)
    {
        var admission = await AdmitAsync(wager, freeSpin, boost, token);
        Assert.Null(admission.CooldownRemaining);
        var outcome = engine.SpinAdmitted(gameId, admission, userId);
        // Once an entitlement is consumed, settling it outlives the original request.
        var settlement = await store.RecordSlotSpinAsync(userId, outcome, admission, DateTime.UtcNow, CancellationToken.None);
        return (outcome with { IsFreeSpin = admission.IsFreeSpin }, settlement);
    }
}
