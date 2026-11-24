# The Crooked Road slot client

An independently buildable Irish folklore slot game. The package, routes and server game contract use the
`crooked-road` identity. No predecessor game source or audio is included.

The executable `CrookedRoadGame` owns its full screen, controller, reel animation,
paid API requests and validation, exact request recovery, controls, audio,
help, win presentation and native road journeys. `src/browser.ts` exports the
same component for an independent browser release. The site injects generic
account/session transport and navigation shell ports. It does not render the
game through a manifest or implement Crooked Road's lifecycle. Neutral
`slot-client-core` and `client-presentation` libraries supply drawing and audio
utilities. Server outcomes remain authoritative for progress, awards and funds.

Approved artwork is documented in `src/assets/art/irish/README.md`. The exported
`CROOKED_ROAD_SYMBOL_IMAGES` and `CROOKED_ROAD_ARTWORK` objects are the single
client import owner for those images. Feature panels should use these exports.

`npm run check` checks source without emitting files. `npm run build` emits the
client modules and copies package-owned artwork and styles into `dist`.

The server-owned `../../src/FortuneForge.Games.CrookedRoad/Data/crooked-road-v1.json`
is the authoritative paytable and line geometry. Checking and building first
synchronize an exact generated mirror into `src/assets/contracts/`; never edit
that mirror. The definition reads its symbol guide from this contract so the
browser package can ship independently without a second hand-maintained table.

The original sound suite is reproducible with
`py -3.10 games/CrookedRoad/tools/render-audio.py` from the repository root.
It includes harp ambience, tactile reel/road sounds and win/journey stings, all
controlled by the game's audio settings using shared device preferences. See
the audio asset README for synthesis details. `npm test` runs package-owned
protocol, recovery, ownership and screen checks when requested.

`npm run local` starts a loopback shell on port 5175 that mounts the production
game with generic account/transport/navbar ports. Start the isolated paid server
fixture on port 5299 first; see the game-root README. All local spins use the
production paid request, persistence and settlement flow with isolated funds.
The local shell is excluded from the published `dist` package and browser entry.
