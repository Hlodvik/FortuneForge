# Frontier Fortune

Frontier Fortune is an independently owned, paid slot game with a spaghetti western saloon cabinet and a reel-driven feature called **Outlaw's Hand**. The five reels stay on screen while the player chooses cards, holds a hand, and uses later deals to improve it. The game stage fits the space beneath the real Fortune Forge navigation without page scrolling.

## Outlaw's Hand

Ordinary paid reel results build a persistent Wanted ante. Two WANTED posters add one chip; three or more add two. Every chip retains the paid wager that funded it. At twelve chips the player can deal in. The server consumes the oldest twelve chips, computes the funded hand stake, and preserves overflow chips and their funding.

The same cabinet deals three included card grids. The player chooses one visible card from each open reel and confirms the hand. Before later deals, the player chooses which hand cards to HOLD. Every reel deals again, while held hand cards stay. Unheld positions must take a replacement from the corresponding reel. The final confirmation settles one hand award.

The feature pays its hand table, rather than ordinary paylines, and charges no additional wager. Fixed J/Q/K/A suits support rank combinations; this game makes no straight or flush claim. Actual WILD cards substitute for ranks, and five actual WILDs have a distinct top award. Numeric rules and awards belong to the versioned JSON contracts, not the frontend.

## Ownership

- `src/FortuneForge.Games.FrontierFortune`: native base evaluator, recorded randomness, Outlaw rules, hand classification and immutable transitions.
- `server/FortuneForge.Games.FrontierFortune.Server`: admission, persistent ante and hand state, command validation, prepared outcomes, wallet settlement, recovery and replay evidence.
- `client/FortuneForge.Games.FrontierFortune.Client`: native React cabinet, actual house controls, card selection and holds, authenticated transport, artwork and help.
- `frontier-rules.json`: frozen version-one base math and historical bounty rules.
- `frontier-outlaw-rules.json`: current version-two ante, card weights, deal count and hand awards.

The financial identifier remains `high-noon-fortune-v1`, and the catalogue/release identifier remains `high-noon-fortune`. These are compatibility identifiers. The active client and backend import no High Noon or sibling game gameplay. The canonical paid route is `/slots/frontier-fortune`; there is no public demo or practice mode.

Only neutral infrastructure crosses the game boundary: accounts, authenticated fetch, wallet operations, storage, audit services and shared site/control components. There is no browser outcome generator, altered local odds, forced bonus or client-owned game balance.

## Persistence and historical obligations

Every accepted command binds one operation ID to its exact terms. Preparation persists the before/after states and recorded random draws. Wallet settlement and finalization are separately recoverable, so an interrupted response retries the same operation without a second charge or credit. Hand actions also bind the expected revision and round identity.

Version-one DTO serialization, rules and replay remain frozen. Version-two fields are explicitly versioned and omitted from historical JSON when absent. Outstanding v1 operations and earned features remain recoverable through the dedicated `legacy-v1` routes; new v1 wagers are disabled. Original High Noon obligations are preserved and require reconciliation rather than being silently discarded or interpreted as an Outlaw hand.

Production metadata is supplied by `FrontierOutlawDefinitions`. Generic classic spins are refused for this game; they cannot become an alternate outcome path when the native worker is unavailable.

## Local paid testing

Local testing uses the production client, endpoints, random source, round service, persistence and settlement flow. Only the account, database and wallet funds are isolated. Deterministic random inputs exist exclusively in automated tests and economics qualification.

From the repository root, start the dedicated emulator:

```powershell
firebase emulators:start --only firestore --project demo-frontier-paid-validation --config games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.LocalHarness/firebase.json
```

Start the paid API in a second terminal:

```powershell
$env:FIRESTORE_EMULATOR_HOST = '127.0.0.1:8873'
dotnet run --project games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.LocalHarness/FortuneForge.Games.FrontierFortune.LocalHarness.csproj --no-launch-profile
```

The loopback API is `http://127.0.0.1:5279`. The harness refuses any other database destination. Its allowlisted local account selector separates the review account from QA while preserving each account's earned rounds. It does not expose forced outcomes.

From the client package, start the local infrastructure mount in a third terminal:

```powershell
npx vite --config local/vite.config.mjs
```

Open `http://127.0.0.1:5187/` for review. Automated or agent-driven interactive QA must use `http://127.0.0.1:5187/?localAccount=qa` so it cannot advance the review account's hand. The local infrastructure sends the account selector; the packaged game has no testing branch. This mount uses the real site shell and the same production game component. Its requests pass through the ordinary paid API; it is not a public play mode or deployment target.

Spin stays mounted during the hand. While cards need selection it is disabled and the adjacent confirmation action becomes available once every open reel has a selection. Confirming the cards unlocks the next included deal, or settles the final hand.

## Verification

```powershell
npm run check --workspace @fortuneforge/games-frontier-fortune
npm run test --workspace @fortuneforge/games-frontier-fortune
dotnet test games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Tests/FortuneForge.Games.FrontierFortune.Tests.csproj -m:1
dotnet build games/shared/FortuneForge.Games.PackagedGame.Server/FortuneForge.Games.PackagedGame.Server.csproj -p:GamePackage=HighNoonFortune -m:1
npm run verify:slot-packages
npm run verify:game-routes
```

Native tests cover ante funding and overflow, visible-card selection, holds, command-array replay, final crediting once, uncertain wallet recovery, audit readback precision and byte-stable v1 evidence. Browser qualification checks document height, internal stage height and the bounds of every gameplay control in base and active-hand phases, including small portrait, short landscape and the in-app panel width.

## Economics qualification

The automated console invokes the production evaluator and hand transitions. The `outlaw` command reports the exact base return and ante rate, an achievable sampled hand strategy, and an analytic upper bound. Sampling is not a proof of optimal play. The `outlaw-optimal` command exhaustively solves all three deals, visible card selections and legal holds using exact integer-weighted Bellman calculations, cross-checks completion feasibility and native hand semantics, and reports optimal fixed-wager RTP.

```powershell
dotnet run --project games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Math/FortuneForge.Games.FrontierFortune.Math.csproj --configuration Release -- outlaw 20000 output/analysis/frontier-outlaw-economics.json
dotnet run --project games/HighNoonFortune/tests/FortuneForge.Games.FrontierFortune.Math/FortuneForge.Games.FrontierFortune.Math.csproj --configuration Release -- outlaw-optimal output/analysis/frontier-outlaw-optimal-rtp.json
```

Both reports assume a constant legal wager and completed earned hands over a long-run horizon. Changing wagers can lower return because the server rounds the twelve-chip average stake down to a legal wager increment. The reports record the semantic rule hash; the exact solver also identifies the native classifier assembly. A persisted math version must not be silently retuned: preserve its schedule and introduce a versioned successor or frozen per-round contract.

## Artwork

Original PNG masters live in `artwork/source`; optimized live assets live in the native client's `src/assets/frontier`. The current cabinet has a thin walnut-and-silver surround, an actual transparent reel aperture and a physical card rail. Its measured placement geometry is generated beside the asset, and the frontend consumes that JSON.

`node tools/artwork/frontier-runtime.mjs` derives runtime variants. Retired v1 target-board props remain historical source masters and are excluded from the current export. Prompt records, including the v2 cabinet and card back, live in `output/artwork/frontier-fortune`.

Building source does not deploy it. Frontend and backend activation remain part of the repository's existing independent release workflow.
