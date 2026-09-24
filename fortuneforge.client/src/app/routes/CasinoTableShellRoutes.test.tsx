import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const casinoRoutes = [
  ['BaccaratRoute.tsx', 'Baccarat'],
  ['CasinoWarRoute.tsx', 'Casino War'],
  ['KenoRoute.tsx', 'Keno'],
  ['VideoPokerRoute.tsx', 'Video Poker'],
  ['RouletteRoute.tsx', 'Roulette'],
  ['CrapsRoute.tsx', 'Craps'],
  ['SicBoRoute.tsx', 'Sic Bo'],
  ['LiarsDiceRoute.tsx', 'Liar’s Dice'],
] as const

describe('casino and table route chrome', () => {
  it.each(casinoRoutes)('%s delegates global navigation to InGameShell', (relativePath, title) => {
    const routeSource = readFileSync(new URL(`./${relativePath}`, import.meta.url), 'utf8')

    expect(routeSource).toContain(`title="${title}" theme="casino"`)
    expect(routeSource).toContain('<InGameShell account={account}')
    expect(routeSource).not.toContain('<PlayerHeader')
    expect(routeSource).not.toContain('backHref=')
  })
})
