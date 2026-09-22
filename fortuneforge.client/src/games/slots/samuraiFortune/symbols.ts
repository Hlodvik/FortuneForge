import amberSunCrest from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-amber-sun-crest-v2.webp'
import bridge from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-bridge-v2.webp'
import samuraiCabinetEmblem from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-cabinet-emblem-v2.webp'
import cherryBlossom from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-cherry-blossom-v2.webp'
import crimsonCraneCrest from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-crimson-crane-crest-v2.webp'
import dragonMask from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-dragon-mask-v2.webp'
import festivalLantern from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-festival-lantern-v2.webp'
import foldingFan from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-folding-fan-v2.webp'
import forgedKatana from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-forged-katana-v2.webp'
import jadeSpiritOrb from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-jade-spirit-orb-v2.webp'
import jadeDragonCrest from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-jade-dragon-crest-v2.webp'
import mountainCastle from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-mountain-castle-v2.webp'
import monCoin from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-mon-coin-v2.webp'
import riceBowl from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-rice-bowl-v2.webp'
import sacredTorii from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-sacred-torii-v2.webp'
import sapphireMoonCrest from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-sapphire-moon-crest-v2.webp'
import shogunSatchel from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-shogun-satchel-v2.webp'
import stormSpiritCharge from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-storm-spirit-charge-v2.webp'
import tripleShuriken from '../../../assets/slots/games/samurai-fortune/optimized/samurai-fortune-triple-shuriken-v2.webp'

export const SAMURAI_FORTUNE_SYMBOL_IMAGES = {
  '2': riceBowl, '3': foldingFan, '4': festivalLantern, '5': cherryBlossom, '6': forgedKatana, '7': mountainCastle,
  ACE: dragonMask, FREE: sacredTorii, POWER: jadeSpiritOrb, BOLT: stormSpiritCharge,
  BANANA: tripleShuriken, PAW: shogunSatchel,
  SEAL_SYNC: crimsonCraneCrest, SEAL_ROWS: sapphireMoonCrest, SEAL_PAW: amberSunCrest, SEAL_RAND: jadeDragonCrest,
  VALUE: monCoin,
  CABINET_EMBLEM: samuraiCabinetEmblem, BACKDROP: bridge,
} as const
