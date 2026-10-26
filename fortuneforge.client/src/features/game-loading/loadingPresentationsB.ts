import dragonTiger from '../../assets/catalogue/non-slots/dragon-tiger-title-v1.webp'
import andarBahar from '../../assets/catalogue/non-slots/andar-bahar-title-v1.webp'
import hiLo from '../../assets/catalogue/non-slots/hi-lo-title-v1.webp'
import casinoWar from '../../assets/catalogue/non-slots/casino-war-title-v1.webp'
import videoPoker from '../../assets/catalogue/non-slots/video-poker-title-v1.webp'
import keno from '../../assets/catalogue/non-slots/keno-title-v1.webp'
import sicBo from '../../assets/catalogue/non-slots/sic-bo-title-v1.webp'
import arcaneArchives from '../../assets/catalogue/slots/arcane-archives-title-v1.webp'
import cosmicFortune from '../../assets/catalogue/slots/cosmic-fortune-title-v1.webp'
import dinoDominion from '../../assets/catalogue/slots/dino-dominion-title-v1.webp'
import neonNights from '../../assets/catalogue/slots/neon-nights-title-v1.webp'
import jungleJackpot from '../../assets/catalogue/slots/jungle-jackpot-title-v1.webp'
import oceanOdyssey from '../../assets/catalogue/slots/ocean-odyssey-title-v1.webp'
import samuraiFortune from '../../assets/catalogue/slots/samurai-fortune-title-v1.webp'
import type { GameLoadingEntry } from './types'

/** Standalone presentation data for parent review; existing routes remain unchanged. */
export const LOADING_PRESENTATIONS_B: readonly GameLoadingEntry[] = [
  {
    id: 'dragon-tiger', title: 'Dragon Tiger', routes: ['/games/dragon-tiger'],
    image: dragonTiger, background: '#1b0709', accent: '#e4bc6a',
  },
  {
    id: 'andar-bahar', title: 'Andar Bahar', routes: ['/games/andar-bahar'],
    image: andarBahar, background: '#031910', accent: '#d4b16a',
  },
  {
    id: 'hi-lo', title: 'Hi-Lo', routes: ['/games/hi-lo'],
    image: hiLo, background: '#03170d', accent: '#dfc68d',
  },
  {
    id: 'casino-war', title: 'Casino War', routes: ['/games/casino-war'],
    image: casinoWar, background: '#051a14', accent: '#e9bd57',
  },
  {
    id: 'video-poker', title: 'Video Poker', routes: ['/games/video-poker'],
    image: videoPoker, background: '#020c25', accent: '#ffd96f',
  },
  {
    id: 'keno', title: 'Keno', routes: ['/games/keno'],
    image: keno, background: '#080d1d', accent: '#58e4da',
  },
  {
    id: 'sic-bo', title: 'Sic Bo', routes: ['/games/sic-bo'],
    image: sicBo, background: '#26070e', accent: '#ffd77a',
  },
  {
    id: 'arcane-archives', title: 'Arcane Archives',
    routes: ['/slots/arcane-archives', '/slots/arcane-archives/demo'],
    image: arcaneArchives, background: '#120a28', accent: '#c084fc',
  },
  {
    id: 'cosmic-fortune', title: 'Cosmic Fortune',
    routes: ['/slots/cosmic-fortune', '/slots/cosmic-fortune/demo'],
    image: cosmicFortune, background: '#05081e', accent: '#54d9ff',
  },
  {
    id: 'dino-dominion', title: 'Dino Dominion',
    routes: ['/slots/dino-dominion', '/slots/dino-dominion/demo'],
    image: dinoDominion, background: '#141d10', accent: '#e0ad4c',
  },
  {
    id: 'neon-nights', title: 'Neon Nights',
    routes: ['/slots/neon-nights', '/slots/neon-nights/demo'],
    image: neonNights, background: '#070b2d', accent: '#ff4fd8',
  },
  {
    id: 'jungle-jackpot', title: 'Jungle Jackpot',
    routes: ['/slots/jungle-jackpot', '/slots/jungle-jackpot/demo'],
    image: jungleJackpot, background: '#071d15', accent: '#f2b94b',
  },
  {
    id: 'ocean-odyssey', title: 'Ocean Odyssey',
    routes: ['/slots/ocean-odyssey', '/slots/ocean-odyssey/demo'],
    image: oceanOdyssey, background: '#031629', accent: '#69eaff',
  },
  {
    id: 'samurai-fortune', title: 'Samurai Fortune',
    routes: ['/slots/samurai-fortune', '/slots/samurai-fortune/demo'],
    image: samuraiFortune, background: '#180b0c', accent: '#e6bd63',
  },
]
