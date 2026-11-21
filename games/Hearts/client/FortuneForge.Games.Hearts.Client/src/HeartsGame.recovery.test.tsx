// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { HeartsGame } from './HeartsGame'
import { HeartsGatewayError, type HeartsCard, type HeartsGateway, type HeartsMatch, type HeartsRecoveredSession } from './contracts'

const two: HeartsCard = { code: '2|clubs', rank: '2', suit: 'clubs', label: '2♣' }
const five: HeartsCard = { code: '5|clubs', rank: '5', suit: 'clubs', label: '5♣' }
const match = (overrides: Partial<HeartsMatch> = {}): HeartsMatch => ({
  matchId: 'practice-hand-one', targetScore: 100, roundNumber: 1, phase: 'playing', passDirection: 'left', turn: 'north', humanSeat: 'north', yourTurn: true, opponentsThinking: false, heartsBroken: false,
  submittedPasses: ['north', 'east', 'south', 'west'],
  players: (['north', 'east', 'south', 'west'] as const).map((seat, index) => ({ seat, displayName: ['You', 'Trial player 1', 'Trial player 2', 'Trial player 3'][index], handCount: 13, roundScore: 0, matchScore: 0, hasPassed: true })),
  hand: [two, five], legalCards: [two, five], currentTrick: { leader: 'north', plays: [] }, recentTrick: null, completedTricks: [], score: { north: 0, east: 0, south: 0, west: 0 }, roundScore: { north: 0, east: 0, south: 0, west: 0 }, difficulty: 'standard', winner: null, message: '', ...overrides,
})
function freeGateway(overrides: Partial<HeartsGateway> = {}): HeartsGateway {
  return {
    getStatus: vi.fn().mockResolvedValue({ available: true, defaultTargetScore: 100, mode: 'free-play' }),
    startMatch: vi.fn().mockResolvedValue(match()),
    pass: vi.fn().mockResolvedValue(match()),
    playCard: vi.fn().mockResolvedValue(match()),
    advance: vi.fn().mockResolvedValue(match()),
    nextRound: vi.fn().mockResolvedValue(match({ roundNumber: 2 })),
    ...overrides,
  }
}
async function beginFree(gateway: HeartsGateway) {
  const user = userEvent.setup()
  render(<HeartsGame gateway={gateway} />)
  const start = await screen.findByRole('button', { name: 'Play free' })
  await waitFor(() => expect((start as HTMLButtonElement).disabled).toBe(false))
  await user.click(start)
  await screen.findByLabelText('Your hand')
  return user
}

beforeEach(() => {
  vi.stubGlobal('Audio', class { volume = 0; play() { return Promise.resolve() } pause() {} addEventListener() {} })
  localStorage.setItem('fortuneforge:hearts:muted', 'true')
})
afterEach(() => { cleanup(); localStorage.clear(); vi.unstubAllGlobals() })

describe('Hearts practice recovery', () => {
  it('restores an accepted trial before fresh session or onboarding reads and never opens another hand', async () => {
    let finishRecovery!: (value: HeartsRecoveredSession) => void
    const restore = new Promise<HeartsRecoveredSession>(resolve => { finishRecovery = resolve })
    const getState = vi.fn().mockResolvedValue({ shouldAskPlayedBefore: true })
    const getSession = vi.fn().mockResolvedValue({ kind: 'idle', version: 0, balanceCredits: 100, entryOptions: [1], queue: null, table: null })
    const gateway = freeGateway({
      recoverSession: vi.fn().mockReturnValue(restore),
      onboarding: { getState, confirmExperience: vi.fn() },
      paid: { getSession, join: vi.fn(), getMatch: vi.fn(), pass: vi.fn(), play: vi.fn(), advance: vi.fn(), nextRound: vi.fn(), leave: vi.fn(), getHistory: vi.fn() },
      startTrial: vi.fn(),
    })
    render(<HeartsGame gateway={gateway} />)
    expect(gateway.getStatus).not.toHaveBeenCalled()
    expect(getSession).not.toHaveBeenCalled()
    expect(getState).not.toHaveBeenCalled()
    finishRecovery({ kind: 'practice', match: match({ isTrial: true }) })
    await screen.findByLabelText('Your hand')
    await waitFor(() => expect(getState).toHaveBeenCalledOnce())
    expect(gateway.startMatch).not.toHaveBeenCalled()
    expect(gateway.startTrial).not.toHaveBeenCalled()
    expect(screen.queryByText('Have you played Hearts before?')).toBeNull()
    expect(screen.getByText('Trial run', { exact: true })).not.toBeNull()
  })

  it('holds fresh setup reads after uncertain recovery and retries the saved hand on connection retry', async () => {
    const getState = vi.fn().mockResolvedValue({ shouldAskPlayedBefore: false })
    const gateway = freeGateway({
      recoverSession: vi.fn().mockRejectedValueOnce(new Error('Saved response still unavailable.')).mockResolvedValueOnce({ kind: 'practice', match: match() }),
      onboarding: { getState, confirmExperience: vi.fn() },
    })
    render(<HeartsGame gateway={gateway} />)
    const retry = await screen.findByRole('button', { name: 'Retry connection' })
    expect(gateway.getStatus).not.toHaveBeenCalled()
    expect(getState).not.toHaveBeenCalled()
    await userEvent.setup().click(retry)
    await screen.findByLabelText('Your hand')
    expect(gateway.recoverSession).toHaveBeenCalledTimes(2)
    expect(gateway.startMatch).not.toHaveBeenCalled()
    expect(getState).toHaveBeenCalledOnce()
  })

  it('recovers the exact uncertain card command through the gateway before presenting another legal action', async () => {
    const recovered = match({ hand: [five], legalCards: [five], currentTrick: { leader: 'north', plays: [{ seat: 'north', card: two }] } })
    const gateway = freeGateway({
      recoverSession: vi.fn().mockResolvedValueOnce(null).mockResolvedValueOnce({ kind: 'practice', match: recovered }),
      playCard: vi.fn().mockRejectedValueOnce(new Error('Card response lost.')),
    })
    const user = await beginFree(gateway)
    await user.click(screen.getByRole('button', { name: 'Two of clubs, play card' }))
    await user.click(await screen.findByRole('button', { name: 'Retry hand' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Two of clubs, play card' })).toBeNull())
    expect(gateway.recoverSession).toHaveBeenCalledTimes(2)
    expect(gateway.playCard).toHaveBeenCalledOnce()
    expect(gateway.advance).not.toHaveBeenCalled()
    expect(gateway.startMatch).toHaveBeenCalledOnce()
  })

  it('retries a failed opponent advance on the same free hand without reopening setup or starting again', async () => {
    const gateway = freeGateway({
      startMatch: vi.fn().mockResolvedValue(match({ turn: 'east', yourTurn: false, opponentsThinking: true, legalCards: [] })),
      advance: vi.fn().mockRejectedValueOnce(new HeartsGatewayError('Opponent update lost.', 'offline', 0)).mockResolvedValueOnce(match()),
    })
    const user = await beginFree(gateway)
    expect(gateway.paid).toBeUndefined()
    const retry = await screen.findByRole('button', { name: 'Retry hand' }, { timeout: 3000 })
    expect(gateway.advance).toHaveBeenCalledWith('practice-hand-one', expect.any(AbortSignal))
    await user.click(retry)
    await waitFor(() => expect((screen.getByRole('button', { name: 'Two of clubs, play card' }) as HTMLButtonElement).disabled).toBe(false))
    expect(gateway.advance).toHaveBeenCalledTimes(2)
    expect(gateway.advance).toHaveBeenLastCalledWith('practice-hand-one')
    expect(gateway.startMatch).toHaveBeenCalledOnce()
    expect(gateway.getStatus).toHaveBeenCalledOnce()
    expect(screen.queryByRole('button', { name: 'Retry hand' })).toBeNull()
    expect(screen.getAllByText('Free play', { exact: true }).length).toBeGreaterThan(0)
  })

  it('resynchronizes an uncertain free card write without replaying it or replacing the hand', async () => {
    const gateway = freeGateway({
      playCard: vi.fn().mockRejectedValue(new HeartsGatewayError('Card response lost.', 'offline', 0)),
      advance: vi.fn().mockResolvedValue(match({ hand: [five], legalCards: [five], currentTrick: { leader: 'north', plays: [{ seat: 'north', card: two }] } })),
    })
    const user = await beginFree(gateway)
    await user.click(screen.getByRole('button', { name: 'Two of clubs, play card' }))
    const retry = await screen.findByRole('button', { name: 'Retry hand' })
    expect((screen.getByRole('button', { name: 'Five of clubs, play card' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(retry)
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Two of clubs, play card' })).toBeNull())
    await waitFor(() => expect((screen.getByRole('button', { name: 'Five of clubs, play card' }) as HTMLButtonElement).disabled).toBe(false))
    expect(gateway.playCard).toHaveBeenCalledOnce()
    expect(gateway.playCard).toHaveBeenCalledWith('practice-hand-one', '2|clubs')
    expect(gateway.advance).toHaveBeenCalledOnce()
    expect(gateway.advance).toHaveBeenCalledWith('practice-hand-one')
    expect(gateway.startMatch).toHaveBeenCalledOnce()
    expect(gateway.getStatus).toHaveBeenCalledOnce()
  })

  it('keeps a second failed read retryable and prevents more card writes until it succeeds', async () => {
    const gateway = freeGateway({
      playCard: vi.fn().mockRejectedValue(new Error('Play response lost.')),
      advance: vi.fn().mockRejectedValueOnce(new Error('Still offline.')).mockResolvedValueOnce(match()),
    })
    const user = await beginFree(gateway)
    await user.click(screen.getByRole('button', { name: 'Two of clubs, play card' }))
    await user.click(await screen.findByRole('button', { name: 'Retry hand' }))
    await screen.findByText('Still offline.')
    const card = screen.getByRole('button', { name: 'Five of clubs, play card' }) as HTMLButtonElement
    expect(card.disabled).toBe(true)
    await user.click(card)
    expect(gateway.playCard).toHaveBeenCalledOnce()
    await user.click(screen.getByRole('button', { name: 'Retry hand' }))
    await waitFor(() => expect(card.disabled).toBe(false))
    expect(gateway.advance).toHaveBeenCalledTimes(2)
    expect(gateway.advance).toHaveBeenNthCalledWith(1, 'practice-hand-one')
    expect(gateway.advance).toHaveBeenNthCalledWith(2, 'practice-hand-one')
    expect(gateway.startMatch).toHaveBeenCalledOnce()
    expect(gateway.getStatus).toHaveBeenCalledOnce()
  })

  it('recovers the dedicated trial identity and coaching without marking experience or starting free play', async () => {
    const trial = match({ matchId: 'intro-hand-one', isTrial: true })
    const gateway = freeGateway({
      onboarding: { getState: vi.fn().mockResolvedValue({ shouldAskPlayedBefore: true }), confirmExperience: vi.fn().mockResolvedValue({ shouldAskPlayedBefore: false }) },
      startTrial: vi.fn().mockResolvedValue(trial),
      cancelTrial: vi.fn().mockResolvedValue({ shouldAskPlayedBefore: true }),
      playCard: vi.fn().mockRejectedValue(new HeartsGatewayError('Trial response lost.', 'offline', 0)),
      advance: vi.fn().mockResolvedValue(match({ ...trial, hand: [five], legalCards: [five] })),
    })
    const user = userEvent.setup()
    render(<HeartsGame gateway={gateway} />)
    await user.click(await screen.findByRole('button', { name: 'Learn to play' }))
    await user.click(await screen.findByRole('button', { name: 'Two of clubs, play card' }))
    await user.click(await screen.findByRole('button', { name: 'Retry hand' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Two of clubs, play card' })).toBeNull())
    expect(screen.getByText('Trial run', { exact: true })).toBeTruthy()
    expect(screen.getByText('One hand · 13 tricks')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Five of clubs, play card' }).getAttribute('aria-describedby')).toBe('hearts-trial-coach')
    expect(screen.getByRole('button', { name: 'Leave trial' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Next round' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Play free' })).toBeNull()
    expect(gateway.advance).toHaveBeenCalledWith('intro-hand-one')
    expect(gateway.startTrial).toHaveBeenCalledOnce()
    expect(gateway.startMatch).not.toHaveBeenCalled()
    expect(gateway.getStatus).toHaveBeenCalledOnce()
    expect(gateway.onboarding!.getState).toHaveBeenCalledOnce()
    expect(gateway.onboarding!.confirmExperience).not.toHaveBeenCalled()
    expect(gateway.cancelTrial).not.toHaveBeenCalled()
    expect(gateway.playCard).toHaveBeenCalledOnce()
  })
})
