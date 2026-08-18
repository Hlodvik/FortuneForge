import { createThemedSymbolSet } from '../shared/themedSymbolSet'
import { DINO_DOMINION_VISUALS as art } from './visuals'

export const DINO_DOMINION_SYMBOLS = createThemedSymbolSet({
  id: 'dino-dominion-symbols-v1',
  serverSymbolSetId: 'dino-dominion-v1-symbols',
  symbols: {
    '2': { label: 'Ancient fossil bone', image: art.bone },
    '3': { label: 'Speckled dinosaur egg', image: art.egg },
    '4': { label: 'Primeval fern', image: art.fern },
    '5': { label: 'Giant dinosaur footprint', image: art.footprint },
    '6': { label: 'Swift raptor', image: art.raptor },
    '7': { label: 'Mighty tyrannosaurus', image: art.rex },
    ACE: { label: 'Triceratops wild crest', image: art.wild },
    FREE: { label: 'Volcanic cave free game', image: art.free },
    POWER: { label: 'Golden amber power stone', image: art.power },
    BOLT: { label: 'Falling meteor charge', image: art.energy },
    BANANA: { label: 'Fossil claw trio', image: art.lineBonus },
    PAW: { label: 'Paleontologist field kit', image: art.collector },
    SEAL_SYNC: { label: 'Crimson fang fossil', image: art.sync },
    SEAL_ROWS: { label: 'Sapphire shell fossil', image: art.rows },
    SEAL_PAW: { label: 'Amber track fossil', image: art.paw },
    SEAL_RAND: { label: 'Emerald leaf fossil', image: art.rand },
  },
  valueToken: { label: 'museum amber token', image: art.value },
  energyEarnLabel: '+1 meteor charge',
  collectorFirstValue: 'field kit gathers amber tokens',
  collectorSecondValue: 'double museum haul',
  collectionAwardLabels: {
    SEAL_SYNC: '10 predator-pack spins',
    SEAL_ROWS: '10 deep-strata spins',
    SEAL_PAW: '10 fossil-rush spins',
    SEAL_RAND: '10 amber-vein spins',
  },
})
