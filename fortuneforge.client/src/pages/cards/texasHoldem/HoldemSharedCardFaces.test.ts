/// <reference types="node" />

import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

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

function readSource(relativePath: string): string {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8')
}
