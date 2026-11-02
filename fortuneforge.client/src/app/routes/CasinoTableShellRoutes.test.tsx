import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const casinoRoutes = [
  ['BaccaratRoute.tsx', 'Baccarat'],
  ['DragonTigerRoute.tsx', 'Dragon Tiger'],
  ['HiLoRoute.tsx', 'Hi-Lo'],
  ['AndarBaharRoute.tsx', 'Andar Bahar'],
  ['CasinoWarRoute.tsx', 'Casino War'],
  ['KenoRoute.tsx', 'Keno'],
  ['VideoPokerRoute.tsx', 'Video Poker'],
  ['RouletteRoute.tsx', 'Roulette'],
  ['CrapsRoute.tsx', 'Craps'],
  ['SicBoRoute.tsx', 'Sic Bo'],
  ['LiarsDiceRoute.tsx', 'Liar’s Dice'],
] as const

describe('casino and table route chrome', () => {
  it('lets short Andar Bahar shared-shell viewports scroll vertically instead of clipping controls', () => {
    const styles = readFileSync(new URL('./AndarBaharRoute.css', import.meta.url), 'utf8')
    expect(styles).not.toMatch(/\.(?:site-navbar|in-game-navbar)\b/)
    expect(styles).toContain('overflow-x: hidden !important')
    expect(styles).toContain('overflow-y: auto !important')
    expect(styles).toContain('min-height: min-content')
    expect(styles).toContain('grid-template-rows: auto minmax(min-content, 1fr)')
  })
  it('keeps the Dragon Tiger body black and viewport-bounded with shared navbar styling', () => {
    const styles = readFileSync(new URL('./DragonTigerRoute.css', import.meta.url), 'utf8')
    expect(styles).toMatch(/\.dragon-tiger-page\s*\{[^}]*background: #050505/s)
    expect(styles).not.toMatch(/\.(?:site-navbar|in-game-navbar)\b/)
    expect(styles).toMatch(/\.dragon-tiger-page > \.in-game-shell__body\s*\{[^}]*overflow: hidden/s)
    expect(styles).not.toMatch(/(?:^|\n)\s*(?:body|:root|\.in-game-navbar)\s*\{/)
  })

  it.each(casinoRoutes)('%s delegates global navigation to InGameShell', (relativePath, title) => {
    const routeSource = readFileSync(new URL(`./${relativePath}`, import.meta.url), 'utf8')

    expect(routeSource).toContain('<InGameShell account={account}')
    const shellProps = routeSource.match(/<InGameShell\b[^>]*>/)?.[0]
    expect(shellProps).toBeDefined()
    expect(shellProps).toMatch(new RegExp(`(?:title|reportTitle)="${title}"`))
    expect(shellProps).not.toMatch(/\b(?:theme|actions)=/)
    expect(routeSource).not.toContain('<PlayerHeader')
    expect(routeSource).not.toContain('<SiteNavbar')
    expect(routeSource).not.toContain('<InGameNavbar')
    expect(routeSource).not.toContain('backHref=')
  })
})
