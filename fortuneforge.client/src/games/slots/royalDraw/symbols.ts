import aceSpadesCrest from '../../../assets/slots/games/royal-draw/ace-spades-crest.svg'
import blackChip from '../../../assets/slots/games/royal-draw/black-chip.svg'
import blueChip from '../../../assets/slots/games/royal-draw/blue-chip.svg'
import cardVault from '../../../assets/slots/games/royal-draw/card-vault.svg'
import chipTray from '../../../assets/slots/games/royal-draw/chip-tray.svg'
import clubMedallion from '../../../assets/slots/games/royal-draw/club-medallion.svg'
import diamondMedallion from '../../../assets/slots/games/royal-draw/diamond-medallion.svg'
import electricCardShoe from '../../../assets/slots/games/royal-draw/electric-card-shoe.svg'
import goldenAceCrown from '../../../assets/slots/games/royal-draw/golden-ace-crown.svg'
import goldChip from '../../../assets/slots/games/royal-draw/gold-chip.svg'
import greenChip from '../../../assets/slots/games/royal-draw/green-chip.svg'
import heartMedallion from '../../../assets/slots/games/royal-draw/heart-medallion.svg'
import jackpotChipToken from '../../../assets/slots/games/royal-draw/jackpot-chip-token.svg'
import redChip from '../../../assets/slots/games/royal-draw/red-chip.svg'
import royalFlush from '../../../assets/slots/games/royal-draw/royal-flush.svg'
import spadeMedallion from '../../../assets/slots/games/royal-draw/spade-medallion.svg'
import tripleCardStack from '../../../assets/slots/games/royal-draw/triple-card-stack.svg'
import { createThemedSymbolSet } from '../shared/themedSymbolSet'

export const ROYAL_DRAW_SYMBOLS = createThemedSymbolSet({
  id: 'royal-draw-symbols-v1',
  serverSymbolSetId: 'royal-draw-v1-symbols',
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
    SEAL_SYNC: '10 heart spins',
    SEAL_ROWS: '10 diamond spins',
    SEAL_PAW: '10 club spins',
    SEAL_RAND: '10 spade spins',
  },
})
