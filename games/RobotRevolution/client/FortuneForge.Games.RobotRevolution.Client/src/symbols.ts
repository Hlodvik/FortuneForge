import cacao from './assets/art/sunstone-eclipse/sunstone-cacao-v1.webp'
import feather from './assets/art/sunstone-eclipse/sunstone-quetzal-feather-v1.webp'
import earspool from './assets/art/sunstone-eclipse/sunstone-turquoise-earspool-v1.webp'
import serpent from './assets/art/sunstone-eclipse/sunstone-feathered-serpent-v1.webp'
import eagle from './assets/art/sunstone-eclipse/sunstone-eagle-guardian-v1.webp'
import jaguar from './assets/art/sunstone-eclipse/sunstone-jaguar-mask-v1.webp'
import wild from './assets/art/sunstone-eclipse/sunstone-wild-calendar-v1.webp'
import eclipse from './assets/art/sunstone-eclipse/sunstone-eclipse-scatter-v1.webp'
import sunray from './assets/art/sunstone-eclipse/sunstone-sunray-glyph-v1.webp'
import tripleSuns from './assets/art/sunstone-eclipse/sunstone-triple-suns-v1.webp'
import temple from './assets/art/sunstone-eclipse/sunstone-eclipse-temple-background-v1.webp'

export const SUNSTONE_ECLIPSE_SYMBOL_IMAGES = {
  '2': cacao, '3': feather, '4': earspool, '5': serpent, '6': eagle, '7': jaguar,
  ACE: wild, FREE: eclipse, POWER: sunray, BOLT: sunray, BANANA: tripleSuns, PAW: wild,
  SEAL_SYNC: jaguar, SEAL_ROWS: eagle, SEAL_PAW: serpent, SEAL_RAND: sunray,
  VALUE: earspool, CABINET_EMBLEM: wild, BACKDROP: temple,
} as const

export const ROBOT_REVOLUTION_SYMBOL_IMAGES = SUNSTONE_ECLIPSE_SYMBOL_IMAGES
