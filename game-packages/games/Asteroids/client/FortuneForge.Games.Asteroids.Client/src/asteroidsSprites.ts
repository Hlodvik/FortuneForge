import asteroidAtlasSource from './assets/sprites/asteroid-atlas-v3.png?no-inline'
import asteroidExplosionAtlasSource from './assets/sprites/asteroid-explosion-atlas-v1.png?no-inline'
import laserImpactAtlasSource from './assets/sprites/laser-impact-atlas-v1-0.5x.webp?no-inline'
import powerUpAtlasSource from './assets/sprites/powerup-atlas-v1.png?no-inline'
import shipAtlasSource from './assets/sprites/ship-atlas-v1-0.5x.webp?no-inline'
import type { AsteroidsSpriteAtlases } from './asteroidsCanvasRenderer'

const atlasSources = {
  ship: shipAtlasSource,
  asteroid: asteroidAtlasSource,
  laserImpact: laserImpactAtlasSource,
  explosion: asteroidExplosionAtlasSource,
  powerUp: powerUpAtlasSource,
} as const

type AtlasName = keyof typeof atlasSources
type MutableAsteroidsSpriteAtlases = { -readonly [name in AtlasName]?: HTMLImageElement }

export async function loadAsteroidsSpriteAtlases(): Promise<AsteroidsSpriteAtlases> {
  const loaded: MutableAsteroidsSpriteAtlases = {}
  await Promise.all((Object.entries(atlasSources) as [AtlasName, string][]).map(async ([name, source]) => {
    try { loaded[name] = await loadImage(source) } catch { /* The canvas renderer keeps its primitive fallback for a failed atlas. */ }
  }))
  return loaded
}

function loadImage(source: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image()
    image.decoding = 'async'
    image.addEventListener('load', () => resolve(image), { once: true })
    image.addEventListener('error', () => reject(new Error(`Unable to load sprite atlas: ${source}`)), { once: true })
    image.src = source
  })
}
