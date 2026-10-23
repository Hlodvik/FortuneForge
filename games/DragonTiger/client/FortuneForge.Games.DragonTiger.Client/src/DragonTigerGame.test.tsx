// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DragonTigerGame } from './DragonTigerGame'
import { DragonTigerGatewayError, type DragonTigerGateway, type DragonTigerRound, type DragonTigerStatus } from './contracts'
import { playDragonTigerAudio } from './dragonTigerAudio'

vi.mock('./dragonTigerAudio', () => ({ playDragonTigerAudio: vi.fn() }))

const status: DragonTigerStatus = { available: true, minimumStake: .5, maximumStake: 250, maximumTotalStake: 500, stakeIncrement: .5, balance: 100, mode: 'practice' }
const dragonWin: DragonTigerRound = {
  roundId: 'client-round-1', phase: 'settled', balance: 102,
  dragonCard: { rank: 'king', suit: 'hearts' }, tigerCard: { rank: 'ace', suit: 'spades' },
  dragonValue: 13, tigerValue: 1, outcome: 'dragon',
  bets: [
    { side: 'dragon', stake: 2, disposition: 'win', profit: 2, totalReturn: 4 },
    { side: 'tiger', stake: 0, disposition: 'not-placed', profit: 0, totalReturn: 0 },
    { side: 'tie', stake: 0, disposition: 'not-placed', profit: 0, totalReturn: 0 },
  ], totalStake: 2, profit: 2, totalReturn: 4, shoeCardsUsed: 2, shoeCardsRemaining: 414,
}
function makeGateway(round: DragonTigerRound = dragonWin) {
  return {
    getStatus: vi.fn<DragonTigerGateway['getStatus']>().mockResolvedValue(status),
    getLatestRound: vi.fn<NonNullable<DragonTigerGateway['getLatestRound']>>().mockResolvedValue(null),
    createRound: vi.fn<DragonTigerGateway['createRound']>().mockResolvedValue(round),
  }
}
const spot = (side: string) => screen.getByRole('button', { name: new RegExp(`^Bet on ${side},`) })
const total = () => screen.getByTestId('dragon-tiger-total-bet').textContent
const balance = () => screen.getByTestId('dragon-tiger-balance').textContent
const win = () => screen.getByTestId('dragon-tiger-win').textContent
const flush = async () => { await act(async () => { await Promise.resolve(); await Promise.resolve() }) }
const advance = async (ms: number) => { await act(async () => { vi.advanceTimersByTime(ms) }) }
async function start(gateway = makeGateway(), playerId = 'player-a', onBalanceChange?: (balance: number) => void) {
  const view = render(<DragonTigerGame gateway={gateway} playerId={playerId} onBalanceChange={onBalanceChange}/>)
  await flush()
  return view
}

beforeEach(() => {
  localStorage.clear()
  vi.clearAllMocks()
  vi.useFakeTimers()
  vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: false }))
})
afterEach(() => { cleanup(); vi.useRealTimers(); vi.restoreAllMocks(); vi.unstubAllGlobals() })

describe('DragonTigerGame betting and results', () => {
  it('accumulates repeated chip clicks in a visible stack, undoes the last placement, and clears all spots', async () => {
    await start()
    expect(total()).toBe('R0.00')
    expect(balance()).toBe('R100.00')
    expect(win()).toBe('R0.00')
    fireEvent.click(screen.getByRole('button', { name: 'R5.00 chip' }))
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    expect(spot('Dragon').getAttribute('aria-label')).toContain('current bet R15.00')
    expect(within(spot('Dragon')).getByLabelText('3 chips placed').querySelectorAll('img')).toHaveLength(3)
    fireEvent.click(screen.getByRole('button', { name: 'R0.50 chip' }))
    fireEvent.click(spot('Tie'))
    expect(total()).toBe('R15.50')
    fireEvent.click(screen.getByRole('button', { name: /Undo/ }))
    expect(total()).toBe('R15.00')
    expect(spot('Tie').getAttribute('aria-label')).toContain('current bet R0.00')
    fireEvent.click(screen.getByRole('button', { name: /Clear/ }))
    expect(total()).toBe('R0.00')
    expect(screen.getByRole('button', { name: /Deal cards/ })).toHaveProperty('disabled', true)
    expect(balance()).toBe('R100.00')
  })

  it('keeps the deal locked while revealing Dragon, Tiger, then the settlement at 1700 ms', async () => {
    const gateway = makeGateway(), changed = vi.fn()
    await start(gateway, 'player-a', changed)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ }))
    await flush()
    expect(gateway.createRound).toHaveBeenCalledOnce()
    expect(gateway.createRound.mock.calls[0][0]).toEqual({ dragonStake: 2, tigerStake: 0, tieStake: 0 })
    expect(gateway.createRound.mock.calls[0][1].idempotencyKey).toMatch(/^dragon-tiger-/)
    expect(spot('Dragon')).toHaveProperty('disabled', true)
    expect(screen.queryByLabelText('King of hearts')).toBeNull()
    await advance(299)
    expect(screen.queryByLabelText('King of hearts')).toBeNull()
    await advance(1)
    expect(screen.getByLabelText('King of hearts')).toBeTruthy()
    expect(screen.queryByLabelText('Ace of spades')).toBeNull()
    await advance(750)
    expect(screen.getByLabelText('Ace of spades')).toBeTruthy()
    expect(balance()).toBe('R100.00')
    expect(win()).toBe('R0.00')
    expect(changed).not.toHaveBeenCalled()
    await advance(649)
    expect(balance()).toBe('R100.00')
    await advance(1)
    expect(screen.getByText('DRAGON WINS')).toBeTruthy()
    expect(balance()).toBe('R102.00')
    expect(win()).toBe('R2.00')
    expect(screen.getByText('R4.00')).toBeTruthy()
    expect(changed).toHaveBeenCalledExactlyOnceWith(102)
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')).toBeNull()
    expect(spot('Dragon')).toHaveProperty('disabled', false)
    fireEvent.click(screen.getByRole('button', { name: /Rebet/ }))
    expect(total()).toBe('R2.00')
    expect(screen.getByRole('button', { name: /Deal cards/ })).toHaveProperty('disabled', false)
    expect(gateway.createRound).toHaveBeenCalledOnce()
  })

  it('shows the combined return and positive net for a tie with all three spots wagered', async () => {
    const tied: DragonTigerRound = {
      ...dragonWin, outcome: 'tie', dragonCard: { rank: 'ace', suit: 'hearts' }, dragonValue: 1,
      balance: 103, totalStake: 2.5, totalReturn: 5.5, profit: 3,
      bets: [
        { side: 'dragon', stake: 1, disposition: 'half-loss', profit: -.5, totalReturn: .5 },
        { side: 'tiger', stake: 1, disposition: 'half-loss', profit: -.5, totalReturn: .5 },
        { side: 'tie', stake: .5, disposition: 'win', profit: 4, totalReturn: 4.5 },
      ],
    }
    await start(makeGateway(tied))
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Tiger'))
    fireEvent.click(screen.getByRole('button', { name: 'R0.50 chip' })); fireEvent.click(spot('Tie'))
    expect(total()).toBe('R2.50')
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush(); await advance(1700)
    expect(screen.getByText('TIE', { exact: true })).toBeTruthy()
    expect(screen.getByText('Tie · half of each Dragon / Tiger bet returned')).toBeTruthy()
    expect(screen.getByText('R5.50', { exact: true })).toBeTruthy()
    expect(win()).toBe('R3.00')
    expect(balance()).toBe('R103.00')
  })

  it('shows zero Win and the exact half-return on a losing main bet when the cards tie', async () => {
    const tied: DragonTigerRound = {
      ...dragonWin, outcome: 'tie', dragonCard: { rank: 'ace', suit: 'hearts' }, dragonValue: 1,
      balance: 99.5, totalStake: 1, totalReturn: .5, profit: -.5,
      bets: [
        { side: 'dragon', stake: 1, disposition: 'half-loss', profit: -.5, totalReturn: .5 },
        dragonWin.bets[1], dragonWin.bets[2],
      ],
    }
    await start(makeGateway(tied))
    fireEvent.click(spot('Dragon')); fireEvent.click(screen.getByRole('button', { name: /Deal cards/ }))
    await flush(); await advance(1700)
    expect(win()).toBe('R0.00')
    expect(balance()).toBe('R99.50')
    expect(screen.getByText('R0.50', { exact: true })).toBeTruthy()
    expect(screen.getByText('Net −R0.50')).toBeTruthy()
  })

  it('finishes immediately when reduced motion is requested', async () => {
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: true }))
    await start()
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush(); await advance(0)
    expect(screen.getByText('DRAGON WINS')).toBeTruthy()
    expect(balance()).toBe('R102.00')
  })

  it('clears a previous result when the first new chip is placed after reload', async () => {
    const gateway = makeGateway()
    gateway.getLatestRound.mockResolvedValue(dragonWin)
    await start(gateway)
    expect(screen.getByText('DRAGON WINS')).toBeTruthy()
    expect(balance()).toBe('R102.00')
    expect(win()).toBe('R2.00')
    fireEvent.click(spot('Tiger'))
    expect(total()).toBe('R1.00')
    expect(win()).toBe('R0.00')
    expect(screen.queryByText('DRAGON WINS')).toBeNull()
    expect(screen.queryByLabelText('King of hearts')).toBeNull()
  })

  it('refuses additional chips that exceed the wallet or table limit', async () => {
    const gateway = makeGateway()
    gateway.getStatus.mockResolvedValue({ ...status, balance: 2.5 })
    const view = await start(gateway)
    fireEvent.click(screen.getByRole('button', { name: 'R2.00 chip' })); fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    expect(total()).toBe('R2.00')
    expect(screen.getByRole('alert').textContent).toContain('balance is too low')
    view.unmount()
    const limitedGateway = makeGateway()
    limitedGateway.getStatus.mockResolvedValue({ ...status, maximumStake: 2, maximumTotalStake: 3 })
    await start(limitedGateway)
    fireEvent.click(screen.getByRole('button', { name: 'R2.00 chip' })); fireEvent.click(spot('Dragon')); fireEvent.click(spot('Tiger'))
    expect(total()).toBe('R2.00')
    expect(screen.getByRole('alert').textContent).toContain('Table limit')
    expect(limitedGateway.createRound).not.toHaveBeenCalled()
  })

  it('triggers selection, deal and result sounds while honoring the current mute setting', async () => {
    await start()
    fireEvent.click(screen.getByRole('button', { name: 'R2.00 chip' })); fireEvent.click(spot('Dragon'))
    expect(playDragonTigerAudio).toHaveBeenCalledWith('chip', false)
    fireEvent.click(screen.getByRole('button', { name: 'Mute sound' }))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush(); await advance(1700)
    expect(playDragonTigerAudio).toHaveBeenCalledWith('deal', true)
    expect(playDragonTigerAudio).toHaveBeenCalledWith('result', true)
  })
})

describe('DragonTigerGame pending deal recovery', () => {
  it.each([401, 403, 400, 503])('preserves an uncertain pending deal after an HTTP %s response and restores the same key', async httpStatus => {
    const gateway = makeGateway()
    gateway.createRound.mockRejectedValueOnce(new DragonTigerGatewayError('Session must be restored.', httpStatus === 503 ? 'dragon-tiger-disabled' : 'authentication-required', httpStatus))
    await start(gateway)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    const original = gateway.createRound.mock.calls[0]
    expect(JSON.parse(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')!).key).toBe(original[1].idempotencyKey)
    expect(spot('Tiger')).toHaveProperty('disabled', true)
    fireEvent.click(screen.getByRole('button', { name: /Restore deal/ })); await flush(); await advance(1700)
    expect(gateway.createRound.mock.calls[1][1].idempotencyKey).toBe(original[1].idempotencyKey)
    expect(balance()).toBe('R102.00')
  })

  it.each([false, true])('ignores a previous account’s delayed response after switching players (rejected: %s)', async rejectOld => {
    let resolve!: (round: DragonTigerRound) => void
    let reject!: (error: unknown) => void
    const delayed = new Promise<DragonTigerRound>((resolvePromise, rejectPromise) => { resolve = resolvePromise; reject = rejectPromise })
    const oldGateway = makeGateway(), newGateway = makeGateway(), changed = vi.fn()
    oldGateway.createRound.mockReturnValueOnce(delayed)
    newGateway.getStatus.mockResolvedValue({ ...status, balance: 700 })
    const view = await start(oldGateway, 'old-player', changed)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    const oldPending = localStorage.getItem('fortuneforge:dragon-tiger:pending:old-player')
    expect(oldPending).not.toBeNull()
    view.rerender(<DragonTigerGame gateway={newGateway} playerId="new-player" onBalanceChange={changed}/>); await flush()
    await act(async () => { if (rejectOld) reject(new TypeError('Old account response failed')); else resolve(dragonWin); await Promise.resolve() })
    await advance(1700)
    expect(balance()).toBe('R700.00')
    expect(total()).toBe('R0.00')
    expect(win()).toBe('R0.00')
    expect(changed).not.toHaveBeenCalled()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.queryByLabelText('King of hearts')).toBeNull()
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:old-player')).toBe(oldPending)
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:new-player')).toBeNull()
    expect(spot('Dragon')).toHaveProperty('disabled', false)
  })

  it('ignores a previous account’s delayed balance refresh after a rejected wager', async () => {
    let resolveStatus!: (value: DragonTigerStatus) => void
    const delayedStatus = new Promise<DragonTigerStatus>(resolve => { resolveStatus = resolve })
    const oldGateway = makeGateway(), newGateway = makeGateway()
    oldGateway.getStatus.mockResolvedValueOnce(status).mockReturnValueOnce(delayedStatus)
    oldGateway.createRound.mockRejectedValueOnce(new DragonTigerGatewayError('Invalid wager.', 'dragon-tiger-invalid-request', 400))
    newGateway.getStatus.mockResolvedValue({ ...status, balance: 700 })
    const view = await start(oldGateway, 'old-player')
    fireEvent.click(spot('Dragon')); fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    expect(oldGateway.getStatus).toHaveBeenCalledTimes(2)
    view.rerender(<DragonTigerGame gateway={newGateway} playerId="new-player"/>); await flush()
    await act(async () => { resolveStatus({ ...status, balance: 50 }); await Promise.resolve() })
    expect(balance()).toBe('R700.00')
    expect(spot('Dragon')).toHaveProperty('disabled', false)
  })

  it('persists a lost-response wager, blocks new betting, and replays the same key after reload', async () => {
    const gateway = makeGateway()
    gateway.createRound.mockRejectedValueOnce(new TypeError('Connection lost after server settlement'))
    const first = await start(gateway)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    expect(screen.getByRole('button', { name: /Restore deal/ })).toBeTruthy()
    expect(spot('Tiger')).toHaveProperty('disabled', true)
    const persisted = JSON.parse(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')!)
    const original = gateway.createRound.mock.calls[0]
    expect(persisted.key).toBe(original[1].idempotencyKey)
    expect(persisted.stakes).toEqual(original[0])
    first.unmount()
    await start(gateway)
    expect(gateway.createRound).toHaveBeenCalledTimes(2)
    expect(gateway.createRound.mock.calls[1][0]).toEqual(original[0])
    expect(gateway.createRound.mock.calls[1][1].idempotencyKey).toBe(original[1].idempotencyKey)
    expect(total()).toBe('R2.00')
    await advance(1700)
    expect(balance()).toBe('R102.00')
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')).toBeNull()
    const history = JSON.parse(localStorage.getItem('fortuneforge:dragon-tiger:history:player-a')!)
    expect(history).toEqual([{ id: 'client-round-1', outcome: 'dragon' }])
  })

  it('restores in place with the original key after a failed request', async () => {
    const gateway = makeGateway()
    gateway.createRound.mockRejectedValueOnce(new TypeError('Network unavailable'))
    await start(gateway)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    fireEvent.click(screen.getByRole('button', { name: /Restore deal/ })); await flush()
    expect(gateway.createRound.mock.calls[1][1].idempotencyKey).toBe(gateway.createRound.mock.calls[0][1].idempotencyKey)
    await advance(1700)
    expect(win()).toBe('R2.00')
  })

  it('keeps another account’s pending deal and history isolated', async () => {
    const otherPending = {
      key: 'dragon-tiger-00000000-0000-4000-8000-000000000001',
      stakes: { dragonStake: 2, tigerStake: 0, tieStake: 0 },
      placements: [{ side: 'dragon', value: 2 }],
    }
    localStorage.setItem('fortuneforge:dragon-tiger:pending:other-player', JSON.stringify(otherPending))
    localStorage.setItem('fortuneforge:dragon-tiger:history:other-player', JSON.stringify([{ id: 'secret-round', outcome: 'tie' }]))
    const gateway = makeGateway()
    const view = await start(gateway)
    expect(gateway.createRound).not.toHaveBeenCalled()
    expect(total()).toBe('R0.00')
    expect(screen.getByText('Your results will appear here')).toBeTruthy()
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:other-player')).toBe(JSON.stringify(otherPending))
    view.rerender(<DragonTigerGame gateway={gateway} playerId="other-player"/>); await flush()
    expect(gateway.createRound).toHaveBeenCalledExactlyOnceWith(otherPending.stakes, expect.objectContaining({ idempotencyKey: otherPending.key }))
    expect(total()).toBe('R2.00')
  })

  it('discards a confirmed server rejection so the player can correct the wager', async () => {
    const gateway = makeGateway()
    gateway.createRound.mockRejectedValueOnce(new DragonTigerGatewayError('Insufficient balance.', 'insufficient-slot-credits', 400))
    await start(gateway)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    expect(screen.getByRole('alert').textContent).toContain('Insufficient balance.')
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')).toBeNull()
    expect(spot('Dragon')).toHaveProperty('disabled', false)
    expect(screen.queryByRole('button', { name: /Restore deal/ })).toBeNull()
  })

  it('ignores a stored request whose placements disagree with its stakes', async () => {
    localStorage.setItem('fortuneforge:dragon-tiger:pending:player-a', JSON.stringify({
      key: 'dragon-tiger-00000000-0000-4000-8000-000000000001',
      stakes: { dragonStake: 50, tigerStake: 0, tieStake: 0 },
      placements: [{ side: 'dragon', value: 1 }],
    }))
    const gateway = makeGateway()
    await start(gateway)
    expect(gateway.createRound).not.toHaveBeenCalled()
    expect(total()).toBe('R0.00')
    expect(spot('Dragon')).toHaveProperty('disabled', false)
  })

  it('can restore in memory when optional browser storage is unavailable', async () => {
    const gateway = makeGateway()
    gateway.createRound.mockRejectedValueOnce(new TypeError('Network unavailable'))
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Storage blocked', 'SecurityError') })
    await start(gateway)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    fireEvent.click(screen.getByRole('button', { name: /Restore deal/ })); await flush(); await advance(1700)
    expect(gateway.createRound.mock.calls[1][1].idempotencyKey).toBe(gateway.createRound.mock.calls[0][1].idempotencyKey)
    expect(balance()).toBe('R102.00')
  })
})
