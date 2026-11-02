import type { ShowcaseSlotGameDefinition } from '../shared/createShowcaseSlotGame'
import { DRAGON_HOARD_SOUNDS } from '../../../features/slots/config/soundSets'
import { DRAGON_HOARD_RESPONSIVE_IMAGES, DRAGON_HOARD_SYMBOL_IMAGES as symbols } from './symbols'
import { MIDNIGHT_VAULT_LINE_GUIDE, midnightVaultMultiplier } from './math'

export const DRAGON_HOARD_DEFINITION: ShowcaseSlotGameDefinition = {
  id: 'dragon-hoard', title: 'Midnight Vault', subtitle: 'Unlock the Midnight Vault',
  description: 'An Art Deco heist with fifteen reel symbols, brilliant jewels and the Vault Breach bonus.',
  serverGameId: 'dragon-hoard-v1',
  serverSymbolSetId: 'midnight-vault-symbols-v1',
  additionalSymbolSpecs: { '8': ['Cash briefcase', '💼', symbols['8']] },
  initialReels: [
    ['3', '6', 'SEAL_SYNC', '4'], ['7', '2', 'ACE', 'SEAL_ROWS'],
    ['FREE', '5', '8', 'POWER'], ['SEAL_PAW', 'BANANA', '4', '6'],
    ['2', '7', 'SEAL_RAND', '3'],
  ],
  paylinePatternIds: Array.from({ length: 23 }, (_, index) => index + 1),
  presentation: 'vault-access', collectionAriaLabel: 'Vault jewel collections', itemLabel: 'vault jewels',
  energyLabel: 'Vault security', actorName: 'The loot case', awardLabel: 'Vault haul',
  outcomeNarrative: {
    lossTitle: 'Vault stays locked this spin', winTitle: 'Jewels secured', greatWinTitle: 'Vault reward', bigWinTitle: 'Midnight Vault grand haul',
    freeGameSingular: 'Vault Breach spin', freeGamePlural: 'Vault Breach spins',
    lossNextAction: 'Choose a wager, then approach the midnight vault again.',
    winNextAction: 'Choose a wager, then seek another jewel haul.',
    bonusNextAction: 'Press Spin to enter Vault Breach at the locked wager.',
  },
  celebrationEffect: 'vault-jewel-glint',
  responsiveImages: DRAGON_HOARD_RESPONSIVE_IMAGES,
  artwork: { emblem: symbols.CABINET_EMBLEM, accent: symbols.POWER, backdrop: symbols.BACKDROP,
    backdropImageSet: `image-set(url("${symbols.BACKDROP}") 1x, url("${symbols.BACKDROP.replace("1440x810", "1672x941")}") ${1672 / 1440}x)`,
  },
  valueToken: ['vault value token', '🪙', symbols.VALUE], motif: '💎', accentGlyph: '🗝️',
  collectionLabels: [['Ruby access gem', '🔴', symbols.SEAL_SYNC], ['Sapphire access gem', '🔷', symbols.SEAL_ROWS], ['Amber access gem', '🔶', symbols.SEAL_PAW], ['Emerald access gem', '🟢', symbols.SEAL_RAND]],
  symbolSpecs: {
    '2': ['Queen', 'Q', symbols['2']], '3': ['Jack', 'J', symbols['3']], '4': ['King', 'K', symbols['4']],
    '5': ['Ace', 'A', symbols['5']], '6': ['Casino chips', '♣', symbols['6']], '7': ['Ruby ring', '💍', symbols['7']],
    ACE: ['Vault wild', '🏦', symbols.ACE], FREE: ['Fabergé bonus', '🥚', symbols.FREE], POWER: ['Diamond', '💎', symbols.POWER],
    BOLT: ['Vault door', '🏦', symbols.BOLT], BANANA: ['Triple sevens', '777', symbols.BANANA], PAW: ['Cash briefcase', '💼', symbols.PAW],
  },
  symbolGuide: {
    note: 'Whole-number multipliers apply exactly to the wager. Ordinary four-symbol runs pay the three-symbol rate. Vault wilds pay naturally on three or five symbols and substitute only on five-symbol paylines.',
    entries: [
      ...MIDNIGHT_VAULT_LINE_GUIDE,
      { symbol: 'FREE', firstLabel: '3+', firstValue: 'anywhere', secondLabel: 'Award', secondValue: '10-spin Vault Breach round' },
      { symbol: 'BANANA', firstLabel: '3', firstValue: 'row/column/diagonal', secondLabel: 'Pays', secondValue: midnightVaultMultiplier(3) },
      ...(['SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW', 'SEAL_RAND'] as const).map(symbol => ({
        symbol, firstLabel: 'Collect', firstValue: '30 matching gems',
        secondLabel: 'Award', secondValue: '10-spin enhanced Vault Breach round',
      })),
    ],
  },
  fiveMatchPayoutNote: 'All 23 full payline patterns use the same symbol multipliers.',
  fourMatchPayoutNote: 'Ordinary four-symbol runs at either edge of a payline pay the three-symbol rate. Four Vault wilds, or four-symbol runs using wild substitution, do not pay.',
  helpSections: [
    { badge: 'GEMS', title: 'Enhanced Vault Breach',
      body: 'Collect 30 matching gems on any one access track to unlock a 10-spin enhanced Vault Breach round. The growing piles show broad stages of progress. Every gem color awards the same feature: six rows and extra Vault wilds. Each round locks the collected gems’ weighted average wager, rounded to the nearest R0.25. Further rounds are queued with their own locked wagers. New gems are banked toward the next round.' },
    { badge: '777', title: 'Triple sevens',
      body: `Three 777 symbols in a straight row, column, or diagonal award exactly ${midnightVaultMultiplier(3)} the wager per qualifying pattern. These awards are included in the displayed win.` },
    { badge: 'GEM', title: 'Diamond special points',
      body: 'Natural three-symbol Diamond wins also award 1 special point; natural five-symbol wins award 2. Four-symbol wins award no special points. These special points are separate from the win amount.' },
    { badge: 'PAY', title: 'Reading the paytable',
      body: 'Symbol values show whole-number multipliers. Each paying match pays an exact multiple of the wager, including the smallest available bet. The displayed win includes all paid lines and Triple sevens awards.' },
  ],
  specialRound: {
    collectionTarget: 30, collectionAwardedSpins: 10,
    freeGames: { requiredSymbols: 3, awardedSpins: 10, title: 'Vault Breach',
      symbolLabel: 'Fabergé bonus', awardLabel: 'Vault Breach spins' }, sounds: DRAGON_HOARD_SOUNDS,
    usesEnergy: false, usesDirectValueTokens: false,
    collectionRewardDescription: 'Collect 30 matching gems to unlock a 10-spin enhanced Vault Breach round with six rows and extra Vault wilds.',
    feature: {
      id: 'dragon-ember-siege', title: 'Vault Breach',
      earnLabel: 'Land 3 Fabergé bonuses for a 10-spin Vault Breach round, or collect 30 matching gems for an enhanced 10-spin round.',
      earnStyle: 'buckets', earnHint: 'Each color fills its own access track. Complete any 30-gem track for six rows and extra Vault wilds.',
    activeModes: {
      'rows-paw': 'Enhanced Vault Breach · six rows with extra Vault wilds',
    },
    },
  },
  colors: { skyTop: '#071422', skyBottom: '#10273b', horizon: '#122638', ground: '#06101b', primary: '#173149', secondary: '#c8a66b', deep: '#071321', rim: '#dec18b', glow: '#e8d4ac', text: '#f7ecd5' },
}
