import { fileURLToPath } from 'node:url'
import { verifyStandaloneGameClient } from '../../../../../scripts/verify-standalone-game-client.mjs'
await verifyStandaloneGameClient(fileURLToPath(new URL('../', import.meta.url)), fileURLToPath(new URL('../../../../../', import.meta.url)), 'PiratesFortune')
