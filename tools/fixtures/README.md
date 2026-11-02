# Versioned browser and guide inputs

`midnight-vault/` contains seven captured public demo scenarios plus index and
engine provenance. `slot-contracts/` contains the twenty-game capture consumed
by the guide exporter and contract verifier. These are intentional test/tool
inputs, not build output. Their initial promotion hashes and original paths
are recorded in the integration inventory; the dated originals remain on disk.

Refresh both directories from the freshly built server with the SlotContracts
tool, verify with `node tools/verify-slot-contracts.mjs EXPORT --require-current`,
then run `node tools/import-slot-contracts.mjs EXPORT --apply`. The importer
accepts only declared catalogue/scenario JSON files and performs validation
before replacing versioned fixture files.

Build the host application before the specialized Midnight browser suites:

```powershell
npm run build
cd fortuneforge.client
npx playwright test --config playwright.midnight-vault.assembly.config.ts
```

Those suites serve the current `dist` and versioned scenarios; they no longer
depend on a dated local build under output. The default Playwright config owns
only slots.visual.spec.ts. Navbar, Craps, Midnight and game style isolation
have explicit specialized configs so CI cannot accidentally use the wrong host.
