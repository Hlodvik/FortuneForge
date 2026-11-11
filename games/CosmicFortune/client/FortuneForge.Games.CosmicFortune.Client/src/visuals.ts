import alienCaptain from './assets/art/optimized/cosmic-fortune-alien-captain-v2.webp'
import amberSolarWorld from './assets/art/optimized/cosmic-fortune-amber-solar-world-v2.webp'
import astronaut from './assets/art/optimized/cosmic-fortune-astronaut-v2.webp'
import atomicPlasmaCharge from './assets/art/optimized/cosmic-fortune-atomic-plasma-charge-v2.webp'
import cabinetEmblem from './assets/art/optimized/cosmic-fortune-cabinet-emblem-v2.webp'
import comet from './assets/art/optimized/cosmic-fortune-comet-v2.webp'
import crimsonBinaryStar from './assets/art/optimized/cosmic-fortune-crimson-binary-star-v2.webp'
import darkMatterToken from './assets/art/optimized/cosmic-fortune-dark-matter-token-v2.webp'
import emeraldGardenPlanet from './assets/art/optimized/cosmic-fortune-emerald-garden-planet-v2.webp'
import meteorTrio from './assets/art/optimized/cosmic-fortune-meteor-trio-v2.webp'
import moonStation from './assets/art/optimized/cosmic-fortune-moon-station-v2.webp'
import observationDeck from './assets/art/optimized/cosmic-fortune-observation-deck-v2.webp'
import ringedGasGiant from './assets/art/optimized/cosmic-fortune-ringed-gas-giant-v2.webp'
import rocket from './assets/art/optimized/cosmic-fortune-rocket-v2.webp'
import sapphireIcePlanet from './assets/art/optimized/cosmic-fortune-sapphire-ice-planet-v2.webp'
import satellite from './assets/art/optimized/cosmic-fortune-satellite-v2.webp'
import supernovaCore from './assets/art/optimized/cosmic-fortune-supernova-core-v2.webp'
import tractorBeamSaucer from './assets/art/optimized/cosmic-fortune-tractor-beam-saucer-v2.webp'
import wormhole from './assets/art/optimized/cosmic-fortune-wormhole-v2.webp'

export const COSMIC_FORTUNE_VISUALS = {
  backdrop: observationDeck,
  emblem: cabinetEmblem,
  accent: darkMatterToken,
  satellite,
  comet,
  moon: moonStation,
  planet: ringedGasGiant,
  astronaut,
  rocket,
  wild: alienCaptain,
  free: wormhole,
  power: supernovaCore,
  energy: atomicPlasmaCharge,
  lineBonus: meteorTrio,
  collector: tractorBeamSaucer,
  sync: crimsonBinaryStar,
  rows: sapphireIcePlanet,
  paw: amberSolarWorld,
  rand: emeraldGardenPlanet,
  value: darkMatterToken,
} as const
