import { CLASSIC_SLOT_SYMBOL_SET_ID } from '../../../features/slots/config/symbolSets'
import { createThemedSymbolSet } from '../shared/themedSymbolSet'
import { ARCANE_ARCHIVES_VISUALS as art } from './visuals'

export const ARCANE_ARCHIVES_SYMBOLS = createThemedSymbolSet({
  id: 'arcane-archives-symbols-v1',
  serverSymbolSetId: CLASSIC_SLOT_SYMBOL_SET_ID,
  symbols: {
    '2': { label: 'Whispering candle', image: art.candle },
    '3': { label: 'Moon-feather quill', image: art.quill },
    '4': { label: 'Sealed spell scroll', image: art.scroll },
    '5': { label: 'Prismatic potion', image: art.potion },
    '6': { label: 'Oracle crystal ball', image: art.crystal },
    '7': { label: 'Open-book constellation rune', image: art.bookRune, wagerMultiplier: 0.5 },
    ACE: { label: 'Grand grimoire wild', image: art.wild },
    FREE: { label: 'Secret library free game', image: art.free },
    POWER: { label: 'Aether constellation', image: art.power, wagerMultiplier: 1 },
    BOLT: { label: 'Living lightning rune', image: art.energy },
    BANANA: { label: 'Enchanted wand', image: art.lineBonus },
    PAW: { label: 'Enchanted book satchel', image: art.collector },
    SEAL_SYNC: { label: 'Ruby echo pendant', image: art.sync },
    SEAL_ROWS: { label: 'Sapphire moon pendant', image: art.rows },
    SEAL_PAW: { label: 'Amber oracle eye', image: art.paw },
    SEAL_RAND: { label: 'Fortune constellation rune', image: art.rand, wagerMultiplier: 2 },
  },
  valueToken: { label: 'mana shard', image: art.value },
  energyEarnLabel: 'constellation cash',
  collectorFirstValue: 'satchel gathers mana shards',
  collectorSecondValue: 'double enchantment',
  collectionAwardLabels: {
    SEAL_SYNC: '10 echo-chamber spins',
    SEAL_ROWS: '10 moonlit-stack spins',
    SEAL_PAW: '10 oracle-sight spins',
    SEAL_RAND: '10 fortune-script spins',
  },
})
