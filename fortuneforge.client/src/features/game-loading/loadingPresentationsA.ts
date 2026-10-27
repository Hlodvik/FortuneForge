import type { GameLoadingEntry } from './types'
import blackjackArt from '../../assets/catalogue/non-slots/blackjack-title-v2.webp'
import texasHoldemArt from '../../assets/catalogue/non-slots/texas-holdem-title-v2.webp'
import threeCardPokerArt from '../../assets/catalogue/non-slots/three-card-poker-title-v2.webp'
import houseHoldemArt from '../../assets/catalogue/non-slots/house-holdem-title-v2.webp'
import baccaratArt from '../../assets/catalogue/non-slots/baccarat-title-v1.webp'
import rouletteArt from '../../assets/catalogue/non-slots/roulette-title-v3.webp'
import crapsArt from '../../assets/catalogue/non-slots/craps-title-v1.webp'
import wukongJourneyToTheWestArt from '../../assets/catalogue/slots/wukong-journey-to-the-west-title-v2.webp'
import rainbowRealmArt from '../../assets/catalogue/slots/rainbow-realm-title-v2.webp'
import piratesFortuneArt from '../../assets/catalogue/slots/pirates-fortune-title-v2.webp'
import godsOfOlympusArt from '../../assets/catalogue/slots/gods-of-olympus-title-v2.webp'
import reelRichesArt from '../../assets/catalogue/slots/reel-riches-title-v2.webp'
import highNoonFortuneArt from '../../assets/catalogue/slots/high-noon-fortune-title-v2.webp'
import royalDrawArt from '../../assets/catalogue/slots/royal-draw-title-v2.webp'

/** Presentation URLs only; route integration stays with the application. */
export const LOADING_PRESENTATIONS_A: readonly GameLoadingEntry[] = [
  {
    id: "blackjack",
    title: "Fortune Blackjack",
    routes: ["/cards/blackjack","/demo/cards/blackjack"],
    image: blackjackArt,
    background: '#020908',
    accent: '#ffe895',
  },
  {
    id: "texas-holdem",
    title: "Texas Hold’em",
    routes: ["/cards/texas-holdem","/demo/cards/texas-holdem"],
    image: texasHoldemArt,
    background: '#00170e',
    accent: '#d8b66e',
  },
  {
    id: "three-card-poker",
    title: "Three Card Poker",
    routes: ["/cards/three-card-poker"],
    image: threeCardPokerArt,
    background: '#000000',
    accent: '#eee9dd',
  },
  {
    id: "house-holdem",
    title: "House Hold’em",
    routes: ["/cards/house-holdem"],
    image: houseHoldemArt,
    background: '#011b27',
    accent: '#69d7d9',
  },
  {
    id: "baccarat",
    title: "Baccarat",
    routes: ["/games/baccarat"],
    image: baccaratArt,
    background: '#022a23',
    accent: '#e4bd68',
  },
  {
    id: "roulette",
    title: "Roulette",
    routes: ["/games/roulette"],
    image: rouletteArt,
    background: '#0a1511',
    accent: '#f7e9a9',
  },
  {
    id: "craps",
    title: "Craps",
    routes: ["/games/craps"],
    image: crapsArt,
    background: '#07120e',
    accent: '#e8d49a',
  },
  {
    id: "wukong-journey-to-the-west",
    title: "Wukong's Journey to the West",
    routes: ["/slots/wukong","/slots/wukong/demo"],
    image: wukongJourneyToTheWestArt,
    background: '#240716',
    accent: '#20d9cf',
  },
  {
    id: "rainbow-realm",
    title: "Rainbow Realm",
    routes: ["/slots/rainbow-realm","/slots/rainbow-realm/demo"],
    image: rainbowRealmArt,
    background: '#4f1a62',
    accent: '#40cfff',
  },
  {
    id: "pirates-fortune",
    title: "Pirates' Fortune",
    routes: ["/slots/pirates-fortune","/slots/pirates-fortune/demo"],
    image: piratesFortuneArt,
    background: '#241005',
    accent: '#e1a84a',
  },
  {
    id: "gods-of-olympus",
    title: "Gods of Olympus",
    routes: ["/slots/gods-of-olympus","/slots/gods-of-olympus/demo"],
    image: godsOfOlympusArt,
    background: '#160c36',
    accent: '#56b8ff',
  },
  {
    id: "reel-riches",
    title: "Reel Riches",
    routes: ["/slots/reel-riches","/slots/reel-riches/demo"],
    image: reelRichesArt,
    background: '#04283d',
    accent: '#43e0d5',
  },
  {
    id: "high-noon-fortune",
    title: "High Noon Fortune",
    routes: ["/slots/high-noon-fortune","/slots/high-noon-fortune/demo"],
    image: highNoonFortuneArt,
    background: '#2b1512',
    accent: '#3ed5c4',
  },
  {
    id: "royal-draw",
    title: "Royal Draw",
    routes: ["/slots/royal-draw","/slots/royal-draw/demo"],
    image: royalDrawArt,
    background: '#130b18',
    accent: '#32d28a',
  },
]
