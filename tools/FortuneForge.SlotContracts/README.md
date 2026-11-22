# Slot contract capture

Build and run this project from the repository's current source. It references
the production server project directly and uses its actual demo controller,
definitions, reel generator, evaluator and payout calculator. It does not tune
math, start an HTTP server, connect to Firestore or settle an account.

From the repository root, choose a new output directory:

```powershell
dotnet run --project tools/FortuneForge.SlotContracts --configuration Release -- . artifacts/slot-contracts/review
node tools/verify-slot-contracts.mjs artifacts/slot-contracts/review --require-current
```

Refresh guide contracts independently through the actual package-backed definition
provider, without invoking the retired host gameplay controller:

```powershell
dotnet run --project tools/FortuneForge.SlotContracts --configuration Release -- --guide-contracts . artifacts/slot-contracts/guides
node fortuneforge.client/scripts/export-slot-guide-contracts.mjs artifacts/slot-contracts/guides
```

This records all twenty slot source projects declared through the shared slot
contracts/runtime dependencies, their source hashes, and the loaded server assembly.
New captures explicitly identify `sha256-utf8-lf-v1`: source fingerprints decode
strict UTF-8, omit a leading UTF-8 BOM, normalize CRLF/CR to LF, and hash the UTF-8
text. Assembly hashes remain raw SHA-256 bytes. This keeps source identity stable
across Windows and Linux checkouts. Unversioned historical captures retain their
original raw-byte meaning.
Jade Dragon uses its dedicated guide. To refresh the versioned browser
scenario fixtures, also run `node tools/import-slot-contracts.mjs artifacts/slot-contracts/review --apply`.
Capture files use CreateNew; previous runs
are preserved. Fresh source and loaded-assembly hashes are recorded, source
hashes must remain stable during capture, and missing required scenarios fail.
Always rebuild after changing server source; recorded hashes are provenance,
not an independent proof that a stale assembly matches current source.

The bounded seeded captures verify public response identity, payout sums,
bonus continuation and replay. Collection cases begin at target minus one.
They do not establish natural collection frequency, paid settlement integrity,
statistical RTP, owner approval or release authorization.
