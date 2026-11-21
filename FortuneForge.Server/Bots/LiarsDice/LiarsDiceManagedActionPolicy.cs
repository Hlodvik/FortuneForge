using FortuneForge.Games.LiarsDice;
namespace FortuneForge.Server.Bots.LiarsDice;
internal static class LiarsDiceManagedActionPolicy
{
    public static LiarsDiceCommand Choose(LiarsDiceMatchState state, int skillLevel, ulong seed, int version) =>
        FortuneForge.Games.LiarsDice.LiarsDiceManagedActionPolicy.Choose(state, skillLevel, seed, version);
}
