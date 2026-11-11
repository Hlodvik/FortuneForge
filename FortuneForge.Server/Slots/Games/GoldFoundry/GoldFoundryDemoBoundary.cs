using FortuneForge.Server.Controllers;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Games.GoldFoundry;

// The demo transport is host-owned, but Gold Foundry must never inherit another
// game's carry state through that shared envelope.
internal static class GoldFoundryDemoBoundary
{
    internal static void Validate(DemoSpinRequest request)
    {
        GoldFoundrySlotDefinitions.ValidateSpinRequest(request.UseFreeSpin);
        GoldFoundryCrucible.ValidateDemoWager(request.CrucibleWageredCents);
        if (HasForeignRequestState(request))
            throw new ArgumentException("Gold Foundry does not accept another game's demo progression.",
                nameof(request));
    }

    // Generic legacy carry fields are accepted only so old demo saves can be
    // retired to Gold's zeroed response. Explicit state owned by another game
    // is never admitted.
    internal static bool HasForeignRequestState(DemoSpinRequest request) =>
        request.MidnightBonusState is not null ||
        request.OlympusTumbleShowcase is not null || request.NeonDiscoSpinsRemaining != 0;

    internal static bool HasForeignResponseState(SpinResult response) =>
        response.NeonDiscoSpinsRemaining != 0 || response.BonusRound is not null ||
        response.QueuedBonusRounds.Count != 0 || response.AwardedBonusRounds.Count != 0 ||
        response.MidnightBonusState is not null || response.NeonArcade is not null ||
        response.NeonDiscoCascade is not null || response.OlympusTumble is not null ||
        response.LightningCollection is not null;
}
