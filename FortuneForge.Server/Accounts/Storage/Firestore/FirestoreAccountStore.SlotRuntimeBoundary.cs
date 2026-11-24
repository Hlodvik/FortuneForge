using FortuneForge.Server.Slots.Bonuses;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    private static void RejectRemovedSlotRuntime(string gameId)
    {
        if (gameId == SlotSpecialRoundProfiles.ArcaneArchivesGameId)
            throw new ArgumentException("This game requires its native game package and round endpoints.", nameof(gameId));
    }
}
