import type { SlotRulesSet } from '@fortuneforge/slot-game-runtime/experience'
import contract from './assets/rules/client-rules.json' with { type: 'json' }
import { ARCANE_ARCHIVES_GAME_ID } from './symbols'

/** Game-owned input and presentation settings; the server admits and settles every wager. */
export const ARCANE_ARCHIVES_UI_RULES: SlotRulesSet = {
  gameId: ARCANE_ARCHIVES_GAME_ID,
  pointValueInCents: contract.pointValueInCents,
  wagerOptions: Array.from({ length: (contract.maximumWagerPoints - contract.minimumWagerPoints) / contract.wagerIncrementPoints + 1 },
    (_, index) => contract.minimumWagerPoints + index * contract.wagerIncrementPoints),
  defaultWagerRand: 3,
  initialReels: [
    ['2', '3', 'MANDRAKE_ROOT', '6'],
    ['3', '4', '5', 'POWER'],
    ['5', '2', '4', '6'],
    ['4', '6', '5', '3'],
    ['BOLT', '3', '6', '2'],
  ],
  autoSpinDelayMs: 1_000,
  autoSpinSpeedMultiplier: 3,
  baseSpinSpeedMultiplier: 1,
  reelTravelRowCount: 8,
  specialBoostCost: 8,
  energyMeterCapacity: 100,
}
