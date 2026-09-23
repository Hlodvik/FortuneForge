# Slot visual regression tests

The slot screenshot suite renders the real Vite application in Chromium. It intercepts only the public demo status and spin requests, supplying deterministic reel windows, payouts, collection progress, and free-spin state. No account, wallet, Firebase emulator, or live service is required.

The checked-in baselines cover the shared slot architecture in six representative states:

- idle cabinet and persistent feature path;
- rules dialog and payline diagrams;
- active special-feature transition;
- settled win with highlighted symbols and outcome copy;
- demo-service failure;
- narrow mobile layout with the Spin control in view.

The manifest contract tests separately require all twenty cabinets to retain unique IDs, themes, symbols, audio, paylines, and named feature definitions. Together, these checks give the shared rendering system broad coverage without maintaining 120 nearly identical screenshots.

## Run locally

Install the pinned Chromium build once:

```powershell
npx playwright install chromium
```

Compare against the baselines:

```powershell
npm run test:slots:visual
```

After intentionally reviewing a visual change, regenerate baselines with:

```powershell
npm run test:slots:visual:update
```

Review every changed PNG before committing it. Playwright writes failure diffs and traces beneath `fortuneforge.client/test-results`; generated reports and failure artifacts are ignored.

## Platform policy

Font rasterization and image decoding vary by operating system. Baselines are therefore generated as Playwright's platform-specific `chromium-win32` snapshots and CI runs this suite on `windows-latest`. Do not rename them to platform-neutral files or compare them on Linux/macOS. The ordinary unit, integration, and production-contract smoke suites remain platform-independent.
