namespace FortuneForge.Server.Slots.Bonuses;

internal static class RainbowRealmCharmHarvest
{
    internal static decimal WagerMultiplier(string symbol) => symbol switch
    {
        "SEAL_SYNC" => 1.5m,
        "SEAL_ROWS" => 2m,
        "SEAL_PAW" => 2.5m,
        "SEAL_RAND" => 3m,
        _ => 0m
    };
}
