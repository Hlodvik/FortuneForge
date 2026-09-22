import arcadeJoystick from '../../../assets/slots/games/neon-nights/optimized/neon-nights-arcade-joystick-v2.webp'
import arcadeChip from '../../../assets/slots/games/neon-nights/optimized/neon-nights-arcade-chip-v2.webp'
import blueDiamondBadge from '../../../assets/slots/games/neon-nights/optimized/neon-nights-blue-diamond-badge-v2.webp'
import cassette from '../../../assets/slots/games/neon-nights/optimized/neon-nights-cassette-v2.webp'
import cherry from '../../../assets/slots/games/neon-nights/optimized/neon-nights-cherry-v2.webp'
import diamondShades from '../../../assets/slots/games/neon-nights/optimized/neon-nights-diamond-shades-v2.webp'
import djDeck from '../../../assets/slots/games/neon-nights/optimized/neon-nights-dj-deck-v2.webp'
import electricStar from '../../../assets/slots/games/neon-nights/optimized/neon-nights-electric-star-v2.webp'
import greenMusicBadge from '../../../assets/slots/games/neon-nights/optimized/neon-nights-green-music-badge-v2.webp'
import neonSkyline from '../../../assets/slots/games/neon-nights/optimized/neon-nights-neon-skyline-v2.webp'
import neonBoulevard from '../../../assets/slots/games/neon-nights/optimized/neon-nights-boulevard-v2.webp'
import neonCabinetEmblem from '../../../assets/slots/games/neon-nights/optimized/neon-nights-cabinet-emblem-v2.webp'
import nightclubDoorway from '../../../assets/slots/games/neon-nights/optimized/neon-nights-nightclub-doorway-v2.webp'
import orangeStarBadge from '../../../assets/slots/games/neon-nights/optimized/neon-nights-orange-star-badge-v2.webp'
import pinkHeartBadge from '../../../assets/slots/games/neon-nights/optimized/neon-nights-pink-heart-badge-v2.webp'
import rollerSkate from '../../../assets/slots/games/neon-nights/optimized/neon-nights-roller-skate-v2.webp'
import sportsCar from '../../../assets/slots/games/neon-nights/optimized/neon-nights-sports-car-v2.webp'
import tripleArcadeTokens from '../../../assets/slots/games/neon-nights/optimized/neon-nights-triple-arcade-tokens-v2.webp'
import voltageBolt from '../../../assets/slots/games/neon-nights/optimized/neon-nights-voltage-bolt-v2.webp'

export const NEON_NIGHTS_SYMBOL_IMAGES = {
  '2': cherry, '3': cassette, '4': rollerSkate, '5': arcadeJoystick, '6': sportsCar, '7': neonSkyline,
  ACE: diamondShades, FREE: nightclubDoorway, POWER: electricStar, BOLT: voltageBolt,
  BANANA: tripleArcadeTokens, PAW: djDeck,
  SEAL_SYNC: pinkHeartBadge, SEAL_ROWS: blueDiamondBadge, SEAL_PAW: orangeStarBadge, SEAL_RAND: greenMusicBadge,
  VALUE: arcadeChip,
  CABINET_EMBLEM: neonCabinetEmblem, BACKDROP: neonBoulevard,
} as const
