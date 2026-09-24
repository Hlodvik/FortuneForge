import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { CrapsGame, tableMessage } from './CrapsGame'
import type { CrapsGateway, CrapsRound } from './contracts'

const gateway: CrapsGateway = {
  getStatus: async () => ({ available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, mode: 'test' }),
  startRound: async () => { throw new Error('Not used during server render.') },
  roll: async () => { throw new Error('Not used during server render.') },
}

describe('CrapsGame', () => {
  it('renders a real pass-line table rather than a foundation placeholder', () => {
    const markup = renderToStaticMarkup(createElement(CrapsGame, { gateway }))
    expect(markup).toContain('Craps table')
    expect(markup).toContain('PASS LINE')
    expect(markup).toContain('Bet R10 on Pass Line')
    expect(markup).toContain('Tips')
    expect(markup).not.toContain('Other games')
    expect(markup).not.toContain('Fortune Forge home')
    expect(markup).not.toContain('Fortune Forge presents')
    expect(markup).not.toContain('Come-out naturals win')
    expect(markup).not.toContain('Skeleton')
  })

  it('makes another player\'s turn unmistakable and disables play controls', () => {
    const markup = renderToStaticMarkup(createElement(CrapsGame, {
      gateway,
      isYourTurn: false,
      activePlayerName: 'Maya',
    }))
    expect(markup).toContain('Maya is the shooter')
    expect(markup).toContain('Watch the shooter. Your controls unlock when the dice pass.')
    expect(markup).toContain('disabled')
  })

  it('describes the point lifecycle clearly', () => {
    const round: CrapsRound = {
      roundId: 'test-round',
      stake: 10,
      phase: 'point',
      point: 4,
      rolls: [{ rollNumber: 1, first: 2, second: 2, total: 4, result: 'point-established' }],
      lastOutcome: { first: 2, second: 2, total: 4, result: 'point-established', isTerminal: false, totalReturn: null },
    }
    expect(tableMessage(round, true)).toBe('Point is 4. Roll it again before a 7.')
  })
})
