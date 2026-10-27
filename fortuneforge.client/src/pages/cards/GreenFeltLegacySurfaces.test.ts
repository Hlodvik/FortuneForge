/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

function source(path: string) {
  return readFileSync(new URL(path, import.meta.url), 'utf8')
}

function declarations(path: string, selector: string): string[] {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return [...source(path).matchAll(new RegExp(`${escaped}\\s*\\{([^}]+)\\}`, 'g'))]
    .map(match => match[1])
}

describe('legacy host green table materials', () => {
  it.each([
    ['./blackjack/blackjack.css', '.blackjack-table'],
    ['./solitaire/solitaire.css', '.solitaire-board'],
    ['./texasHoldem/texasHoldem.css', '.holdem-table'],
  ])('%s uses the approved shared material for every felt background override', (path, selector) => {
    const backgrounds = declarations(path, selector).flatMap(block =>
      [...block.matchAll(/\bbackground\s*:\s*([^;]+);/g)].map(match => match[1]))

    expect(backgrounds.length).toBeGreaterThan(0)
    for (const background of backgrounds) {
      expect(background).toContain('var(--ff-casino-felt-material,')
      expect(background).not.toMatch(/repeating-(?:radial|linear)-gradient/)
    }
  })

  it('keeps the mobile Solitaire background on the material instead of reverting to flat green', () => {
    const backgrounds = declarations('./solitaire/solitaire.css', '.solitaire-board')
      .filter(block => /\bbackground\s*:/.test(block))
    expect(backgrounds).toHaveLength(2)
    expect(backgrounds.every(block => block.includes('--ff-casino-felt-material'))).toBe(true)
  })

  it('uses the shared green felt on Three Card Poker and the House Hold’em table that inherits it', () => {
    const table = declarations('./threeCardPoker/threeCardPoker.css', '.three-card-poker-page .blackjack-table')
      .find(block => /\bbackground\s*:/.test(block))
    expect(table).toContain('var(--ff-casino-felt-material,')
    expect(table).not.toContain('#07506a')
    expect(source('./houseHoldem/HouseHoldemPage.tsx')).toContain('three-card-poker-page house-holdem-page')
    expect(source('./houseHoldem/HouseHoldemPage.tsx')).toContain("import '../threeCardPoker/threeCardPoker.css'")
  })
})
