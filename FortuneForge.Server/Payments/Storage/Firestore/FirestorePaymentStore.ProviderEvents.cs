using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    public Task<PaymentProviderEventProcessingLease> BeginProviderEventProcessingAsync(
        string providerId,
        string eventId,
        string eventType,
        DateTime occurredAtUtc,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken)
    {
        var eventReference = ProviderEventDocument(providerId, eventId);
        return database.RunTransactionAsync(
            async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(
                    eventReference,
                    cancellationToken);
                if (snapshot.Exists)
                {
                    if (!string.Equals(
                        ReadString(snapshot, "eventType"),
                        eventType,
                        StringComparison.Ordinal))
                    {
                        return new PaymentProviderEventProcessingLease(
                            PaymentProviderEventProcessingState.Conflict,
                            IsRetry: true);
                    }

                    var status = ReadString(snapshot, "status", "applied");
                    if (string.Equals(status, "applied", StringComparison.Ordinal))
                    {
                        return new PaymentProviderEventProcessingLease(
                            PaymentProviderEventProcessingState.Applied,
                            IsRetry: true);
                    }

                    transaction.Update(eventReference, new Dictionary<string, object>
                    {
                        ["status"] = "processing",
                        ["receivedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                        ["lastReceivedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                        ["processingStartedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                        ["attempts"] = FieldValue.Increment(1L)
                    });
                    return new PaymentProviderEventProcessingLease(
                        PaymentProviderEventProcessingState.Processing,
                        IsRetry: true);
                }

                transaction.Create(eventReference, new Dictionary<string, object>
                {
                    ["providerId"] = providerId,
                    ["eventId"] = eventId,
                    ["eventType"] = eventType,
                    ["status"] = "processing",
                    ["occurredAt"] = Timestamp.FromDateTime(occurredAtUtc),
                    ["firstReceivedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                    ["receivedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                    ["lastReceivedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                    ["processingStartedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                    ["attempts"] = 1L,
                    ["expiresAt"] = Timestamp.FromDateTime(receivedAtUtc.AddDays(90))
                });
                return new PaymentProviderEventProcessingLease(
                    PaymentProviderEventProcessingState.Processing,
                    IsRetry: false);
            },
            cancellationToken: cancellationToken);
    }

    public Task MarkProviderEventAppliedAsync(
        string providerId,
        string eventId,
        DateTime appliedAtUtc,
        CancellationToken cancellationToken)
    {
        var eventReference = ProviderEventDocument(providerId, eventId);
        return database.RunTransactionAsync(
            async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(
                    eventReference,
                    cancellationToken);
                if (!snapshot.Exists)
                {
                    return;
                }

                if (string.Equals(ReadString(snapshot, "status", "applied"), "applied", StringComparison.Ordinal))
                {
                    return;
                }

                transaction.Update(eventReference, new Dictionary<string, object>
                {
                    ["status"] = "applied",
                    ["appliedAt"] = Timestamp.FromDateTime(appliedAtUtc)
                });
            },
            cancellationToken: cancellationToken);
    }
}
