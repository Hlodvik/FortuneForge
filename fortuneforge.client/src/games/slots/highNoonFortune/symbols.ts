import amberLassoBadge from '../../../assets/slots/games/high-noon-fortune/amber-lasso-badge.svg'
import cowboyBoot from '../../../assets/slots/games/high-noon-fortune/cowboy-boot.svg'
import crimsonBandanaBadge from '../../../assets/slots/games/high-noon-fortune/crimson-bandana-badge.svg'
import dynamiteBundle from '../../../assets/slots/games/high-noon-fortune/dynamite-bundle.svg'
import dynamiteLantern from '../../../assets/slots/games/high-noon-fortune/dynamite-lantern.svg'
import emeraldCactusBadge from '../../../assets/slots/games/high-noon-fortune/emerald-cactus-badge.svg'
import goldenLasso from '../../../assets/slots/games/high-noon-fortune/golden-lasso.svg'
import goldNuggetToken from '../../../assets/slots/games/high-noon-fortune/gold-nugget-token.svg'
import horseshoe from '../../../assets/slots/games/high-noon-fortune/horseshoe.svg'
import longhornCrest from '../../../assets/slots/games/high-noon-fortune/longhorn-crest.svg'
import marshalStar from '../../../assets/slots/games/high-noon-fortune/marshal-star.svg'
import saloonDoors from '../../../assets/slots/games/high-noon-fortune/saloon-doors.svg'
import sapphireSpurBadge from '../../../assets/slots/games/high-noon-fortune/sapphire-spur-badge.svg'
import sheriffBadge from '../../../assets/slots/games/high-noon-fortune/sheriff-badge.svg'
import sixShooter from '../../../assets/slots/games/high-noon-fortune/six-shooter.svg'
import stagecoach from '../../../assets/slots/games/high-noon-fortune/stagecoach.svg'
import westernSaddle from '../../../assets/slots/games/high-noon-fortune/western-saddle.svg'
import { createThemedSymbolSet } from '../shared/themedSymbolSet'

export const HIGH_NOON_FORTUNE_SYMBOLS = createThemedSymbolSet({
  id: 'high-noon-fortune-symbols-v1',
  serverSymbolSetId: 'high-noon-fortune-v1-symbols',
  symbols: {
    '2': { label: 'Lucky horseshoe', image: horseshoe },
    '3': { label: 'Tooled cowboy boot', image: cowboyBoot },
    '4': { label: 'Silver sheriff badge', image: sheriffBadge },
    '5': { label: 'Polished six-shooter', image: sixShooter },
    '6': { label: 'Western saddle', image: westernSaddle },
    '7': { label: 'Frontier stagecoach', image: stagecoach },
    ACE: { label: 'Longhorn sheriff wild crest', image: longhornCrest },
    FREE: { label: 'Saloon doors free game', image: saloonDoors },
    POWER: { label: 'Golden marshal power star', image: marshalStar },
    BOLT: { label: 'Glowing dynamite charge', image: dynamiteLantern },
    BANANA: { label: 'Dynamite bundle', image: dynamiteBundle },
    PAW: { label: 'Golden lasso roundup', image: goldenLasso },
    SEAL_SYNC: { label: 'Crimson quick-draw badge', image: crimsonBandanaBadge },
    SEAL_ROWS: { label: 'Sapphire canyon-trail badge', image: sapphireSpurBadge },
    SEAL_PAW: { label: 'Amber lasso-rush badge', image: amberLassoBadge },
    SEAL_RAND: { label: 'Emerald gold-trail badge', image: emeraldCactusBadge },
  },
  valueToken: { label: 'gold nugget', image: goldNuggetToken },
  energyEarnLabel: '+1 fuse charge',
  collectorFirstValue: 'ropes gold nuggets',
  collectorSecondValue: 'double roundup',
  collectionAwardLabels: {
    SEAL_SYNC: '10 quick-draw spins',
    SEAL_ROWS: '10 canyon-trail spins',
    SEAL_PAW: '10 lasso-rush spins',
    SEAL_RAND: '10 gold-trail spins',
  },
})
