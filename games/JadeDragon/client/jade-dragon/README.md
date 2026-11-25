# Jade Dragon paid client

The vanilla/Three.js frame owns its screen, controls, sound, animation and all game-specific API work. `src/main.js` calls the game controller in `src/bridge.js`, which reaches the package-owned contract validation, currency conversion, wager commands and saved-operation recovery in `src/api.ts`.

The parent frame supplies only authenticated HTTP and account access through `fortune-forge.game-transport.v1`. It never interprets a spin command, validates a cascade or decides recovery. Account tokens remain in the platform. New play requires the prepared-spin gateway; unavailable native admission is a migration/availability failure, with no host gameplay fallback.

The server package owns paid admission, authoritative outcomes, persisted progression, historical replay and settlement orchestration. Wallet balances and ledger execution remain generic platform services. Runtime/storage ID `empty-slots-v1`, symbol IDs and historical engine entitlements are preserved.

Wagers use 25-cent points. Current cascade awards use one-cent payout points; the game reads `payoutPointValueInCents` rather than confusing wager and payout units. New wagers remain capped at R100. Saved free games retain their admitted wager and engine. No public demo or practice mode is provided.

From the repository root, install with `npm ci`; Jade is an ordinary workspace using the shared root lockfile. Run `npm test --workspace @fortuneforge/jade-dragon-presentation` and `npm run build --workspace @fortuneforge/jade-dragon-presentation`. The host prebuild copies the independently built frame to its public assets.

`tools/FortuneForge.SlotClient.LocalHarness` exercises the same production round service against a loopback Firestore emulator and isolated test wallet. Browser verification includes a real paid spin and a deliberately lost settled reply, then confirms exact-operation recovery without another charge. The harness changes infrastructure and test funds only.
