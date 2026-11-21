using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Games.Slots;

// Used by the gateway and game packages while they share existing slot admissions.
public static class SlotSpinGuard
{
    public const string Collection = "slotSpinGuards";
    public const string ActiveAdmissionField = "activeAdmissionId";
    public const string PreparedAdmissionField = "preparedAdmissionId";
    public const string LastSpinStartedField = "lastSpinStartedAt";
    public static readonly TimeSpan ActiveAdmissionTimeout = TimeSpan.FromSeconds(30);

    public static string DocumentId(string userId, string storageGameId) =>
        $"{userId}_{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(storageGameId)))}";
}
