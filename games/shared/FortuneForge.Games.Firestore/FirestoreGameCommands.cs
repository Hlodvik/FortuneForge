using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Firestore;

/// <summary>Commits a game command and its authenticated replay receipt in one transaction.</summary>
public static class FirestoreGameCommands
{
    public static Task<T> ExecuteAsync<T>(FirestoreDb database, string receiptCollection, string userId, string? bodySha256,
        Func<Transaction, CancellationToken, Task<T>> operation, Func<Exception> conflict, CancellationToken token) =>
        ExecuteAsync(database, receiptCollection, userId, bodySha256, operation, conflict, token, "game command");

    internal static Task<T> ExecuteAsync<T>(FirestoreDb database, string receiptCollection, string userId, string? bodySha256,
        Func<Transaction, CancellationToken, Task<T>> operation, Func<Exception> conflict, CancellationToken token, string title)
    {
        var observation = GameSecurityContext.Current;
        var operationId = observation?.CommandId is { Length: > 0 } commandId ? $"command\n{commandId}"
            : observation?.RequestId is { Length: > 0 } requestId ? $"request\n{requestId}" : null;
        if (operationId is not null && bodySha256 is not { Length: 64 })
            throw new FirestorePuzzleStorageException("The authenticated game command is missing its verified body digest.");
        var receipt = observation is not null && operationId is not null
            ? database.Collection(receiptCollection).Document(GameSecurityEventDocuments.Hash(
                $"{userId}\n{operationId}\n{observation.Method}\n{observation.Path}"))
            : null;

        return database.RunTransactionAsync(async transaction =>
        {
            if (receipt is not null)
            {
                var prior = await transaction.GetSnapshotAsync(receipt, token);
                if (prior.Exists)
                {
                    if (!string.Equals(prior.GetValue<string>("bodySha256"), bodySha256, StringComparison.Ordinal))
                        throw conflict();
                    return ReadReceipt<T>(prior, title);
                }
            }

            var result = await operation(transaction, token);
            if (receipt is not null)
            {
                transaction.Create(receipt, new Dictionary<string, object>
                {
                    ["json"] = JsonSerializer.Serialize(result, RoundAuditCapture.Json),
                    ["bodySha256"] = bodySha256!,
                });
            }
            return result;
        }, cancellationToken: token);
    }

    private static T ReadReceipt<T>(DocumentSnapshot snapshot, string title)
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(snapshot.GetValue<string>("json"), RoundAuditCapture.Json);
            if (result is null)
                throw new FirestorePuzzleStorageException($"A stored {title} document has no state.");
            return result;
        }
        catch (JsonException exception)
        {
            throw new FirestorePuzzleStorageException($"A stored {title} document has invalid state.", exception);
        }
    }
}
