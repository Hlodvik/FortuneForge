/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
const source = (path: string) => readFileSync(new URL(path, import.meta.url), 'utf8')

describe('canonical court-card consumers', () => {
  it.each(['../../../app/routes/VideoPokerRoute.tsx', '../../../app/routes/CasinoWarRoute.tsx'])('%s injects the shared deck without changing table backs', path => {
    const route = source(path)
    expect(route).toContain('renderCardFace={renderStandardCardFace}')
    expect(route).toContain('playingCards.css')
  })
  it('Solitaire uses the same court geometry rather than its old inset rank panel', () => {
    expect(source('../solitaire/SolitaireBoard.tsx')).toContain('<CourtCardSymbol rank={card.rank} />')
  })
  it.each(['../../../app/routes/HeartsRoute.tsx', '../../../app/routes/AndarBaharRoute.tsx', '../../../app/routes/DragonTigerRoute.tsx', '../../../app/routes/HiLoRoute.tsx'])('%s uses the canonical PlayingCard', path => {
    expect(source(path)).toContain('PlayingCard')
    expect(source(path)).toContain('playingCards.css')
  })
})
