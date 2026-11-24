import { CLASSIC_SLOT_SYMBOL_SET_ID, createThemedSymbolSet } from '@fortuneforge/slot-game-runtime/symbols'
import { formatCalibratedMultiplier, getSlotEngineGuideContract } from '@fortuneforge/slot-game-runtime/engine-guide'
import { COSMIC_FORTUNE_VISUALS as art } from './visuals'
import { PHARAOH_MATH, PHARAOH_PAYLINE_GUIDE } from './math'

const contract = getSlotEngineGuideContract('cosmic-fortune-v1')
const themedSymbols = createThemedSymbolSet({
  id: 'pharaohs-ascent-symbols-v1',
  serverSymbolSetId: CLASSIC_SLOT_SYMBOL_SET_ID,
  symbols: {
    '2': { label: 'Lapis ankh', image: art.ankh },
    '3': { label: 'Eye of Horus', image: art.eye },
    '4': { label: 'Winged scarab', image: art.scarab },
    '5': { label: 'Ruby lotus', image: art.lotus },
    '6': { label: 'Golden falcon', image: art.falcon },
    '7': { label: 'Pharaoh’s mask', image: art.pharaoh },
    ACE: { label: 'Pharaoh wild', image: art.wild },
    FREE: { label: 'Pyramid gate scatter', image: art.gate },
    POWER: { label: 'Cobra sun disk', image: art.sun },
    BOLT: { label: 'Sunfire crystal', image: art.sunfire },
    BANANA: { label: 'Trail scarab', image: art.trail },
    PAW: { label: 'Sealed chest', image: art.chest },
    SEAL_SYNC: { label: 'Horus guardian seal', image: art.horus },
    SEAL_ROWS: { label: 'Thoth guardian seal', image: art.thoth },
    SEAL_PAW: { label: 'Anubis guardian seal', image: art.anubis },
    SEAL_RAND: { label: 'Wadjet guardian seal', image: art.wadjet },
  },
  valueToken: { label: 'gold coin', image: art.coin },
  energyEarnLabel: '+1 Sunfire',
  collectorFirstValue: 'chest gathers coins',
  collectorSecondValue: 'double chest reward',
  freeRequiredLabel: `${contract.scatterRequiredSymbols}+`,
  freeAwardLabel: `${contract.scatterAwardedSpins} Tomb Spins`,
  collectionRequiredCount: contract.collectionTarget,
  collectionAwardLabels: {
    SEAL_SYNC: '7 Horus Tomb Spins', SEAL_ROWS: '7 Thoth Tomb Spins',
    SEAL_PAW: '7 Anubis Tomb Spins', SEAL_RAND: '7 Wadjet Tomb Spins',
  },
})

export const COSMIC_FORTUNE_SYMBOLS = {
  ...themedSymbols,
  definitions: Object.fromEntries(Object.entries(themedSymbols.definitions)
    .filter(([id]) => id !== 'PAW' && !id.startsWith('RAND_'))),
  guideEntries: [
    ...PHARAOH_PAYLINE_GUIDE,
    ...themedSymbols.guideEntries.filter(entry => entry.symbol === 'FREE' || entry.symbol === 'BOLT'),
    {
      symbol: 'BANANA', firstLabel: '3 / 4 / 5+', firstValue: 'connected', secondLabel: 'Pays',
      secondValue: [3, 5, 8].map(multiplier => formatCalibratedMultiplier(multiplier, PHARAOH_MATH.payoutMultiplier)).join(' / '),
    },
    ...themedSymbols.guideEntries.filter(entry => entry.symbol.startsWith('SEAL_')),
  ],
  guideNote: 'Payline multipliers shown apply to the wager. Each paying match is credited in whole points. Ordinary four-symbol edge runs pay the three-symbol rate. The pharaoh wild pays naturally on three or five symbols and substitutes only on five-symbol paylines. Three natural cobra sun disks award 1 special point, and five award 2; four award none. Eight special points activate a boost that reduces common symbols on the next spin. Red scarabs pay only for edge-connected groups; the blue winged scarab is an ordinary payline symbol. Saved funded wagers stay locked.',
}
