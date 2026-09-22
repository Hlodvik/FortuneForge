import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

describe('Other games catalogue', () => {
  it('makes each released table and arcade game discoverable from its appropriate game sections', () => {
    const page = source('./OtherGamesPage.tsx')

    for (const [name, href, tone] of [
      ['Video Poker', '/games/video-poker', 'poker'],
      ['Baccarat', '/games/baccarat', 'baccarat'],
      ['Casino War', '/games/casino-war', 'war'],
      ['Keno', '/games/keno', 'keno'],
      ['Sic Bo', '/games/sic-bo', 'sic-bo'],
      ['Flappy', '/games/flappy', 'flappy'],
      ['Horse Flight', '/games/horse-flight', 'horse'],
    ]) {
      expect(page).toContain(`{ name: '${name}'`)
      expect(page).toContain(`href: '${href}'`)
      expect(page).toContain(`tone: '${tone}'`)
    }

    expect(page).toContain("{ name: 'Roulette'")
    expect(page).toContain("href: '/games/roulette'")
    expect(page).toContain("{ name: 'Craps'")
    expect(page).toContain("href: '/games/craps'")

    expect(page).toContain('const slotGames: readonly CatalogGame[] = SLOT_GAME_CATALOG.map(catalogGameFromSlot)')
    expect(page).toContain('<GameSection games={slotGames} title="Slot machines" />')
    expect(page).toContain("imagePresentation: game.imagePresentation")
    expect(page).not.toContain("icon: '☀'")

    expect(page).toContain('const newGames: readonly CatalogGame[] = [')
    expect(page).toContain('<GameSection games={newGames} title="New games" />')
    expect(page).toContain('<GameSection games={casinoGames} title="Casino games" />')
    expect(page).toContain('<GameSection games={arcadeGames} title="Arcade games" />')
  })
})

function source(relativePath: string): string {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8')
}
