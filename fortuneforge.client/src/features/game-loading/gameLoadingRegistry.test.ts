import { readFileSync, readdirSync, statSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { SLOT_ROUTE_DEFINITIONS } from '../../games/slots/routeRegistry'
import { GAME_LOADING_PRESENTATIONS, resolveGameLoadingEntry } from './gameLoadingRegistry'
import { LOADING_PRESENTATIONS_A } from './loadingPresentationsA'
import { LOADING_PRESENTATIONS_B } from './loadingPresentationsB'
import { LOADING_PRESENTATIONS_C } from './loadingPresentationsC'

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
  title: string
  file: string
  width: number
  height: number
  bytes: number
}

describe('combined game loading registry', () => {
  it('combines the three manifests into forty unique game identities, images, and sixty-two aliases', () => {
    expect(GAME_LOADING_PRESENTATIONS).toEqual([...LOADING_PRESENTATIONS_A, ...LOADING_PRESENTATIONS_B, ...LOADING_PRESENTATIONS_C])
    expect(Object.isFrozen(GAME_LOADING_PRESENTATIONS)).toBe(true)
    expect(GAME_LOADING_PRESENTATIONS).toHaveLength(40)
    expect(new Set(GAME_LOADING_PRESENTATIONS.map(entry => entry.id)).size).toBe(40)
    expect(new Set(GAME_LOADING_PRESENTATIONS.map(entry => entry.image)).size).toBe(40)
    const routes = GAME_LOADING_PRESENTATIONS.flatMap(entry => entry.routes)
    expect(routes).toHaveLength(62)
    expect(new Set(routes).size).toBe(routes.length)
  })

  it('uses every selected metadata export once with actual 768×384 WebPs under a three-megabyte total budget', () => {
    const metadataIds = new Set<string>()
    let totalBytes = 0
    for (const group of ['non-slots', 'slots']) {
      const directory = new URL(`../../assets/catalogue/${group}/`, import.meta.url)
      const files = readdirSync(directory).filter(file => file.endsWith('.artwork.json'))
      expect(files).toHaveLength(20)
      for (const filename of files) {
        const metadata = JSON.parse(readFileSync(new URL(filename, directory), 'utf8')) as ArtworkMetadata
        expect(metadataIds.has(metadata.id)).toBe(false)
        metadataIds.add(metadata.id)
        const entry = GAME_LOADING_PRESENTATIONS.find(entry => entry.id === metadata.id)!
        expect(entry, metadata.id).toBeDefined()
        expect(entry.title).toBe(metadata.title)
        expect(entry.image.split('?')[0].split('/').at(-1)).toBe(metadata.file)
        const asset = new URL(metadata.file, directory)
        expect(statSync(asset).size).toBe(metadata.bytes)
        expect(metadata.bytes).toBeLessThan(180_000)
        expect(webpDimensions(readFileSync(asset))).toEqual({ width: 768, height: 384 })
        expect({ width: metadata.width, height: metadata.height }).toEqual({ width: 768, height: 384 })
        totalBytes += metadata.bytes
      }
    }
    expect(metadataIds.size).toBe(40)
    expect(new Set(GAME_LOADING_PRESENTATIONS.map(entry => entry.id))).toEqual(metadataIds)
    expect(totalBytes).toBeLessThan(3_000_000)
  })

  it('matches all twenty slot canonical/demo aliases to the existing route registry without loading engines', () => {
    expect(SLOT_ROUTE_DEFINITIONS).toHaveLength(20)
    for (const slot of SLOT_ROUTE_DEFINITIONS) {
      const entry = GAME_LOADING_PRESENTATIONS.find(entry => entry.id === slot.id)!
      expect(entry.title).toBe(slot.title)
      expect(entry.routes).toEqual([slot.playPath, slot.demoPath].filter(route => route !== null))
      for (const route of entry.routes) expect(resolveGameLoadingEntry(route)).toBe(entry)
    }
  })

  it('prepares twenty non-slot identities without enabling a route, and matches active route aliases', () => {
    const appRoutes = readFileSync(new URL('../../app/AppRoutes.tsx', import.meta.url), 'utf8')
    expect(Object.keys(nonSlotRoutes)).toHaveLength(20)
    for (const [id, route] of Object.entries(nonSlotRoutes)) {
      const entry = GAME_LOADING_PRESENTATIONS.find(entry => entry.id === id)!
      const expectedAliases = id === 'blackjack' || id === 'texas-holdem' ? [route, `/demo/cards/${id}`] : [route]
      expect(entry.routes).toEqual(expectedAliases)
      for (const alias of expectedAliases) {
        // Mines' production route is a separate integration, not enabled by loading art.
        if (id !== 'mines') expect(appRoutes).toContain(`pathname === '${alias}'`)
        expect(resolveGameLoadingEntry(alias)).toBe(entry)
      }
    }
    expect(resolveGameLoadingEntry('/games/mines')?.id).toBe('mines')
  })

  it('normalizes query strings, fragments, and trailing slashes while retaining entry identity and case', () => {
    for (const entry of GAME_LOADING_PRESENTATIONS) {
      for (const route of entry.routes) {
        expect(resolveGameLoadingEntry(`${route}/`)).toBe(entry)
        expect(resolveGameLoadingEntry(`${route}///?from=catalogue#start`)).toBe(entry)
        expect(resolveGameLoadingEntry(`${route}#artwork?unused=value`)).toBe(entry)
        expect(resolveGameLoadingEntry(route.toUpperCase())).toBeUndefined()
      }
    }
  })

  it('returns undefined for unknown, prototype-property, invented, and unlisted arcade routes', () => {
    for (const route of ['', '/', '?from=home', '#start', 'toString', '__proto__', 'constructor', 'hasOwnProperty', '/unknown', '/slots/not-listed/demo', '/games/mines/demo', '/demo/cards/hearts', '/games/blackjack', '/slots/wukong-journey-to-the-west', '/slots/wukong/demo/demo', 'https://example.test/games/mines']) {
      expect(resolveGameLoadingEntry(route), route).toBeUndefined()
    }
    for (const id of ['horse-flight', 'asteroids', 'flappy', 'snake']) {
      expect(GAME_LOADING_PRESENTATIONS.some(entry => entry.id === id)).toBe(false)
      expect(resolveGameLoadingEntry(`/games/${id}`)).toBeUndefined()
    }
  })

  it('imports only presentation manifests and image URLs at runtime, with no browser reads or game packages', () => {
    const source = readFileSync(new URL('./gameLoadingRegistry.ts', import.meta.url), 'utf8')
    const imports = [...source.matchAll(/^import (?!type\b).+ from '([^']+)'/gm)].map(match => match[1])
    expect(imports).toEqual(['./loadingPresentationsA', './loadingPresentationsB', './loadingPresentationsC'])
    expect(source).not.toMatch(/\bimport\s*\(|@fortuneforge|\bfetch\s*\(|\b(?:window|document)\s*\.|toLowerCase/)
    for (const manifest of ['loadingPresentationsA.ts', 'loadingPresentationsB.ts', 'loadingPresentationsC.ts']) {
      const manifestSource = readFileSync(new URL(`./${manifest}`, import.meta.url), 'utf8')
      const manifestImports = [...manifestSource.matchAll(/^import (?!type\b).+ from ['"]([^'"]+)['"]/gm)].map(match => match[1])
      expect(manifestImports.every(path => /^\.\.\/\.\.\/assets\/catalogue\/(?:non-slots|slots)\/[^/]+\.webp$/.test(path))).toBe(true)
      expect(manifestSource).not.toMatch(/\bimport\s*\(|@fortuneforge|\bfetch\s*\(/)
    }
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
