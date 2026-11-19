// @vitest-environment jsdom
// Edited: 2026-11-18T22:55:31-06:00 | Computer: Vinnabok
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { CardRoomHistoryResult } from './cardRoomHistoryApi'
import { useCardRoomHistory } from './useCardRoomHistory'

const startedAtUtc = '2026-11-18T12:00:00Z'
const paths = {
  blackjack: '/api/cards/blackjack/table/session',
  'texas-holdem': '/api/cards/texas-holdem/credit/session',
  'three-card-poker': '/api/cards/three-card-poker/table/session',
  solitaire: '/api/solitaire/session',
}
const historyPath = '/api/cards/history?limit=40'
const completed = (overrides: Partial<CardRoomHistoryResult> = {}): CardRoomHistoryResult => ({
  resultId: 'result-1', matchId: 'match-1', game: 'blackjack', mode: 'credit', startedAtUtc,
  completedAtUtc: '2026-11-18T12:02:00Z', unseen: true, requiresClaim: false,
  winningsCredits: 20, wagerCredits: 10, netCredits: 10, score: null, moves: null, schemaVersion: 1, ...overrides,
})

let root: Root
let host: HTMLDivElement
let history: ReturnType<typeof useCardRoomHistory>
let replies: Map<string, unknown>
let statuses: Map<string, number>
const assign = vi.fn(), balanceChanged = vi.fn(), fetchMock = vi.fn()
function Harness() { history = useCardRoomHistory(balanceChanged); return null }
const mount = () => act(async () => root.render(<Harness />))

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  replies = new Map([[historyPath, []], ...Object.values(paths).map(path => [path, { kind: 'idle' }] as [string, unknown])])
  statuses = new Map()
  assign.mockReset(); balanceChanged.mockReset(); fetchMock.mockReset()
  fetchMock.mockImplementation(async (path: string) => new Response(JSON.stringify(replies.get(path) ?? null), { status: statuses.get(path) ?? 200 }))
  vi.stubGlobal('fetch', fetchMock)
  const originalWindow = window
  const location = new Proxy({}, { get(_target, property) {
    if (property === 'assign') return assign
    return Reflect.get(originalWindow.location, property, originalWindow.location)
  } })
  vi.stubGlobal('window', new Proxy(originalWindow, { get(target, property) {
    if (property === 'location') return location
    const value = Reflect.get(target, property, target)
    return typeof value === 'function' ? value.bind(target) : value
  } }))
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.unstubAllGlobals() })

describe('account history without playable clients', () => {
  it.each([
    { game: 'blackjack', session: { kind: 'queue', ticketId: 'queue-1', joinedAtUtc: startedAtUtc, players: [{}] }, title: 'Waiting for a table', summary: '1 player ready', href: '/cards/blackjack' },
    { game: 'blackjack', session: { kind: 'table', table: { tableId: 'table-1', round: 3, phase: 'taking-wagers', createdAtUtc: startedAtUtc } }, title: 'Table · round 3', summary: 'Taking Wagers', href: '/cards/blackjack' },
    { game: 'texas-holdem', session: { kind: 'queue', ticketId: 'queue-1', joinedAtUtc: startedAtUtc, players: [{}, {}] }, title: 'Waiting for a table', summary: '2 players ready', href: '/cards/texas-holdem' },
    { game: 'texas-holdem', session: { kind: 'match', table: { matchId: 'table-1', handNumber: 4, pot: 1234, currentBet: 56, startedAtUtc } }, title: 'Table · hand 4', summary: 'Pot R12.34 · current bet R0.56', href: '/cards/texas-holdem' },
    { game: 'three-card-poker', session: { kind: 'queue', ticketId: 'queue-1', joinedAtUtc: startedAtUtc, players: [{}] }, title: 'Waiting for a table', summary: '1 players ready', href: '/cards/three-card-poker' },
    { game: 'three-card-poker', session: { kind: 'table', table: { tableId: 'table-1', round: 2, phase: 'player-turn', createdAtUtc: startedAtUtc } }, title: 'Table · round 2', summary: 'Player Turn', href: '/cards/three-card-poker' },
    { game: 'solitaire', session: { kind: 'queued', ticketId: 'queue-1', joinedAtUtc: startedAtUtc, players: [{}], playerCount: 4 }, title: 'Waiting for a game', summary: '1 of 4 seats', href: '/cards/solitaire' },
    { game: 'solitaire', session: { kind: 'match', matchId: 'match-1', startedAtUtc, score: 820, moves: 74 }, title: 'Solitaire run', summary: '820 points · 74 moves', href: '/cards/solitaire' },
  ] as const)('preserves $game $session.kind summaries and Resume', async ({ game, session, title, summary, href }) => {
    replies.set(paths[game], session)
    await mount()
    expect(history.loading).toBe(false)
    expect(history.error).toBeNull()
    expect(history.activities).toHaveLength(1)
    expect(history.activities[0]).toMatchObject({ game, title, summary, completedAtUtc: null })
    await act(async () => history.select(history.activities[0]))
    expect(assign).toHaveBeenCalledExactlyOnceWith(href)
    expect(balanceChanged).not.toHaveBeenCalled()
  })

  it('groups table rounds, sums exact money, and acknowledges every source result', async () => {
    replies.set(historyPath, [completed(), completed({ resultId: 'result-2', startedAtUtc: '2026-11-18T12:03:00Z', completedAtUtc: '2026-11-18T12:05:00Z', netCredits: -.5, winningsCredits: 9.5 })])
    await mount()
    expect(history.activities).toHaveLength(1)
    expect(history.activities[0]).toMatchObject({ title: '2 rounds at one table', rounds: 2, wagerCredits: 20, winningsCredits: 29.5, netCredits: 9.5 })
    await act(async () => history.select(history.activities[0]))
    expect(fetchMock.mock.calls.map(call => call[0])).toContain('/api/cards/history/result-1/seen')
    expect(fetchMock.mock.calls.map(call => call[0])).toContain('/api/cards/history/result-2/seen')
    expect(history.activities[0].unseen).toBe(false)
  })

  it('keeps the active table instead of duplicating its completed rounds', async () => {
    replies.set(historyPath, [completed()])
    replies.set(paths.blackjack, { kind: 'table', table: { tableId: 'match-1', round: 2, phase: 'taking-wagers', createdAtUtc: startedAtUtc } })
    await mount()
    expect(history.activities).toHaveLength(1)
    expect(history.activities[0].completedAtUtc).toBeNull()
  })

  it('claims solitaire through the shared transport and reports the server balance', async () => {
    replies.set(historyPath, [completed({ game: 'solitaire', matchId: 'match /1', requiresClaim: true, score: 820, moves: 74 })])
    replies.set('/api/solitaire/matches/match%20%2F1/claim', { balanceCredits: 129.5 })
    await mount()
    await act(async () => history.select(history.activities[0]))
    expect(balanceChanged).toHaveBeenCalledExactlyOnceWith(129.5)
    expect(history.activities[0]).toMatchObject({ unseen: false, requiresClaim: false })
    const request = fetchMock.mock.calls.find(call => String(call[0]).endsWith('/claim'))!
    expect(request[1].method).toBe('POST')
    expect(new Headers(request[1].headers).get('Idempotency-Key')).toMatch(/^history_/)
    expect(request[1].body).toBeUndefined()
  })

  it('leaves a claim available when the server returns an invalid balance', async () => {
    replies.set(historyPath, [completed({ game: 'solitaire', requiresClaim: true })])
    replies.set('/api/solitaire/matches/match-1/claim', { balanceCredits: -1 })
    await mount()
    await act(async () => history.select(history.activities[0]))
    expect(balanceChanged).not.toHaveBeenCalled()
    expect(history.activities[0]).toMatchObject({ unseen: true, requiresClaim: true })
    expect(history.error).toContain('invalid balance')
    expect(history.busyId).toBeNull()
  })

  it('isolates malformed active summaries while preserving completed history', async () => {
    replies.set(historyPath, [completed({ game: 'mines' })])
    replies.set(paths['texas-holdem'], { kind: 'match', table: { matchId: 'table-1', handNumber: 4, pot: 1.5, currentBet: 56, startedAtUtc } })
    await mount()
    expect(history.error).toBeNull()
    expect(history.activities.map(activity => activity.game)).toEqual(['mines'])
  })

  it('retains active games when completed history is unavailable', async () => {
    statuses.set(historyPath, 503)
    replies.set(paths.solitaire, { kind: 'match', matchId: 'match-1', startedAtUtc, score: 820, moves: 74 })
    await mount()
    expect(history.activities[0].game).toBe('solitaire')
    expect(history.error).toBe('Completed games could not be loaded. Active games are still available.')
  })
})
