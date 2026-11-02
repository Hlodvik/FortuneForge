# Mines production integration

Source-integrated October 25, 2026. The approved dark slate design is unchanged.
This document does not claim a live deployment or authorize production test wagers.

## Host wiring

- Root workspaces/lockfile, both .NET solutions and host dependencies include Mines.
- `/games/mines` is lazy and authenticated, with shared navigation/reporting and account-scoped
  recovery. Identity comes from the normal host session, never a submitted user ID.
- The website is account-credit only: no practice toggle, practice URL override or silent fallback.
  Explicit practice API requests are rejected, not charged to a real wallet.
- The checked-in general flag is enabled; `Features:MinesPaidEnabled` stays false pending release.
- Browser writes require JSON, account authentication and an exact allowed origin. Firebase Hosting
  origins are explicitly trusted; forwarding headers cannot extend that allowlist.
- Catalogue, title, smoke inventory, normal/admin history and visibility/report allowlists include
  Mines. No bots are used.

## Wallet and history

The Firestore transaction commits integer-cent changes to the existing
`userBalances/{userId}_slotsCredits` wallet, financial ledger, private round state, session pointer
and immutable receipt together. Missing wallets start at zero, never auto-funded.
Terminal settlement creates exactly one `cardGameResults` entry in the same transaction; a
positive late-mine return follows the same single-payment path as cash-out/full-clear.
Replaying or racing a command cannot pay twice or reset an existing history acknowledgement.

New wagers are R1–R5, payout policy 2, 90% nominal RTP and maximum 50× (R250 at R5).
The full board remains playable; late-game mine returns preserve the expected RTP.
The API exposes the stage's next-mine return without exposing tile locations.
Old policy-1 rounds/receipts preserve their original wagers, edge and uncapped liability.
The client refreshes the session after a replay; current wallet units are not mistaken for cents.

The isolated sample remains a non-cash developer ledger, not public practice play. Its file store
is never used by the account-credit route.

## Privacy and release

`firestore.rules` is now tracked and configured in `firebase.json`. It denies all direct browser
reads/writes at every depth, including private Mines state/receipts and wallets. The website uses
the .NET API; the Firestore server SDK is governed separately by Google Cloud IAM.

`scripts/verify-firestore-privacy.mjs` proves the actual REST/rules boundary on a unique localhost
demo project: anonymous, authenticated owner, different player and forged browser-admin requests
are denied for direct get/list/create/update/delete, top-level and nested documents. Positive
controls prove the mock identity is recognized and the privileged server path still works.
It cannot contact a production project.

The explicit-loopback demo Firestore factory supplies a per-client emulator server identity so
the actual .NET backend remains functional under deny-all rules. Production credential selection
is unchanged. A host integration test verifies both backend transactions and browser denial with
the checked-in policy; ambiguous/credentialed/non-loopback emulator endpoints are rejected.

An earlier read-only live check found no active Firebase Rules release and anonymous direct
access denied. That alone does not prove all IAM principals are appropriately restricted.
Before enabling paid play in a release:

1. Deploy the checked-in deny rules and verify the effective live rules, including absence of a
   broader allow. The server must use its intended least-privilege service identity; no private
   collection may be publicly exposed through IAM or client rules.
2. Deploy the matching API and client from one reviewed commit; perform normal release checks.
3. Enable paid wagering only after those gates pass. Do not use production money for automation.

The current work changes no live flag, rule, wallet, commit or deployment. The manifest remains
`rules-pilot` until the paid release gates are met.
