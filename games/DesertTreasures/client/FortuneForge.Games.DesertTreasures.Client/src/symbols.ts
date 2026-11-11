import amberSunRelic from './assets/art/optimized/desert-treasures-amber-sun-relic-v2.webp'
import desertCabinetEmblem from './assets/art/optimized/desert-treasures-cabinet-emblem-v2.webp'
import desertCamel from './assets/art/optimized/desert-treasures-desert-camel-v2.webp'
import crimsonAnkhRelic from './assets/art/optimized/desert-treasures-crimson-ankh-relic-v2.webp'
import emeraldScarabRelic from './assets/art/optimized/desert-treasures-emerald-scarab-relic-v2.webp'
import goldScarab from './assets/art/optimized/desert-treasures-gold-scarab-v2.webp'
import greatGoldenPyramid from './assets/art/optimized/desert-treasures-great-golden-pyramid-v2.webp'
import guardianSphinx from './assets/art/optimized/desert-treasures-guardian-sphinx-v2.webp'
import hiddenOasisPalm from './assets/art/optimized/desert-treasures-hidden-oasis-palm-v2.webp'
import jeweledScarab from './assets/art/optimized/desert-treasures-jeweled-scarab-v2.webp'
import paintedClayUrn from './assets/art/optimized/desert-treasures-painted-clay-urn-v2.webp'
import pharaohCrown from './assets/art/optimized/desert-treasures-pharaoh-crown-v2.webp'
import radiantSunDisc from './assets/art/optimized/desert-treasures-radiant-sun-disc-v2.webp'
import royalExcavationSatchel from './assets/art/optimized/desert-treasures-royal-excavation-satchel-v2.webp'
import sandstormCharge from './assets/art/optimized/desert-treasures-sandstorm-charge-v2.webp'
import sunDunes from './assets/art/optimized/desert-treasures-sun-dunes-v2.webp'
import sapphireLotusRelic from './assets/art/optimized/desert-treasures-sapphire-lotus-relic-v2.webp'
import sealedTomb from './assets/art/optimized/desert-treasures-sealed-tomb-v2.webp'
import tripleGoldenAnkhs from './assets/art/optimized/desert-treasures-triple-golden-ankhs-v2.webp'

export const DESERT_TREASURES_SYMBOL_IMAGES = {
  '2': paintedClayUrn, '3': jeweledScarab, '4': desertCamel, '5': hiddenOasisPalm, '6': guardianSphinx, '7': greatGoldenPyramid,
  ACE: pharaohCrown, FREE: sealedTomb, POWER: radiantSunDisc, BOLT: sandstormCharge,
  BANANA: tripleGoldenAnkhs, PAW: royalExcavationSatchel,
  SEAL_SYNC: crimsonAnkhRelic, SEAL_ROWS: sapphireLotusRelic, SEAL_PAW: amberSunRelic, SEAL_RAND: emeraldScarabRelic,
  VALUE: goldScarab,
  CABINET_EMBLEM: desertCabinetEmblem, BACKDROP: sunDunes,
} as const
