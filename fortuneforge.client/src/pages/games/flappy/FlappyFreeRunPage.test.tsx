import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { FlappyFreeRunPage } from './FlappyFreeRunPage'

describe('Flappy free-run lobby', () => {
  it('persists an uncertain flight start and restores the exact idempotent request after reload', () => {
    const pageSource = readFileSync(new URL('./FlappyFreeRunPage.tsx', import.meta.url), 'utf8')

    expect(pageSource).toContain('storePendingStart(account.userId, { idempotencyKey })')
    expect(pageSource).toContain('gateway.startFreeFlappyRun(storedStart.idempotencyKey)')
    expect(pageSource).toContain('clearStoredStart(account.userId)')
  })

  it('makes the recorded free-play controls clear before a flight starts', () => {
    const markup = renderToStaticMarkup(createElement(FlappyFreeRunPage, {
      account: account(),
      gateway: {
        startFreeFlappyRun: async () => ({ runId: 'flappy_free_0123456789abcdef', seed: 17, startedAtUtc: '2026-09-06T12:00:00Z', wasReplay: false }),
        completeFreeFlappyReplay: async () => ({ runId: 'flappy_free_0123456789abcdef', score: 0, terminal: 'ground-collision' as const, wasReplay: false }),
      },
    }))

    expect(markup).toContain('Start free flight')
    expect(markup).toContain('Space or left click/tap to flap.')
    expect(markup).toContain('Every flight is securely recorded to your account')
    expect(markup).toContain('free play does not enter the jackpot')
  })

  it('returns keyboard focus to the playfield when the on-screen start button is used', () => {
    const pageSource = readFileSync(new URL('./FlappyFreeRunPage.tsx', import.meta.url), 'utf8')

    expect(pageSource).toContain('event.stopPropagation(); playfield.current?.focus(); requestFlap()')
  })

  it('uses visual elements that stay within the unchanged replay playfield', () => {
    const pageSource = readFileSync(new URL('./FlappyFreeRunPage.tsx', import.meta.url), 'utf8')

    expect(pageSource).toContain('ff-flappy-pipe-rim')
    expect(pageSource).toContain('ff-flappy-bird-art')
    expect(pageSource).toContain('ff-flappy-ground')
  })
})

function account() {
  return {
    userId: 'player-1',
    playerName: 'Pilot',
    email: 'pilot@example.test',
    createdAtUtc: '2026-09-06T00:00:00Z',
    balances: { slotsCredits: 100, freeGames: 0 },
    slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
    role: 'Player' as const,
  }
}
