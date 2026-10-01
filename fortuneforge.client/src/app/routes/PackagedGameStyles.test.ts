import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const packagedGameEntryPoints = [
  ['../../pages/games/asteroids/AsteroidsCompetitionPage.tsx', '@fortuneforge/games-asteroids'],
  ['./BaccaratRoute.tsx', '@fortuneforge/games-baccarat'],
  ['./CasinoWarRoute.tsx', '@fortuneforge/games-casino-war'],
  ['./CrapsRoute.tsx', '@fortuneforge/games-craps'],
  ['./DropMergeRoute.tsx', '@fortuneforge/games-drop-merge'],
  ['./HeartsRoute.tsx', '@fortuneforge/games-hearts'],
  ['./HorseFlightRoute.tsx', '@fortuneforge/games-horse-flight'],
  ['./KenoRoute.tsx', '@fortuneforge/games-keno'],
  ['./LiarsDiceRoute.tsx', '@fortuneforge/games-liars-dice'],
  ['./RouletteRoute.tsx', '@fortuneforge/games-roulette'],
  ['./SicBoRoute.tsx', '@fortuneforge/games-sic-bo'],
  ['./SnakeRoute.tsx', '@fortuneforge/games-snake'],
  ['./TwentyFortyEightRoute.tsx', '@fortuneforge/games-2048'],
  ['./VideoPokerRoute.tsx', '@fortuneforge/games-video-poker'],
] as const

describe('packaged game styles', () => {
  it('the host-owned recorded Flappy view loads its scoped styles without the standalone sample globals', () => {
    const source = readFileSync(new URL('../../pages/games/flappy/FlappyFreeRunPage.tsx', import.meta.url), 'utf8')
    expect(source).toContain("import './FlappyFreeRunPage.css'")
    expect(source).not.toContain("import '@fortuneforge/games-flappy/styles.css'")
    const styles = readFileSync(new URL('../../pages/games/flappy/FlappyFreeRunPage.css', import.meta.url), 'utf8')
    expect(styles).not.toMatch(/(?:^|\n)\s*(?:body\b|:root\b|\*\s*\{|button\s*\{|a\s*\{)/)
  })
  it.each(packagedGameEntryPoints)('%s imports the package stylesheet', (relativePath, packageName) => {
    const source = readFileSync(new URL(relativePath, import.meta.url), 'utf8')

    expect(source).toContain(`import '${packageName}/styles.css'`)
  })
})
