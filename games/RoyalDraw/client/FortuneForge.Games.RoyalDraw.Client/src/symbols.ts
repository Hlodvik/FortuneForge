import { CLASSIC_SLOT_SYMBOL_SET_ID } from '@fortuneforge/slot-game-runtime/symbols'
import aceSpadesCrest from './assets/art/optimized/royal-draw-ace-spades-crest-v2.webp'
import blackChip from './assets/art/optimized/royal-draw-black-chip-v2.webp'
import blueChip from './assets/art/optimized/royal-draw-blue-chip-v2.webp'
import cardVault from './assets/art/optimized/royal-draw-card-vault-v2.webp'
import chipTray from './assets/art/optimized/royal-draw-chip-tray-v2.webp'
import clubMedallion from './assets/art/optimized/royal-draw-club-medallion-v2.webp'
import diamondMedallion from './assets/art/optimized/royal-draw-diamond-medallion-v2.webp'
import electricCardShoe from './assets/art/optimized/royal-draw-electric-card-shoe-v2.webp'
import goldenAceCrown from './assets/art/optimized/royal-draw-golden-ace-crown-v2.webp'
import goldChip from './assets/art/optimized/royal-draw-gold-chip-v2.webp'
import greenChip from './assets/art/optimized/royal-draw-green-chip-v2.webp'
import heartMedallion from './assets/art/optimized/royal-draw-heart-medallion-v2.webp'
import jackpotChipToken from './assets/art/optimized/royal-draw-jackpot-chip-v2.webp'
import redChip from './assets/art/optimized/royal-draw-red-chip-v2.webp'
import royalFlush from './assets/art/optimized/royal-draw-royal-flush-v2.webp'
import spadeMedallion from './assets/art/optimized/royal-draw-spade-medallion-v2.webp'
import tripleCardStack from './assets/art/optimized/royal-draw-triple-card-stack-v2.webp'
import { createThemedSymbolSet } from '@fortuneforge/slot-game-runtime/symbols'

export const ROYAL_DRAW_SYMBOLS = createThemedSymbolSet({
  id: 'royal-draw-symbols-v1',
  serverSymbolSetId: CLASSIC_SLOT_SYMBOL_SET_ID,
  symbols: {
    '2': { label: 'Blue poker chip', image: blueChip },
    '3': { label: 'Red poker chip', image: redChip },
    '4': { label: 'Green poker chip', image: greenChip },
    '5': { label: 'Black poker chip', image: blackChip },
    '6': { label: 'Gold poker chip', image: goldChip },
    '7': { label: 'Royal flush', image: royalFlush },
    ACE: { label: 'Ace of spades wild crest', image: aceSpadesCrest },
    FREE: { label: 'Card vault free game', image: cardVault },
    POWER: { label: 'Golden ace crown power', image: goldenAceCrown },
    BOLT: { label: 'Electric card shoe', image: electricCardShoe },
    BANANA: { label: 'Triple card stack', image: tripleCardStack },
    PAW: { label: 'Dealer chip-tray sweep', image: chipTray },
    SEAL_SYNC: { label: 'Heart medallion', image: heartMedallion },
    SEAL_ROWS: { label: 'Diamond medallion', image: diamondMedallion },
    SEAL_PAW: { label: 'Club medallion', image: clubMedallion },
    SEAL_RAND: { label: 'Spade medallion', image: spadeMedallion },
  },
  valueToken: { label: 'jackpot chip', image: jackpotChipToken },
  energyEarnLabel: '+1 table heat',
  collectorFirstValue: 'sweeps jackpot chips',
  collectorSecondValue: 'double pot',
  collectionAwardLabels: {
    SEAL_SYNC: '7 heart spins',
    SEAL_ROWS: '7 diamond spins',
    SEAL_PAW: '7 club spins',
    SEAL_RAND: '7 spade spins',
  },
})
