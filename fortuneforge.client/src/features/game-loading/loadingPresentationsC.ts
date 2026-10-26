import minesImage from '../../assets/catalogue/non-slots/mines-title-v2.webp'
import solitaireImage from '../../assets/catalogue/non-slots/solitaire-title-v2.webp'
import heartsImage from '../../assets/catalogue/non-slots/hearts-title-v2.webp'
import liarsDiceImage from '../../assets/catalogue/non-slots/liars-dice-title-v3.webp'
import twentyFortyEightImage from '../../assets/catalogue/non-slots/2048-title-v1.webp'
import dropMergeImage from '../../assets/catalogue/non-slots/drop-merge-title-v1.webp'
import candyCarnivalImage from '../../assets/catalogue/slots/candy-carnival-title-v2.webp'
import phantomManorImage from '../../assets/catalogue/slots/phantom-manor-title-v2.webp'
import nordicLegendsImage from '../../assets/catalogue/slots/nordic-legends-title-v2.webp'
import desertTreasuresImage from '../../assets/catalogue/slots/desert-treasures-title-v2.webp'
import robotRevolutionImage from '../../assets/catalogue/slots/robot-revolution-title-v2.webp'
import dragonHoardImage from '../../assets/catalogue/slots/dragon-hoard-title-v2.webp'
import type { GameLoadingEntry } from './types'

/** Selected catalogue artwork only; route integration belongs to the host. */
export const LOADING_PRESENTATIONS_C: readonly GameLoadingEntry[] = [
  {
    id: 'mines', title: 'Mines', routes: ['/games/mines'],
    image: minesImage, background: '#0d1b26', accent: '#a0ed76',
  },
  {
    id: 'solitaire', title: 'Competitive Solitaire', routes: ['/cards/solitaire'],
    image: solitaireImage, background: '#072d23', accent: '#d5be7d',
  },
  {
    id: 'hearts', title: 'Hearts', routes: ['/cards/hearts'],
    image: heartsImage, background: '#071b1b', accent: '#f2929b',
  },
  {
    id: 'liars-dice', title: 'Liar’s Dice', routes: ['/games/liars-dice'],
    image: liarsDiceImage, background: '#081628', accent: '#ddc184',
  },
  {
    id: '2048', title: '2048', routes: ['/games/2048'],
    image: twentyFortyEightImage, background: '#101722', accent: '#e4bd55',
  },
  {
    id: 'drop-merge', title: 'Drop Merge', routes: ['/games/drop-merge'],
    image: dropMergeImage, background: '#071d21', accent: '#b2ec8e',
  },
  {
    id: 'candy-carnival', title: 'Candy Carnival',
    routes: ['/slots/candy-carnival', '/slots/candy-carnival/demo'],
    image: candyCarnivalImage, background: '#230c2f', accent: '#ff80bb',
  },
  {
    id: 'phantom-manor', title: 'Phantom Manor',
    routes: ['/slots/phantom-manor', '/slots/phantom-manor/demo'],
    image: phantomManorImage, background: '#101024', accent: '#a58cff',
  },
  {
    id: 'nordic-legends', title: 'Nordic Legends',
    routes: ['/slots/nordic-legends', '/slots/nordic-legends/demo'],
    image: nordicLegendsImage, background: '#0a1d2b', accent: '#80dfca',
  },
  {
    id: 'desert-treasures', title: 'Desert Treasures',
    routes: ['/slots/desert-treasures', '/slots/desert-treasures/demo'],
    image: desertTreasuresImage, background: '#25170f', accent: '#ffd078',
  },
  {
    id: 'robot-revolution', title: 'Robot Revolution',
    routes: ['/slots/robot-revolution', '/slots/robot-revolution/demo'],
    image: robotRevolutionImage, background: '#07142d', accent: '#37ddff',
  },
  {
    id: 'dragon-hoard', title: 'Dragon Hoard',
    routes: ['/slots/dragon-hoard', '/slots/dragon-hoard/demo'],
    image: dragonHoardImage, background: '#210b09', accent: '#f2ce67',
  },
]
