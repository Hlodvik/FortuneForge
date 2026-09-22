import ancientSkeletonKey from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-ancient-skeleton-key-v2.webp'
import phantomCabinetEmblem from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-cabinet-emblem-v2.webp'
import amberCandleSeal from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-amber-candle-seal-v2.webp'
import forbiddenCrypt from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-forbidden-crypt-v2.webp'
import fullMoonCharge from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-full-moon-charge-v2.webp'
import gates from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-gates-v2.webp'
import hauntedMirror from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-haunted-mirror-v2.webp'
import crimsonRavenSeal from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-crimson-raven-seal-v2.webp'
import emeraldKeySeal from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-emerald-key-seal-v2.webp'
import jeweledSkull from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-jeweled-skull-v2.webp'
import midnightRaven from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-midnight-raven-v2.webp'
import moonlitManor from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-moonlit-manor-v2.webp'
import restlessPhantom from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-restless-phantom-v2.webp'
import seanceCrystal from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-seance-crystal-v2.webp'
import seanceSpiritLantern from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-seance-spirit-lantern-v2.webp'
import sapphireMirrorSeal from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-sapphire-mirror-seal-v2.webp'
import spiritWisp from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-spirit-wisp-v2.webp'
import tripleShadowBats from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-triple-shadow-bats-v2.webp'
import whisperingCandle from '../../../assets/slots/games/phantom-manor/optimized/phantom-manor-whispering-candle-v2.webp'

export const PHANTOM_MANOR_SYMBOL_IMAGES = {
  '2': whisperingCandle, '3': ancientSkeletonKey, '4': midnightRaven, '5': hauntedMirror, '6': restlessPhantom, '7': moonlitManor,
  ACE: jeweledSkull, FREE: forbiddenCrypt, POWER: seanceCrystal, BOLT: fullMoonCharge,
  BANANA: tripleShadowBats, PAW: seanceSpiritLantern,
  SEAL_SYNC: crimsonRavenSeal, SEAL_ROWS: sapphireMirrorSeal, SEAL_PAW: amberCandleSeal, SEAL_RAND: emeraldKeySeal,
  VALUE: spiritWisp,
  CABINET_EMBLEM: phantomCabinetEmblem, BACKDROP: gates,
} as const
