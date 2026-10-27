/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const gameSource = (game: string, file: string) => readFileSync(new URL(
  `../../../../games/${game}/client/FortuneForge.Games.${game}.Client/src/${file}`, import.meta.url,
), 'utf8')

function rule(source: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const matches = [...source.matchAll(new RegExp(`(?:^|\\n)${escaped}\\s*\\{([^}]+)\\}`, 'g'))]
  return matches.at(-1)?.[1] ?? ''
}

const sharedImport = "@import '../../../../shared/visuals/casino-felt.css';"

describe('shared emerald casino felt materials', () => {
  it.each([
    ['Baccarat', 'baccaratTable.css'], ['HiLo', 'hiloCasino.css'], ['Roulette', 'roulette.css'],
  ])('%s imports the common material before its scoped game styles', (game, file) => {
    expect(gameSource(game, file).trimStart().startsWith(sharedImport)).toBe(true)
  })

  it('replaces only Baccarat’s rigid felt layer while retaining its printed arcs and textured leather rail', () => {
    const styles = gameSource('Baccarat', 'baccaratTable.css')
    const felt = rule(styles, '.ff-baccarat__play-surface::before')
    expect(felt).toContain('background: var(--ff-casino-felt-material)')
    expect(felt).toContain('filter: none')
    expect(felt).not.toMatch(/blur|radial-gradient/)
    expect(felt).toContain('width: max(100cqw, 157.377cqh)')
    expect(felt).toContain('height: max(100cqh, 63.542cqw)')
    expect(felt).toContain('transform: translate(-50%, -50%)')
    expect(styles).not.toContain('baccarat-felt-generated-v3.png')
    const board = gameSource('Baccarat', 'BaccaratGame.tsx')
    expect(board).toContain('className="ff-baccarat__felt-lines" viewBox="0 0 1440 915"')
    expect(board).toContain('M 0 330 Q 720 660 1440 330')
    expect(board).toContain('fill="url(#baccarat-black-leather)"')
  })

  it('uses the same untinted material on Hi-Lo without introducing another grain overlay', () => {
    const styles = gameSource('HiLo', 'hiloCasino.css')
    const felt = rule(styles, '.hilo-table')
    expect(felt).toContain('background:var(--ff-casino-felt-material)')
    expect(felt).not.toMatch(/radial-gradient|filter:/)
    expect(rule(styles, '.hilo-table::before')).toContain('content:none')
    expect(styles).not.toContain('classic-green-felt.webp')
  })

  it('uses the material on Roulette’s desktop felt and both mobile/short-view playing surfaces', () => {
    const styles = gameSource('Roulette', 'roulette.css')
    const viewport = gameSource('Roulette', 'rouletteViewport.css')
    expect(rule(styles, '.ff-roulette-felt')).toContain('background:var(--ff-casino-felt-material)')
    expect(rule(styles, '.ff-roulette-felt')).not.toContain('#043f286b')
    expect(rule(styles, '.ff-roulette-table::before')).toContain('content:none')
    expect(rule(styles, '.ff-roulette-felt::before')).toContain('content:none')
    expect(styles).not.toContain('feTurbulence')
    expect(viewport.match(/background:var\(--ff-casino-felt-material\)/g)).toHaveLength(2)
    expect(viewport).not.toMatch(/#288964|#2a8c62/)
    expect(rule(styles, '.ff-roulette-felt::after')).toContain('box-shadow:inset 0 0 0 2px #0007')
  })

  it('reveals Roulette felt under green bets without changing colored pockets or the selected-state overlay', () => {
    const styles = gameSource('Roulette', 'roulette.css')
    expect(rule(styles, '.ff-roulette-board button')).toContain('background:transparent')
    expect(rule(styles, '.ff-roulette-board .number.zero')).toContain('background:transparent')
    expect(styles).toContain('.ff-roulette-board .outside-grid button { background:transparent; }')
    expect(styles).toContain('.ff-roulette-board .outside-grid .black { background:#151d17; }')
    expect(styles).toContain('linear-gradient(125deg,#b34142,#a12e33),#a12e33')
    expect(styles).toContain('radial-gradient(circle at 30% 20%,#27352b,#121b16),#121b16')
    expect(rule(styles, '.ff-roulette-board button.selected')).toContain('background-image:linear-gradient(#fff2,#fff2)')
  })
})
