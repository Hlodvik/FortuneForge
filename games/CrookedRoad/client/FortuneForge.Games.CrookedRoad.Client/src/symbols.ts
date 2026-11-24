import clover from './assets/art/irish/symbols/crooked-road-clover-v1.webp'
import horseshoe from './assets/art/irish/symbols/crooked-road-horseshoe-v1.webp'
import lantern from './assets/art/irish/symbols/crooked-road-lantern-v1.webp'
import fiddle from './assets/art/irish/symbols/crooked-road-fiddle-v1.webp'
import harp from './assets/art/irish/symbols/crooked-road-harp-v1.webp'
import fox from './assets/art/irish/symbols/crooked-road-fox-v1.webp'
import hat from './assets/art/irish/symbols/crooked-road-wild-hat-v1.webp'
import fairyDoor from './assets/art/irish/symbols/crooked-road-fairy-door-v1.webp'
import trail from './assets/art/irish/symbols/crooked-road-trail-v1.webp'
import potOfGold from './assets/art/irish/symbols/crooked-road-pot-of-gold-black-v2.webp'
import village from './assets/art/irish/backgrounds/crooked-road-village-v1.webp'
import well from './assets/art/irish/backgrounds/crooked-road-wishing-well-v1.webp'
import fort from './assets/art/irish/backgrounds/crooked-road-fairy-fort-v1.webp'
import hill from './assets/art/irish/backgrounds/crooked-road-hollow-hill-v1.webp'
import wordmark from './assets/art/irish/branding/crooked-road-wordmark-v1.webp'
import reelSurround from './assets/art/irish/cabinet/crooked-road-reel-surround-v1.webp'
import mapBoard from './assets/art/irish/feature/crooked-road-map-board-v1.webp'
import straight from './assets/art/irish/feature/road-tile-straight-v1.webp'
import junction from './assets/art/irish/feature/road-tile-junction-v1.webp'
import finbar from './assets/art/irish/character/finbar-the-wayward-v1.webp'

export const CROOKED_ROAD_ARTWORK = {
  wordmark,
  reelSurround,
  mapBoard,
  finbar,
  goldPot: potOfGold,
  roadTiles: { straight, junction },
  backgrounds: { village, well, fort, hill },
} as const

export const CROOKED_ROAD_SYMBOL_IMAGES = {
  '2': clover,
  '3': horseshoe,
  '4': lantern,
  '5': fiddle,
  '6': harp,
  '7': fox,
  ACE: hat,
  FREE: fairyDoor,
  POWER: trail,
} as const
