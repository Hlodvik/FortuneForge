// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { HiLoGame } from './HiLoGame'
import { HiLoGatewayError, type HiLoGateway, type HiLoRound, type HiLoSession } from './contracts'
import { HttpHiLoGateway } from './httpHiLoGateway'
import { HiLoPendingOperationStore } from './hiLoPendingCommand'
import { playHiLoAudio } from './hiLoAudio'
import { hiLoSessionFixture } from './testFixtures'

vi.mock('./hiLoAudio', async importOriginal => ({
  ...await importOriginal<typeof import('./hiLoAudio')>(), playHiLoAudio: vi.fn(),
}))

const idle: HiLoSession = { ...hiLoSessionFixture, walletBalanceCents: 10000, round: null }
const startedRound: HiLoRound = { ...hiLoSessionFixture.round!, canCashOut: true }
const started: HiLoSession = { ...idle, walletBalanceCents: 9900, round: startedRound }
const wonRound: HiLoRound = {
  ...startedRound, revision: 1, currentCard: { rank: 'king', suit: 'spades' },
  history: [...startedRound.history, { card: { rank: 'king', suit: 'spades' }, choice: 'higher', won: true }],
  currentReturnCents: 214, multiplier: 2.145, streak: 1,
  higher: { probability: 0, multiplier: 0, projectedReturnCents: 0, available: false },
  lower: { probability: 12 / 13, multiplier: 2.3001375, projectedReturnCents: 230, available: true },
}
const won: HiLoSession = { ...started, round: wonRound }
const collected: HiLoSession = {
  ...won, walletBalanceCents: 10114,
  round: { ...wonRound, revision: 2, status: 'cashedOut', canCashOut: false, canSkip: false, lower: { ...wonRound.lower, multiplier: 0, projectedReturnCents: 0, available: false } },
}
const balance = () => screen.getByTestId('hilo-balance').textContent
const higher = () => screen.getByRole('button', { name: /^Choose higher/ }) as HTMLButtonElement
const lower = () => screen.getByRole('button', { name: /^Choose lower/ }) as HTMLButtonElement
const cashout = () => screen.getByRole('button', { name: /^Cash out/ }) as HTMLButtonElement

function gateway(session: HiLoSession = idle) {
  return {
    getSession: vi.fn<HiLoGateway['getSession']>().mockResolvedValue(session),
    startRound: vi.fn<HiLoGateway['startRound']>().mockResolvedValue(started),
    act: vi.fn<HiLoGateway['act']>().mockResolvedValue(won),
  }
}
function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (error: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => { resolve = resolvePromise; reject = rejectPromise })
  return { promise, resolve, reject }
}
async function enabledStart(name: RegExp = /^Place wager/) {
  const button = await screen.findByRole('button', { name }) as HTMLButtonElement
  await waitFor(() => expect(button.disabled).toBe(false))
  return button
}
function json(value: unknown) { return new Response(JSON.stringify(value), { status: 200, headers: { 'Content-Type': 'application/json' } }) }
const originalDialogMethods = {
  showModal: Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'showModal'),
  close: Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'close'),
}

beforeEach(() => {
  localStorage.clear(); sessionStorage.clear(); vi.clearAllMocks()
  // jsdom does not implement native dialog focus/close behavior. Model those
  // browser primitives so these tests cover the component's dialog lifecycle.
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', { configurable: true, value: function(this: HTMLDialogElement) {
    this.open = true
    this.querySelector<HTMLButtonElement>('button')?.focus()
  } })
  Object.defineProperty(HTMLDialogElement.prototype, 'close', { configurable: true, value: function(this: HTMLDialogElement) {
    this.open = false
    this.dispatchEvent(new Event('close'))
  } })
})
afterEach(() => {
  cleanup(); localStorage.clear(); sessionStorage.clear(); vi.restoreAllMocks()
  for (const method of ['showModal', 'close'] as const) {
    const descriptor = originalDialogMethods[method]
    if (descriptor) Object.defineProperty(HTMLDialogElement.prototype, method, descriptor)
    else Reflect.deleteProperty(HTMLDialogElement.prototype, method)
  }
})

describe('Hi-Lo authoritative round flow', () => {
  it('loads without charging, then starts, guesses and collects the server return once', async () => {
    const api = gateway(), changed = vi.fn(), user = userEvent.setup()
    api.act.mockResolvedValueOnce(won).mockResolvedValueOnce(collected)
    render(<HiLoGame gateway={api} playerId="alice:practice" onBalanceChange={changed} />)
    const place = await enabledStart()
    expect(balance()).toBe('R100.00')
    expect(screen.getByText('Practice credits · no cash value')).toBeTruthy()
    expect(api.startRound).not.toHaveBeenCalled()
    expect(higher().disabled).toBe(true)
    await user.click(place)
    expect(await screen.findByRole('img', { name: 'seven of hearts' })).toBeTruthy()
    expect(api.startRound).toHaveBeenCalledExactlyOnceWith(100, expect.objectContaining({ operationId: expect.stringMatching(/^[a-f0-9-]{36}$/) }))
    expect(higher().getAttribute('aria-label')).toContain('46.15% chance, return R2.14')
    await user.click(higher())
    expect(await screen.findByRole('img', { name: 'king of spades' })).toBeTruthy()
    expect(screen.getByLabelText('1 correct guesses')).toBeTruthy()
    expect(screen.getByTestId('hilo-return').textContent).toBe('R2.14')
    expect(higher().disabled).toBe(true)
    expect(lower().disabled).toBe(false)
    expect(api.act.mock.calls[0]).toEqual([startedRound.roundId, 'higher', expect.objectContaining({ expectedRevision: 0, operationId: expect.any(String) })])
    await user.click(cashout())
    await waitFor(() => expect(balance()).toBe('R101.14'))
    expect(screen.getByRole('status').textContent).toContain('Return collected')
    expect(api.act.mock.calls[1]).toEqual([startedRound.roundId, 'cashout', expect.objectContaining({ expectedRevision: 1, operationId: expect.any(String) })])
    expect(api.act.mock.calls[0][2].operationId).not.toBe(api.act.mock.calls[1][2].operationId)
    expect(changed).toHaveBeenLastCalledWith(101.14)
    expect(new HiLoPendingOperationStore('alice:practice').read()).toBeNull()
    expect((screen.getByRole('button', { name: /^Collected/ }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('locks duplicate clicks while a wager is in flight', async () => {
    const pending = deferred<HiLoSession>(), api = gateway()
    api.startRound.mockReturnValue(pending.promise)
    render(<HiLoGame gateway={api} playerId="alice:practice" />)
    const place = await enabledStart()
    fireEvent.click(place); fireEvent.click(place)
    expect(api.startRound).toHaveBeenCalledOnce()
    expect(place.disabled).toBe(true)
    expect(higher().disabled).toBe(true)
    await act(async () => pending.resolve(started))
    expect(await screen.findByRole('img', { name: 'seven of hearts' })).toBeTruthy()
  })

  it('blocks insufficient or invalid wagers before sending a paid request', async () => {
    const api = gateway({ ...idle, walletBalanceCents: 50 }), user = userEvent.setup()
    render(<HiLoGame gateway={api} playerId="alice:practice" />)
    await user.click(await enabledStart())
    expect(screen.getByRole('alert').textContent).toContain('insufficient credits')
    expect(api.startRound).not.toHaveBeenCalled()
    const input = screen.getByRole('textbox', { name: 'Wager amount' })
    await user.clear(input); await user.type(input, '0.001')
    await user.click(screen.getByRole('button', { name: /^Place wager/ }))
    expect(screen.getByRole('alert').textContent).toContain('whole cents')
    expect(api.startRound).not.toHaveBeenCalled()
    expect(new HiLoPendingOperationStore('alice:practice').read()).toBeNull()
  })

  it('refreshes a definitive insufficient-funds rejection without retaining an unresolved wager', async () => {
    const api = gateway(), user = userEvent.setup()
    api.startRound.mockRejectedValue(new HiLoGatewayError('Your practice balance changed. Insufficient funds.', 'insufficient_funds', 409))
    api.getSession.mockResolvedValueOnce(idle).mockResolvedValueOnce({ ...idle, walletBalanceCents: 0 })
    render(<HiLoGame gateway={api} playerId="alice:practice" />)
    await user.click(await enabledStart())
    await waitFor(() => expect(balance()).toBe('R0.00'))
    expect(screen.getByRole('alert').textContent).toContain('Insufficient funds')
    expect(screen.queryByRole('button', { name: 'Restore action' })).toBeNull()
    expect(new HiLoPendingOperationStore('alice:practice').read()).toBeNull()
    expect((screen.getByRole('textbox', { name: 'Wager amount' }) as HTMLInputElement).disabled).toBe(false)
  })

  it('restores a stale revision from the latest session before the next action', async () => {
    const api = gateway(started), user = userEvent.setup()
    api.getSession.mockResolvedValueOnce(started).mockResolvedValueOnce(won)
    api.act.mockRejectedValueOnce(new HiLoGatewayError('This round changed. Refresh the session before choosing again.', 'stale_revision', 409)).mockResolvedValueOnce(collected)
    render(<HiLoGame gateway={api} playerId="alice:practice" />)
    await screen.findByRole('img', { name: 'seven of hearts' })
    await user.click(higher())
    expect(await screen.findByRole('img', { name: 'king of spades' })).toBeTruthy()
    expect(screen.getByRole('alert').textContent).toContain('This round changed')
    expect(screen.queryByRole('button', { name: 'Restore action' })).toBeNull()
    await user.click(cashout())
    await waitFor(() => expect(balance()).toBe('R101.14'))
    expect(api.act.mock.calls[1][2].expectedRevision).toBe(1)
  })

  it('keeps an existing wager collectible during maintenance while disabling every new risk', async () => {
    const api = gateway({ ...started, enabled: false }), user = userEvent.setup()
    api.act.mockResolvedValue({ ...started, enabled: false, walletBalanceCents: 10000, round: {
      ...startedRound, revision: 1, status: 'cashedOut', canCashOut: false, canSkip: false,
      higher: { ...startedRound.higher, multiplier: 0, projectedReturnCents: 0, available: false },
      lower: { ...startedRound.lower, multiplier: 0, projectedReturnCents: 0, available: false },
    } })
    render(<HiLoGame gateway={api} playerId="alice:practice" />)
    await screen.findByRole('img', { name: 'seven of hearts' })
    expect(higher().disabled).toBe(true)
    expect(lower().disabled).toBe(true)
    expect((screen.getByRole('button', { name: /^Skip card/ }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('textbox', { name: 'Wager amount' }) as HTMLInputElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: /^Wager in play/ }) as HTMLButtonElement).disabled).toBe(true)
    expect(cashout().disabled).toBe(false)
    await user.click(cashout())
    await waitFor(() => expect(balance()).toBe('R100.00'))
    expect(api.act).toHaveBeenCalledExactlyOnceWith(startedRound.roundId, 'cashout', expect.objectContaining({ expectedRevision: 0 }))
    expect(api.startRound).not.toHaveBeenCalled()
    expect((screen.getByRole('button', { name: /^Play again/ }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('releases a guess rejected by a maintenance race so the unchanged round can cash out', async () => {
    const api = gateway({ ...started, enabled: true }), user = userEvent.setup()
    api.getSession.mockResolvedValueOnce({ ...started, enabled: true }).mockResolvedValueOnce({ ...started, enabled: false })
    api.act.mockRejectedValueOnce(new HiLoGatewayError('Hi-Lo is paused. Existing rounds can still be cashed out.', 'hi_lo_disabled', 503))
      .mockResolvedValueOnce({ ...started, enabled: false, walletBalanceCents: 10000, round: {
        ...startedRound, revision: 1, status: 'cashedOut', canCashOut: false, canSkip: false,
        higher: { ...startedRound.higher, multiplier: 0, projectedReturnCents: 0, available: false },
        lower: { ...startedRound.lower, multiplier: 0, projectedReturnCents: 0, available: false },
      } })
    render(<HiLoGame gateway={api} playerId="alice:practice" />)
    await screen.findByRole('img', { name: 'seven of hearts' })
    expect(higher().disabled).toBe(false)
    await user.click(higher())
    await waitFor(() => expect(api.getSession).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(higher().disabled).toBe(true))
    expect(new HiLoPendingOperationStore('alice:practice').read()).toBeNull()
    expect(screen.queryByRole('button', { name: 'Restore action' })).toBeNull()
    expect(lower().disabled).toBe(true)
    expect((screen.getByRole('button', { name: /^Skip card/ }) as HTMLButtonElement).disabled).toBe(true)
    expect(cashout().disabled).toBe(false)
    await user.click(cashout())
    await waitFor(() => expect(balance()).toBe('R100.00'))
    expect(api.act.mock.calls[1]).toEqual([startedRound.roundId, 'cashout', expect.objectContaining({ expectedRevision: 0 })])
    expect(api.act.mock.calls[1][2].operationId).not.toBe(api.act.mock.calls[0][2].operationId)
    expect(new HiLoPendingOperationStore('alice:practice').read()).toBeNull()
  })
})

describe('Hi-Lo interrupted mutations and account isolation', () => {
  it.each(['start', 'cashout'] as const)('recovers an actually committed %s with a lost HTTP response after refresh', async commandKind => {
    let authoritative = commandKind === 'start' ? idle : won
    const postedBodies: string[] = []
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async (_input, init) => {
      if (init?.method !== 'POST') return json(authoritative)
      postedBodies.push(String(init.body))
      authoritative = commandKind === 'start' ? started : collected
      if (postedBodies.length === 1) throw new TypeError('The server committed but its response was lost')
      return json(authoritative)
    })
    const api = new HttpHiLoGateway('/api/games/hi-lo', fetcher), user = userEvent.setup()
    const view = render(<HiLoGame gateway={api} playerId="alice:practice" />)
    if (commandKind === 'start') await user.click(await enabledStart())
    else { await screen.findByRole('img', { name: 'king of spades' }); await user.click(cashout()) }
    await screen.findByRole('button', { name: 'Restore action' })
    const saved = new HiLoPendingOperationStore('alice:practice').read()
    expect(saved?.operationId).toBe(JSON.parse(postedBodies[0]).operationId)
    expect(higher().disabled).toBe(true)
    expect((screen.getByRole('textbox', { name: 'Wager amount' }) as HTMLInputElement).disabled).toBe(true)
    view.unmount()
    render(<HiLoGame gateway={api} playerId="alice:practice" />)
    await waitFor(() => expect(balance()).toBe(commandKind === 'start' ? 'R99.00' : 'R101.14'))
    expect(postedBodies).toHaveLength(2)
    expect(postedBodies[1]).toBe(postedBodies[0])
    expect(new HiLoPendingOperationStore('alice:practice').read()).toBeNull()
    expect(screen.queryByRole('button', { name: 'Restore action' })).toBeNull()
    expect(screen.getByRole('status').textContent).toContain(commandKind === 'start' ? 'Your card is dealt' : 'Return collected')
  })

  it.each([false, true])('ignores a previous account’s delayed mutation after switching player scope (rejected: %s)', async rejects => {
    const pending = deferred<HiLoSession>(), previous = gateway(), next = gateway({ ...idle, walletBalanceCents: 70000 })
    const oldChanged = vi.fn(), newChanged = vi.fn(), user = userEvent.setup()
    previous.startRound.mockReturnValue(pending.promise)
    const view = render(<HiLoGame gateway={previous} playerId="alice:practice" onBalanceChange={oldChanged} />)
    await user.click(await enabledStart())
    expect(new HiLoPendingOperationStore('alice:practice').read()).not.toBeNull()
    view.rerender(<HiLoGame gateway={next} playerId="bob:account" onBalanceChange={newChanged} />)
    await waitFor(() => expect(balance()).toBe('R700.00'))
    newChanged.mockClear()
    await act(async () => { if (rejects) pending.reject(new TypeError('Old connection closed')); else pending.resolve(started) })
    expect(balance()).toBe('R700.00')
    expect(newChanged).not.toHaveBeenCalled()
    expect(screen.queryByRole('img', { name: 'seven of hearts' })).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(new HiLoPendingOperationStore('bob:account').read()).toBeNull()
    expect((screen.getByRole('button', { name: /^Place wager/ }) as HTMLButtonElement).disabled).toBe(false)
    if (rejects) expect(new HiLoPendingOperationStore('alice:practice').read()).not.toBeNull()
  })

  it('ignores a previous account’s delayed startup response and revealed history', async () => {
    const opening = deferred<HiLoSession>(), previous = gateway(), next = gateway({ ...idle, walletBalanceCents: 70000 }), changed = vi.fn()
    previous.getSession.mockReturnValue(opening.promise)
    const view = render(<HiLoGame gateway={previous} playerId="alice:practice" />)
    view.rerender(<HiLoGame gateway={next} playerId="bob:account" onBalanceChange={changed} />)
    await waitFor(() => expect(balance()).toBe('R700.00'))
    changed.mockClear()
    await act(async () => opening.resolve(won))
    expect(balance()).toBe('R700.00')
    expect(changed).not.toHaveBeenCalled()
    expect(screen.queryByRole('img', { name: 'king of spades' })).toBeNull()
    expect(screen.getByText('Your card journey starts here')).toBeTruthy()
  })

  it('ignores a previous account’s delayed failed reconciliation after switching players', async () => {
    const refresh = deferred<HiLoSession>(), previous = gateway(), next = gateway({ ...idle, walletBalanceCents: 70000 }), user = userEvent.setup()
    previous.getSession.mockResolvedValueOnce(idle).mockReturnValueOnce(refresh.promise)
    previous.startRound.mockRejectedValue(new HiLoGatewayError('Insufficient funds.', 'insufficient_funds', 409))
    const view = render(<HiLoGame gateway={previous} playerId="alice:practice" />)
    await user.click(await enabledStart())
    await waitFor(() => expect(previous.getSession).toHaveBeenCalledTimes(2))
    view.rerender(<HiLoGame gateway={next} playerId="bob:account" />)
    await waitFor(() => expect(balance()).toBe('R700.00'))
    await act(async () => refresh.reject(new TypeError('Old account refresh failed')))
    expect(balance()).toBe('R700.00')
    expect(screen.queryByRole('button', { name: 'Restore action' })).toBeNull()
    expect((screen.getByRole('button', { name: /^Place wager/ }) as HTMLButtonElement).disabled).toBe(false)
  })
})

describe('Hi-Lo keyboard rules and sound controls', () => {
  it('uses the latest mute preference when a pending server deal finishes', async () => {
    const pending = deferred<HiLoSession>(), api = gateway(), user = userEvent.setup()
    api.startRound.mockReturnValue(pending.promise)
    render(<HiLoGame gateway={api} playerId="alice:practice" />)
    await user.click(await enabledStart())
    expect(playHiLoAudio).toHaveBeenCalledWith('shuffle', false)
    await user.click(screen.getByRole('button', { name: 'Mute sound' }))
    expect(screen.getByRole('button', { name: 'Turn sound on' }).getAttribute('aria-pressed')).toBe('true')
    await act(async () => pending.resolve(started))
    await screen.findByRole('img', { name: 'seven of hearts' })
    expect(playHiLoAudio).toHaveBeenCalledWith('deal', true)
    expect(playHiLoAudio).not.toHaveBeenCalledWith('deal', false)
    expect(localStorage.getItem('fortuneforge:hi-lo:muted')).toBe('true')
  })

  it('opens a named native rules dialog by keyboard and restores trigger focus after close or cancel', async () => {
    const api = gateway(), user = userEvent.setup()
    render(<HiLoGame gateway={api} playerId="alice:practice" />)
    await enabledStart()
    const trigger = screen.getByRole('button', { name: 'How to play Hi-Lo' })
    trigger.focus(); await user.keyboard('{Enter}')
    const dialog = screen.getByRole('dialog', { name: 'One card. Higher or lower.' }) as HTMLDialogElement
    expect(dialog.open).toBe(true)
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Close rules' }))
    expect(dialog.textContent).toContain('An equal rank loses')
    expect(dialog.textContent).toContain('Before your first guess, cashing out returns your wager')
    await user.click(screen.getByRole('button', { name: 'Close rules' }))
    await waitFor(() => expect(document.activeElement).toBe(trigger))
    await user.keyboard('{Enter}')
    fireEvent(dialog, new Event('cancel', { cancelable: true }))
    await waitFor(() => expect(dialog.open).toBe(false))
    expect(document.activeElement).toBe(trigger)
    expect(api.startRound).not.toHaveBeenCalled()
  })

  it('persists keyboard mute changes and uses the current preference for game sounds', async () => {
    const api = gateway(), user = userEvent.setup()
    const view = render(<HiLoGame gateway={api} playerId="alice:practice" />)
    await enabledStart()
    const mute = screen.getByRole('button', { name: 'Mute sound' })
    mute.focus(); await user.keyboard(' ')
    expect(screen.getByRole('button', { name: 'Turn sound on' }).getAttribute('aria-pressed')).toBe('true')
    expect(localStorage.getItem('fortuneforge:hi-lo:muted')).toBe('true')
    await user.click(screen.getByRole('button', { name: /^Place wager/ }))
    await screen.findByRole('img', { name: 'seven of hearts' })
    expect(playHiLoAudio).toHaveBeenCalledWith('shuffle', true)
    expect(playHiLoAudio).toHaveBeenCalledWith('deal', true)
    view.unmount()
    render(<HiLoGame gateway={gateway()} playerId="bob:practice" />)
    await enabledStart()
    expect(screen.getByRole('button', { name: 'Turn sound on' }).getAttribute('aria-pressed')).toBe('true')
    await user.click(screen.getByRole('button', { name: 'Turn sound on' }))
    expect(localStorage.getItem('fortuneforge:hi-lo:muted')).toBe('false')
    expect(playHiLoAudio).toHaveBeenLastCalledWith('click', false)
  })
})
