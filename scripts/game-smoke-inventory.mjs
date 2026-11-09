export const slotSmokeInventory = [
  ['wukong', 'wukong-journey-v1'],
  ['rainbow-realm', 'rainbow-realm-fruits-v1'],
  ['pirates-fortune', 'pirates-fortune-v1'],
  ['gods-of-olympus', 'gods-of-olympus-v1'],
  ['reel-riches', 'reel-riches-v1'],
  ['high-noon-fortune', 'high-noon-fortune-v1'],
  ['royal-draw', 'royal-draw-v1'],
  ['arcane-archives', 'arcane-archives-v1'],
  ['cosmic-fortune', 'cosmic-fortune-v1'],
  ['gold-foundry', 'gold-foundry-v1'],
  ['neon-nights', 'neon-nights-v1'],
  ['ocean-odyssey', 'ocean-odyssey-v1'],
  ['samurai-fortune', 'samurai-fortune-v1'],
  ['candy-carnival', 'candy-carnival-v1'],
  ['phantom-manor', 'phantom-manor-v1'],
  ['nordic-legends', 'nordic-legends-v1'],
  ['desert-treasures', 'desert-treasures-v1'],
  ['robot-revolution', 'robot-revolution-v1'],
  ['dragon-hoard', 'dragon-hoard-v1'],
]

export const nonSlotSmokeInventory = [
  { smokePath: '/games/keno' },
  { smokePath: '/demo/cards/blackjack', catalogPath: '/cards/blackjack' },
  { smokePath: '/games/casino-war' },
  { smokePath: '/games/baccarat' },
  { smokePath: '/games/dragon-tiger' },
  { smokePath: '/games/hi-lo' },
  { smokePath: '/games/andar-bahar' },
  { smokePath: '/games/mines' },
  { smokePath: '/games/video-poker' },
  { smokePath: '/games/roulette' },
  { smokePath: '/games/craps' },
  { smokePath: '/games/liars-dice', listed: false },
  { smokePath: '/games/sic-bo' },
  { smokePath: '/demo/cards/texas-holdem', catalogPath: '/cards/texas-holdem' },
  { smokePath: '/cards/three-card-poker' },
  { smokePath: '/cards/house-holdem' },
  { smokePath: '/games/flappy', listed: false },
  { smokePath: '/games/asteroids', listed: false },
  { smokePath: '/games/drop-merge' },
  { smokePath: '/games/2048' },
  { smokePath: '/games/snake', listed: false },
  { smokePath: '/games/horse-flight', listed: false },
  { smokePath: '/cards/solitaire' },
  { smokePath: '/cards/free-solitaire' },
  { smokePath: '/cards/hearts' },
]

// Starter surfaces return the app shell but do not expose a playable demo spin.
export const shellOnlySmokeInventory = [
  { smokePath: '/slots/empty-slots-game' },
]

export const gameSmokeRoutes = [
  ...nonSlotSmokeInventory.map(({ smokePath }) => smokePath),
  ...slotSmokeInventory.map(([route]) => `/slots/${route}/demo`),
  ...shellOnlySmokeInventory.map(({ smokePath }) => smokePath),
]
