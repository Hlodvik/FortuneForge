// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { HeartsGame } from './HeartsGame'
import { HeartsGatewayError, type HeartsCard, type HeartsGateway, type HeartsMatch, type HeartsPaidGateway, type HeartsPaidSession } from './contracts'
import { seatsClockwise } from './heartsHelpers'

const card = (rank: string, suit = 'clubs'): HeartsCard => ({ code: `${rank}|${suit}`, rank, suit, label: rank + ({ clubs: '♣', hearts: '♥', spades: '♠', diamonds: '♦' } as Record<string, string>)[suit] })
const match = (overrides: Partial<HeartsMatch> = {}): HeartsMatch => ({ matchId: 'match-one', targetScore: 100, roundNumber: 1, phase: 'passing', passDirection: 'left', turn: 'north', humanSeat: 'north', yourTurn: false, opponentsThinking: false, heartsBroken: false, submittedPasses: [], players: seatsClockwise.map((seat, index) => ({ seat, displayName: ['Ada', 'Rowan', 'Clover', 'Quinn'][index], handCount: 13, roundScore: 0, matchScore: 0, hasPassed: false })), hand: [card('2'), card('5'), card('K'), card('Q', 'spades')], legalCards: [], currentTrick: { leader: 'north', plays: [] }, recentTrick: null, completedTricks: [], score: { north: 0, east: 0, south: 0, west: 0 }, roundScore: { north: 0, east: 0, south: 0, west: 0 }, difficulty: 'standard', winner: null, message: '', ...overrides })
const idle = (): HeartsPaidSession => ({ kind: 'idle', version: 0, balanceCredits: 100, entryOptions: [1, 2, 5, 10, 25], queue: null, table: null })
function atTable(overrides: Partial<HeartsMatch> = {}): HeartsPaidSession {
  const current = match(overrides)
  return { ...idle(), kind: 'match', version: 4, balanceCredits: 99, table: { match: current, entryCredits: 1, potCredits: 4, payoutCredits: 0, winnerSeats: [], players: current.players.map((player, index) => ({ seat: player.seat, displayName: player.displayName, isCurrentPlayer: index === 0, hasLeft: false, entryCredits: 1, payoutCredits: 0 })), actionDeadlineAtUtc: null, completedAtUtc: null } }
}
function gateway(overrides: Partial<HeartsGateway> = {}, paidOverrides: Partial<HeartsPaidGateway> = {}): HeartsGateway {
  return {
    getStatus: vi.fn().mockResolvedValue({ available: true, defaultTargetScore: 100, mode: 'free-play' }),
    startMatch: vi.fn().mockResolvedValue(match()), pass: vi.fn().mockResolvedValue(match({ phase: 'playing' })), playCard: vi.fn().mockResolvedValue(match({ phase: 'playing' })), advance: vi.fn().mockResolvedValue(match()), nextRound: vi.fn().mockResolvedValue(match({ roundNumber: 2 })),
    paid: { getSession: vi.fn().mockResolvedValue(idle()), join: vi.fn().mockResolvedValue(atTable()), getMatch: vi.fn().mockResolvedValue(atTable()), pass: vi.fn().mockResolvedValue(atTable({ phase: 'playing' })), play: vi.fn().mockResolvedValue(atTable({ phase: 'playing' })), advance: vi.fn().mockResolvedValue(atTable()), nextRound: vi.fn().mockResolvedValue(atTable({ roundNumber: 2 })), leave: vi.fn().mockResolvedValue(idle()), getHistory: vi.fn().mockResolvedValue([]), ...paidOverrides }, ...overrides,
  }
}
beforeEach(() => { vi.stubGlobal('Audio', class { volume = 0; play() { return Promise.resolve() } pause() {} addEventListener() {} }); localStorage.setItem('fortuneforge:hearts:muted', 'true') })
afterEach(() => { cleanup(); vi.unstubAllGlobals(); localStorage.clear() })

describe('Hearts table', () => {
  it('does not open or charge for a match on mount and starts free only after a click', async () => {
    const user = userEvent.setup(), api = gateway()
    render(<HeartsGame gateway={api} balance={100} />)
    const free = await screen.findByRole('button', { name: 'Play free' })
    await waitFor(() => expect((free as HTMLButtonElement).disabled).toBe(false))
    expect(api.startMatch).not.toHaveBeenCalled(); expect(api.paid!.join).not.toHaveBeenCalled()
    await user.click(free)
    expect(await screen.findByLabelText('Your hand')).toBeTruthy()
    expect(api.startMatch).toHaveBeenCalledWith({ targetScore: 100, difficulty: 'standard' })
    expect(api.paid!.join).not.toHaveBeenCalled()
  })

  it('chooses a fixed buy-in without charging and enters with a unique request key when pressed', async () => {
    const user = userEvent.setup(), api = gateway()
    render(<HeartsGame gateway={api} />)
    await waitFor(() => expect((screen.getByRole('button', { name: 'Select R5.00 entry' }) as HTMLButtonElement).disabled).toBe(false))
    await user.click(screen.getByRole('button', { name: 'Select R5.00 entry' }))
    expect(api.paid!.join).not.toHaveBeenCalled()
    expect(screen.getByText('R20.00')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Play for R5.00' }))
    await screen.findByLabelText('Your hand')
    expect(api.paid!.join).toHaveBeenCalledWith(5, expect.objectContaining({ idempotencyKey: expect.stringMatching(/^hearts-/), expectedVersion: 0 }))
  })

  it('resumes a paid hand without a second entry and keeps the viewer at the bottom', async () => {
    const restored = atTable({ humanSeat: 'east' })
    const api = gateway({}, { getSession: vi.fn().mockResolvedValue(restored) })
    const rendered = render(<HeartsGame gateway={api} />)
    await screen.findByLabelText('Your hand')
    expect(api.startMatch).not.toHaveBeenCalled(); expect(api.paid!.join).not.toHaveBeenCalled()
    expect(rendered.container.querySelector('.ff-hearts-seat.seat-bottom')?.textContent).toContain('You')
    expect(rendered.container.querySelectorAll('.ff-hearts-seat')).toHaveLength(4)
  })

  it('requires exactly three selected cards and submits the pass once', async () => {
    const user = userEvent.setup(), api = gateway()
    render(<HeartsGame gateway={api} />)
    await user.click(await enabledButton('Play free'))
    await user.click(await screen.findByRole('button', { name: 'Two of clubs, select to pass' }))
    await user.click(screen.getByRole('button', { name: 'Five of clubs, select to pass' }))
    expect((screen.getByRole('button', { name: 'Pass cards 2/3' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'King of clubs, select to pass' }))
    await user.click(screen.getByRole('button', { name: 'Queen of spades, select to pass' }))
    expect(screen.getByRole('button', { name: 'Queen of spades, select to pass' }).getAttribute('aria-pressed')).toBe('false')
    await user.click(screen.getByRole('button', { name: 'Pass cards 3/3' }))
    await waitFor(() => expect(api.pass).toHaveBeenCalledOnce())
    expect(api.pass).toHaveBeenCalledWith('match-one', ['2|clubs', '5|clubs', 'K|clubs'])
  })

  it('reviews passing picks and removes an off-screen selection without submitting it', async () => {
    const user = userEvent.setup(), api = gateway()
    render(<HeartsGame gateway={api} />)
    await user.click(await enabledButton('Play free'))
    await user.click(await screen.findByRole('button', { name: 'Two of clubs, select to pass' }))
    await user.click(screen.getByRole('button', { name: 'Five of clubs, select to pass' }))
    await user.click(screen.getByRole('button', { name: 'King of clubs, select to pass' }))
    await user.click(screen.getByRole('button', { name: 'Queen of spades, select to pass' }))
    expect(screen.getByText('Three picked! Remove a card below to choose another.')).toBeTruthy()
    const picks = screen.getByRole('group', { name: 'Cards selected to pass' })
    await user.click(within(picks).getByRole('button', { name: 'Remove King of clubs from pass' }))
    expect((screen.getByRole('button', { name: 'Pass cards 2/3' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Queen of spades, select to pass' }))
    expect(within(picks).getByRole('button', { name: 'Remove Queen of spades from pass' })).toBeTruthy()
    expect(api.pass).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'Pass cards 3/3' }))
    expect(api.pass).toHaveBeenCalledWith('match-one', ['2|clubs', '5|clubs', 'Q|spades'])
  })

  it('explains why cards are unavailable in ordinary play without requiring the trial', async () => {
    const user = userEvent.setup(), current = match({ phase: 'playing', yourTurn: true, legalCards: [card('2')], currentTrick: { leader: 'east', plays: [{ seat: 'east', card: card('K') }] } })
    render(<HeartsGame gateway={gateway({ startMatch: vi.fn().mockResolvedValue(current) })} />)
    await user.click(await enabledButton('Play free'))
    expect(await screen.findByText('Clubs were led, so you must follow clubs.')).toBeTruthy()
  })

  it('plays a legal card in one click and does not enable off-suit cards', async () => {
    const user = userEvent.setup(), current = match({ phase: 'playing', yourTurn: true, legalCards: [card('2')] })
    const api = gateway({ startMatch: vi.fn().mockResolvedValue(current) })
    render(<HeartsGame gateway={api} />)
    await user.click(await enabledButton('Play free'))
    const legal = await screen.findByRole('button', { name: 'Two of clubs, play card' })
    expect((screen.getByRole('button', { name: 'Queen of spades' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(legal)
    await waitFor(() => expect(api.playCard).toHaveBeenCalledWith('match-one', '2|clubs'))
  })

  it('selects and changes a mobile card without playing, then confirms exactly once', async () => {
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() }))
    const user = userEvent.setup(), current = atTable({ phase: 'playing', yourTurn: true, legalCards: [card('2'), card('5')] })
    const api = gateway({}, { getSession: vi.fn().mockResolvedValue(current) })
    render(<HeartsGame gateway={api} />)
    const first = await screen.findByRole('button', { name: 'Two of clubs, select to play' })
    expect((screen.getByRole('button', { name: 'Select a card' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(first)
    expect(screen.getByRole('button', { name: 'Two of clubs, selected to play' }).getAttribute('aria-pressed')).toBe('true')
    expect(api.paid!.play).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'Five of clubs, select to play' }))
    expect(first.getAttribute('aria-pressed')).toBe('false')
    expect((screen.getByRole('button', { name: 'Queen of spades' }) as HTMLButtonElement).disabled).toBe(true)
    await user.dblClick(screen.getByRole('button', { name: 'Play 5♣' }))
    await waitFor(() => expect(api.paid!.play).toHaveBeenCalledOnce())
    expect(api.paid!.play).toHaveBeenCalledWith('match-one', '5|clubs', expect.objectContaining({ expectedVersion: 4 }))
  })

  it('does not select a mobile card at the end of a swipe, including during passing', async () => {
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() }))
    const user = userEvent.setup(), api = gateway()
    render(<HeartsGame gateway={api} />)
    await user.click(await enabledButton('Play free'))
    const target = await screen.findByRole('button', { name: 'Two of clubs, select to pass' })
    fireEvent.pointerDown(target, { clientX: 150, clientY: 400 })
    fireEvent.pointerMove(target, { clientX: 90, clientY: 400 })
    fireEvent.click(target, { detail: 1 })
    expect(target.getAttribute('aria-pressed')).toBe('false')
    expect(api.pass).not.toHaveBeenCalled()
    await user.click(target)
    expect(target.getAttribute('aria-pressed')).toBe('true')
  })

  it('clears a mobile play selection after an authoritative turn update', async () => {
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() }))
    const user = userEvent.setup(), current = atTable({ phase: 'playing', yourTurn: true, legalCards: [card('2')] })
    let rejectPlay!: (reason: Error) => void
    const api = gateway({}, { getSession: vi.fn().mockResolvedValueOnce(current).mockResolvedValue(atTable({ phase: 'playing', yourTurn: false, turn: 'east', legalCards: [] })), play: vi.fn().mockImplementation(() => new Promise((_, reject) => { rejectPlay = reject })) })
    render(<HeartsGame gateway={api} />)
    await user.click(await screen.findByRole('button', { name: 'Two of clubs, select to play' }))
    await user.click(screen.getByRole('button', { name: 'Play 2♣' }))
    rejectPlay(new HeartsGatewayError('Refresh needed.', 'stale', 409))
    await user.click(await screen.findByRole('button', { name: 'Refresh table' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Play 2♣' })).toBeNull())
    expect(screen.queryByRole('button', { name: 'Two of clubs, selected to play' })).toBeNull()
    expect(api.paid!.play).toHaveBeenCalledOnce()
  })

  it('locks uncertain paid writes until a read refresh confirms the authoritative table', async () => {
    const user = userEvent.setup(), current = atTable({ phase: 'playing', yourTurn: true, legalCards: [card('2')] })
    const play = vi.fn().mockRejectedValue(new HeartsGatewayError('Connection lost.', 'offline', 0))
    const api = gateway({}, { getSession: vi.fn().mockResolvedValue(current), play })
    render(<HeartsGame gateway={api} />)
    await user.click(await screen.findByRole('button', { name: 'Two of clubs, play card' }))
    const refresh = await screen.findByRole('button', { name: 'Refresh table' })
    expect((screen.getByRole('button', { name: 'Two of clubs, play card' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(refresh)
    await waitFor(() => expect((screen.getByRole('button', { name: 'Two of clubs, play card' }) as HTMLButtonElement).disabled).toBe(false))
    expect(play).toHaveBeenCalledOnce()
    expect(play).toHaveBeenCalledWith('match-one', '2|clubs', expect.objectContaining({ expectedVersion: 4 }))
  })

  it('cancels a queued seat and publishes the returned balance', async () => {
    const user = userEvent.setup(), publish = vi.fn()
    const queued: HeartsPaidSession = { ...idle(), kind: 'queue', version: 2, balanceCredits: 99, queue: { matchId: 'q1', entryCredits: 1, potCredits: 1, joinedAtUtc: new Date().toISOString(), message: '', players: [{ seat: 'north', displayName: 'Ada', isCurrentPlayer: true }] } }
    const api = gateway({}, { getSession: vi.fn().mockResolvedValue(queued) })
    render(<HeartsGame gateway={api} onBalanceChange={publish} />)
    await user.click(await screen.findByRole('button', { name: 'Cancel & return entry' }))
    await screen.findByRole('button', { name: 'Play free' })
    expect(api.paid!.leave).toHaveBeenCalledWith('q1', expect.objectContaining({ expectedVersion: 2 }))
    expect(publish).toHaveBeenLastCalledWith(100)
  })

  it('shows tied winners and each share from the server settlement', async () => {
    const settled = atTable({ phase: 'complete', winner: 'north', score: { north: 48, east: 48, south: 104, west: 70 } })
    const result = { ...settled, kind: 'result' as const, table: { ...settled.table!, winnerSeats: ['north', 'east'] as const, payoutCredits: 2, completedAtUtc: new Date().toISOString(), players: settled.table!.players.map(player => ({ ...player, payoutCredits: player.seat === 'north' || player.seat === 'east' ? 2 : 0 })) } }
    render(<HeartsGame gateway={gateway({}, { getSession: vi.fn().mockResolvedValue(result) })} />)
    expect(await screen.findByText('You share the win!')).toBeTruthy()
    expect(screen.getAllByText('R2.00').length).toBeGreaterThan(1)
  })

  it('opens bounded instructions and restores focus when the dialog closes', async () => {
    const user = userEvent.setup()
    render(<HeartsGame gateway={gateway()} />)
    const trigger = screen.getByRole('button', { name: 'How to play' })
    await user.click(trigger)
    expect(screen.getByRole('dialog', { name: 'How to play Hearts' })).toBeTruthy()
    expect(screen.getByText(/Tied winners split it/)).toBeTruthy()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.activeElement).toBe(trigger)
  })

  it('shows history loading rather than a false empty result, with errors and retry inside its dialog', async () => {
    const user = userEvent.setup()
    let failHistory!: (reason: Error) => void
    const api = gateway({}, { getHistory: vi.fn().mockImplementationOnce(() => new Promise((_, reject) => { failHistory = reject })).mockResolvedValueOnce([]) })
    render(<HeartsGame gateway={api} />)
    await user.click(await screen.findByRole('button', { name: 'Match history' }))
    const dialog = screen.getByRole('dialog', { name: 'Match history' })
    expect(within(dialog).getByText('Loading match history…')).toBeTruthy()
    expect(within(dialog).queryByText('No completed chip matches yet.')).toBeNull()
    failHistory(new Error('History is offline.'))
    await user.click(await within(dialog).findByRole('button', { name: 'Retry history' }))
    expect(await within(dialog).findByText('No completed chip matches yet.')).toBeTruthy()
    expect(api.paid!.getHistory).toHaveBeenCalledTimes(2)
    expect(api.paid!.join).not.toHaveBeenCalled()
  })

  it('aborts a history read when its window closes and ignores a late response', async () => {
    const user = userEvent.setup()
    let finishHistory!: (items: []) => void
    const getHistory = vi.fn().mockImplementationOnce(() => new Promise(resolve => { finishHistory = resolve })).mockResolvedValueOnce([])
    render(<HeartsGame gateway={gateway({}, { getHistory })} />)
    await user.click(await screen.findByRole('button', { name: 'Match history' }))
    const signal = getHistory.mock.calls[0][0] as AbortSignal
    await user.click(screen.getByRole('button', { name: 'Close dialog' }))
    expect(signal.aborted).toBe(true)
    finishHistory([])
    await user.click(screen.getByRole('button', { name: 'Match history' }))
    expect(await screen.findByText('No completed chip matches yet.')).toBeTruthy()
  })

  it('keeps a competitive turn countdown visible while help is open', async () => {
    const user = userEvent.setup(), source = atTable({ phase: 'playing', yourTurn: true, legalCards: [card('2')] })
    const current = { ...source, table: { ...source.table!, actionDeadlineAtUtc: new Date(Date.now() + 45000).toISOString() } }
    render(<HeartsGame gateway={gateway({}, { getSession: vi.fn().mockResolvedValue(current) })} />)
    await screen.findByLabelText('Your hand')
    await user.click(screen.getByRole('button', { name: 'How to play' }))
    const help = screen.getByRole('dialog', { name: 'How to play Hearts' })
    expect(within(help).getByText('Competitive play continues while this window is open.')).toBeTruthy()
    expect(within(help).getByLabelText(/seconds remaining for your turn/)).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Two of clubs, play card' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('warns before a competitive expiry and disables expired card submissions', async () => {
    const user = userEvent.setup(), source = atTable({ phase: 'playing', yourTurn: true, legalCards: [card('2')] })
    const expiring = { ...source, table: { ...source.table!, actionDeadlineAtUtc: new Date(Date.now() + 5000).toISOString() } }
    const first = render(<HeartsGame gateway={gateway({}, { getSession: vi.fn().mockResolvedValue(expiring) })} />)
    expect(await screen.findByText('Play now—timeout forfeits your entry.')).toBeTruthy()
    first.unmount()
    const expired = { ...source, table: { ...source.table!, actionDeadlineAtUtc: new Date(Date.now() - 1000).toISOString() } }
    const api = gateway({}, { getSession: vi.fn().mockResolvedValue(expired) })
    render(<HeartsGame gateway={api} />)
    const cardButton = await screen.findByRole('button', { name: 'Two of clubs, play card' })
    expect((cardButton as HTMLButtonElement).disabled).toBe(true)
    await user.click(cardButton)
    expect(api.paid!.play).not.toHaveBeenCalled()
    expect(screen.getAllByText('Time’s up—checking the table.').length).toBeGreaterThan(0)
  })

  it('waits for free-table cleanup and keeps a failed leave retryable', async () => {
    const user = userEvent.setup(), leaveMatch = vi.fn().mockRejectedValueOnce(new Error('Could not close table.')).mockResolvedValueOnce(undefined)
    const api = gateway({ leaveMatch })
    render(<HeartsGame gateway={api} />)
    await user.click(await enabledButton('Play free'))
    await screen.findByLabelText('Your hand')
    await user.click(screen.getByRole('button', { name: 'Leave table' }))
    const retry = await screen.findByRole('button', { name: 'Retry leaving table' })
    expect(screen.getByLabelText('Your hand')).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Two of clubs, select to pass' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(retry)
    await screen.findByRole('button', { name: 'Play free' })
    expect(leaveMatch).toHaveBeenNthCalledWith(1, 'match-one')
    expect(leaveMatch).toHaveBeenNthCalledWith(2, 'match-one')
    expect(api.paid!.leave).not.toHaveBeenCalled()
  })
})
async function enabledButton(name: string) {
  const button = await screen.findByRole('button', { name })
  await waitFor(() => expect((button as HTMLButtonElement).disabled).toBe(false))
  return button
}
