import { readFileSync, statSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { SLOT_ROUTE_DEFINITIONS } from '../../games/slots/routeRegistry'
import { GameLoadingScreen } from './GameLoadingScreen'
import { LOADING_PRESENTATIONS_B } from './loadingPresentationsB'

const assignedIds = [
  'dragon-tiger', 'andar-bahar', 'hi-lo', 'casino-war', 'video-poker', 'keno', 'sic-bo',
  'arcane-archives', 'cosmic-fortune', 'dino-dominion', 'neon-nights',
  'jungle-jackpot', 'ocean-odyssey', 'samurai-fortune',
] as const

const nonSlotComponents: Readonly<Record<string, string>> = {
  'dragon-tiger': 'AuthenticatedDragonTigerRoute',
  'andar-bahar': 'AuthenticatedAndarBaharRoute',
  'hi-lo': 'AuthenticatedHiLoRoute',
  'casino-war': 'AuthenticatedCasinoWarRoute',
  'video-poker': 'AuthenticatedVideoPokerRoute',
  keno: 'AuthenticatedKenoRoute',
  'sic-bo': 'AuthenticatedSicBoRoute',
}

describe('B game loading presentations', () => {
  it('covers its fourteen assigned games and twenty-one unique existing paths', () => {
    expect(LOADING_PRESENTATIONS_B.map((entry) => entry.id)).toEqual(assignedIds)
    const routes = LOADING_PRESENTATIONS_B.flatMap((entry) => entry.routes)
    expect(routes).toHaveLength(21)
    expect(new Set(routes).size).toBe(routes.length)

    const appRoutes = readFileSync(new URL('../../app/AppRoutes.tsx', import.meta.url), 'utf8')
    const appMappings = [...appRoutes.matchAll(/pathname === '([^']+)'\) route = <(\w+)/g)]
    for (const entry of LOADING_PRESENTATIONS_B) {
      const slot = SLOT_ROUTE_DEFINITIONS.find((route) => route.id === entry.id)
      if (slot) {
        expect(entry.title).toBe(slot.title)
        expect(entry.routes).toEqual([slot.playPath, slot.demoPath].filter((route) => route !== null))
      } else {
        const paths = appMappings
          .filter((match) => match[2] === nonSlotComponents[entry.id])
          .map((match) => match[1])
        expect(paths).toHaveLength(1)
        expect(entry.routes).toEqual(paths)
      }
    }
  })

  for (const entry of LOADING_PRESENTATIONS_B) {
    it(`${entry.id} uses its selected title and a small valid WebP`, () => {
      const folder = entry.routes[0].startsWith('/slots/') ? 'slots' : 'non-slots'
      const metadata = JSON.parse(readFileSync(
        new URL(`../../assets/catalogue/${folder}/${entry.id}.artwork.json`, import.meta.url), 'utf8',
      )) as { id: string; title: string; file: string; width: number; height: number; bytes: number }
      expect(entry.id).toBe(metadata.id)
      expect(entry.title).toBe(metadata.title)
      expect(entry.image.split('?')[0]).toMatch(new RegExp(`/${metadata.file.replaceAll('.', '\\.')}$`))

      const asset = new URL(`../../assets/catalogue/${folder}/${metadata.file}`, import.meta.url)
      const bytes = readFileSync(asset)
      expect(statSync(asset).size).toBe(metadata.bytes)
      expect(metadata.bytes).toBeLessThan(180 * 1024)
      expect(webpSize(bytes)).toEqual({ width: 768, height: 384 })
      expect([metadata.width, metadata.height]).toEqual([768, 384])
    })

    it(`${entry.id} supplies a dark background and contrasting accent`, () => {
      expect(entry.background).toMatch(/^#[\da-f]{6}$/i)
      expect(entry.accent).toMatch(/^#[\da-f]{6}$/i)
      const background = luminance(entry.background)
      const accent = luminance(entry.accent)
      expect(background).toBeLessThan(0.04)
      expect((accent + 0.05) / (background + 0.05)).toBeGreaterThanOrEqual(4.5)
    })

    it(`${entry.id} renders its identity as an accessible pending screen`, () => {
      const html = renderToStaticMarkup(createElement(GameLoadingScreen, { entry }))
      expect(html).toContain(`data-game-loading="${entry.id}"`)
      expect(html).toContain(`>${entry.title}</h1>`)
      expect(html).toContain(`Loading ${entry.title}…`)
      expect(html).toContain(`src="${entry.image}"`)
      expect(html).toContain('--game-loading-background:' + entry.background)
      expect(html).toContain('--game-loading-accent:' + entry.accent)
      expect(html.match(/role="status"/g)).toHaveLength(1)
      expect(html).not.toMatch(/<(?:button|a|input|audio|video|progress)\b|aria-valuenow|role="progressbar"/)
    })
  }

  it('imports only selected artwork at runtime, keeping game packages lazy', () => {
    const source = readFileSync(new URL('./loadingPresentationsB.ts', import.meta.url), 'utf8')
    const runtimeImports = [...source.matchAll(/^import (?!type\b).+ from '([^']+)'/gm)]
      .map((match) => match[1])
    expect(runtimeImports).toHaveLength(14)
    expect(runtimeImports.every((path) => /^\.\.\/\.\.\/assets\/catalogue\/(?:non-slots|slots)\/.+\.webp$/.test(path))).toBe(true)
    expect(source).toContain("import type { GameLoadingEntry } from './types'")
    expect(source).not.toMatch(/\bimport\s*\(/)
    expect(source).not.toMatch(/@fortuneforge|\/games\/.+\/(?:manifest|definition)/)
  })
})

function luminance(hex: string): number {
  const channels = [1, 3, 5].map((offset) => parseInt(hex.slice(offset, offset + 2), 16) / 255)
    .map((value) => value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4)
  return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722
}

function webpSize(bytes: Buffer): { width: number; height: number } {
  expect(bytes.toString('ascii', 0, 4)).toBe('RIFF')
  expect(bytes.toString('ascii', 8, 12)).toBe('WEBP')
  switch (bytes.toString('ascii', 12, 16)) {
    case 'VP8X': return { width: bytes.readUIntLE(24, 3) + 1, height: bytes.readUIntLE(27, 3) + 1 }
    case 'VP8 ': return { width: bytes.readUInt16LE(26) & 0x3fff, height: bytes.readUInt16LE(28) & 0x3fff }
    case 'VP8L': return {
      width: 1 + (bytes[21] | ((bytes[22] & 0x3f) << 8)),
      height: 1 + ((bytes[22] >> 6) | (bytes[23] << 2) | ((bytes[24] & 0x0f) << 10)),
    }
    default: throw new Error('Unsupported WebP header')
  }
}
