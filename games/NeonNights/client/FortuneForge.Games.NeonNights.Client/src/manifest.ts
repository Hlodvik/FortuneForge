import type { SlotCabinetTheme, SlotHelpDefinition, SlotSpecialRoundFeature, SlotSymbolSet } from '@fortuneforge/slot-client-core'
import { NEON_NIGHTS_SYMBOL_IMAGES as images } from './symbols.js'
import { NEON_NIGHTS_SOUNDS } from './audio.js'
import { neonDiscoRoundSpins } from './roundState.js'
import { NEON_GAME_ID } from './types.js'
import wordmark from './assets/symbols/neon-nights-wordmark-v1.webp'

const definitions = {
  '2': { id: '2', label: 'Jack', image: images['2'] },
  '3': { id: '3', label: 'Retro cassette', image: images['3'] },
  '4': { id: '4', label: 'Queen', image: images['4'] },
  '5': { id: '5', label: 'King', image: images['5'] },
  '6': { id: '6', label: 'Sports car', image: images['6'] },
  '7': { id: '7', label: 'Ace', image: images['7'] },
  ACE: { id: 'ACE', label: 'Neon Wild', image: images.ACE },
  FREE: { id: 'FREE', label: 'NEON BONUS', image: images.FREE },
  POWER: { id: 'POWER', label: 'Disco ball', image: images.POWER },
  BANANA: { id: 'BANANA', label: 'Triple neon sevens', image: images.BANANA },
  SEAL_SYNC: { id: 'SEAL_SYNC', label: 'Pink neon koi', image: images.SEAL_SYNC },
  SEAL_ROWS: { id: 'SEAL_ROWS', label: 'Blue dice', image: images.SEAL_ROWS },
  SEAL_PAW: { id: 'SEAL_PAW', label: 'Neon shades', image: images.SEAL_PAW },
} as const
const animationWeights = { '2': 232, '3': 927, '4': 232, '5': 232, '6': 2, '7': 232, ACE: 160, FREE: 16, POWER: 1, BANANA: 202, SEAL_SYNC: 512, SEAL_ROWS: 836, SEAL_PAW: 512 }
const animationPool = Object.entries(animationWeights).flatMap(([symbol, weight]) => Array.from({ length: weight }, () => symbol))
const linePayouts = [['7', 3, 11], ['SEAL_PAW', 2, 8], ['4', 1, 7], ['5', 2, 6], ['SEAL_ROWS', 1, 5], ['2', 1, 4], ['SEAL_SYNC', 1, 4], ['3', 1, 2]] as const
export const neonSymbols: SlotSymbolSet = {
  id: 'neon-nights-arcade-symbols-v3', serverSymbolSetId: 'neon-arcade-symbols-v3', definitions,
  animationSymbolIds: animationPool.map((_, index) => animationPool[(index * 811 + 331) % animationPool.length]),
  guidePayoutScale: .82, guideSymbolGroups: [['2', '4', '5', '7']],
  guideEntries: [
    { symbol: 'BANANA', compactInSidePanel: true, payoutScale: 1, firstLabel: '3 in a line', firstValue: '12×' },
    ...linePayouts.map(([symbol, three, five]) => ({ symbol, compactInSidePanel: true, firstLabel: '3', firstValue: `${three}×`, secondLabel: '5', secondValue: `${five}×` })),
    { symbol: 'ACE', compactInSidePanel: true, firstLabel: 'Wild', firstValue: 'Substitutes on paylines' },
    { symbol: 'FREE', compactInSidePanel: true, firstLabel: 'Each BONUS', firstValue: '+25% to payline wins' },
    { symbol: '6', compactInSidePanel: true, firstLabel: 'Car Plays', firstValue: '7 paid plays · locked wager' },
    { symbol: 'POWER', compactInSidePanel: true, firstLabel: 'Disco Plays', firstValue: `${neonDiscoRoundSpins} paid cascade spins` },
  ],
}
export const neonCabinet: SlotCabinetTheme = {
  id: 'neon-nights-cabinet-v1', chrome: 'simple', accessibleName: 'Neon Nights themed slot machine',
  eyebrow: 'Fortune Forge presents', title: 'Neon Nights', subtitle: 'Light Up the Midnight Jackpot',
  titleArtwork: { src: wordmark, width: 720, height: 415 }, celebrationEffect: 'neon-scan',
  emblemImage: images.CABINET_EMBLEM, accentImage: images.POWER,
  backdropImage: images.BACKDROP, visualsBackdropImage: images.BACKDROP, pageBackdropImage: images.BACKDROP,
  palette: { shellTop: '#cf2cff', shellBottom: '#16072e', panel: '#050819', trim: '#ffd34d', trimBright: '#ff4fd8', accent: '#22e7ff', glow: '#ff4fd8', text: '#fff4ff' },
}
export const neonSpecialRound: SlotSpecialRoundFeature = {
  id: 'neon-midnight-mix', title: 'Special Plays', earnStyle: 'orbit',
  earnLabel: `A sports car starts 7 paid Car Plays. A disco ball starts ${neonDiscoRoundSpins} paid cascade spins.`,
  earnHint: 'Each Car Play costs the locked triggering wager and uses three extra rows. Each Disco spin costs one chosen wager; groups of four or more touching symbols pay and clear, with cascades building up to ×5.',
  activeModes: { 'car-rows': 'Car Plays · three extra rows', 'arcade-rows': 'Car Plays · three extra rows' },
}
export const neonHelp: SlotHelpDefinition = {
  paylineCount: 23, paylinePatternIds: Array.from({ length: 23 }, (_, index) => index + 1),
  extraSections: [
    { badge: '+25%', title: 'BONUS boosts', body: 'Each BONUS sign anywhere on the board adds 25% to the spin’s payline wins. Two signs add 50%, three add 75%, and so on. The boost applies to paying lines only; it does not multiply triple-sevens pattern prizes or award free spins.' },
    { badge: '777', title: 'Triple sevens', body: 'Three triple-sevens symbols in a row, column or diagonal pay 12× the wager per qualifying pattern.' },
    { badge: '7', title: 'Car Plays', body: 'A sports car starts 7 paid Car Plays at the triggering wager, with three extra rows and one new horizontal payline per row. The wager stays locked for the round. Landing a sports car during Car Plays adds 7 more plays.' },
    { badge: String(neonDiscoRoundSpins), title: 'Disco Plays', body: `A disco ball starts ${neonDiscoRoundSpins} paid Disco spins using seven exclusive symbols. Match four or more identical symbols touching horizontally or vertically. Each winning board raises the multiplier from ×1 through ×2, ×3 and ×5. The multiplier carries into the next spin and drops one tier on a spin with no wins.` },
  ],
}
export const neonRules = {
  gameId: NEON_GAME_ID, pointValueInCents: 1, wagerOptions: Array.from({ length: 1_000 }, (_, index) => (index + 1) * 50),
  initialReels: [['SEAL_SYNC', '3', '4', 'SEAL_ROWS'], ['3', 'SEAL_PAW', '5', '2'], ['SEAL_ROWS', '4', 'SEAL_SYNC', '7'], ['5', 'SEAL_SYNC', '7', 'ACE'], ['SEAL_PAW', '7', '3', 'BANANA']],
  autoSpinDelayMs: 1000, autoSpinSpeedMultiplier: 3, baseSpinSpeedMultiplier: 1, reelTravelRowCount: 8, specialBoostCost: 0, energyMeterCapacity: 100,
}
export const NEON_NIGHTS_SLOT_GAME = {
  id: 'neon-nights', routes: { play: '/slots/neon-nights', demo: '/slots/neon-nights/demo' },
  catalog: { id: 'neon-nights', title: 'Neon Nights', shortTitle: 'Neon Nights', description: 'Race through an electric city of arcade lights, synth beats, and glowing midnight prizes.', image: images['7'], imagePresentation: 'contain' as const, slotDivBackgroundImage: images.BACKDROP },
  experience: {
    id: 'neon-nights-arcade-experience-v3', cabinet: neonCabinet, features: { specialRound: neonSpecialRound }, help: neonHelp,
    shellBackdrop: 'theme' as const, symbols: neonSymbols, mascot: null, sounds: NEON_NIGHTS_SOUNDS, rules: neonRules,
  },
}
