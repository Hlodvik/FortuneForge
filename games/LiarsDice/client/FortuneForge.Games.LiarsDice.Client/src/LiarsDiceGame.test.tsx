import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { LiarsDiceGame } from './LiarsDiceGame'
import type { LiarsDiceGateway, LiarsDiceMatch } from './contracts'

const unusedMatch = async (): Promise<LiarsDiceMatch> => { throw new Error('Not used during server render.') }
const gateway: LiarsDiceGateway = {
  getStatus: async () => ({ available: true, startingDicePerPlayer: 5, mode: 'test' }),
  startMatch: unusedMatch,
  bid: unusedMatch,
  challenge: unusedMatch,
  advance: unusedMatch,
  nextRound: unusedMatch,
}

describe('LiarsDiceGame', () => {
  it('keeps table controls but leaves global navigation to the host shell', () => {
    const markup = renderToStaticMarkup(createElement(LiarsDiceGame, { gateway }))

    expect(markup).toContain('Liar&#x27;s Dice')
    expect(markup).toContain('Starting dice')
    expect(markup).toContain('Opening…')
    expect(markup).toContain('Your private cup')
    expect(markup).not.toContain('Estimated chance')
    expect(markup).not.toContain('Patient counter')
    expect(markup).not.toContain('Other games')
    expect(markup).not.toContain('Fortune Forge home')
  })
  it('allows the host to own the title and keeps optional rules out of the play surface', () => {
    const markup = renderToStaticMarkup(createElement(LiarsDiceGame, { gateway, showTitle:false, playerId:'account-a' }))
    expect(markup).not.toContain('<h1')
    expect(markup).toContain('Table options')
    expect(markup).not.toContain('Last player with dice wins')
    expect(markup).not.toContain('20s')
    expect(markup).toContain('aria-busy="true"')
  })
})
