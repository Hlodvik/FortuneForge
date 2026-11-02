# Mines payout-policy 2 verification — October 25, 2026

Source verification only. No commit, push, live deployment, production wallet transaction or
live rule/flag change was performed. The main paid flag remains off pending release.

## Automated checks

- 49 engine tests: independent exact safe/mine curve, 300 cash-out strategies, conditional and
  full-path nominal 90% expected RTP, cent-rounding bounds, all 24 full-board configurations,
  maximum 50× / R250, current limits, original policy-1 recovery and corrupt-state rejection.
- 38 backend tests against isolated persistence/emulator: exact consolation and full-clear
  amounts, single credit/history under replay/races/restart, private active views, account/origin
  gates, client policy/payout injection rejection and unchanged old accepted liabilities.
- 6 host API Mines tests: real host wiring, account-only enforcement and current policy metadata.
- 206 Mines client tests, including three mobile-spacing regression guards.
- 66 scoped host UI tests, including route/account balance/history/report/catalogue behavior.
- 2 privacy-script safety tests; external/non-demo targets are rejected.
- 53 targeted host/emulator checks passed after correcting the explicit-localhost SDK identity.
  The actual host factory completes wallet transactions, reads and queries under the checked-in
  deny-all rules; 30 anonymous/player/forged-admin browser reads/writes remain denied.
- 200 emulator browser REST operations denied: get/list/create/update/delete for anonymous,
  authenticated owner, another player and forged browser-admin identities; top-level and nested
  game/wallet records. A positive control proves mock authentication is recognized. Privileged
  reads/writes still work. No dependencies were added for this proof.
- TypeScript, Mines package build, fresh main client build, source ownership, 44-route inventory
  and whitespace checks passed. The historical browser E2E source was syntax checked, not rerun.

## Integrated browser checks

The real authenticated `/games/mines` host route was exercised using the normal sign-in flow and
a disposable account in the localhost `demo-fortuneforge-mines-integration` Firestore emulator.
Paid protocol enablement was an environment override only in that demo server, not a source/live
flag change. No website practice UI or fallback was introduced.

- R1 / three-mine wager: debit, safe reveal, exact R1.02 cash-out and account refresh passed.
- R5 / 21-mine fixture: 5.625× → 45× → 47.5× → 50×. At two gems, the active UI disclosed
  R223.80 if the next tile hit a mine. It remained playable through the last safe tile and paid
  exactly R250 on natural full clear. Refresh restored the server state and accepted terms.
- Edge-case safe picks were selected using emulator-only private fixture data, not any player
  API disclosure. This is a deterministic reachability check, not an ordinary random-play result.
- Account navbar/reporting remained present; the board retained the approved slate art.

Late-mine information exposed two real spacing issues, both corrected and rechecked:

| Viewport | Measured result with late-mine disclosure |
| --- | --- |
| 320×568 | No content overflow; 45px-high tiles retained; action bottom 546.4px. |
| 390×844 | 60px-high tiles; action bottom 751.9px; supporting footer may scroll 7px. |
| 667×375 | No overflow; 34px-high landscape tiles unchanged; action bottom 331.6px, no clipping. |

The browser screenshot `.tmp/mines-v2-full-clear.png` records the integrated 50× / R250 result.
It is local test evidence, not a production balance or release screenshot.

## Security / release boundary

The tracked root `firestore.rules` deny all direct browser access, including recursive paths;
the app uses its account-authenticated .NET API. A read-only project IAM snapshot found no
`allUsers` or `allAuthenticatedUsers` project bindings. That is not an exhaustive audit of every
inherited or separately IAM-authorized principal.

Applying those rules to the integrated demo project exposed an explicit-endpoint emulator setup
bug: the local .NET SDK had no server identity and was correctly denied as an anonymous client.
The guarded demo-only factory now supplies the emulator's owner metadata per client. It rejects
credentialed/ambiguous/non-loopback endpoints and leaves production credentials unchanged.
No global environment or rules weakening was used to repair the test environment.
After rebuilding/restarting that demo server, the integrated browser route successfully restored
the same completed 50× / R250 round and balance under the deny-all rules.

Deploy and verify the effective server-only rules alongside the matching reviewed API/client
before enabling paid play. Old accepted policy-1 rounds must retain original payouts; the new cap
cannot be used to erase those obligations.
