import neonK from './assets/symbols/neon-nights-tube-k-v1.png'
import blueDice from './assets/symbols/neon-nights-dice-v4.webp'
import cassette from './assets/symbols/neon-nights-cassette-v2.webp'
import neonJ from './assets/symbols/neon-nights-tube-j-v1.png'
import diamondShades from './assets/symbols/neon-nights-diamond-shades-v3.webp'
import neonWild from './assets/symbols/neon-nights-wild-tube-v2.png'
import discoBall from './assets/symbols/neon-nights-disco-ball-v4.webp'
import neonA from './assets/symbols/neon-nights-tube-a-v1.png'
import neonNightMarket from './assets/symbols/neon-nights-night-market-v3.webp'
import neonCabinetEmblem from './assets/symbols/neon-nights-cabinet-emblem-v2.webp'
import bonusSign from './assets/symbols/neon-nights-bonus-sign-v4.webp'
import neonKoi from './assets/symbols/neon-nights-koi-v5.webp'
import neonQ from './assets/symbols/neon-nights-tube-q-v1.png'
import sportsCar from './assets/symbols/neon-nights-sports-car-v2.webp'
import nitroBottle from './assets/symbols/neon-nights-nitro-bottle-v1.webp'
import tripleSevens from './assets/symbols/neon-nights-triple-sevens-v4.webp'
import discoMartini from './assets/symbols/neon-nights-disco-martini-v1.png'
import discoSeven from './assets/symbols/neon-nights-disco-seven-v1.png'
import discoTubeBall from './assets/symbols/neon-nights-disco-tube-ball-v1.png'

export const NEON_NIGHTS_SYMBOL_IMAGES = {
  '2': neonJ, '3': cassette, '4': neonQ, '5': neonK, '6': sportsCar, '7': neonA,
  ACE: neonWild, FREE: bonusSign, POWER: discoBall,
  NITRO: nitroBottle,
  BANANA: tripleSevens,
  SEAL_SYNC: neonKoi, SEAL_ROWS: blueDice, SEAL_PAW: diamondShades,
  DISCO_MARTINI: discoMartini, DISCO_SEVEN: discoSeven, DISCO_BALL: discoTubeBall,
  CABINET_EMBLEM: neonCabinetEmblem, BACKDROP: neonNightMarket,
} as const

