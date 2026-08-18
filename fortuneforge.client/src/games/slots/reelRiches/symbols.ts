import amberNetBadge from '../../../assets/slots/games/reel-riches/amber-net-badge.svg'
import blueMarlin from '../../../assets/slots/games/reel-riches/blue-marlin.svg'
import bobber from '../../../assets/slots/games/reel-riches/bobber.svg'
import crimsonLureBadge from '../../../assets/slots/games/reel-riches/crimson-lure-badge.svg'
import emeraldHookBadge from '../../../assets/slots/games/reel-riches/emerald-hook-badge.svg'
import featherLure from '../../../assets/slots/games/reel-riches/feather-lure.svg'
import fishingNet from '../../../assets/slots/games/reel-riches/fishing-net.svg'
import goldenFishSchool from '../../../assets/slots/games/reel-riches/golden-fish-school.svg'
import largemouthBass from '../../../assets/slots/games/reel-riches/largemouth-bass.svg'
import moonlitPier from '../../../assets/slots/games/reel-riches/moonlit-pier.svg'
import pearlCoralToken from '../../../assets/slots/games/reel-riches/pearl-coral-token.svg'
import rainbowTrout from '../../../assets/slots/games/reel-riches/rainbow-trout.svg'
import sapphireWaveBadge from '../../../assets/slots/games/reel-riches/sapphire-wave-badge.svg'
import sonarMedallion from '../../../assets/slots/games/reel-riches/sonar-medallion.svg'
import tackleBox from '../../../assets/slots/games/reel-riches/tackle-box.svg'
import tideLantern from '../../../assets/slots/games/reel-riches/tide-lantern.svg'
import trophyFishCrest from '../../../assets/slots/games/reel-riches/trophy-fish-crest.svg'
import { createThemedSymbolSet } from '../shared/themedSymbolSet'

export const REEL_RICHES_SYMBOLS = createThemedSymbolSet({
  id: 'reel-riches-symbols-v1',
  serverSymbolSetId: 'reel-riches-v1-symbols',
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
  energyEarnLabel: '+1 tide charge',
  collectorFirstValue: 'nets pearl tokens',
  collectorSecondValue: 'double haul',
  collectionAwardLabels: {
    SEAL_SYNC: '10 perfect-cast spins',
    SEAL_ROWS: '10 rising-tide spins',
    SEAL_PAW: '10 net-frenzy spins',
    SEAL_RAND: '10 jackpot-hook spins',
  },
})
