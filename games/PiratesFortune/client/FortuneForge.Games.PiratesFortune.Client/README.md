# Treasure Cove / Pirates’ Fortune client

`src/browser.ts` exports the real package-owned `PiratesGame` screen/controller. The host injects only account access, authenticated transport, wallet presentation and navigation chrome. The package owns five-reel play, gem collections, direct doubloon hauls, powder-keg awards and funded Island Search rounds, its controls, audio, help, win display, API contract validation and saved-request recovery.

`src/api.ts` builds intent-only requests and requires the production prepared-spin gateway. Lost or invalid replies retain the original operation and funded wager. Account changes cannot present an old response as the new account's result. Free wagers remain server locked, including odd historical averages. No public demo or practice engine is included.

The corresponding server package owns admission, randomness, rules, progression, persistence, retries and settlement orchestration through neutral platform wallet/storage ports. This client migration does not change engine odds, payouts or storage identifiers.

Run `npm run build --workspace @fortuneforge/games-pirates-fortune`, `npm test --workspace @fortuneforge/games-pirates-fortune` and `npm run verify:standalone --workspace @fortuneforge/games-pirates-fortune` from the repository root. Import-graph checks establish browser packaging boundaries, not financial correctness. Native emulator tests and `tools/FortuneForge.SlotClient.LocalHarness` verify the production paid round and exactly-once recovery separately.
