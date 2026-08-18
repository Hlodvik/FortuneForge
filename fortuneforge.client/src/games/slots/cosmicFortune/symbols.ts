import { createThemedSymbolSet } from '../shared/themedSymbolSet'
import { COSMIC_FORTUNE_VISUALS as art } from './visuals'

export const COSMIC_FORTUNE_SYMBOLS = createThemedSymbolSet({
  id: 'cosmic-fortune-symbols-v1',
  serverSymbolSetId: 'cosmic-fortune-v1-symbols',
  symbols: {
    '2': { label: 'Fortune satellite', image: art.satellite },
    '3': { label: 'Silver comet', image: art.comet },
    '4': { label: 'Crescent moon station', image: art.moon },
    '5': { label: 'Ringed gas giant', image: art.planet },
    '6': { label: 'Lucky astronaut', image: art.astronaut },
    '7': { label: 'Interstellar rocket', image: art.rocket },
    ACE: { label: 'Alien captain wild', image: art.wild },
    FREE: { label: 'Wormhole free game', image: art.free },
    POWER: { label: 'Supernova power core', image: art.power },
    BOLT: { label: 'Atomic plasma charge', image: art.energy },
    BANANA: { label: 'Meteor shower trio', image: art.lineBonus },
    PAW: { label: 'Tractor-beam saucer', image: art.collector },
    SEAL_SYNC: { label: 'Crimson binary star', image: art.sync },
    SEAL_ROWS: { label: 'Sapphire ice planet', image: art.rows },
    SEAL_PAW: { label: 'Amber solar world', image: art.paw },
    SEAL_RAND: { label: 'Emerald garden planet', image: art.rand },
  },
  valueToken: { label: 'dark-matter crystal', image: art.value },
  energyEarnLabel: '+1 plasma charge',
  collectorFirstValue: 'tractor beam gathers crystals',
  collectorSecondValue: 'double beam haul',
  collectionAwardLabels: {
    SEAL_SYNC: '10 binary-link spins',
    SEAL_ROWS: '10 orbital-expansion spins',
    SEAL_PAW: '10 tractor-beam spins',
    SEAL_RAND: '10 star-map spins',
  },
})
