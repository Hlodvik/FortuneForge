// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MinesGame } from './MinesGame'
import { MinesGatewayError, type MinesGateway, type MinesRound, type MinesSession } from './contracts'
import { pendingKey, savePending } from './pendingCommand'
import { playMinesAudio, unlockMinesAudio } from './minesAudio'

vi.mock('./minesAudio', () => ({ playMinesAudio: vi.fn(), unlockMinesAudio: vi.fn() }))

const idle: MinesSession = { balanceCents: 10000, practice: true, round: null, minimumWagerCents: 10, maximumWagerCents: 10000, houseEdgeBasisPoints: 100 }
function round(overrides: Partial<MinesRound> = {}): MinesRound {
  return { roundId: 'round-client-1', wagerCents: 100, mineCount: 3, status: 'active', safeReveals: 0, gemsRemaining: 22,
    currentMultiplier: 1, nextMultiplier: 1.125, cashOutCents: 0, canCashOut: false, payoutCents: 0,
    tiles: Array.from({ length: 25 }, (_, index) => ({ index, isRevealed: false, kind: 'hidden' as const })), ...overrides }
}
function gemRound(): MinesRound {
  return round({ safeReveals: 1, gemsRemaining: 21, currentMultiplier: 1.125, nextMultiplier: 1.2857142857142858, cashOutCents: 112, canCashOut: true,
    tiles: round().tiles.map(tile => tile.index === 4 ? { ...tile, kind: 'gem', isRevealed: true } : tile) })
}
function terminalRound(status: 'cashed-out' | 'lost' | 'cleared' = 'cashed-out'): MinesRound {
  const won = status !== 'lost'
  return { ...gemRound(), status, nextMultiplier: null, currentMultiplier: won ? 1.125 : 0,
    canCashOut: false, cashOutCents: 0, payoutCents: won ? 112 : 0,
    tiles: round().tiles.map(tile => ({ ...tile, kind: tile.index < 3 ? 'mine' : 'gem', isRevealed: tile.index === 4 || !won && tile.index === 0 })) }
}
function gateway(initial = idle) {
  let current = initial
  return {
    getSession: vi.fn<MinesGateway['getSession']>().mockImplementation(async () => current),
    execute: vi.fn<MinesGateway['execute']>().mockImplementation(async () => current),
    set(next: MinesSession) { current = next },
  }
}
function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (reason: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => { resolve = resolvePromise; reject = rejectPromise })
  return { promise, resolve, reject }
}
const flush = async () => { await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve() }) }
const wager = () => screen.getByRole('textbox', { name: 'Wager amount' })
const mines = () => screen.getByRole('combobox', { name: 'Mine count' })
const tile = (index: number) => screen.getByRole('button', { name: new RegExp(`^Tile ${index + 1},`) })
const balance = () => screen.getByTestId('mines-balance').textContent
async function open(fake = gateway(), playerId = 'player-a', onBalanceChange?: (cents: number) => void) {
  const view = render(<MinesGame gateway={fake} playerId={playerId} onBalanceChange={onBalanceChange}/>)
  await flush()
  return view
}

beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks()
  // jsdom omits the browser dialog methods; these tests exercise game/recovery controls.
  Object.defineProperties(HTMLDialogElement.prototype, {
    showModal: { configurable: true, value: vi.fn() }, close: { configurable: true, value: vi.fn() },
  })
})
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals() })

describe('MinesGame server state and normal play', () => {
  it('requires a loaded wallet and valid affordable wager before starting', async () => {
    const fake = gateway({ ...idle, balanceCents: 50 })
    await open(fake)
    expect(balance()).toBe('R0.50')
    expect(screen.getByText('PRACTICE BALANCE')).toBeTruthy()
    expect(screen.getByText('Play credits only · no cash value')).toBeTruthy()
    expect(screen.getByRole('button', { name: /Place wager/ })).toHaveProperty('disabled', true)
    expect(screen.getByText('Not enough credits for this wager.')).toBeTruthy()
    fireEvent.change(wager(), { target: { value: '0.09' } })
    expect(screen.getByRole('button', { name: /Place wager/ })).toHaveProperty('disabled', true)
    fireEvent.change(wager(), { target: { value: '0.101' } })
    expect(screen.getByRole('button', { name: /Place wager/ })).toHaveProperty('disabled', true)
    fireEvent.change(wager(), { target: { value: '0.50' } })
    expect(screen.getByRole('button', { name: /Place wager/ })).toHaveProperty('disabled', false)
    expect(fake.execute).not.toHaveBeenCalled()
    expect(tile(0)).toHaveProperty('disabled', true)
  })

  it('locks the accepted wager and mine count, reveals a gem, cashes out, and clears for a new board', async () => {
    const fake = gateway(), changed = vi.fn()
    await open(fake, 'player-a', changed)
    fake.execute.mockImplementationOnce(async command => {
      expect(command).toMatchObject({ action: 'start', wagerCents: 100, mineCount: 3 })
      const next = { ...idle, balanceCents: 9900, round: round() }; fake.set(next); return next
    })
    fireEvent.click(screen.getByRole('button', { name: /Place wager/ })); await flush()
    expect(balance()).toBe('R99.00')
    expect(wager()).toHaveProperty('disabled', true)
    expect(mines()).toHaveProperty('disabled', true)
    expect(screen.getByRole('button', { name: 'Halve wager' })).toHaveProperty('disabled', true)
    expect(screen.getByRole('button', { name: 'Double wager' })).toHaveProperty('disabled', true)
    expect(screen.getByRole('button', { name: /Cash out/ })).toHaveProperty('disabled', true)
    expect(tile(4)).toHaveProperty('disabled', false)
    fake.execute.mockImplementationOnce(async command => {
      expect(command).toMatchObject({ action: 'reveal', roundId: 'round-client-1', tile: 4 })
      const next = { ...idle, balanceCents: 9900, round: gemRound() }; fake.set(next); return next
    })
    fireEvent.click(tile(4)); await flush()
    expect(tile(4)).toHaveProperty('disabled', true)
    expect(tile(4).getAttribute('aria-label')).toContain('gem, revealed')
    expect(screen.getByTestId('mines-cashout-value').textContent).toBe('R1.12')
    expect(screen.getByTestId('mines-next').textContent).toBe('1.29×')
    expect(playMinesAudio).toHaveBeenCalledWith('gem', false)
    fake.execute.mockImplementationOnce(async command => {
      expect(command).toMatchObject({ action: 'cashout', roundId: 'round-client-1' })
      const next = { ...idle, balanceCents: 10012, round: terminalRound() }; fake.set(next); return next
    })
    fireEvent.click(screen.getByRole('button', { name: /Cash out/ })); await flush()
    expect(balance()).toBe('R100.12')
    expect(changed).toHaveBeenLastCalledWith(10012)
    expect(screen.getByText('CASHED OUT')).toBeTruthy()
    expect(screen.getByTestId('mines-next').textContent).toBe('—')
    expect(screen.getByTestId('mines-cashout-value').textContent).toBe('R1.12')
    expect(playMinesAudio).toHaveBeenCalledWith('cashout', false)
    expect(tile(0).getAttribute('aria-label')).toContain('mine')
    expect(tile(0)).toHaveProperty('disabled', true)
    expect(wager()).toHaveProperty('disabled', false)
    fireEvent.click(screen.getByRole('button', { name: 'Clear board' }))
    expect(screen.queryByText('CASHED OUT')).toBeNull()
    expect(tile(0).getAttribute('aria-label')).toContain('covered')
    expect(screen.getByTestId('mines-cashout-value').textContent).toBe('R0.00')
    expect(screen.getByRole('button', { name: /Place wager/ })).toHaveProperty('disabled', false)
    expect(fake.execute).toHaveBeenCalledTimes(3)
    expect(localStorage.getItem(pendingKey('player-a'))).toBeNull()
  })

  it('shows immediate server-confirmed loss without offering cash out or changing the balance again', async () => {
    const fake = gateway({ ...idle, balanceCents: 9900, round: round() })
    await open(fake)
    fake.execute.mockImplementationOnce(async () => {
      const next = { ...idle, balanceCents: 9900, round: terminalRound('lost') }; fake.set(next); return next
    })
    fireEvent.click(tile(0)); await flush()
    expect(screen.getByText('MINE FOUND')).toBeTruthy()
    expect(screen.getByText('R1.00 wager lost')).toBeTruthy()
    expect(balance()).toBe('R99.00')
    expect(screen.getByTestId('mines-cashout-value').textContent).toBe('R0.00')
    expect(screen.queryByRole('button', { name: /Cash out/ })).toBeNull()
    expect(screen.getByRole('button', { name: /Bet again/ })).toHaveProperty('disabled', false)
    expect(playMinesAudio).toHaveBeenCalledWith('mine', false)
    expect(screen.getAllByRole('button', { name: /^Tile / }).every(button => (button as HTMLButtonElement).disabled)).toBe(true)
  })

  it('does not replay result sounds when restoring an already terminal round', async () => {
    await open(gateway({ ...idle, balanceCents: 10012, round: terminalRound('cleared') }))
    expect(screen.getByText('ALL GEMS FOUND')).toBeTruthy()
    expect(playMinesAudio).not.toHaveBeenCalled()
  })

  it('honors the current mute preference when a pending reveal settles', async () => {
    const fake = gateway({ ...idle, balanceCents: 9900, round: round() }), pending = deferred<MinesSession>()
    fake.execute.mockReturnValueOnce(pending.promise)
    await open(fake)
    fireEvent.click(tile(4)); await flush()
    fireEvent.click(screen.getByRole('button', { name: 'Mute sound' }))
    fake.set({ ...idle, balanceCents: 9900, round: gemRound() })
    await act(async () => { pending.resolve({ ...idle, balanceCents: 9900, round: gemRound() }); await Promise.resolve() })
    await flush()
    expect(playMinesAudio).toHaveBeenCalledWith('gem', true)
    expect(localStorage.getItem('fortuneforge:mines:muted')).toBe('true')
    expect(screen.getByRole('button', { name: 'Turn sound on' }).getAttribute('aria-pressed')).toBe('false')
    expect(unlockMinesAudio).toHaveBeenCalledOnce()
  })
})

describe('MinesGame command recovery', () => {
  it('preserves the same uncertain wager command, blocks new actions, and restores the latest ledger after replay', async () => {
    const fake = gateway()
    fake.execute.mockRejectedValueOnce(new TypeError('Response lost after wager acceptance'))
    await open(fake)
    fireEvent.click(screen.getByRole('button', { name: /Place wager/ })); await flush()
    const original = fake.execute.mock.calls[0][0]
    expect(JSON.parse(localStorage.getItem(pendingKey('player-a'))!)).toEqual(original)
    expect(screen.getByRole('alert').textContent).toContain('Response lost')
    expect(wager()).toHaveProperty('disabled', true)
    expect(mines()).toHaveProperty('disabled', true)
    expect(tile(0)).toHaveProperty('disabled', true)
    fake.execute.mockResolvedValueOnce({ ...idle, balanceCents: 9900, round: round() }) // Older replay receipt.
    fake.set({ ...idle, balanceCents: 9900, round: gemRound() }) // Another tab has advanced it.
    fireEvent.click(screen.getByRole('button', { name: /Restore round/ })); await flush()
    expect(fake.execute.mock.calls[1][0]).toEqual(original)
    expect(tile(4).getAttribute('aria-label')).toContain('gem, revealed')
    expect(screen.getByTestId('mines-cashout-value').textContent).toBe('R1.12')
    expect(balance()).toBe('R99.00')
    expect(localStorage.getItem(pendingKey('player-a'))).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('replays a persisted cash-out after refresh with its original id and no new wager', async () => {
    const fake = gateway({ ...idle, balanceCents: 9900, round: gemRound() })
    fake.execute.mockRejectedValueOnce(new TypeError('Cash-out response lost'))
    const first = await open(fake)
    fireEvent.click(screen.getByRole('button', { name: /Cash out/ })); await flush()
    const original = fake.execute.mock.calls[0][0]
    expect(original.action).toBe('cashout')
    first.unmount()
    fake.set({ ...idle, balanceCents: 10012, round: terminalRound() })
    await open(fake)
    expect(fake.execute).toHaveBeenCalledTimes(2)
    expect(fake.execute.mock.calls[1][0]).toEqual(original)
    expect(screen.getByText('CASHED OUT')).toBeTruthy()
    expect(balance()).toBe('R100.12')
    expect(localStorage.getItem(pendingKey('player-a'))).toBeNull()
    expect(playMinesAudio).not.toHaveBeenCalled()
  })

  it('keeps every betting control locked while a request is outstanding and prevents concurrent reveals', async () => {
    const fake = gateway({ ...idle, balanceCents: 9900, round: round() }), request = deferred<MinesSession>()
    fake.execute.mockReturnValueOnce(request.promise)
    await open(fake)
    fireEvent.click(tile(4)); fireEvent.click(tile(5)); await flush()
    expect(fake.execute).toHaveBeenCalledOnce()
    expect(wager()).toHaveProperty('disabled', true)
    expect(mines()).toHaveProperty('disabled', true)
    expect(screen.getByRole('button', { name: /Confirming/ })).toHaveProperty('disabled', true)
    expect(screen.getAllByRole('button', { name: /^Tile / }).every(button => (button as HTMLButtonElement).disabled)).toBe(true)
    fake.set({ ...idle, balanceCents: 9900, round: gemRound() })
    await act(async () => { request.resolve({ ...idle, balanceCents: 9900, round: gemRound() }); await Promise.resolve() })
    await flush()
    expect(tile(5)).toHaveProperty('disabled', false)
  })

  it('retains a committed command when its following session read fails and reuses it on restore', async () => {
    const fake = gateway()
    await open(fake)
    fake.getSession.mockRejectedValueOnce(new TypeError('Latest balance read failed'))
    fireEvent.click(screen.getByRole('button', { name: /Place wager/ })); await flush()
    const original = fake.execute.mock.calls[0][0]
    expect(localStorage.getItem(pendingKey('player-a'))).not.toBeNull()
    expect(wager()).toHaveProperty('disabled', true)
    fake.set({ ...idle, balanceCents: 9900, round: round() })
    fireEvent.click(screen.getByRole('button', { name: /Restore round/ })); await flush()
    expect(fake.execute.mock.calls[1][0]).toEqual(original)
    expect(balance()).toBe('R99.00')
    expect(localStorage.getItem(pendingKey('player-a'))).toBeNull()
  })

  it('refreshes funds after a definite insufficient-funds rejection, then permits a corrected wager', async () => {
    const fake = gateway()
    await open(fake)
    fake.set({ ...idle, balanceCents: 50 })
    fake.execute.mockRejectedValueOnce(new MinesGatewayError('Insufficient credits.', 'insufficient-funds', 400))
    fireEvent.click(screen.getByRole('button', { name: /Place wager/ })); await flush()
    expect(screen.getByRole('alert').textContent).toContain('Insufficient credits.')
    expect(balance()).toBe('R0.50')
    expect(wager()).toHaveProperty('disabled', false)
    expect(mines()).toHaveProperty('disabled', false)
    expect(screen.queryByRole('button', { name: /Restore round/ })).toBeNull()
    expect(localStorage.getItem(pendingKey('player-a'))).toBeNull()
    fireEvent.change(wager(), { target: { value: '0.50' } })
    expect(screen.getByRole('button', { name: /Place wager/ })).toHaveProperty('disabled', false)
    expect(fake.execute).toHaveBeenCalledOnce()
  })

  it('recovers a failed initial read without inventing a balance or issuing a wager', async () => {
    const fake = gateway()
    fake.getSession.mockRejectedValueOnce(new TypeError('Table temporarily unavailable'))
    await open(fake)
    expect(balance()).toBe('—')
    expect(wager()).toHaveProperty('disabled', true)
    expect(screen.getByRole('alert').textContent).toContain('Table temporarily unavailable')
    fireEvent.click(screen.getByRole('button', { name: /Restore round/ })); await flush()
    expect(balance()).toBe('R100.00')
    expect(wager()).toHaveProperty('disabled', false)
    expect(screen.queryByRole('alert')).toBeNull()
    expect(fake.execute).not.toHaveBeenCalled()
  })

  it('blocks a wager before sending if command persistence fails, then restores authoritative state', async () => {
    const fake = gateway()
    await open(fake)
    vi.spyOn(Storage.prototype, 'setItem').mockImplementationOnce(() => { throw new DOMException('Storage quota exceeded', 'QuotaExceededError') })
    fireEvent.click(screen.getByRole('button', { name: /Place wager/ })); await flush()
    expect(fake.execute).not.toHaveBeenCalled()
    expect(screen.getByRole('alert').textContent).toContain('Restore your round')
    expect(wager()).toHaveProperty('disabled', true)
    fireEvent.click(screen.getByRole('button', { name: /Restore round/ })); await flush()
    expect(wager()).toHaveProperty('disabled', false)
    expect(fake.execute).not.toHaveBeenCalled()
    expect(balance()).toBe('R100.00')
  })

  it.each([false, true])('isolates an old account delayed request after switching players (reject: %s)', async rejectOld => {
    const oldGateway = gateway(), newGateway = gateway({ ...idle, balanceCents: 70000 }), pending = deferred<MinesSession>(), changed = vi.fn()
    oldGateway.execute.mockReturnValueOnce(pending.promise)
    const view = await open(oldGateway, 'old-player', changed)
    fireEvent.click(screen.getByRole('button', { name: /Place wager/ })); await flush()
    const oldPending = localStorage.getItem(pendingKey('old-player'))
    expect(oldPending).not.toBeNull()
    changed.mockClear()
    view.rerender(<MinesGame gateway={newGateway} playerId="new-player" onBalanceChange={changed}/>)
    expect(balance()).toBe('—') // No old wallet during the new account's load.
    await flush()
    expect(balance()).toBe('R700.00')
    await act(async () => {
      if (rejectOld) pending.reject(new TypeError('Old account response lost'))
      else pending.resolve({ ...idle, balanceCents: 9900, round: round() })
      await Promise.resolve()
    })
    await flush()
    expect(balance()).toBe('R700.00')
    expect(changed.mock.calls).toEqual([[70000]])
    expect(newGateway.execute).not.toHaveBeenCalled()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByRole('button', { name: /Place wager/ })).toHaveProperty('disabled', false)
    expect(localStorage.getItem(pendingKey('new-player'))).toBeNull()
    if (rejectOld) expect(localStorage.getItem(pendingKey('old-player'))).toBe(oldPending)
  })

  it('never replays a saved command belonging to a different account', async () => {
    const saved = { action: 'reveal' as const, requestId: 'mines-other-account-0001', roundId: 'other-private-round', tile: 7 }
    savePending('other-player', saved)
    const fake = gateway()
    await open(fake)
    expect(fake.execute).not.toHaveBeenCalled()
    expect(localStorage.getItem(pendingKey('other-player'))).toBe(JSON.stringify(saved))
    expect(localStorage.getItem(pendingKey('player-a'))).toBeNull()
    expect(balance()).toBe('R100.00')
  })

  it('restores a new active round from another tab even after the previous board was cleared', async () => {
    const fake = gateway({ ...idle, balanceCents: 10012, round: terminalRound() })
    await open(fake)
    fireEvent.click(screen.getByRole('button', { name: 'Clear board' }))
    expect(screen.getByRole('button', { name: /Place wager/ })).toBeTruthy()
    fake.set({ ...idle, balanceCents: 9912, round: round({ roundId: 'new-round-from-another-tab' }) })
    fireEvent(window, new Event('focus')); await flush()
    expect(balance()).toBe('R99.12')
    expect(wager()).toHaveProperty('disabled', true)
    expect(mines()).toHaveProperty('disabled', true)
    expect(screen.getByRole('button', { name: /Cash out/ })).toHaveProperty('disabled', true)
    expect(tile(0)).toHaveProperty('disabled', false)
    expect(fake.execute).not.toHaveBeenCalled()
  })

  it('preserves the next wager settings when a focus refresh returns the same completed round', async () => {
    const fake = gateway({ ...idle, balanceCents: 10012, round: terminalRound() })
    await open(fake)
    fireEvent.click(screen.getByRole('button', { name: 'Clear board' }))
    fireEvent.change(wager(), { target: { value: '2.50' } })
    fireEvent.change(mines(), { target: { value: '5' } })
    fake.set({ ...idle, balanceCents: 10012, round: terminalRound() }) // Fresh response, same completed round ID.
    fireEvent(window, new Event('focus')); await flush()
    expect(wager()).toHaveProperty('value', '2.50')
    expect(mines()).toHaveProperty('value', '5')
    expect(screen.getByRole('button', { name: /Place wager/ })).toHaveProperty('disabled', false)
    expect(tile(0).getAttribute('aria-label')).toContain('covered')
    expect(fake.execute).not.toHaveBeenCalled()
  })
})
