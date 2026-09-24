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
    expect(markup).toContain('New match')
    expect(markup).not.toContain('Other games')
    expect(markup).not.toContain('Fortune Forge home')
  })
})
