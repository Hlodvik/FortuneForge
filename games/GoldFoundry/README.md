# Gold Foundry game package

Gold Foundry is a first-class standalone game package with independently owned backend and browser client. `FortuneForge.Games.GoldFoundry` owns the versioned game, symbol, reel and paytable definitions; reel generation; match evaluation; line, collector and hammer payouts; energy awards; crucible progression; and current-engine replay.

`@fortuneforge/games-gold-foundry` owns the complete slot client: controller and sequencing, API validation and save-state shapes, cabinet manifest, rules/help, symbols, Gold Crucible and reel-crucible presentation, artwork, audio and CSS. It may depend only on neutral shared client packages. It must not import the Fortune Forge host or another game.

The package depends only on `games/shared/FortuneForge.Games.Slots.Contracts`. It must not reference `FortuneForge.Server`, Firebase, authentication, wallets, another game package, or client code.

## Host boundary

The files under `FortuneForge.Server/Slots/Games/GoldFoundry` are compatibility adapters. They may translate package outcomes into the existing `SpinResult`, audit, wallet and Firestore shapes. Firebase transactions, player identity, admission, wallet debits/credits and persistence remain host-owned. Do not put outcome rules in those adapters.

The browser host follows the same boundary. `GoldFoundryHost.tsx` supplies authenticated transport, the shared navbar and in-game shell, account/payment alerts, and no gameplay. The lobby keeps only lightweight catalog metadata and card artwork. Both `/slots/gold-foundry` and `/slots/gold-foundry/demo` lazy-load the standalone client package; Firebase Hosting still deploys the composed application normally.

The public route and stored identifiers remain:

- game: `gold-foundry-v1`
- symbols: `gold-foundry-symbols-v1`
- reels: `gold-foundry-reels-v1`
- paytable: `gold-foundry-paytable-v1`
- historical engine evidence: `gold-foundry-reel-collector-v1`
- current engine evidence: `gold-foundry-engine-v2`
- crucible rule: `gold-foundry-crucible-v2`

Historical `gold-foundry-reel-collector-v1` evidence continues through the host's legacy generic replay. New outcomes use the package replay and must not rewrite historical evidence.

## Making changes

Change gameplay in this package and add coverage in `tests/FortuneForge.Games.GoldFoundry.Tests`. A gameplay change that alters deterministic outcomes requires a new engine/rule identifier and a replay path for both the new and prior identifiers. Keep route IDs and serialized field shapes stable unless a coordinated data migration is explicitly planned.

Change browser gameplay in `client/FortuneForge.Games.GoldFoundry.Client`. Keep all Gold-specific media and presentation there. The shared slot client may expose neutral primitives, but it must not regain Gold-specific symbols, assets, controller branches or CSS.

Run the package-local check with:

```text
dotnet test games/GoldFoundry/tests/FortuneForge.Games.GoldFoundry.Tests/FortuneForge.Games.GoldFoundry.Tests.csproj
npm run check --workspace @fortuneforge/games-gold-foundry
npm run verify:gold-foundry-standalone
npm run verify:gold-foundry-bundle
```
