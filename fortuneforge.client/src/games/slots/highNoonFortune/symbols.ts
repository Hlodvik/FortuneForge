import amberLassoBadge from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-amber-lasso-badge-v2.webp'
import cowboyBoot from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-cowboy-boot-v2.webp'
import crimsonBandanaBadge from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-crimson-bandana-badge-v2.webp'
import dynamiteBundle from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-dynamite-bundle-v2.webp'
import dynamiteLantern from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-dynamite-lantern-v2.webp'
import emeraldCactusBadge from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-emerald-cactus-badge-v2.webp'
import goldenLasso from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-golden-lasso-v2.webp'
import goldNuggetToken from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-gold-nugget-v2.webp'
import horseshoe from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-horseshoe-v2.webp'
import longhornCrest from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-longhorn-crest-v2.webp'
import marshalStar from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-marshal-star-v2.webp'
import saloonDoors from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-saloon-doors-v2.webp'
import sapphireSpurBadge from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-sapphire-spur-badge-v2.webp'
import sheriffBadge from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-sheriff-badge-v2.webp'
import sixShooter from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-six-shooter-v2.webp'
import stagecoach from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-stagecoach-v2.webp'
import westernSaddle from '../../../assets/slots/games/high-noon-fortune/optimized/high-noon-western-saddle-v2.webp'
import { createThemedSymbolSet } from '../shared/themedSymbolSet'

export const HIGH_NOON_FORTUNE_SYMBOLS = createThemedSymbolSet({
  id: 'high-noon-fortune-symbols-v1',
  serverSymbolSetId: 'wukong-treasures-v3',
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
    SEAL_SYNC: '8 quick-draw spins',
    SEAL_ROWS: '8 canyon-trail spins',
    SEAL_PAW: '8 lasso-rush spins',
    SEAL_RAND: '8 gold-trail spins',
  },
})
