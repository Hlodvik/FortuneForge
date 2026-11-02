# Local Craps browser fixture

This loopback-only test host calls the production paid Craps store and auditor
against isolated `demo-craps-browser-*` Firestore emulator projects. It refuses
an absent or non-loopback FIRESTORE_EMULATOR_HOST. It never uses a live wallet.
The test assembly name grants the same internal access as the server tests.

From the repository root, after starting a local emulator:

```powershell
$env:FIRESTORE_EMULATOR_HOST = '127.0.0.1:8080'
dotnet run --project tools/FortuneForge.CrapsBrowserFixture --configuration Release
```

The specialized Playwright config starts this host itself; do not also start a
second copy on port 5298. From fortuneforge.client run:

```powershell
npx playwright test --config playwright.craps-continuation.config.ts
```

The emulator must already be available. Fixture session resets generate fresh
demo projects, and deterministic dice are supplied only by this local harness.
