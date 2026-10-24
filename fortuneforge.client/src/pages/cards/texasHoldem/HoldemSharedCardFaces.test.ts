/// <reference types="node" />

import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { PlayingCard } from '../../../games/cards/shared/PlayingCard'

const pageSources = [
  './CreditTexasHoldemPage.tsx',
  './TexasHoldemPage.tsx',
  '../houseHoldem/HouseHoldemPage.tsx',
] as const

const tableStyles = [
  './pokerClientHoldem.css',
  './texasHoldem.css',
  '../houseHoldem/houseHoldem.css',
  '../blackjack/blackjack.css',
  '../threeCardPoker/threeCardPoker.css',
  '../../../../../games/TexasHoldem/client/FortuneForge.Games.TexasHoldem.Client/src/holdem.css',
] as const

describe('Hold’em shared card faces', () => {
  it.each(pageSources)('%s renders the canonical PlayingCard with the shared face styles', path => {
    const source = readSource(path)

    expect(source).toContain("import { PlayingCard } from '../../../games/cards/shared/PlayingCard'")
    expect(source).toContain("import '../../../games/cards/shared/playingCards.css'")
    expect(source).toContain('<PlayingCard ')
  })

  it.each(tableStyles)('%s leaves rank, suit, and court artwork to the shared face styles', path => {
    const styles = readSource(path)

    // Table-specific card-slot sizing and spacing are fine. Card face artwork,
    // including the illustrated court figures, must not differ by Hold’em variant.
    expect(styles).not.toMatch(/\.ff-playing-card(?:--(?:face|red|black)|__(?:corner|center))\b/)
  })
})

describe('Hold’em canonical casino card backs', () => {
  it.each(pageSources)('%s uses PlayingCard faceDown rather than a game-themed back renderer', path => {
    const source = readSource(path)

    expect(source).toMatch(/<PlayingCard\b[^>]*\bfaceDown(?:\s|=|\/)/)
    expect(source).not.toMatch(/card-back-asian|ff-dt__card--back|dragonTigerTypography/)
  })

  it('keeps the shared FF back opaque to the rank, suit, and court artwork', () => {
    const markup = renderToStaticMarkup(createElement(PlayingCard, {
      card: { id: 'private-king-hearts', rank: 13, suit: 'hearts' }, faceDown: true,
    }))

    expect(markup).toContain('ff-playing-card--back')
    expect(markup).toContain('aria-label="Face-down card"')
    expect(markup).toContain('ff-playing-card__back-frame')
    expect(markup).toContain('<b>FF</b>')
    expect(markup).not.toContain('King')
    expect(markup).not.toContain('hearts')
    expect(markup).not.toContain('ff-playing-card__corner')
    expect(markup).not.toContain('<img')
    expect(markup).not.toContain('<svg')
  })

  it('retains the navy-and-gold lattice back with an unflipped FF medallion', () => {
    const styles = readSource('../../../games/cards/shared/playingCards.css')
    const back = styles.match(/\.ff-playing-card--back\s*\{([^}]+)\}/)?.[1]
    const lattice = styles.match(/\.ff-playing-card__back-frame i\s*\{([^}]+)\}/)?.[1]
    const medallion = styles.match(/\.ff-playing-card__back-frame b\s*\{([^}]+)\}/)?.[1]

    expect(back).toContain('#28365d')
    expect(back).toContain('#101a3d')
    expect(lattice).toContain('linear-gradient(45deg')
    expect(lattice).toContain('linear-gradient(-45deg')
    expect(medallion).toContain('transform: rotate(-8deg)')
    expect(medallion).not.toMatch(/rotate[XY]|scale[XY]?\(\s*-/)
    expect(styles).not.toContain('card-back-asian')
  })

  it.each(tableStyles)('%s does not override the shared card back', path => {
    expect(readSource(path)).not.toMatch(/\.ff-playing-card(?:--back|__back-frame)\b/)
  })
})

function readSource(relativePath: string): string {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8')
}
