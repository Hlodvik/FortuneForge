# Slot contract capture

Build and run this project from the repository's current source. It references
the production server project directly and uses its actual demo controller,
definitions, reel generator, evaluator and payout calculator. It does not tune
math, start an HTTP server, connect to Firestore or settle an account.

From the repository root, choose a new output directory:

```powershell
dotnet run --project tools/FortuneForge.SlotContracts --configuration Release -- . artifacts/slot-contracts/review
node tools/verify-slot-contracts.mjs artifacts/slot-contracts/review --require-current
node tools/import-slot-contracts.mjs artifacts/slot-contracts/review --apply
node fortuneforge.client/scripts/export-slot-guide-contracts.mjs
```

The last command regenerates presentation contracts and must be reviewed with
the corresponding server changes. Capture files use CreateNew; previous runs
are preserved. Fresh source and loaded-assembly hashes are recorded, source
hashes must remain stable during capture, and missing required scenarios fail.
Always rebuild after changing server source; recorded hashes are provenance,
not an independent proof that a stale assembly matches current source.

The bounded seeded captures verify public response identity, payout sums,
bonus continuation and replay. Collection cases begin at target minus one.
They do not establish natural collection frequency, paid settlement integrity,
statistical RTP, owner approval or release authorization.
