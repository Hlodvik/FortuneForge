import type { SlotCabinetTheme, SlotHelpDefinition } from '@fortuneforge/slot-client-core'
import { GOLD_FOUNDRY_SOUNDS } from './audio.js'
import { FOUNDRY_INGOT_IDS, GOLD_FOUNDRY_SYMBOLS } from './symbols.js'
import { GOLD_FOUNDRY_GAME_ID, type GoldFoundrySymbolId } from './types.js'
import { GOLD_FOUNDRY_VISUALS as art } from './visuals.js'

export const GOLD_FOUNDRY_CABINET: SlotCabinetTheme = {
  id: 'gold-foundry-cabinet-v1', chrome: 'simple', accessibleName: 'Gold Foundry slot machine',
  eyebrow: 'Fortune Forge presents', title: 'Gold Foundry', subtitle: 'Cast molten gold',
  titleArtwork: { src: art.wordmark, width: 960, height: 126 }, celebrationEffect: 'foundry-sparks',
  emblemImage: art.smithMask, accentImage: art.goldIngots, backdropImage: art.backdrop,
  visualsBackdropImage: art.reelBacking, pageBackdropImage: art.backdrop,
  palette: { shellTop: '#3b2c25', shellBottom: '#171314', panel: '#242a30', trim: '#d39142',
    trimBright: '#ffe0a3', accent: '#e5a161', glow: '#f1793e', text: '#fff1ce' },
}

export const GOLD_FOUNDRY_HELP: SlotHelpDefinition = {
  paylineCount: 14,
  paylinePatternIds: [1, 2, 3, 4, 5, 6, 16, 17, 18, 19, 20, 21, 22, 23],
  fourMatchPayoutNote: 'Four matching symbols pay their own rate. Wild substitution applies only to five-symbol matches.',
  extraSections: [
    { badge: 'CAST', title: 'Reel crucible', body: 'When a reel crucible lands, it pays for every visible ingot, even if the ingots are on different reels or already paid on a line. Each iron ingot pays 0.5× the wager, bronze 1×, copper 1.5×, and gold 2×. Two or more reel crucibles double the haul. The reel crucible itself never counts toward a payline.' },
    { badge: 'POUR', title: 'Filling crucible', body: 'Each payback ingot bundle adds one charge to the crucible above the reels. A paid spin that lands one or more bundles adds its wager once to the payback total. At 100 charge, the crucible pays 15% of that total, rounded down to the cent. Payback bundles do not pay on lines or count toward an ingot haul.' },
    { badge: 'HAMMER', title: 'Hammer award', body: 'Three smithing hammers in a row, column, or diagonal pay 3× the wager per qualifying pattern.' },
  ],
}

export const GOLD_FOUNDRY_RULES = {
  gameId: GOLD_FOUNDRY_GAME_ID,
  initialReels: [
    ['2', 'SEAL_ROWS', '6', 'BOLT'], ['5', '3', 'BANANA', 'SEAL_SYNC'],
    ['4', 'FREE', 'SEAL_PAW', '7'], ['POWER', 'SEAL_RAND', '2', 'ACE'], ['7', '6', '5', '3'],
  ] as GoldFoundrySymbolId[][],
  pointValueInCents: 25 as const,
  wagerOptions: Array.from({ length: 1_000 }, (_, index) => (index + 1) * 2),
  autoSpinDelayMs: 1_000, autoSpinSpeedMultiplier: 3, baseSpinSpeedMultiplier: 1,
  reelTravelRowCount: 8, energyMeterCapacity: 100,
}

export const GOLD_FOUNDRY_FEATURES = {
  energy: { label: 'Gold crucible', symbol: 'BOLT' as GoldFoundrySymbolId },
  moneyGrab: {
    actorName: 'The reel crucible', awardLabel: 'Ingot haul', collectorSymbol: 'PAW' as GoldFoundrySymbolId,
    valueSymbols: FOUNDRY_INGOT_IDS, presentation: 'separate-award' as const,
  },
}

export const GOLD_FOUNDRY_SLOT_GAME = {
  id: 'gold-foundry', routes: { play: '/slots/gold-foundry', demo: '/slots/gold-foundry/demo' },
  catalog: { id: 'gold-foundry', title: 'Gold Foundry', shortTitle: 'Gold Foundry',
    description: 'Match metal ingots on paylines, collect them with a reel crucible, and fill the crucible above the reels for a payout based on your paid wagers.',
    image: art.crucible, imagePresentation: 'contain' as const, slotDivBackgroundImage: art.backdrop },
  experience: { id: 'gold-foundry-experience-v1', cabinet: GOLD_FOUNDRY_CABINET,
    features: GOLD_FOUNDRY_FEATURES, help: GOLD_FOUNDRY_HELP, shellBackdrop: 'theme' as const,
    symbols: GOLD_FOUNDRY_SYMBOLS, mascot: null, sounds: GOLD_FOUNDRY_SOUNDS, rules: GOLD_FOUNDRY_RULES },
}

export { GOLD_FOUNDRY_SOUNDS } from './audio.js'
export { GOLD_FOUNDRY_GAME_ID } from './types.js'
