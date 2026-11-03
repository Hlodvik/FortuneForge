# Wukong paw fixture preview

Local synthetic prototype only; not a playable paid game or production inventory.
Run from the existing checkout after coordinating the shared .NET build window:

```powershell
dotnet run --project tools/FortuneForge.WukongPawPreview --no-launch-profile
```

The host binds only `http://127.0.0.1:4197`. Its API is under
`/__prototype/wukong-paws`; the standalone UI uses port 4196. Use the same
`127.0.0.1` hostname and credentials-enabled fetches so the HttpOnly local owner
cookie survives a reload. No Firestore, account, authentication or wallet services
are registered, and `/api/slots` is absent. Restarting the host discards fixtures.

The initial three 100-point paw examples are a synthetic fixture bank, not a
production inventory cap or approved wager-binding rule. Unused examples stay
banked across spins, next previews and browser reloads. They never expire or get
selected automatically. Only an explicitly placed example is made unavailable
when its synthetic spin is admitted. A failed engine call blocks the fixture;
there is no invented automatic refund. Explicit fixture reset creates fresh
examples. No real paws are granted, bought, persisted or consumed.

`PAW_EARN_SCROLL` is a proposed distinct earn-symbol role. Threshold, rate and
production wager-binding are unset. The initial illustrated board and optional
`fixture-earn-symbol` switch show this placeholder without awarding any paw.
The cash-collecting `PAW`, paying peach symbol `3`, seals and banana retain their
separate roles. The versioned prototype calls the actual shared `SpinService`;
the production endpoint and historical replay rules are separate.

The prototype evidence version is `wukong-every-paw-preview-v1`. Its nonfinancial
collector pays exactly `visible paw count × displayed wager-linked cash sum`,
without a hidden cash deduction. Existing production/historical
`shared-slots-engine-v1` still uses its collector cap of two and original 0.946
scale. Regular paylines and banana behavior are unchanged. Preview totals do not
certify a production payout model or authorize an earning/consumption rule.

GET `/state` returns the flat controlled UI state. POST requests have immutable
`requestId` receipts; changed-key reuse is rejected. Endpoints are `/place`,
`/clear`, `/spin`, `/next-preview`, `/reset-fixture`, `/fixture-earn-symbol`.
`next-preview` clears the sealed placement after settlement while preserving the
bank, result board and receipts. `clear` only cancels an unspun placement. Calls
without placement never use banked paws. Concurrent requests cannot alter a spin
after admission. Fixture injection is server-controlled and is not an earn rate.
