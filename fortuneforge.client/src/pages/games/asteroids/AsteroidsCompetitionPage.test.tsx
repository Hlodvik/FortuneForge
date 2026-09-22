import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { ArcadeCompetitionRequestError } from '../../../games/arcade/arcadeCompetitionApi'
import type { AsteroidsCompetitionGateway } from './AsteroidsCompetitionPage'
import { AsteroidsCompetitionPage } from './AsteroidsCompetitionPage'
import {
  createAsteroidsIdempotencyKey,
  createPendingFreeAsteroidsSubmission,
  createPendingPaidAsteroidsSubmission,
  friendlyAsteroidsCompetitionError,
  paidOfficialScoreText,
} from './asteroidsCompetitionHelpers'

describe('AsteroidsCompetitionPage', () => {
  it('server-renders the initial lobby with leaderboard ahead of entry choices', () => {
    const markup = renderToStaticMarkup(createElement(AsteroidsCompetitionPage, {
      account: accountSummary,
      gateway: gateway,
    }))

    expect(markup).toContain('Asteroids leaderboard')
    expect(markup).toContain('Daily competition · R1')
    expect(markup).toContain('Weekly competition · R1')
    expect(markup).toContain('Free play')
    expect(markup).toContain('Free practice runs are recorded to your account but are not leaderboard eligible.')
    expect(markup).toContain('Competition arena')
    expect(markup.indexOf('Asteroids leaderboard')).toBeLessThan(markup.indexOf('Choose your flight'))
    expect(markup).toContain('asteroids-competition-page__lobby-grid')
    expect(markup).toMatch(/^<div class="asteroids-competition-page"><header/)
    expect(markup).toContain('<main class="asteroids-competition-page__content">')
  })

  it('uses the compact active layout only while a replay is in progress', () => {
    const pageSource = readFileSync(new URL('./AsteroidsCompetitionPage.tsx', import.meta.url), 'utf8')

    expect(pageSource).toContain("phase === 'playing-free' || phase === 'playing-paid'")
    expect(pageSource).toContain('asteroids-competition-page--active')
    expect(pageSource).not.toContain('asteroids-active-run')
  })

  it('creates fresh valid start keys and gets the free identity and seed from the gateway', () => {
    const firstKey = createAsteroidsIdempotencyKey()
    const secondKey = createAsteroidsIdempotencyKey()
    const pageSource = readFileSync(new URL('./AsteroidsCompetitionPage.tsx', import.meta.url), 'utf8')

    expect(firstKey).toMatch(/^[a-z0-9-]{16,128}$/)
    expect(secondKey).toMatch(/^[a-z0-9-]{16,128}$/)
    expect(firstKey).not.toBe(secondKey)
    expect(pageSource).toContain("storeAsteroidsState(account.userId, { state: 'starting', start })")
    expect(pageSource).toContain('gateway.startFreeAsteroidsRun(start.idempotencyKey)')
    expect(pageSource).toContain("storeAsteroidsState(account.userId, { state: 'submitting', submission })")
    expect(pageSource).not.toContain('createFreeAsteroidsIdentity')
  })

  it('retains the exact completed replay and display result for a paid-submission retry', () => {
    const replay = { totalSteps: 44, commands: [{ step: 5, input: 3 }] }
    const display = { score: 725, wave: 2, lives: 1, reason: 'game-over' as const }
    const pending = createPendingPaidAsteroidsSubmission('daily', 'paid-run-12345678', replay, display)

    expect(pending).toMatchObject({ period: 'daily', runId: 'paid-run-12345678' })
    expect(pending.replay).toBe(replay)
    expect(pending.display).toBe(display)
  })

  it('retains the exact completed free replay for an idempotent retry', () => {
    const replay = { totalSteps: 44, commands: [{ step: 5, input: 3 }] }
    const display = { score: 725, wave: 2, lives: 1, reason: 'game-over' as const }
    const pending = createPendingFreeAsteroidsSubmission('free-run-12345678', replay, display)

    expect(pending).toMatchObject({ kind: 'free', runId: 'free-run-12345678' })
    expect(pending.replay).toBe(replay)
    expect(pending.display).toBe(display)
  })

  it('maps real competition server codes by precedence without exposing codes', () => {
    expect(friendlyAsteroidsCompetitionError(new ArcadeCompetitionRequestError(409, 'arcade-competition-insufficient-credits')))
      .toBe('You need at least R1 in your balance to enter this competition.')
    expect(friendlyAsteroidsCompetitionError(new ArcadeCompetitionRequestError(409, 'arcade-competition-settlement-closed')))
      .toBe('That competition has just closed. Please choose another available competition.')
    expect(friendlyAsteroidsCompetitionError(new ArcadeCompetitionRequestError(400, 'arcade-asteroids-replay-invalid')))
      .toContain('run can no longer be submitted')
    expect(friendlyAsteroidsCompetitionError(new ArcadeCompetitionRequestError(409, 'arcade-asteroids-run-conflict')))
      .toContain('run can no longer be submitted')
    expect(friendlyAsteroidsCompetitionError(new ArcadeCompetitionRequestError(409, 'arcade-competition-settlement-closed')))
      .not.toContain('arcade-competition-settlement-closed')
  })

  it('uses only the server score wording for final paid results', () => {
    expect(paidOfficialScoreText(940)).toBe('Official score: 940')
    expect(paidOfficialScoreText(940)).not.toContain('Your game score')
  })
})

const accountSummary = {
  userId: 'player-1',
  playerName: 'Pilot',
  email: 'pilot@example.test',
  createdAtUtc: '2026-09-05T00:00:00Z',
  balances: { slotsCredits: 100, freeGames: 0 },
  slots: {
    spinsPlayed: 0,
    wins: 0,
    losses: 0,
    creditsWagered: 0,
    creditsWon: 0,
    netCredits: 0,
  },
  role: 'Player',
}

const gateway: AsteroidsCompetitionGateway = {
  getLeaderboard: async () => ({ gameId: 'asteroids', period: 'all-time', leaderboard: [] }),
  startAsteroidsAttempt: async () => {
    throw new Error('not invoked during server rendering')
  },
  completeAsteroidsReplay: async () => {
    throw new Error('not invoked during server rendering')
  },
  startFreeAsteroidsRun: async () => {
    throw new Error('not invoked during server rendering')
  },
  completeFreeAsteroidsReplay: async () => {
    throw new Error('not invoked during server rendering')
  },
}
