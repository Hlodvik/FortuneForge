import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const packagedGameEntryPoints = [
  ['../../pages/games/asteroids/AsteroidsCompetitionPage.tsx', '@fortuneforge/games-asteroids'],
  ['./BaccaratRoute.tsx', '@fortuneforge/games-baccarat'],
  ['./DragonTigerRoute.tsx', '@fortuneforge/games-dragon-tiger'],
  ['./HiLoRoute.tsx', '@fortuneforge/games-hi-lo'],
  ['./AndarBaharRoute.tsx', '@fortuneforge/games-andar-bahar'],
  ['./MinesRoute.tsx', '@fortuneforge/games-mines'],
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
  it('keeps the integrated Mines page slate instead of inheriting the host cloud artwork', () => {
    const styles = readFileSync(new URL('./MinesRoute.css', import.meta.url), 'utf8')
    expect(styles).toContain('.mines-page {')
    expect(styles).toContain('background: #0d1b26')
    expect(styles).not.toContain('felt')
  })
  it('the host-owned recorded Flappy view loads its scoped styles without the standalone sample globals', () => {
    const source = readFileSync(new URL('../../pages/games/flappy/FlappyFreeRunPage.tsx', import.meta.url), 'utf8')
    expect(source).toContain("import './FlappyFreeRunPage.css'")
    expect(source).not.toContain("import '@fortuneforge/games-flappy/styles.css'")
    const styles = readFileSync(new URL('../../pages/games/flappy/FlappyFreeRunPage.css', import.meta.url), 'utf8')
    expect(styles).not.toMatch(/(?:^|\n)\s*(?:body\b|:root\b|\*\s*\{|button\s*\{|a\s*\{)/)
  })
  it('loads Hold’em styles in bundled and published mode', () => {
    const route = readFileSync(new URL('./TexasHoldemRoute.tsx', import.meta.url), 'utf8')
    const page = readFileSync(new URL('../../pages/cards/texasHoldem/CreditTexasHoldemPage.tsx', import.meta.url), 'utf8')
    const entry = readFileSync(new URL('../../../../games/TexasHoldem/client/FortuneForge.Games.TexasHoldem.Client/src/browser.ts', import.meta.url), 'utf8')
    expect(route).toContain("import bundledStyles from '@fortuneforge/games-texas-holdem/styles.css?inline'")
    expect(route).toMatch(/useReleasedGame\([^\n]+bundledStyles/)
    expect(page).toContain('useBundledGameStyles(views ? undefined : bundledStyles)')
    expect(entry).toContain("import './holdem.css'")
  })
  it.each(packagedGameEntryPoints)('%s loads its package stylesheet', (relativePath, packageName) => {
    const source = readFileSync(new URL(relativePath, import.meta.url), 'utf8')

    if (!source.includes('useReleasedGame(')) {
      expect(source).toContain(`import '${packageName}/styles.css'`)
      return
    }

    expect(source).toContain(`import bundledStyles from '${packageName}/styles.css?inline'`)
    expect(source).toMatch(/useReleasedGame\([^\n]+bundledStyles/)
    const game = relativePath.slice(2, -'Route.tsx'.length)
    const client = new URL(`../../../../games/${game}/client/FortuneForge.Games.${game}.Client/src/`, import.meta.url)
    expect(readFileSync(new URL('browser.ts', client), 'utf8')).toContain(`export { ${game}Game as default } from './index.js'`)
    const packageSource = ['index.ts', `${game}Game.tsx`].map(path => readFileSync(new URL(path, client), 'utf8')).join('\n')
    expect(packageSource).toMatch(/^import '\.\/[^']+\.css'/m)
  })
})
