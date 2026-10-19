using System.Text.Json;
using FortuneForge.Server.Bots;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed partial class FirestoreCompetitiveSolitaireStore
{
    private sealed record QueuedManagedSeat(
        string UserId,
        string PlayerName,
        int SkillLevel,
        DateTime JoinedAtUtc)
    {
        public ManagedPlayerProfile ToProfile() => new(
            UserId, PlayerName, SkillLevel,
            new HashSet<string>([ManagedPlayerGames.Solitaire], StringComparer.OrdinalIgnoreCase),
            JoinedAtUtc);
    }

    private static IReadOnlyList<QueuedManagedSeat> ReadQueuedManagedSeats(DocumentSnapshot partition)
    {
        var json = ReadString(partition, "queuedManagedSeatsJson");
        return string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<QueuedManagedSeat[]>(json, JsonOptions) ?? [];
    }

    private static Dictionary<string, object> ManagedQueueFields(
        IReadOnlyList<QueuedManagedSeat> seats,
        string assignmentId,
        string firstHumanTicketId,
        DateTime? nextAtUtc) => new()
    {
        ["queuedManagedSeatsJson"] = seats.Count == 0
            ? string.Empty
            : JsonSerializer.Serialize(seats, JsonOptions),
        ["queuedManagedAssignmentId"] = seats.Count == 0 ? string.Empty : assignmentId,
        ["queuedManagedFirstTicketId"] = seats.Count == 0 ? string.Empty : firstHumanTicketId,
        ["queuedManagedNextAt"] = seats.Count == 0 || nextAtUtc is null
            ? string.Empty
            : Timestamp.FromDateTime(nextAtUtc.Value)
    };

    private static string QueueAssignmentId(string partitionKey, string firstHumanTicketId) =>
        $"solitaire-queue:{CreateLookupKey($"{partitionKey}\n{firstHumanTicketId}")}";
}
