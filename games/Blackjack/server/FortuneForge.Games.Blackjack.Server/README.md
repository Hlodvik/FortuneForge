The Blackjack package owns both historical solo hands and current table play,
including paid admission, private decks, split/double/insurance decisions, managed
seats, deadlines, results, replay, history and interrupted-command recovery.
Public URLs remain /api/cards/blackjack and /api/cards/blackjack/table. Existing
/internal/v1/solo and /internal/v1/tables compute aliases remain for rolling upgrades.

Solo commands freeze their original outcome before signed wallet settlement.
Table commands freeze their whole domain mutation and original response under
account and shard locks. Each account's gross game journal determines its net
wallet effect and minimum opening balance. The only refusable actor is confirmed
before credits; state becomes visible only after receipts reconcile. Pending joins
are discoverable through the account lock even before a session route exists.
Reads and the background sweep resume the original command after reply loss or
process restart. Roster finalization is durable and completes before locks release.

Historical collections, raw decks, prepaid wagers, command guards and result IDs
remain readable. New wallet-backed records retain backendOwner and walletCommandIds;
TableLedgerEvidence retains gross game accounting linked to generic wallet receipts.
The package never writes platform balances, account profiles or balanceTransactions.

Configure the worker with GameWallet:Url/SigningKey and, for managed seats or legacy
multiplayer recovery, ManagedPlayerRoster:Url/SigningKey. Platform game scopes must
allow account balance and player-name reads (AllowAccountBalanceRead and
AllowPlayerNameRead). Features:BlackjackEnabled and Features:BlackjackTableEnabled
control new admissions; existing hands, exact retries, history and sweeps continue.
Features:BlackjackMultiplayerEnabled selects new table mode. Keep whole-prefix
public forwarding authoritative while admissions are paused; fallback host routes
fail closed. Use the existing Firestore database and generic signed wallet host.

Protected completion evidence freezes the exact opening, double, split, insurance
and payout ledger IDs with verified wallet receipts. Historical prepaid hands keep
their original stake; unavailable old ledgers are explicitly marked incomplete.
Mutable table metadata is not the completion evidence. Pre-proof prepared commands
retain their original wallet outcome hashes.

Managed ownership is confirmed before new progression. Lost seats leave passively
without changing already-paid frozen terms. Permanent assignment/profile retirement
records serialize cleanup against future adoption and protect against delayed releases.
Do not expire or delete these retirement records. Ambiguous abort cleanup retains
bounded platform leases conservatively while ownership storage is unavailable.

Local verification: 71 backend tests (including emulator reply-loss/legacy and
immutable financial-proof cases), 19 client recovery tests, package client build
and standalone worker build with zero warnings/errors. Shared
host/gateway/security-audit integration is verified separately by its owner. This
source migration does not constitute a production deployment.
