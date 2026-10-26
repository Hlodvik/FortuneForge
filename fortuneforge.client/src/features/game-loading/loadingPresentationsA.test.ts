import { readFileSync, statSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { SLOT_ROUTE_DEFINITIONS } from '../../games/slots/routeRegistry'
import { LOADING_PRESENTATIONS_A } from './loadingPresentationsA'

const nonSlots = [
  ['blackjack', 'Fortune Blackjack', ['/cards/blackjack', '/demo/cards/blackjack']],
  ['texas-holdem', 'Texas Hold’em', ['/cards/texas-holdem', '/demo/cards/texas-holdem']],
  ['three-card-poker', 'Three Card Poker', ['/cards/three-card-poker']],
  ['house-holdem', 'House Hold’em', ['/cards/house-holdem']],
  ['baccarat', 'Baccarat', ['/games/baccarat']],
  ['roulette', 'Roulette', ['/games/roulette']],
  ['craps', 'Craps', ['/games/craps']],
] as const
const slotIds = [
  'wukong-journey-to-the-west', 'rainbow-realm', 'pirates-fortune', 'gods-of-olympus',
  'reel-riches', 'high-noon-fortune', 'royal-draw',
] as const

describe('A game loading presentations', () => {
  it('covers exactly the fourteen assigned games with unique existing play/demo aliases', () => {
    const expectedIds = [...nonSlots.map(([id]) => id), ...slotIds]
    expect(LOADING_PRESENTATIONS_A.map(({ id }) => id)).toEqual(expectedIds)
    expect(new Set(LOADING_PRESENTATIONS_A.map(({ id }) => id)).size).toBe(14)
    const routes = LOADING_PRESENTATIONS_A.flatMap(({ routes }) => routes)
    expect(routes).toHaveLength(23)
    expect(new Set(routes).size).toBe(routes.length)

    const appSource = readFileSync(new URL('../../app/AppRoutes.tsx', import.meta.url), 'utf8')
    for (const [id, title, routes] of nonSlots) {
      const entry = LOADING_PRESENTATIONS_A.find((entry) => entry.id === id)!
      expect(entry.title).toBe(title)
      expect([...entry.routes].sort()).toEqual([...routes].sort())
      for (const route of routes) expect(appSource).toContain(`pathname === '${route}'`)
    }
    for (const id of slotIds) {
      const slot = SLOT_ROUTE_DEFINITIONS.find((slot) => slot.id === id)!
      const entry = LOADING_PRESENTATIONS_A.find((entry) => entry.id === id)!
      expect(entry.title).toBe(slot.title)
      expect(entry.routes).toEqual([slot.playPath, slot.demoPath].filter((route) => route !== null))
    }
  })

  for (const id of [...nonSlots.map(([id]) => id), ...slotIds]) {
    it(`${id} uses the selected exact title and compact 2:1 artwork`, () => {
      const entry = LOADING_PRESENTATIONS_A.find((entry) => entry.id === id)!
      const folder = slotIds.some((slotId) => slotId === id) ? 'slots' : 'non-slots'
      const metadata = JSON.parse(readFileSync(new URL(
        `../../assets/catalogue/${folder}/${id}.artwork.json`, import.meta.url,
      ), 'utf8')) as { id: string; title: string; file: string; width: number; height: number; bytes: number }
      expect(entry.title).toBe(metadata.title)
      expect(entry.id).toBe(metadata.id)
      expect(entry.image.split('?')[0].split('/').at(-1)).toBe(metadata.file)
      const asset = new URL(`../../assets/catalogue/${folder}/${metadata.file}`, import.meta.url)
      const data = readFileSync(asset)
      expect(statSync(asset).size).toBe(metadata.bytes)
      expect(data.length).toBeLessThan(180 * 1024)
      expect(webpDimensions(data)).toEqual([768, 384])
      expect([metadata.width, metadata.height]).toEqual([768, 384])
      expect(entry.background).toMatch(/^#[\da-f]{6}$/i)
      expect(entry.accent).toMatch(/^#[\da-f]{6}$/i)
      const background = luminance(entry.background)
      expect(background).toBeLessThan(0.04)
      expect((luminance(entry.accent) + 0.05) / (background + 0.05)).toBeGreaterThanOrEqual(4.5)
    })
  }

  it('keeps the component and manifest import graph independent of game packages and application services', () => {
    const manifest = readFileSync(new URL('./loadingPresentationsA.ts', import.meta.url), 'utf8')
    const runtimeImports = [...manifest.matchAll(/^import (?!type\b).+ from ['"]([^'"]+)['"]/gm)].map((match) => match[1])
    expect(runtimeImports).toHaveLength(14)
    expect(runtimeImports.every((path) => /^\.\.\/\.\.\/assets\/catalogue\/(non-slots|slots)\/[^/]+\.webp$/.test(path))).toBe(true)
    expect(manifest).not.toMatch(/\bimport\s*\(|@fortuneforge|fetch\s*\(/)
    const component = readFileSync(new URL('./GameLoadingScreen.tsx', import.meta.url), 'utf8')
    const componentImports = [...component.matchAll(/^import (?!type\b).+ from ['"]([^'"]+)['"]/gm)].map((match) => match[1])
    expect(componentImports).toEqual(['react'])
    expect(component).not.toMatch(/\bimport\s*\(|@fortuneforge|fetch\s*\(|setTimeout|setInterval|requestAnimationFrame|window\.location/)
  })
})

function luminance(hex: string): number {
  const values = [1, 3, 5].map((offset) => parseInt(hex.slice(offset, offset + 2), 16) / 255)
    .map((channel) => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4)
  return values[0] * 0.2126 + values[1] * 0.7152 + values[2] * 0.0722
}

function webpDimensions(data: Buffer): readonly [number, number] {
  expect(data.toString('ascii', 0, 4)).toBe('RIFF')
  expect(data.toString('ascii', 8, 12)).toBe('WEBP')
  switch (data.toString('ascii', 12, 16)) {
    case 'VP8X': return [data.readUIntLE(24, 3) + 1, data.readUIntLE(27, 3) + 1]
    case 'VP8 ': return [data.readUInt16LE(26) & 0x3fff, data.readUInt16LE(28) & 0x3fff]
    case 'VP8L': return [
      1 + (data[21] | ((data[22] & 0x3f) << 8)),
      1 + ((data[22] >> 6) | (data[23] << 2) | ((data[24] & 0x0f) << 10)),
    ]
    default: throw new Error('Unsupported WebP header')
  }
}
