using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    public async Task InitializeSchemaAsync(CancellationToken cancellationToken)
    {
        var createdAtUtc = DateTime.UtcNow;
        var legacyLoadedMoneyCurrency = CurrencyDocument(LegacyLoadedMoneyCurrencyId);
        var slotsCreditsCurrency = CurrencyDocument(SlotsCreditsCurrencyId);
        var freeGamesCurrency = CurrencyDocument(FreeGamesCurrencyId);
        var specialPointsCurrency = CurrencyDocument(SpecialPointsCurrencyId);
        var energyCurrency = CurrencyDocument(EnergyCurrencyId);
        var legacyLoadedMoneySnapshot = await legacyLoadedMoneyCurrency.GetSnapshotAsync(cancellationToken);
        var currencySnapshots = await Task.WhenAll(
            slotsCreditsCurrency.GetSnapshotAsync(cancellationToken),
            freeGamesCurrency.GetSnapshotAsync(cancellationToken),
            specialPointsCurrency.GetSnapshotAsync(cancellationToken),
            energyCurrency.GetSnapshotAsync(cancellationToken));

        var slotsCurrencyNeedsPrecision =
            !currencySnapshots[0].Exists ||
            !currencySnapshots[0].TryGetValue<long>("precision", out var slotsPrecision) ||
            slotsPrecision != 2;
        if (legacyLoadedMoneySnapshot.Exists ||
            slotsCurrencyNeedsPrecision ||
            currencySnapshots.Skip(1).Any(snapshot => !snapshot.Exists))
        {
            var batch = database.StartBatch();
            if (legacyLoadedMoneySnapshot.Exists)
            {
                batch.Delete(legacyLoadedMoneyCurrency);
            }

            if (slotsCurrencyNeedsPrecision)
            {
                batch.Set(
                    slotsCreditsCurrency,
                    CurrencyData(SlotsCreditsCurrencyId, "South African rand", 2, createdAtUtc),
                    SetOptions.MergeAll);
            }

            if (!currencySnapshots[1].Exists)
            {
                batch.Set(
                    freeGamesCurrency,
                    CurrencyData(FreeGamesCurrencyId, "Free games", 0, createdAtUtc),
                    SetOptions.MergeAll);
            }

            if (!currencySnapshots[2].Exists)
            {
                batch.Set(
                    specialPointsCurrency,
                    CurrencyData(
                        SpecialPointsCurrencyId,
                        "Wukong power points",
                        0,
                        createdAtUtc),
                    SetOptions.MergeAll);
            }

            if (!currencySnapshots[3].Exists)
            {
                batch.Set(
                    energyCurrency,
                    CurrencyData(EnergyCurrencyId, "Energy", 0, createdAtUtc),
                    SetOptions.MergeAll);
            }

            await batch.CommitAsync(cancellationToken);
        }

        var users = await database.Collection("users").GetSnapshotAsync(cancellationToken);
        foreach (var user in users.Documents)
        {
            await EnsureAccountSchemaAsync(user.Id, cancellationToken);
        }
    }
}
