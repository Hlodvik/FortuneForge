import { createThemedSymbolSet } from '../shared/themedSymbolSet'
import { ARCANE_ARCHIVES_VISUALS as art } from './visuals'

export const ARCANE_ARCHIVES_SYMBOLS = createThemedSymbolSet({
  id: 'arcane-archives-symbols-v1',
  serverSymbolSetId: 'arcane-archives-v1-symbols',
  symbols: {
    '2': { label: 'Whispering candle', image: art.candle },
    '3': { label: 'Moon-feather quill', image: art.quill },
    '4': { label: 'Sealed spell scroll', image: art.scroll },
    '5': { label: 'Prismatic potion', image: art.potion },
    '6': { label: 'Oracle crystal ball', image: art.crystal },
    '7': { label: 'Archivist owl', image: art.owl },
    ACE: { label: 'Grand grimoire wild', image: art.wild },
    FREE: { label: 'Secret library free game', image: art.free },
    POWER: { label: 'Celestial spellburst power', image: art.power },
    BOLT: { label: 'Living lightning rune', image: art.energy },
    BANANA: { label: 'Triad of magic wands', image: art.lineBonus },
    PAW: { label: 'Enchanted book satchel', image: art.collector },
    SEAL_SYNC: { label: 'Ruby echo rune', image: art.sync },
    SEAL_ROWS: { label: 'Sapphire moon rune', image: art.rows },
    SEAL_PAW: { label: 'Amber oracle rune', image: art.paw },
    SEAL_RAND: { label: 'Emerald fortune rune', image: art.rand },
  },
  valueToken: { label: 'mana shard', image: art.value },
  energyEarnLabel: '+1 aether charge',
  collectorFirstValue: 'satchel gathers mana shards',
  collectorSecondValue: 'double enchantment',
  collectionAwardLabels: {
    SEAL_SYNC: '10 echo-chamber spins',
    SEAL_ROWS: '10 moonlit-stack spins',
    SEAL_PAW: '10 oracle-sight spins',
    SEAL_RAND: '10 fortune-script spins',
  },
})
