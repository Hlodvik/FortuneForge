import ancientSeaTurtle from './assets/art/optimized/ocean-odyssey-ancient-sea-turtle-v2.webp'
import amberStarTreasure from './assets/art/optimized/ocean-odyssey-amber-star-treasure-v2.webp'
import oceanCabinetEmblem from './assets/art/optimized/ocean-odyssey-cabinet-emblem-v2.webp'
import crimsonCoralTreasure from './assets/art/optimized/ocean-odyssey-crimson-coral-treasure-v2.webp'
import emeraldTurtleTreasure from './assets/art/optimized/ocean-odyssey-emerald-turtle-treasure-v2.webp'
import goldenStarfish from './assets/art/optimized/ocean-odyssey-golden-starfish-v2.webp'
import greatBlueWhale from './assets/art/optimized/ocean-odyssey-great-blue-whale-v2.webp'
import pearlCavern from './assets/art/optimized/ocean-odyssey-pearl-cavern-v2.webp'
import pearlDiverNet from './assets/art/optimized/ocean-odyssey-pearl-diver-net-v2.webp'
import pearlToken from './assets/art/optimized/ocean-odyssey-pearl-token-v2.webp'
import pearlKingdom from './assets/art/optimized/ocean-odyssey-pearl-kingdom-v2.webp'
import playfulDolphin from './assets/art/optimized/ocean-odyssey-playful-dolphin-v2.webp'
import poseidonTrident from './assets/art/optimized/ocean-odyssey-poseidon-trident-v2.webp'
import royalOceanPearl from './assets/art/optimized/ocean-odyssey-royal-ocean-pearl-v2.webp'
import sapphireShellTreasure from './assets/art/optimized/ocean-odyssey-sapphire-shell-treasure-v2.webp'
import spiralSeashell from './assets/art/optimized/ocean-odyssey-spiral-seashell-v2.webp'
import stripedTropicalFish from './assets/art/optimized/ocean-odyssey-striped-tropical-fish-v2.webp'
import tidalWave from './assets/art/optimized/ocean-odyssey-tidal-wave-v2.webp'
import tripleCoralBranches from './assets/art/optimized/ocean-odyssey-triple-coral-branches-v2.webp'

export const OCEAN_ODYSSEY_SYMBOL_IMAGES = {
  '2': spiralSeashell, '3': goldenStarfish, '4': stripedTropicalFish, '5': playfulDolphin, '6': ancientSeaTurtle, '7': greatBlueWhale,
  ACE: poseidonTrident, FREE: pearlCavern, POWER: royalOceanPearl, BOLT: tidalWave,
  BANANA: tripleCoralBranches, PAW: pearlDiverNet,
  SEAL_SYNC: crimsonCoralTreasure, SEAL_ROWS: sapphireShellTreasure, SEAL_PAW: amberStarTreasure, SEAL_RAND: emeraldTurtleTreasure,
  VALUE: pearlToken,
  CABINET_EMBLEM: oceanCabinetEmblem, BACKDROP: pearlKingdom,
} as const
