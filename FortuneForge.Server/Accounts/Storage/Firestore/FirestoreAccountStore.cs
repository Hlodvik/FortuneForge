using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore(FirestoreDb database) : IAccountStore
{
    private const long NewAccountSlotsCredits = 0;
    private const long LegacySlotsCreditsFallback = 10_000;
    private const long AccountSchemaVersion = 7;
    private const string LegacyLoadedMoneyCurrencyId = "loadedMoney";
    private const string SlotsCreditsCurrencyId = "slotsCredits";
    private const string AvailableFractionalCentsField = "availableFractionalCents";
    private const string FreeGamesCurrencyId = "freeGames";
    private const string SpecialPointsCurrencyId = "specialPoints";
    private const string EnergyCurrencyId = "energy";
    private const string LegacyWukongGameId = "classic-demo-v1";
    private const int SealCompletionFreeSpins = 10;
    private const string SyncedReelsFeatureMode = "sync";
    private const string ExtraRowsFeatureMode = "rows";
    private const string PawBoostFeatureMode = "paw";
    private const string RandColumnFeatureMode = "rand";
    private static readonly IReadOnlyDictionary<string, string> SealFeatureModesBySymbolId =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SEAL_SYNC"] = SyncedReelsFeatureMode,
            ["SEAL_ROWS"] = ExtraRowsFeatureMode,
            ["SEAL_PAW"] = PawBoostFeatureMode,
            ["SEAL_RAND"] = RandColumnFeatureMode
        };
    private static readonly string[] SealFeatureModes =
    [
        SyncedReelsFeatureMode,
        ExtraRowsFeatureMode,
        PawBoostFeatureMode,
        RandColumnFeatureMode
    ];

}
