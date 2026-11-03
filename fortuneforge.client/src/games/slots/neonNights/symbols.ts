import ornateK from '../../../assets/slots/games/neon-nights/optimized/neon-nights-ornate-k-v3.webp'
import arcadeChip from '../../../assets/slots/games/neon-nights/optimized/neon-nights-arcade-chip-v2.webp'
import blueDice from '../../../assets/slots/games/neon-nights/optimized/neon-nights-dice-v4.webp'
import cassette from '../../../assets/slots/games/neon-nights/optimized/neon-nights-cassette-v2.webp'
import ornateJ from '../../../assets/slots/games/neon-nights/optimized/neon-nights-ornate-j-v3.webp'
import diamondShades from '../../../assets/slots/games/neon-nights/optimized/neon-nights-diamond-shades-v3.webp'
import djDeck from '../../../assets/slots/games/neon-nights/optimized/neon-nights-dj-deck-v2.webp'
import discoBall from '../../../assets/slots/games/neon-nights/optimized/neon-nights-disco-ball-v4.webp'
import arcadeCabinet from '../../../assets/slots/games/neon-nights/optimized/neon-nights-arcade-cabinet-v4.webp'
import ornateA from '../../../assets/slots/games/neon-nights/optimized/neon-nights-ornate-a-v3.webp'
import neonNightMarket from '../../../assets/slots/games/neon-nights/optimized/neon-nights-night-market-v3.webp'
import neonCabinetEmblem from '../../../assets/slots/games/neon-nights/optimized/neon-nights-cabinet-emblem-v2.webp'
import bonusSign from '../../../assets/slots/games/neon-nights/optimized/neon-nights-bonus-sign-v4.webp'
import neonLantern from '../../../assets/slots/games/neon-nights/optimized/neon-nights-lantern-v5.webp'
import neonKoi from '../../../assets/slots/games/neon-nights/optimized/neon-nights-koi-v5.webp'
import ornateQ from '../../../assets/slots/games/neon-nights/optimized/neon-nights-ornate-q-v3.webp'
import sportsCar from '../../../assets/slots/games/neon-nights/optimized/neon-nights-sports-car-v2.webp'
import tripleSevens from '../../../assets/slots/games/neon-nights/optimized/neon-nights-triple-sevens-v4.webp'
import voltageBolt from '../../../assets/slots/games/neon-nights/optimized/neon-nights-voltage-bolt-v2.webp'

export const NEON_NIGHTS_SYMBOL_IMAGES = {
  '2': ornateJ, '3': cassette, '4': ornateQ, '5': ornateK, '6': sportsCar, '7': ornateA,
  ACE: diamondShades, FREE: bonusSign, POWER: discoBall, BOLT: voltageBolt,
  BANANA: tripleSevens, PAW: djDeck,
  SEAL_SYNC: neonKoi, SEAL_ROWS: blueDice, SEAL_PAW: neonLantern, SEAL_RAND: arcadeCabinet,
  VALUE: arcadeChip,
  CABINET_EMBLEM: neonCabinetEmblem, BACKDROP: neonNightMarket,
} as const
