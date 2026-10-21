// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { HeartsGame } from './HeartsGame'
import { HeartsGatewayError, type HeartsGateway, type HeartsMatch, type HeartsOnboardingState, type HeartsPaidSession } from './contracts'
import { seatsClockwise } from './heartsHelpers'

const two = { code: '2|clubs', rank: '2', suit: 'clubs', label: '2♣' }
function hand(isTrial = true, overrides: Partial<HeartsMatch> = {}): HeartsMatch {
  return { matchId: 'intro-hand', isTrial, targetScore: 100, roundNumber: 1, phase: 'passing', passDirection: 'left', turn: 'north', humanSeat: 'north', yourTurn: false, opponentsThinking: false, heartsBroken: false, submittedPasses: [], players: seatsClockwise.map((seat, index) => ({ seat, displayName: isTrial && index > 0 ? `Trial bot ${index}` : ['Ada', 'Rowan', 'Clover', 'Quinn'][index], handCount: 13, roundScore: 0, matchScore: 0, hasPassed: false })), hand: [two], legalCards: [], currentTrick: { leader: 'north', plays: [] }, recentTrick: null, completedTricks: [], score: { north: 0, east: 0, south: 0, west: 0 }, roundScore: { north: 0, east: 0, south: 0, west: 0 }, difficulty: 'standard', winner: null, message: '', ...overrides }
}
const tricks = (count: number) => Array.from({ length: count }, (_, index) => ({ number: index + 1, leader: 'north' as const, winner: 'north' as const, points: 0 }))
const idle: HeartsPaidSession = { kind: 'idle', version: 0, balanceCredits: 100, entryOptions: [1, 2, 5], queue: null, table: null }
function api(pending = true): HeartsGateway {
  let shouldAskPlayedBefore = pending
  return {
    onboarding: { getState: vi.fn(async () => ({ shouldAskPlayedBefore })), confirmExperience: vi.fn(async () => { shouldAskPlayedBefore = false; return { shouldAskPlayedBefore } }) },
    getStatus: vi.fn().mockResolvedValue({ available: true, defaultTargetScore: 100, mode: 'free-play' }),
    startTrial: vi.fn().mockResolvedValue(hand()), cancelTrial: vi.fn().mockResolvedValue({ shouldAskPlayedBefore: true }), startMatch: vi.fn().mockResolvedValue(hand(false)), pass: vi.fn().mockResolvedValue(hand()), playCard: vi.fn().mockResolvedValue(hand()), advance: vi.fn().mockResolvedValue(hand()), nextRound: vi.fn().mockResolvedValue(hand(false)),
    paid: { getSession: vi.fn().mockResolvedValue(idle), getMatch: vi.fn().mockResolvedValue(idle), join: vi.fn().mockResolvedValue(idle), pass: vi.fn().mockResolvedValue(idle), play: vi.fn().mockResolvedValue(idle), advance: vi.fn().mockResolvedValue(idle), nextRound: vi.fn().mockResolvedValue(idle), leave: vi.fn().mockResolvedValue(idle), getHistory: vi.fn().mockResolvedValue([]) },
  }
}
function expectNoModeChoices() {
  expect(screen.queryByRole('button', { name: 'Play free' })).toBeNull()
  expect(screen.queryByRole('button', { name: /^Play for R/ })).toBeNull()
  expect(screen.queryByLabelText('Choose your table buy-in')).toBeNull()
}
async function beginTrial(gateway: HeartsGateway) {
  const user = userEvent.setup()
  render(<HeartsGame gateway={gateway} />)
  await user.click(await screen.findByRole('button', { name: 'Learn to play' }))
  await screen.findByLabelText('Your hand')
  return user
}
beforeEach(() => { vi.stubGlobal('Audio', class { volume = 0; play() { return Promise.resolve() } pause() {} addEventListener() {} }); localStorage.setItem('fortuneforge:hearts:muted', 'true') })
afterEach(() => { cleanup(); vi.unstubAllGlobals(); localStorage.clear() })

describe('account Hearts onboarding', () => {
  it('shows no mode choices while loading and orders Learn to play above I know how to play', async () => {
    const gateway = api()
    let finish!: (state: HeartsOnboardingState) => void
    gateway.onboarding!.getState = vi.fn(() => new Promise<HeartsOnboardingState>(resolve => { finish = resolve }))
    render(<HeartsGame gateway={gateway} />)
    expect(screen.getByRole('heading', { name: 'Opening Hearts' })).toBeTruthy()
    expectNoModeChoices()
    await act(async () => finish({ shouldAskPlayedBefore: true }))
    expect(await screen.findByRole('heading', { name: 'Have you played Hearts before?' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'I know how to play' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Learn to play' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: /^No$/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /^Yes$/ })).toBeNull()
    const answers = screen.getByRole('button', { name: 'Learn to play' }).parentElement!
    expect([...answers.querySelectorAll('button')].map(button => button.textContent)).toEqual(['Learn to play', 'I know how to play'])
    expectNoModeChoices()
    expect(gateway.startMatch).not.toHaveBeenCalled()
    expect(gateway.paid!.join).not.toHaveBeenCalled()
  })

  it('keeps the question gated until experience has been saved and reads that account answer on remount', async () => {
    const user = userEvent.setup(), gateway = api()
    let save!: (state: HeartsOnboardingState) => void
    gateway.onboarding!.confirmExperience = vi.fn(() => new Promise<HeartsOnboardingState>(resolve => { save = resolve }))
    const rendered = render(<HeartsGame gateway={gateway} />)
    await user.click(await screen.findByRole('button', { name: 'I know how to play' }))
    expect(screen.getByText('Saving your answer…')).toBeTruthy()
    expectNoModeChoices()
    expect((screen.getByRole('button', { name: 'I know how to play' }) as HTMLButtonElement).disabled).toBe(true)
    await act(async () => save({ shouldAskPlayedBefore: false }))
    expect(await screen.findByRole('button', { name: 'Play free' })).toBeTruthy()
    expect(gateway.startTrial).not.toHaveBeenCalled()
    rendered.unmount()
    gateway.onboarding!.getState = vi.fn().mockResolvedValue({ shouldAskPlayedBefore: false })
    render(<HeartsGame gateway={gateway} />)
    expect(await screen.findByRole('button', { name: 'Play free' })).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Have you played Hearts before?' })).toBeNull()
  })

  it('keeps a failed experience answer pending and lets that action retry', async () => {
    const user = userEvent.setup(), gateway = api()
    gateway.onboarding!.confirmExperience = vi.fn().mockRejectedValueOnce(new Error('Answer was not saved.')).mockResolvedValueOnce({ shouldAskPlayedBefore: false })
    render(<HeartsGame gateway={gateway} />)
    await user.click(await screen.findByRole('button', { name: 'I know how to play' }))
    expect(await screen.findByText('Answer was not saved.')).toBeTruthy()
    expectNoModeChoices()
    await user.click(screen.getByRole('button', { name: 'I know how to play' }))
    expect(await screen.findByRole('button', { name: 'Play free' })).toBeTruthy()
    expect(gateway.onboarding!.confirmExperience).toHaveBeenCalledTimes(2)
  })

  it('does not expose mode choices when account setup fails to load and offers a retry', async () => {
    const user = userEvent.setup(), gateway = api()
    gateway.onboarding!.getState = vi.fn().mockRejectedValueOnce(new Error('Setup connection lost.')).mockResolvedValueOnce({ shouldAskPlayedBefore: true })
    render(<HeartsGame gateway={gateway} />)
    await user.click(await screen.findByRole('button', { name: 'Retry setup' }))
    expect(await screen.findByRole('button', { name: 'Learn to play' })).toBeTruthy()
    expectNoModeChoices()
    expect(gateway.onboarding!.getState).toHaveBeenCalledTimes(2)
  })

  it('opens the dedicated Trial from Learn to play without confirming experience or starting Free Play', async () => {
    const gateway = api()
    await beginTrial(gateway)
    expect(gateway.startTrial).toHaveBeenCalledOnce()
    expect(gateway.startMatch).not.toHaveBeenCalled()
    expect(gateway.onboarding!.confirmExperience).not.toHaveBeenCalled()
    expect(screen.getByText('Trial run · No entry')).toBeTruthy()
    expect(screen.getByText('Trial run', { exact: true })).toBeTruthy()
    expect(screen.getByText('One hand · 13 tricks')).toBeTruthy()
    expect(screen.getByText('Trial bot 1')).toBeTruthy()
    expect(screen.getByText('Trial bot 2')).toBeTruthy()
    expect(screen.getByText('Trial bot 3')).toBeTruthy()
    expectNoModeChoices()
    expect(screen.queryByRole('button', { name: 'Next round' })).toBeNull()
  })

  it('suggests passing three own cards, then guides confirmation without auto-selecting them', async () => {
    const gateway = api()
    const queen = { code: 'Q|spades', rank: 'queen', suit: 'spades', label: 'Q♠' }, king = { code: 'K|spades', rank: 'king', suit: 'spades', label: 'K♠' }, heart = { code: '5|hearts', rank: '5', suit: 'hearts', label: '5♥' }
    gateway.startTrial = vi.fn().mockResolvedValue(hand(true, { hand: [two, queen, king, heart] }))
    const user = await beginTrial(gateway)
    const suggested = ['Queen of spades', 'King of spades', 'Five of hearts'].map(name => screen.getByRole('button', { name: `${name}, select to pass` }))
    for (const card of suggested) {
      expect(card.className).toContain('is-suggested')
      expect(card.getAttribute('aria-pressed')).toBe('false')
      await user.click(card)
    }
    expect(screen.getByText('Three picked—send them!')).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Pass cards 3/3' }) as HTMLButtonElement).disabled).toBe(false)
    expect(gateway.pass).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Two of clubs, select to pass' }).className).not.toContain('is-suggested')
  })

  it('allows another legal card instead of forcing the coached suggestion', async () => {
    const gateway = api(), five = { code: '5|clubs', rank: '5', suit: 'clubs', label: '5♣' }
    gateway.startTrial = vi.fn().mockResolvedValue(hand(true, { phase: 'playing', yourTurn: true, hand: [two, five], legalCards: [two, five] }))
    const user = await beginTrial(gateway)
    expect(screen.getByRole('button', { name: 'Two of clubs, play card' }).className).toContain('is-suggested')
    await user.click(screen.getByRole('button', { name: 'Five of clubs, play card' }))
    expect(gateway.playCard).toHaveBeenCalledWith('intro-hand', '5|clubs')
  })

  it('retains the question after a trial start fails and lets Learn to play retry', async () => {
    const user = userEvent.setup(), gateway = api()
    gateway.startTrial = vi.fn().mockRejectedValueOnce(new Error('Trial unavailable.')).mockResolvedValueOnce(hand())
    render(<HeartsGame gateway={gateway} />)
    await user.click(await screen.findByRole('button', { name: 'Learn to play' }))
    expect(await screen.findByText('Trial unavailable.')).toBeTruthy()
    expectNoModeChoices()
    await user.click(screen.getByRole('button', { name: 'Learn to play' }))
    expect(await screen.findByText('Let’s pass three troublemakers')).toBeTruthy()
    expect(gateway.startTrial).toHaveBeenCalledTimes(2)
  })

  it('returns an interrupted trial to the question only after the server releases the trial', async () => {
    const gateway = api()
    let close!: (state: HeartsOnboardingState) => void
    gateway.cancelTrial = vi.fn(() => new Promise<HeartsOnboardingState>(resolve => { close = resolve }))
    const user = await beginTrial(gateway)
    await user.click(screen.getByRole('button', { name: 'Leave trial' }))
    expect(gateway.cancelTrial).toHaveBeenCalledWith('intro-hand')
    expect(screen.getByLabelText('Your hand')).toBeTruthy()
    expectNoModeChoices()
    await act(async () => close({ shouldAskPlayedBefore: true }))
    expect(await screen.findByRole('heading', { name: 'Have you played Hearts before?' })).toBeTruthy()
    expectNoModeChoices()
    expect(gateway.onboarding!.confirmExperience).not.toHaveBeenCalled()
  })

  it('keeps an unconfirmed trial exit retryable without exposing mode choices', async () => {
    const gateway = api()
    gateway.cancelTrial = vi.fn().mockRejectedValueOnce(new Error('Trial cleanup offline.')).mockResolvedValueOnce({ shouldAskPlayedBefore: true })
    const user = await beginTrial(gateway)
    await user.click(screen.getByRole('button', { name: 'Leave trial' }))
    const retry = await screen.findByRole('button', { name: 'Retry closing trial' })
    expect(screen.getByLabelText('Your hand')).toBeTruthy()
    expectNoModeChoices()
    await user.click(retry)
    expect(await screen.findByRole('button', { name: 'Learn to play' })).toBeTruthy()
    expectNoModeChoices()
    expect(gateway.cancelTrial).toHaveBeenCalledTimes(2)
  })

  it('recovers a trial lost after a server restart by rereading account setup without marking it complete', async () => {
    const gateway = api()
    gateway.cancelTrial = vi.fn().mockRejectedValue(new HeartsGatewayError('Trial no longer exists.', 'hearts-match-not-found', 404))
    const user = await beginTrial(gateway)
    await user.click(screen.getByRole('button', { name: 'Leave trial' }))
    expect(await screen.findByRole('button', { name: 'Learn to play' })).toBeTruthy()
    expect(gateway.onboarding!.getState).toHaveBeenCalledTimes(2)
    expect(gateway.onboarding!.confirmExperience).not.toHaveBeenCalled()
    expectNoModeChoices()
  })

  it('does not mistake an old paid result for an active table that may bypass the entry question', async () => {
    const gateway = api()
    gateway.paid!.getSession = vi.fn().mockResolvedValue({ ...idle, kind: 'result', table: { match: hand(false, { phase: 'complete', winner: 'north' }), entryCredits: 1, potCredits: 4, payoutCredits: 4, winnerSeats: ['north'], players: [], actionDeadlineAtUtc: null, completedAtUtc: new Date().toISOString() } })
    render(<HeartsGame gateway={gateway} />)
    expect(await screen.findByRole('heading', { name: 'Have you played Hearts before?' })).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'You win!' })).toBeNull()
    expectNoModeChoices()
  })

  it('requires all 13 trial tricks and a fresh server confirmation before exposing table choices', async () => {
    const gateway = api()
    gateway.startTrial = vi.fn().mockResolvedValue(hand(true, { phase: 'playing', yourTurn: true, legalCards: [two], completedTricks: tricks(12) }))
    gateway.playCard = vi.fn().mockResolvedValue(hand(true, { phase: 'complete', hand: [], legalCards: [], completedTricks: tricks(13) }))
    gateway.onboarding!.getState = vi.fn().mockResolvedValueOnce({ shouldAskPlayedBefore: true }).mockResolvedValueOnce({ shouldAskPlayedBefore: false })
    const user = await beginTrial(gateway)
    expect(gateway.onboarding!.getState).toHaveBeenCalledOnce()
    expectNoModeChoices()
    await user.click(screen.getByRole('button', { name: 'Two of clubs, play card' }))
    expect(await screen.findByRole('heading', { name: 'Trial complete' })).toBeTruthy()
    expect(gateway.onboarding!.getState).toHaveBeenCalledTimes(2)
    expect(gateway.onboarding!.confirmExperience).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: 'Next round' })).toBeNull()
    expectNoModeChoices()
    await user.click(await screen.findByRole('button', { name: 'Choose how to play' }))
    expect(await screen.findByRole('button', { name: 'Play free' })).toBeTruthy()
    expect(gateway.nextRound).not.toHaveBeenCalled()
  })

  it('keeps a completed trial separate when confirming completion fails, then retries only the read', async () => {
    const gateway = api()
    gateway.startTrial = vi.fn().mockResolvedValue(hand(true, { phase: 'complete', hand: [], completedTricks: tricks(13) }))
    gateway.onboarding!.getState = vi.fn().mockResolvedValueOnce({ shouldAskPlayedBefore: true }).mockRejectedValueOnce(new Error('Confirmation offline.')).mockResolvedValueOnce({ shouldAskPlayedBefore: false })
    const user = await beginTrial(gateway)
    await user.click(await screen.findByRole('button', { name: 'Retry confirmation' }))
    expect(await screen.findByRole('button', { name: 'Choose how to play' })).toBeTruthy()
    expect(gateway.startTrial).toHaveBeenCalledOnce()
    expect(gateway.onboarding!.confirmExperience).not.toHaveBeenCalled()
    expectNoModeChoices()
  })

  it('never records ordinary Free Play as trial completion', async () => {
    const user = userEvent.setup(), gateway = api(false)
    gateway.startMatch = vi.fn().mockResolvedValue(hand(false, { phase: 'complete', winner: 'north', completedTricks: tricks(13), hand: [] }))
    render(<HeartsGame gateway={gateway} />)
    await user.click(await screen.findByRole('button', { name: 'Play free' }))
    expect(await screen.findByRole('heading', { name: 'You win!' })).toBeTruthy()
    expect(gateway.onboarding!.getState).toHaveBeenCalledOnce()
    expect(gateway.onboarding!.confirmExperience).not.toHaveBeenCalled()
    expect(gateway.startTrial).not.toHaveBeenCalled()
    expect(screen.queryByRole('heading', { name: 'Trial complete' })).toBeNull()
  })

  it.each(['queue', 'match'] as const)('does not interrupt an already-active paid %s with the pending question', async kind => {
    const gateway = api()
    const session: HeartsPaidSession = kind === 'queue' ? { ...idle, kind, queue: { matchId: 'paid-q', entryCredits: 1, potCredits: 1, joinedAtUtc: new Date().toISOString(), players: [{ seat: 'north', displayName: 'Ada', isCurrentPlayer: true }], message: '' } } : { ...idle, kind, table: { match: hand(false), entryCredits: 1, potCredits: 4, payoutCredits: 0, winnerSeats: [], players: [], actionDeadlineAtUtc: null, completedAtUtc: null } }
    gateway.paid!.getSession = vi.fn().mockResolvedValue(session)
    render(<HeartsGame gateway={gateway} />)
    if (kind === 'queue') expect(await screen.findByRole('heading', { name: 'Finding your table' })).toBeTruthy()
    else expect(await screen.findByLabelText('Your hand')).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Have you played Hearts before?' })).toBeNull()
    expectNoModeChoices()
    expect(gateway.paid!.join).not.toHaveBeenCalled()
  })

  it('isolates the gate when the signed-in account key changes', async () => {
    const first = api(false), second = api(true)
    const rendered = render(<HeartsGame key="account-one" gateway={first} />)
    await screen.findByRole('button', { name: 'Play free' })
    rendered.rerender(<HeartsGame key="account-two" gateway={second} />)
    expectNoModeChoices()
    expect(await screen.findByRole('button', { name: 'Learn to play' })).toBeTruthy()
    expectNoModeChoices()
  })

  it('restores an active paid hand immediately even while the onboarding read is still pending', async () => {
    const gateway = api()
    gateway.onboarding!.getState = vi.fn(() => new Promise<HeartsOnboardingState>(() => undefined))
    gateway.paid!.getSession = vi.fn().mockResolvedValue({ ...idle, kind: 'match', table: { match: hand(false), entryCredits: 1, potCredits: 4, payoutCredits: 0, winnerSeats: [], players: [], actionDeadlineAtUtc: null, completedAtUtc: null } })
    render(<HeartsGame gateway={gateway} />)
    expect(await screen.findByLabelText('Your hand')).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Have you played Hearts before?' })).toBeNull()
    expectNoModeChoices()
  })

  it('starts keyboard-accessible Trial run directly on the board with a card-anchored coach', async () => {
    const user = userEvent.setup(), gateway = api()
    render(<HeartsGame gateway={gateway} />)
    const learn = await screen.findByRole('button', { name: 'Learn to play' })
    learn.focus()
    await user.keyboard('{Enter}')
    expect(await screen.findByText('Let’s pass three troublemakers')).toBeTruthy()
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Start hand' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Two of clubs, select to pass' }).getAttribute('aria-describedby')).toBe('hearts-trial-coach')
    expectNoModeChoices()
  })

  it('explains the current suggestion inline before offering the complete friendly rules', async () => {
    const gateway = api(), queen = { code: 'Q|spades', rank: 'queen', suit: 'spades', label: 'Q♠' }
    gateway.startTrial = vi.fn().mockResolvedValue(hand(true, { hand: [two, queen] }))
    const user = await beginTrial(gateway)
    const why = screen.getByText('Why?')
    expect(why.parentElement?.hasAttribute('open')).toBe(false)
    await user.click(why)
    expect(why.parentElement?.hasAttribute('open')).toBe(true)
    expect(why.parentElement?.textContent).toMatch(/queen of spades.*13/i)
    expect(screen.queryByRole('dialog')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'All rules & tips' }))
    expect(await screen.findByRole('dialog', { name: 'How to play Hearts' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Around the table we go' })).toBeTruthy()
    expect(screen.getByText(/Play moves clockwise, one card each/)).toBeTruthy()
    const strategy = screen.getByText('Why keep or pass certain cards?')
    await user.tab()
    expect(document.activeElement).toBe(strategy)
    await user.click(strategy)
    expect(strategy.parentElement?.hasAttribute('open')).toBe(true)
  })
})
