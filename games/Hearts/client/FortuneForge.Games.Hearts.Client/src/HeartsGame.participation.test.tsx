// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { HeartsGame, type HeartsParticipation } from './HeartsGame'
import { HeartsGatewayError, type HeartsGateway, type HeartsMatch, type HeartsPaidSession } from './contracts'

const seats = ['north', 'east', 'south', 'west'] as const
const two = { code: '2|clubs', rank: '2', suit: 'clubs', label: '2♣' }
const match = (overrides: Partial<HeartsMatch> = {}): HeartsMatch => ({
  matchId: 'accepted-match', targetScore: 100, roundNumber: 1, phase: 'playing', passDirection: 'left',
  turn: 'north', humanSeat: 'north', yourTurn: true, opponentsThinking: false, heartsBroken: false,
  submittedPasses: seats, players: seats.map(seat => ({ seat, displayName: seat, handCount: 1, roundScore: 0, matchScore: 0, hasPassed: true })),
  hand: [two], legalCards: [two], currentTrick: { leader: 'north', plays: [] }, recentTrick: null, completedTricks: [],
  score: { north: 0, east: 0, south: 0, west: 0 }, roundScore: { north: 0, east: 0, south: 0, west: 0 },
  difficulty: 'standard', winner: null, message: '', ...overrides,
})
const idle = (): HeartsPaidSession => ({ kind: 'idle', version: 1, balanceCredits: 100, entryOptions: [1, 2, 5, 10, 25], queue: null, table: null })
function active(overrides: Partial<HeartsMatch> = {}, left = false): HeartsPaidSession {
  const state = match(overrides)
  return { ...idle(), kind: 'match', version: 2, table: {
    match: state, entryCredits: 1, potCredits: 4, payoutCredits: 0, winnerSeats: [],
    players: state.players.map(player => ({ seat: player.seat, displayName: player.displayName,
      isCurrentPlayer: player.seat === 'north', hasLeft: player.seat === 'north' && left, entryCredits: 1, payoutCredits: 0 })),
    actionDeadlineAtUtc: null, completedAtUtc: null,
  } }
}
function gateway(session = idle()): HeartsGateway {
  return {
    getStatus: vi.fn().mockResolvedValue({ available: true, defaultTargetScore: 100, mode: 'free-play' }),
    startMatch: vi.fn().mockResolvedValue(match()), leaveMatch: vi.fn(),
    startTrial: vi.fn().mockResolvedValue(match({ isTrial: true })), cancelTrial: vi.fn(),
    pass: vi.fn(), playCard: vi.fn(), advance: vi.fn(), nextRound: vi.fn(),
    paid: { getSession: vi.fn().mockResolvedValue(session), join: vi.fn(), playHouse: vi.fn(),
      getMatch: vi.fn(), pass: vi.fn(), play: vi.fn(), advance: vi.fn(), nextRound: vi.fn(),
      leave: vi.fn(), getHistory: vi.fn().mockResolvedValue([]) },
  }
}
let root: Root, host: HTMLDivElement
const participation = vi.fn<(value: HeartsParticipation) => void>()
const latest = () => participation.mock.calls.at(-1)?.[0]
const open = (api: HeartsGateway) => act(async () => root.render(<HeartsGame gateway={api} onParticipationChange={participation} />))
async function click(name: RegExp) {
  const button = [...host.querySelectorAll<HTMLButtonElement>('button')].find(button => name.test(button.getAttribute('aria-label') ?? button.textContent ?? ''))!
  expect(button).toBeTruthy()
  await act(async () => button.click())
}
beforeEach(() => {
  vi.useFakeTimers(); participation.mockClear(); vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  localStorage.setItem('fortuneforge:hearts:muted', 'true')
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); localStorage.clear(); vi.useRealTimers(); vi.unstubAllGlobals() })

describe('Hearts explicit participation lifecycle', () => {
  it.each(['passing', 'playing', 'complete'] as const)('protects the whole accepted paid match during %s, including waiting', async phase => {
    await open(gateway(active({ phase, yourTurn: false, turn: 'east' })))
    expect(latest()).toBe('paid-match')
  })

  it('protects a reserved queue and publishes completion without calling financial leave on unmount', async () => {
    const api = gateway({ ...idle(), kind: 'queue', queue: { matchId: 'queue', entryCredits: 1, potCredits: 1,
      joinedAtUtc: '', players: [{ seat: 'north', displayName: 'You', isCurrentPlayer: true }], message: '' } })
    await open(api); expect(latest()).toBe('paid-queue')
    vi.mocked(api.paid!.getSession).mockResolvedValue({ ...active(), kind: 'result' })
    await act(async () => { await vi.advanceTimersByTimeAsync(1_000) })
    expect(latest()).toBeNull()
    await act(async () => root.render(<div />))
    expect(latest()).toBeNull()
    expect(api.paid!.leave).not.toHaveBeenCalled()
    expect(api.leaveMatch).not.toHaveBeenCalled()
    expect(api.cancelTrial).not.toHaveBeenCalled()
  })

  it('does not protect a seat already forfeited by the authoritative server', async () => {
    await open(gateway(active({}, true)))
    expect(latest()).toBeNull()
  })

  it('keeps an uncertain entry request protected until session recovery resolves it', async () => {
    const api = gateway()
    let reject!: (error: unknown) => void
    vi.mocked(api.paid!.join).mockReturnValue(new Promise((_, fail) => { reject = fail }))
    await open(api)
    expect(latest()).toBeNull()
    await click(/^Play for R1\.00$/)
    expect(latest()).toBe('paid-pending')
    await act(async () => reject(new HeartsGatewayError('Response lost', 'offline', 0)))
    expect(latest()).toBe('paid-pending')
    vi.mocked(api.paid!.getSession).mockResolvedValue(active())
    await click(/^Refresh table$/)
    expect(latest()).toBe('paid-match')
    expect(api.paid!.join).toHaveBeenCalledOnce()
    expect(api.paid!.leave).not.toHaveBeenCalled()
  })

  it('clears an unaccepted entry after a definite rejection', async () => {
    const api = gateway()
    vi.mocked(api.paid!.join).mockRejectedValue(new HeartsGatewayError('Rejected', 'insufficient-credits', 400))
    await open(api); await click(/^Play for R1\.00$/)
    expect(latest()).toBeNull()
  })

  it('publishes free participation until the whole practice match ends', async () => {
    const api = gateway()
    vi.mocked(api.playCard).mockResolvedValue(match({ winner: 'north', phase: 'complete', hand: [], legalCards: [] }))
    await open(api); await click(/^Play free$/)
    expect(latest()).toBe('free-match')
    await click(/Two of clubs, play card/)
    expect(latest()).toBeNull()
  })

  it('protects the learning trial until all thirteen tricks complete, without claiming an entry wager', async () => {
    const api = gateway()
    api.onboarding = { getState: vi.fn().mockResolvedValue({ shouldAskPlayedBefore: true }), confirmExperience: vi.fn() }
    vi.mocked(api.playCard).mockResolvedValue(match({ isTrial: true, phase: 'complete', hand: [], legalCards: [],
      completedTricks: Array.from({ length: 13 }, (_, index) => ({ number: index + 1, leader: 'north', winner: 'north', points: 0 })) }))
    await open(api); await click(/^Learn to play$/)
    expect(latest()).toBe('trial')
    await click(/Two of clubs, play card/)
    expect(latest()).toBeNull()
    expect(api.paid!.join).not.toHaveBeenCalled()
  })
})
