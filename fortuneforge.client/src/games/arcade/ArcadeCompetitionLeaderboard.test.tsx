import { readFileSync } from 'node:fs'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import {
  canNavigateToNextCompetitionWindow,
  competitionHistoryAt,
  formatCompetitionDateTime,
  periodLabel,
  selectCompetitionPeriod,
} from './arcadeCompetitionLeaderboardPresentation'
import { ArcadeCompetitionLeaderboardView } from './ArcadeCompetitionLeaderboard'
import {
  ArcadeCompetitionRequestError,
  HttpArcadeCompetitionGateway,
} from './arcadeCompetitionApi'
import type { ArcadeCompetitionWindow } from './arcadeCompetitionApi'

describe('arcade competition leaderboard', () => {
  it('offers a retry that repeats a failed read without changing the selected competition period', () => {
    const source = readFileSync(new URL('./ArcadeCompetitionLeaderboard.tsx', import.meta.url), 'utf8')

    expect(source).toContain("const [loadAttempt, setLoadAttempt] = useState(0)")
    expect(source).toContain("}, [at, gameId, gateway, loadAttempt, period])")
    expect(source).toContain('Retry leaderboard')
  })

  it('labels the three read-only periods', () => {
    expect((['daily', 'weekly', 'all-time'] as const).map(periodLabel)).toEqual(['Daily', 'Weekly', 'All-time'])
  })

  it('switches periods back to the current window', () => {
    expect(selectCompetitionPeriod('weekly')).toEqual({ period: 'weekly', at: undefined })
    expect(selectCompetitionPeriod('all-time')).toEqual({ period: 'all-time', at: undefined })
  })

  it('renders the public daily response with Johannesburg times and compact competition details', () => {
    const markup = renderToStaticMarkup(createElement(ArcadeCompetitionLeaderboardView, {
      snapshot: dailyWindow,
    }))

    expect(markup).toContain('03 Sept 2026, 02:00 SAST')
    expect(markup).toContain('04 Sept 2026, 02:00 SAST')
    expect(markup).toContain('Current jackpot: R12.50')
    expect(markup).toContain('Entries')
    expect(markup).toContain('Open')
    expect(markup).toContain('Players')
    expect(markup).toContain('Sole-player refund: R2.50 to player-1')
    expect(markup).toContain('#1')
    expect(markup).toContain('player-1')
    expect(markup).toContain('860')
    expect(markup).toMatch(/<time dateTime="2026-09-03T00:00:00Z">03 Sept 2026, 02:00 SAST<\/time>/)
  })

  it('formats competition windows in South Africa/Johannesburg local time', () => {
    expect(formatCompetitionDateTime('2026-09-03T00:00:00Z')).toBe('03 Sept 2026, 02:00 SAST')
  })

  it('keeps previous navigation for the current window but hides its future next window', () => {
    const currentMarkup = renderToStaticMarkup(createElement(ArcadeCompetitionLeaderboardView, {
      snapshot: dailyWindow,
      onNavigate: () => undefined,
    }))
    const historicWindow = { ...dailyWindow, entriesOpen: false, isCompleted: true }
    const historicMarkup = renderToStaticMarkup(createElement(ArcadeCompetitionLeaderboardView, {
      snapshot: historicWindow,
      onNavigate: () => undefined,
    }))

    expect(canNavigateToNextCompetitionWindow(dailyWindow)).toBe(false)
    expect(currentMarkup).toContain('Previous Daily')
    expect(currentMarkup).not.toContain('Next Daily')
    expect(canNavigateToNextCompetitionWindow(historicWindow)).toBe(true)
    expect(historicMarkup).toContain('Next Daily')
    expect(historicMarkup).toContain('Final jackpot: R12.50')
  })

  it('requests the preceding and following returned competition windows through at', async () => {
    const fetcher = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse(dailyWindow)))
    const gateway = new HttpArcadeCompetitionGateway(fetcher)
    const previous = competitionHistoryAt(dailyWindow, 'previous')
    const next = competitionHistoryAt(dailyWindow, 'next')

    await gateway.getLeaderboard('asteroids', 'daily', previous)
    await gateway.getLeaderboard('asteroids', 'daily', next)

    expect(fetcher.mock.calls[0][0]).toBe(`/api/arcade-competitions/asteroids/daily?at=${encodeURIComponent(previous)}`)
    expect(fetcher.mock.calls[1][0]).toBe(`/api/arcade-competitions/asteroids/daily?at=${encodeURIComponent(next)}`)
    expect(previous).toBe('2026-09-02T23:59:59.999Z')
    expect(next).toBe('2026-09-04T00:00:00.000Z')
  })

  it('renders all-time as scores only, with no window or jackpot details', () => {
    const markup = renderToStaticMarkup(createElement(ArcadeCompetitionLeaderboardView, {
      snapshot: { gameId: 'asteroids', period: 'all-time', leaderboard: dailyWindow.leaderboard },
    }))

    expect(markup).toContain('player-1')
    expect(markup).not.toContain('Current jackpot')
    expect(markup).not.toContain('Final jackpot')
    expect(markup).not.toContain('Entries')
    expect(markup).not.toContain('Players')
    expect(markup).not.toContain('Previous')
    expect(markup).not.toMatch(/house|cut/i)
  })

  it('surfaces a read-only API error without treating it as leaderboard data', async () => {
    const gateway = new HttpArcadeCompetitionGateway(vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ code: 'arcade-competition-game-not-found' }),
      { status: 404, headers: { 'Content-Type': 'application/json' } },
    )))

    await expect(gateway.getLeaderboard('unknown', 'daily')).rejects.toMatchObject({
      name: ArcadeCompetitionRequestError.name,
      status: 404,
      code: 'arcade-competition-game-not-found',
    })
  })
})

const dailyWindow: ArcadeCompetitionWindow = {
  gameId: 'asteroids',
  period: 'daily',
  startsAtUtc: '2026-09-03T00:00:00Z',
  endsAtUtc: '2026-09-04T00:00:00Z',
  entriesOpen: true,
  isCompleted: false,
  totalUniquePlayers: 2,
  visibleJackpotCents: 1250,
  solePlayerRefund: { playerId: 'player-1', amountCents: 250 },
  leaderboard: [
    { position: 1, playerId: 'player-1', score: 860 },
    { position: 2, playerId: 'player-2', score: 710 },
  ],
}

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}
