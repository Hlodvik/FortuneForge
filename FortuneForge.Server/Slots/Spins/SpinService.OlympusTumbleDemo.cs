using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Spins;

public sealed partial class SpinService
{
    internal SpinResult SpinOlympusTumbleDemo(long wagerPoints, bool showcase) =>
        SpinOlympusTumble(OlympusTumbleDemo.EngineVersion, wagerPoints, showcase,
            pityMissesBefore: 0, energyBalanceBefore: 0);

    private SpinResult SpinOlympusTumble(
        string engineVersion, long wagerPoints, bool showcase, int pityMissesBefore, long energyBalanceBefore)
        => standaloneSlotPackages.SpinOlympusTumble(engineVersion, wagerPoints,
            showcase, pityMissesBefore, energyBalanceBefore);
}
