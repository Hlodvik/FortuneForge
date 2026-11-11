import type { SlotCabinetTheme, SlotHelpDefinition, SlotSpecialRoundFeature, SlotSymbolSet } from '@fortuneforge/slot-client-core'
import { MIDNIGHT_VAULT_SOUNDS } from './audio.js'
import { MIDNIGHT_VAULT_LINE_GUIDE, midnightVaultMultiplier } from './math.js'
import { MIDNIGHT_BACKDROP_IMAGE_SET, MIDNIGHT_IMAGES as images, MIDNIGHT_RESPONSIVE_IMAGES as responsive } from './symbols.js'
import { MIDNIGHT_GAME_ID, type MidnightCollectionId, type MidnightSymbolId } from './types.js'

const definition = (id: MidnightSymbolId, label: string) => ({
  id, label, image: images[id], responsiveImages: responsive[id], animatedImage: images[id],
})

const definitions = {
  '2': definition('2', 'Queen'), '3': definition('3', 'Jack'), '4': definition('4', 'King'),
  '5': definition('5', 'Ace'), '6': definition('6', 'Casino chips'), '7': definition('7', 'Ruby ring'),
  '8': definition('8', 'Cash briefcase'), ACE: definition('ACE', 'Vault wild'),
  FREE: definition('FREE', 'Fabergé bonus'), POWER: definition('POWER', 'Diamond'),
  BANANA: definition('BANANA', 'Triple sevens'), SEAL_SYNC: definition('SEAL_SYNC', 'Ruby access gem'),
  SEAL_ROWS: definition('SEAL_ROWS', 'Sapphire access gem'), SEAL_PAW: definition('SEAL_PAW', 'Amber access gem'),
  SEAL_RAND: definition('SEAL_RAND', 'Emerald access gem'),
} as const

export const midnightSymbols: SlotSymbolSet<MidnightSymbolId> = {
  id: 'dragon-hoard-symbols-v1',
  serverSymbolSetId: 'midnight-vault-symbols-v1',
  definitions,
  animationSymbolIds: [
    '3', '2', '4', '5', '6', '7', '8', 'ACE', 'POWER', 'FREE', 'BANANA',
    'SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW', 'SEAL_RAND',
  ],
  guideEntries: [
    ...MIDNIGHT_VAULT_LINE_GUIDE,
    { symbol: 'FREE', firstLabel: '3+', firstValue: 'anywhere', secondLabel: 'Award', secondValue: '10-spin Vault Breach round' },
    { symbol: 'BANANA', firstLabel: '3', firstValue: 'row/column/diagonal', secondLabel: 'Pays', secondValue: midnightVaultMultiplier(3) },
    ...(['SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW', 'SEAL_RAND'] as const).map(symbol => ({
      symbol, firstLabel: 'Collect', firstValue: '30 matching gems', secondLabel: 'Award', secondValue: '10-spin enhanced Vault Breach round',
    })),
  ],
  guideNote: 'Whole-number multipliers apply exactly to the wager. Ordinary four-symbol runs pay the three-symbol rate. Vault wilds pay naturally on three or five symbols and substitute only on five-symbol paylines.',
}

export const midnightCabinet: SlotCabinetTheme = {
  id: 'dragon-hoard-cabinet-v1', chrome: 'simple', accessibleName: 'Midnight Vault themed slot machine',
  eyebrow: 'Fortune Forge presents', title: 'Midnight Vault', subtitle: 'Unlock the Midnight Vault',
  celebrationEffect: 'vault-jewel-glint', emblemImage: images.CABINET_EMBLEM,
  accentImage: images.POWER, backdropImage: images.BACKDROP, visualsBackdropImage: images.BACKDROP,
  pageBackdropImage: images.BACKDROP, backdropImageSet: MIDNIGHT_BACKDROP_IMAGE_SET,
  palette: { shellTop: '#173149', shellBottom: '#071321', panel: '#06101b', trim: '#dec18b',
    trimBright: '#e8d4ac', accent: '#c8a66b', glow: '#e8d4ac', text: '#f7ecd5' },
}

export const midnightSpecialRound: SlotSpecialRoundFeature = {
  id: 'dragon-ember-siege', title: 'Vault Breach', earnStyle: 'buckets',
  earnLabel: 'Land 3 Fabergé bonuses for a 10-spin Vault Breach round, or collect 30 matching gems for an enhanced 10-spin round.',
  earnHint: 'Each color fills its own access track. Complete any 30-gem track for six rows and extra Vault wilds.',
  activeModes: { 'rows-paw': 'Enhanced Vault Breach · six rows with extra Vault wilds' },
}

export const midnightHelp: SlotHelpDefinition = {
  paylineCount: 23, paylinePatternIds: Array.from({ length: 23 }, (_, index) => index + 1),
  freeGames: { requiredSymbols: 3, awardedSpins: 10, title: 'Vault Breach', symbolLabel: 'Fabergé bonus', awardLabel: 'Vault Breach spins' },
  fiveMatchPayoutNote: 'All 23 full payline patterns use the same symbol multipliers.',
  fourMatchPayoutNote: 'Ordinary four-symbol runs at either edge of a payline pay the three-symbol rate. Four Vault wilds, or four-symbol runs using wild substitution, do not pay.',
  extraSections: [
    { badge: 'GEMS', title: 'Enhanced Vault Breach', body: 'Collect 30 matching gems on any one access track to unlock a 10-spin enhanced Vault Breach round. The growing piles show broad stages of progress. Every gem color awards the same feature: six rows and extra Vault wilds. Each round locks the collected gems’ weighted average wager, rounded to the nearest R0.25. Further rounds are queued with their own locked wagers. New gems are banked toward the next round.' },
    { badge: '777', title: 'Triple sevens', body: `Three 777 symbols in a straight row, column, or diagonal award exactly ${midnightVaultMultiplier(3)} the wager per qualifying pattern. These awards are included in the displayed win.` },
    { badge: 'GEM', title: 'Diamond special points', body: 'Natural three-symbol Diamond wins also award 1 special point; natural five-symbol wins award 2. Four-symbol wins award no special points. These special points are separate from the win amount.' },
    { badge: 'PAY', title: 'Reading the paytable', body: 'Symbol values show whole-number multipliers. Each paying match pays an exact multiple of the wager, including the smallest available bet. The displayed win includes all paid lines and Triple sevens awards.' },
  ],
}

export const midnightRules = {
  gameId: MIDNIGHT_GAME_ID,
  initialReels: [
    ['3', '6', 'SEAL_SYNC', '4'], ['7', '2', 'ACE', 'SEAL_ROWS'],
    ['FREE', '5', '8', 'POWER'], ['SEAL_PAW', 'BANANA', '4', '6'],
    ['2', '7', 'SEAL_RAND', '3'],
  ] as MidnightSymbolId[][],
  pointValueInCents: 25,
  wagerOptions: Array.from({ length: 1_000 }, (_, index) => (index + 1) * 2),
  autoSpinDelayMs: 1_000, autoSpinSpeedMultiplier: 3, baseSpinSpeedMultiplier: 1,
  reelTravelRowCount: 8, specialSpinDelayMs: 2_400,
}

export const midnightCollections = [
  { id: 'sync' as MidnightCollectionId, symbol: 'SEAL_SYNC' as MidnightSymbolId, label: 'Ruby access gem', shortLabel: 'Ruby' },
  { id: 'rows' as MidnightCollectionId, symbol: 'SEAL_ROWS' as MidnightSymbolId, label: 'Sapphire access gem', shortLabel: 'Sapphire' },
  { id: 'paw' as MidnightCollectionId, symbol: 'SEAL_PAW' as MidnightSymbolId, label: 'Amber access gem', shortLabel: 'Amber' },
  { id: 'rand' as MidnightCollectionId, symbol: 'SEAL_RAND' as MidnightSymbolId, label: 'Emerald access gem', shortLabel: 'Emerald' },
] as const

export const MIDNIGHT_VAULT_SLOT_GAME = {
  id: 'dragon-hoard', routes: { play: '/slots/dragon-hoard', demo: '/slots/dragon-hoard/demo' },
  catalog: { id: 'dragon-hoard', title: 'Midnight Vault', shortTitle: 'Midnight Vault',
    description: 'An Art Deco heist with fifteen reel symbols, brilliant jewels and the Vault Breach bonus.',
    image: images['7'], imagePresentation: 'contain' as const, slotDivBackgroundImage: images.BACKDROP },
  experience: {
    id: 'dragon-hoard-experience-v1', cabinet: midnightCabinet,
    features: {
      collections: { ariaLabel: 'Vault jewel collections', itemLabel: 'vault jewels', presentation: 'vault-access',
        entries: midnightCollections.map(entry => ({ ...entry, requiredCount: 30,
          rewardDescription: 'Collect 30 to unlock 10 enhanced bonus rounds with two extra rows and extra wilds.',
          progressDescription: 'Land matching vault jewels anywhere on the reels.' })) },
      specialRound: midnightSpecialRound,
    },
    help: midnightHelp, shellBackdrop: 'theme' as const, symbols: midnightSymbols, mascot: null,
    sounds: MIDNIGHT_VAULT_SOUNDS, rules: midnightRules,
  },
}
