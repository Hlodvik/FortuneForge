import { readFileSync, readdirSync, statSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { SLOT_ROUTE_DEFINITIONS } from '../../games/slots/routeRegistry'
import { getCataloguePresentationArt } from './cataloguePresentationArt'

const nonSlotRoutes: Readonly<Record<string, string>> = {
  blackjack: '/cards/blackjack',
  'texas-holdem': '/cards/texas-holdem',
  'three-card-poker': '/cards/three-card-poker',
  'house-holdem': '/cards/house-holdem',
  baccarat: '/games/baccarat',
  roulette: '/games/roulette',
  craps: '/games/craps',
  'dragon-tiger': '/games/dragon-tiger',
  'andar-bahar': '/games/andar-bahar',
  'hi-lo': '/games/hi-lo',
  'casino-war': '/games/casino-war',
  'video-poker': '/games/video-poker',
  keno: '/games/keno',
  'sic-bo': '/games/sic-bo',
  mines: '/games/mines',
  solitaire: '/cards/solitaire',
  hearts: '/cards/hearts',
  'liars-dice': '/games/liars-dice',
  '2048': '/games/2048',
  'drop-merge': '/games/drop-merge',
}

type ArtworkMetadata = {
  id: string
  file: string
  width: number
  height: number
  bytes: number
}

const selectedMetadata = ['non-slots', 'slots'].flatMap(group => {
  const directory = new URL(`../../assets/catalogue/${group}/`, import.meta.url)
  return readdirSync(directory).filter(file => file.endsWith('.artwork.json')).map(file => ({
    directory,
    metadata: JSON.parse(readFileSync(new URL(file, directory), 'utf8')) as ArtworkMetadata,
  }))
})

describe('landing and home catalogue presentation art', () => {
  it('selects exactly the forty unique metadata-approved exports with a bounded image budget', () => {
    expect(selectedMetadata).toHaveLength(40)
    expect(new Set(selectedMetadata.map(({ metadata }) => metadata.id)).size).toBe(40)
    const slotRoutes = new Map(SLOT_ROUTE_DEFINITIONS.map(definition => [definition.id, definition.playPath]))
    const selectedSources = new Set<string>()
    let totalBytes = 0

    for (const { directory, metadata } of selectedMetadata) {
      const route = nonSlotRoutes[metadata.id] ?? slotRoutes.get(metadata.id)
      expect(route, metadata.id).toBeTruthy()
      const art = getCataloguePresentationArt(route)!
      expect(art, metadata.id).toBeDefined()
      expect(art.src).toContain(`/${metadata.file}`)
      expect(art).toMatchObject({ width: 768, height: 384 })
      expect(Object.isFrozen(art)).toBe(true)
      expect({ width: metadata.width, height: metadata.height }).toEqual({ width: 768, height: 384 })

      const asset = new URL(metadata.file, directory)
      expect(statSync(asset).size).toBe(metadata.bytes)
      expect(metadata.bytes).toBeLessThan(180_000)
      expect(webpDimensions(readFileSync(asset))).toEqual({ width: 768, height: 384 })
      selectedSources.add(art.src)
      totalBytes += metadata.bytes
    }

    expect(selectedSources.size).toBe(40)
    expect(totalBytes).toBeLessThan(3_000_000)
  })

  it('resolves every slot play/demo alias from the actual route registry to the same art', () => {
    expect(SLOT_ROUTE_DEFINITIONS).toHaveLength(20)
    for (const definition of SLOT_ROUTE_DEFINITIONS) {
      const art = getCataloguePresentationArt(definition.playPath)
      expect(art, definition.id).toBeDefined()
      expect(getCataloguePresentationArt(definition.demoPath)).toBe(art)
      expect(getCataloguePresentationArt(`${definition.demoPath}/?source=home#preview`)).toBe(art)
    }
  })

  it('covers the twenty non-slot play routes and only the two real internal card demo aliases', () => {
    const appRoutes = readFileSync(new URL('../../app/AppRoutes.tsx', import.meta.url), 'utf8')
    expect(Object.keys(nonSlotRoutes)).toHaveLength(20)
    for (const route of Object.values(nonSlotRoutes)) {
      // Mines art can be prepared before its separately reviewed host integration ships.
      // Presentation metadata must not force an unrelated game release.
      if (route !== '/games/mines') expect(appRoutes).toContain(`pathname === '${route}'`)
      expect(getCataloguePresentationArt(route)).toBeDefined()
      expect(getCataloguePresentationArt(`${route}/?source=home#play`)).toBe(getCataloguePresentationArt(route))
    }
    for (const id of ['blackjack', 'texas-holdem']) {
      expect(appRoutes).toContain(`pathname === '/demo/cards/${id}'`)
      expect(getCataloguePresentationArt(`/demo/cards/${id}`)).toBe(getCataloguePresentationArt(`/cards/${id}`))
    }
    expect(getCataloguePresentationArt('/demo/cards/hearts')).toBeUndefined()
    expect(getCataloguePresentationArt('/games/mines/demo')).toBeUndefined()
  })

  it('keeps unknown routes and excluded arcade games safe from object-property lookups', () => {
    for (const route of [undefined, null, '', '/', 'toString', '__proto__', 'constructor', '/games/not-listed', '/slots/not-listed/demo', '/games/horse-flight', '/games/asteroids', '/games/flappy', '/games/snake']) {
      expect(getCataloguePresentationArt(route)).toBeUndefined()
    }
  })

  it('imports only forty selected image URLs, with no playable game code or archived integration', () => {
    const source = readFileSync(new URL('./cataloguePresentationArt.ts', import.meta.url), 'utf8')
    const imports = [...source.matchAll(/^import .+ from '([^']+)'$/gm)].map(match => match[1])
    expect(imports).toHaveLength(40)
    expect(new Set(imports).size).toBe(40)
    for (const path of imports) expect(path).toMatch(/^\.\.\/\.\.\/assets\/catalogue\/(?:non-slots|slots)\/[a-z0-9-]+\.webp$/)
    expect(source).not.toContain('import(')
    expect(source).not.toContain('nonSlotCatalogueArt')
  })
})

function webpDimensions(bytes: Buffer): { width: number; height: number } {
  expect(bytes.toString('ascii', 0, 4)).toBe('RIFF')
  expect(bytes.toString('ascii', 8, 12)).toBe('WEBP')
  const format = bytes.toString('ascii', 12, 16)
  if (format === 'VP8X') return { width: 1 + bytes.readUIntLE(24, 3), height: 1 + bytes.readUIntLE(27, 3) }
  if (format === 'VP8L') {
    const packed = bytes.readUInt32LE(21)
    return { width: 1 + (packed & 0x3fff), height: 1 + ((packed >>> 14) & 0x3fff) }
  }
  expect(format).toBe('VP8 ')
  return { width: bytes.readUInt16LE(26) & 0x3fff, height: bytes.readUInt16LE(28) & 0x3fff }
}
