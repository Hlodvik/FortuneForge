import { CLASSIC_SLOT_SYMBOL_SET_ID } from '@fortuneforge/slot-game-runtime/symbols'
import amberNetBadge from './assets/art/optimized/reel-riches-amber-net-badge-v2.webp'
import blueMarlin from './assets/art/optimized/reel-riches-blue-marlin-v2.webp'
import bobber from './assets/art/optimized/reel-riches-bobber-v2.webp'
import crimsonLureBadge from './assets/art/optimized/reel-riches-crimson-lure-badge-v2.webp'
import emeraldHookBadge from './assets/art/optimized/reel-riches-emerald-hook-badge-v2.webp'
import featherLure from './assets/art/optimized/reel-riches-feather-lure-v2.webp'
import fishingNet from './assets/art/optimized/reel-riches-fishing-net-v2.webp'
import goldenFishSchool from './assets/art/optimized/reel-riches-golden-fish-school-v2.webp'
import largemouthBass from './assets/art/optimized/reel-riches-largemouth-bass-v2.webp'
import moonlitPier from './assets/art/optimized/reel-riches-moonlit-pier-v2.webp'
import pearlCoralToken from './assets/art/optimized/reel-riches-pearl-token-v2.webp'
import rainbowTrout from './assets/art/optimized/reel-riches-rainbow-trout-v2.webp'
import sapphireWaveBadge from './assets/art/optimized/reel-riches-sapphire-wave-badge-v2.webp'
import sonarMedallion from './assets/art/optimized/reel-riches-sonar-medallion-v2.webp'
import tackleBox from './assets/art/optimized/reel-riches-tackle-box-v2.webp'
import tideLantern from './assets/art/optimized/reel-riches-tide-lantern-v2.webp'
import trophyFishCrest from './assets/art/optimized/reel-riches-trophy-fish-crest-v2.webp'
import { createThemedSymbolSet } from '@fortuneforge/slot-game-runtime/symbols'

export const REEL_RICHES_SYMBOLS = createThemedSymbolSet({
  id: 'reel-riches-symbols-v1',
  serverSymbolSetId: CLASSIC_SLOT_SYMBOL_SET_ID,
  symbols: {
    '2': { label: 'Lucky fishing bobber', image: bobber },
    '3': { label: 'Sparkling feather lure', image: featherLure },
    '4': { label: 'Wooden tackle box', image: tackleBox },
    '5': { label: 'Rainbow trout', image: rainbowTrout },
    '6': { label: 'Largemouth bass', image: largemouthBass },
    '7': { label: 'Leaping blue marlin', image: blueMarlin },
    ACE: { label: 'Trophy fish wild crest', image: trophyFishCrest },
    FREE: { label: 'Moonlit pier free game', image: moonlitPier },
    POWER: { label: 'Golden sonar power', image: sonarMedallion },
    BOLT: { label: 'Glowing tide lantern', image: tideLantern },
    BANANA: { label: 'Golden fish school', image: goldenFishSchool },
    PAW: { label: 'Fishing net haul', image: fishingNet },
    SEAL_SYNC: { label: 'Crimson perfect-cast badge', image: crimsonLureBadge },
    SEAL_ROWS: { label: 'Sapphire rising-tide badge', image: sapphireWaveBadge },
    SEAL_PAW: { label: 'Amber net-frenzy badge', image: amberNetBadge },
    SEAL_RAND: { label: 'Emerald jackpot-hook badge', image: emeraldHookBadge },
  },
  valueToken: { label: 'pearl token', image: pearlCoralToken },
  energyEarnLabel: '+1 Gem Multiplier point',
  collectorFirstValue: 'nets pearl tokens',
  collectorSecondValue: 'double haul',
  collectionAwardLabels: {
    SEAL_SYNC: '10 perfect-cast spins',
    SEAL_ROWS: '10 rising-tide spins',
    SEAL_PAW: '10 net-frenzy spins',
    SEAL_RAND: '10 jackpot-hook spins',
  },
})
