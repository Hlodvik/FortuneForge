using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore(FirestoreDb database) : IPaymentStore
{
    private const string SlotsCreditsCurrencyId = "slotsCredits";

}
