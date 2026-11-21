using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Blackjack;

public sealed partial class BlackjackSoloStore
{
    private async Task<BlackjackFinancialSnapshot> ReadPriorFinancialEvidenceAsync(Transaction transaction, BlackjackGame game, CancellationToken token)
    {
        var commands = await transaction.GetSnapshotAsync(database.Collection(Commands).WhereEqualTo("gameId", game.GameId), token);
        var prior = commands.Documents.Select(snapshot => ReadCommand(snapshot, game.UserId))
            .Where(command => command.Wallet is { InsufficientFunds: false }).ToArray();
        var entries = prior.SelectMany(CurrentFinancialEntries).ToList();
        if (!prior.Any(command => command.ExpectedVersion == 0))
        {
            var snapshot = await transaction.GetSnapshotAsync(database.Collection("balanceTransactions").Document(game.GameId + "-wager"), token);
            var legacy = snapshot.Exists ? BlackjackFinancialProof.Legacy(snapshot, game.GameId)
                : new("balanceTransactions", game.GameId + "-wager", game.UserId, "blackjack-wager", game.GameId,
                    -game.WagerCents, "legacy-host-unavailable", EvidenceAvailable: false);
            if (legacy.UserId != game.UserId || legacy.Kind != "blackjack-wager" || legacy.AmountCents != -game.WagerCents)
                throw new GameWalletUnavailableException();
            entries.Add(legacy);
        }
        return new(entries.ToArray(), prior.Select(CurrentFinancialOperation).ToArray());
    }

    private static BlackjackFinancialSnapshot FinancialEvidence(SoloCommand command) => new(
        (command.PriorFinancialEvidence ?? BlackjackFinancialSnapshot.Empty).Entries.Concat(CurrentFinancialEntries(command)).ToArray(),
        (command.PriorFinancialEvidence ?? BlackjackFinancialSnapshot.Empty).Operations.Append(CurrentFinancialOperation(command)).ToArray());

    private static IEnumerable<BlackjackLedgerProof> CurrentFinancialEntries(SoloCommand command)
    {
        if (command.Wallet is not { InsufficientFunds: false } wallet) yield break;
        if (wallet.WagerTransactionId is { } wager)
            yield return new("balanceTransactions", wager, command.UserId,
                command.ExpectedVersion == 0 ? "blackjack-wager" : "blackjack-double", command.GameId,
                -command.WagerCents, "signed-wallet", command.Id, wallet.SettlementId);
        if (wallet.PayoutTransactionId is { } payout)
            yield return new("balanceTransactions", payout, command.UserId, "blackjack-payout", command.GameId,
                command.PayoutCents, "signed-wallet", command.Id, wallet.SettlementId);
    }

    private static BlackjackWalletOperationProof CurrentFinancialOperation(SoloCommand command) =>
        new(BlackjackBackend.SoloCommands, command.Id, OutcomeHash(command),
            CurrentFinancialEntries(command).Select(entry => entry.DocumentId).ToArray(),
            new Dictionary<string, GameWalletSettlementReceipt> { [command.UserId] = command.Wallet ?? throw new GameWalletUnavailableException() });
}
