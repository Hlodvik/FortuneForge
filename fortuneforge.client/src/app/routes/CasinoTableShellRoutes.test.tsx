import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const casinoRoutes = [
  ['BaccaratRoute.tsx', 'Baccarat'],
  ['DragonTigerRoute.tsx', 'Dragon Tiger'],
  ['HiLoRoute.tsx', 'Hi-Lo'],
  ['CasinoWarRoute.tsx', 'Casino War'],
  ['KenoRoute.tsx', 'Keno'],
  ['VideoPokerRoute.tsx', 'Video Poker'],
  ['RouletteRoute.tsx', 'Roulette'],
  ['CrapsRoute.tsx', 'Craps'],
  ['SicBoRoute.tsx', 'Sic Bo'],
  ['LiarsDiceRoute.tsx', 'Liar’s Dice'],
] as const

describe('casino and table route chrome', () => {
  it('keeps Dragon Tiger black and viewport-bounded without restyling other games', () => {
    const styles = readFileSync(new URL('./DragonTigerRoute.css', import.meta.url), 'utf8')
    expect(styles).toMatch(/\.dragon-tiger-page > \.in-game-navbar\s*\{[^}]*background: #050505/s)
    expect(styles).toMatch(/\.dragon-tiger-page > \.in-game-shell__body\s*\{[^}]*overflow: hidden/s)
    expect(styles).not.toMatch(/(?:^|\n)\s*(?:body|:root|\.in-game-navbar)\s*\{/)
  })

  it.each(casinoRoutes)('%s delegates global navigation to InGameShell', (relativePath, title) => {
    const routeSource = readFileSync(new URL(`./${relativePath}`, import.meta.url), 'utf8')

    expect(routeSource).toContain(`title="${title}" theme="casino"`)
    expect(routeSource).toContain('<InGameShell account={account}')
    expect(routeSource).not.toContain('<PlayerHeader')
    expect(routeSource).not.toContain('backHref=')
  })
})
