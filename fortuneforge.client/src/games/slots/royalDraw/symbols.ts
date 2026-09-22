import aceSpadesCrest from '../../../assets/slots/games/royal-draw/optimized/royal-draw-ace-spades-crest-v2.webp'
import blackChip from '../../../assets/slots/games/royal-draw/optimized/royal-draw-black-chip-v2.webp'
import blueChip from '../../../assets/slots/games/royal-draw/optimized/royal-draw-blue-chip-v2.webp'
import cardVault from '../../../assets/slots/games/royal-draw/optimized/royal-draw-card-vault-v2.webp'
import chipTray from '../../../assets/slots/games/royal-draw/optimized/royal-draw-chip-tray-v2.webp'
import clubMedallion from '../../../assets/slots/games/royal-draw/optimized/royal-draw-club-medallion-v2.webp'
import diamondMedallion from '../../../assets/slots/games/royal-draw/optimized/royal-draw-diamond-medallion-v2.webp'
import electricCardShoe from '../../../assets/slots/games/royal-draw/optimized/royal-draw-electric-card-shoe-v2.webp'
import goldenAceCrown from '../../../assets/slots/games/royal-draw/optimized/royal-draw-golden-ace-crown-v2.webp'
import goldChip from '../../../assets/slots/games/royal-draw/optimized/royal-draw-gold-chip-v2.webp'
import greenChip from '../../../assets/slots/games/royal-draw/optimized/royal-draw-green-chip-v2.webp'
import heartMedallion from '../../../assets/slots/games/royal-draw/optimized/royal-draw-heart-medallion-v2.webp'
import jackpotChipToken from '../../../assets/slots/games/royal-draw/optimized/royal-draw-jackpot-chip-v2.webp'
import redChip from '../../../assets/slots/games/royal-draw/optimized/royal-draw-red-chip-v2.webp'
import royalFlush from '../../../assets/slots/games/royal-draw/optimized/royal-draw-royal-flush-v2.webp'
import spadeMedallion from '../../../assets/slots/games/royal-draw/optimized/royal-draw-spade-medallion-v2.webp'
import tripleCardStack from '../../../assets/slots/games/royal-draw/optimized/royal-draw-triple-card-stack-v2.webp'
import { createThemedSymbolSet } from '../shared/themedSymbolSet'

export const ROYAL_DRAW_SYMBOLS = createThemedSymbolSet({
  id: 'royal-draw-symbols-v1',
  serverSymbolSetId: 'wukong-treasures-v3',
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
