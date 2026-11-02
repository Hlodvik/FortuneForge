// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi, type Mocked } from 'vitest'
import type { CrapsExtraBetRequest, CrapsGateway, CrapsOutcome, CrapsRound, CrapsStatus } from './contracts'
import { CrapsGame } from './CrapsGame'

const sounds = vi.hoisted(() => ({
  click: vi.fn(), chip: vi.fn(), roll: vi.fn(), result: vi.fn(), getMuted: vi.fn(() => false), setMuted: vi.fn(), dispose: vi.fn(),
}))

vi.mock('./crapsAudio', () => ({
  playCrapsClick: sounds.click,
  playCrapsChip: sounds.chip,
  playCrapsRoll: sounds.roll,
  playCrapsResult: sounds.result,
  getCrapsMuted: sounds.getMuted,
  setCrapsMuted: sounds.setMuted,
  disposeCrapsAudio: sounds.dispose,
}))

const playerId = 'craps-visual-player'
const roundId = '00000000-0000-4000-8000-000000000051'
const status: CrapsStatus = { available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, mode: 'visual-test' }
const storageKey = `fortuneforge:craps:round:${encodeURIComponent(playerId)}:${encodeURIComponent(status.mode)}`

beforeEach(() => {
  vi.clearAllMocks()
  sounds.getMuted.mockReturnValue(false)
  vi.stubGlobal('matchMedia', vi.fn((query: string) => ({
    matches: query === '(prefers-reduced-motion: reduce)', media: query, onchange: null,
    addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn(),
  })))
})

afterEach(() => {
  cleanup()
  sessionStorage.clear()
  localStorage.clear()
  vi.unstubAllGlobals()
})

describe('Craps table visual interaction safety', () => {
  it.each([
    { action: 'Escape', moveFocus: false },
    { action: 'Close', moveFocus: false },
    { action: 'Escape', moveFocus: true },
  ])('defers $action return focus while the accepted removal refreshes balance, moveFocus=$moveFocus', async ({ action, moveFocus }) => {
    const server = fakeServer({ ...status, mode: 'paid-pass-line', walletBalanceCents: 5000 })
    server.state = { ...pointRound(), revision: 2, walletBalanceCents: 5000,
      extraBets: [{ kind: 'field', stake: 5, resolved: false, won: false, totalReturn: null, wagerId: 'working-field', placedForRoll: 2, cancelled: false }] }
    server.gateway.getActiveRound.mockImplementation(async () => server.state)
    const balanceRead = deferred<CrapsStatus>()
    let holdBalance = false
    server.gateway.getStatus.mockImplementation(async () => holdBalance ? balanceRead.promise : { ...status, mode: 'paid-pass-line', walletBalanceCents: server.state.walletBalanceCents })
    server.gateway.setWager = vi.fn(async () => {
      server.state = { ...server.state, revision: 3, walletBalanceCents: 5500,
        extraBets: server.state.extraBets!.map(bet => ({ ...bet, cancelled: true, resolved: true, totalReturn: 0 })) }
      return server.state
    })
    render(<CrapsGame gateway={server.gateway} playerId={playerId} requirePaid />)
    const roll = await screen.findByRole('button', { name: 'Roll point 6' }) as HTMLButtonElement
    await waitFor(() => expect(roll.disabled).toBe(false))
    const field = screen.getByRole('button', { name: /^Field, edit next-roll bet/ }) as HTMLButtonElement
    fireEvent.click(field)
    holdBalance = true
    fireEvent.click(screen.getByRole('button', { name: 'Remove working Field' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Remove working Field' })).toBeNull())
    expect(field.disabled).toBe(true)
    expect(roll.disabled).toBe(true)
    if (action === 'Escape') fireEvent.keyDown(document, { key: 'Escape' })
    else fireEvent.click(screen.getByRole('button', { name: 'Close Craps details' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    const otherControl = screen.getByRole('button', { name: /^Rolls$/ })
    if (moveFocus) otherControl.focus()
    await act(async () => balanceRead.resolve({ ...status, mode: 'paid-pass-line', walletBalanceCents: 5500 }))
    await waitFor(() => expect(roll.disabled).toBe(false))
    expect(document.activeElement).toBe(moveFocus ? otherControl : field)
  })
  it('keeps a legacy local table on its supported single-odds contract even when the HTTP gateway exposes newer methods', async () => {
    const server = fakeServer()
    restore(server, { ...pointRound(), revision: 1 })
    server.gateway.setWager = vi.fn()
    render(<CrapsGame gateway={server.gateway} playerId={playerId} />)
    await screen.findByRole('button', { name: 'Roll point 6' })
    expect(screen.queryByRole('button', { name: 'Change bets' })).toBeNull()
    expect((screen.getByRole('button', { name: /^Field,/ }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Add odds' }))
    fireEvent.click(screen.getByRole('button', { name: 'Place odds' }))
    await waitFor(() => expect(server.gateway.placeOdds).toHaveBeenCalledTimes(1))
    expect(server.gateway.setWager).not.toHaveBeenCalled()
  })
  it('repeats only the initial come-out layout after a hand with later-roll wagers and removed odds', async () => {
    const server = fakeServer()
    const ended = advance(pointRound(), { first: 3, second: 4, total: 7, result: 'seven-out', isTerminal: true, totalReturn: 0 })
    restore(server, { ...ended, revision: 7, extraBets: [
      { kind: 'field', stake: 5, resolved: true, won: false, totalReturn: 0, wagerId: 'initial-field', placedForRoll: 1, resolvedOnRoll: 1, cancelled: false },
      { kind: 'field', stake: 100, resolved: true, won: false, totalReturn: 0, wagerId: 'later-field', placedForRoll: 2, resolvedOnRoll: 2, cancelled: false },
      { kind: 'any-seven', stake: 7, resolved: true, won: true, totalReturn: 35, wagerId: 'later-seven', placedForRoll: 2, resolvedOnRoll: 2, cancelled: false },
      { kind: 'odds', stake: 100, resolved: true, won: false, totalReturn: 0, wagerId: 'removed-odds', placedForRoll: 2, cancelled: true },
    ] })
    render(<CrapsGame gateway={server.gateway} playerId={playerId} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Repeat R15' }))
    await screen.findByRole('button', { name: 'Roll come-out' })
    expect(server.gateway.startRound.mock.calls[0][0]).toBe(10)
    expect(server.gateway.startRound.mock.calls[0][2]).toEqual([{ kind: 'field', stake: 5 }])
  })
  it('keeps principal locked while editing next-roll bets and reducing or removing odds against the net wallet difference', async () => {
    const server = fakeServer({ ...status, mode: 'paid-pass-line', walletBalanceCents: 0 })
    server.state = { ...pointRound(), revision: 2, walletBalanceCents: 0,
      extraBets: [{ kind: 'odds', stake: 10, resolved: false, won: false, totalReturn: null, wagerId: 'odds-original', placedForRoll: 2, cancelled: false }] }
    server.gateway.getActiveRound.mockImplementation(async () => server.state)
    server.gateway.getStatus.mockImplementation(async () => ({ ...status, mode: 'paid-pass-line', walletBalanceCents: server.state.walletBalanceCents }))
    const setWager = vi.fn(async (_id: string, kind: string, stake: number) => {
      const previous = server.state.extraBets?.find(bet => bet.kind === kind && !bet.resolved)
      server.state = { ...server.state, revision: server.state.revision! + 1,
        walletBalanceCents: server.state.walletBalanceCents! + Math.round(((previous?.stake ?? 0) - stake) * 100),
        extraBets: [
          ...(server.state.extraBets ?? []).map(bet => bet === previous ? { ...bet, cancelled: true, resolved: true, totalReturn: 0 } : bet),
          ...(stake > 0 ? [{ kind: kind as 'odds', stake, resolved: false, won: false, totalReturn: null, wagerId: 'new-' + server.state.revision, placedForRoll: 2, cancelled: false }] : []),
        ] }
      return server.state
    })
    server.gateway.setWager = setWager
    render(<CrapsGame gateway={server.gateway} playerId={playerId} requirePaid />)
    await screen.findByRole('button', { name: 'Roll point 6' })
    expect((screen.getByRole('button', { name: /Pass Line, R10.00/ }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Change odds' }))
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Pass Line odds stake' }), { target: { value: '5' } })
    expect((screen.getByRole('button', { name: 'Update odds' }) as HTMLButtonElement).disabled).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Update odds' }))
    await waitFor(() => expect(screen.getByLabelText('Hand totals').textContent).toContain('Balance R5.00'))
    fireEvent.click(screen.getByRole('button', { name: 'Remove odds' }))
    await waitFor(() => expect(screen.getByLabelText('Hand totals').textContent).toContain('Balance R10.00'))
    fireEvent.click(screen.getByRole('button', { name: 'Change bets' }))
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Field next-roll stake' }), { target: { value: '5' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Field' }))
    await screen.findByRole('button', { name: 'Remove working Field' })
    expect(screen.getByRole('spinbutton', { name: 'Field next-roll stake' }).getAttribute('aria-invalid')).toBe('false')
    fireEvent.click(screen.getByRole('button', { name: 'Remove working Field' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Remove working Field' })).toBeNull())
    expect(setWager.mock.calls.map(call => call.slice(1, 3))).toEqual([['odds', 5], ['odds', 0], ['field', 5], ['field', 0]])
    expect(screen.queryByRole('spinbutton', { name: 'Pass Line bet in Rand' })).toBeNull()
    expect(screen.getByText('Locked until this hand resolves')).toBeTruthy()
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.roll).not.toHaveBeenCalled()
  })
  it('uses the borderless default without bundling the old wood-frame artwork', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    const board = screen.getByRole('region', { name: 'Craps table' })
    expect(board.classList.contains('ff-craps-table--default-art')).toBe(true)
    expect(board.style.getPropertyValue('--ff-craps-table-art')).toBe('')
    expect(screen.getByLabelText('Hand totals').parentElement).toBe(board)
    expect(screen.getByLabelText('Hand totals').textContent).toBe('Bet R10.00Win R0.00')
    expect(screen.queryByText('Free play')).toBeNull()
    expect(screen.getByRole('button', { name: 'Bet R10 on Pass Line' }).querySelector('strong')?.textContent).toBe('BET')
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.roll).not.toHaveBeenCalled()
  })

  it('preserves an explicitly supplied custom table artwork', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} tableArtworkUrl="/custom-table.webp" />)
    await readyToBet()
    const board = screen.getByRole('region', { name: 'Craps table' })
    expect(board.classList.contains('ff-craps-table--default-art')).toBe(false)
    expect(board.getAttribute('style')).toContain('/custom-table.webp')
  })

  it('shows a paid wallet shortage and enables only an affordable wager', async () => {
    const server = fakeServer({ ...status, mode: 'paid-pass-line', walletBalanceCents: 500 })
    render(<CrapsGame gateway={server.gateway} playerId={playerId} requirePaid />)
    const bet = await screen.findByRole('button', { name: 'Bet R10 on Pass Line' }) as HTMLButtonElement
    await screen.findByText('Insufficient balance for these bets.')
    expect(bet.disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Bets' }))
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Pass Line bet in Rand' }), { target: { value: '5' } })
    await waitFor(() => expect((screen.getByRole('button', { name: 'Bet R5 on Pass Line' }) as HTMLButtonElement).disabled).toBe(false))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('Balance R5.00')
    expect(server.gateway.startRound).not.toHaveBeenCalled()
  })

  it('uses existing chip pictures in the selector and builds an aligned image stack on the felt', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    const selector = screen.getByLabelText('Select chip value')
    expect(selector.querySelectorAll('img')).toHaveLength(1)
    expect(screen.queryByRole('group', { name: 'Chip denominations' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Chip options, selected R5' }))
    const buttons = screen.getByRole('group', { name: 'Chip denominations' }).querySelectorAll('button')
    expect(buttons.length).toBeGreaterThanOrEqual(5)
    for (const button of buttons) {
      if (button.getAttribute('aria-label') === 'Select R100 chip') expect(button.querySelector('.ff-craps-custom-chip')?.textContent).toBe('R100')
      else expect(button.querySelector('img')?.getAttribute('src')).toMatch(/chip-(?:1|2|5|10|25|50)\.png/)
      expect(button.getAttribute('aria-label')).toMatch(/^Select R\d+ chip$/)
    }
    fireEvent.click(screen.getByRole('button', { name: 'Select R2 chip' }))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R10.00')
    expect(screen.getByRole('button', { name: 'Chip options, selected R2' }).getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R2' }))
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R2' }))
    expect(sounds.chip).toHaveBeenCalledTimes(2)
    const stack = screen.getByLabelText('R14.00 on Pass Line')
    expect(stack.classList.contains('ff-craps-chip-stack')).toBe(true)
    const images = stack.querySelectorAll('img')
    expect(images).toHaveLength(3)
    expect(stack.style.getPropertyValue('--chip-stack-count')).toBe('3')
    expect([...images].map(image => image.style.getPropertyValue('--chip-index'))).toEqual(['0', '1', '2'])
    expect([...images].every(image => /chip-(?:10|2)\.png/.test(image.getAttribute('src') ?? ''))).toBe(true)
    expect(stack.textContent).toBe('R14.00')
    expect(server.gateway.startRound).not.toHaveBeenCalled()
  })

  it('adds every supported wager to the total and submits those exact independent amounts', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    fireEvent.click(screen.getByRole('button', { name: 'Clear bets' }))
    chooseChip(2)
    for (let index = 0; index < 3; index++) fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R2' }))
    chooseChip(5)
    fireEvent.click(screen.getByRole('button', { name: /^Field, add R5,/ }))
    fireEvent.click(screen.getByRole('button', { name: /^Field, add R5,/ }))
    fireEvent.click(screen.getByRole('button', { name: /^Any Seven, add R5,/ }))
    fireEvent.click(screen.getByRole('button', { name: /^Any Craps, add R5,/ }))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R26.00')
    expect(screen.getByLabelText('R6.00 on Pass Line')).toBeTruthy()
    expect(screen.getByLabelText('R10.00 on Field')).toBeTruthy()
    expect(screen.getByLabelText('R5.00 on Any Seven')).toBeTruthy()
    expect(screen.getByLabelText('R5.00 on Any Craps')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Don't Pass|Hardway|Place six|Come bet/i })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Bet R26 total, including Pass Line R6' }))
    await screen.findByRole('button', { name: 'Roll come-out' })
    expect(server.gateway.startRound).toHaveBeenCalledTimes(1)
    expect(server.gateway.startRound.mock.calls[0]?.[0]).toBe(6)
    expect(server.gateway.startRound.mock.calls[0]?.[2]).toEqual([
      { kind: 'field', stake: 10 }, { kind: 'any-seven', stake: 5 }, { kind: 'any-craps', stake: 5 },
    ])
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R26.00')
    expect(server.gateway.roll).not.toHaveBeenCalled()
  })

  it('offers quarter-Rand chips only when authoritative table limits allow those wagers', async () => {
    const server = fakeServer({ ...status, minimumStake: .25, stakeIncrement: .25 })
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    fireEvent.click(screen.getByRole('button', { name: 'Clear bets' }))
    chooseChip(.25)
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R0.25' }))
    fireEvent.click(screen.getByRole('button', { name: 'Increase chip value' }))
    expect(screen.getByRole('button', { name: 'Chip options, selected R0.5' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R0.5' }))
    expect(screen.getByLabelText('Hand totals').textContent).toBe('Bet R0.75Win R0.00')
    fireEvent.click(screen.getByRole('button', { name: 'Bet R0.75 on Pass Line' }))
    await screen.findByRole('button', { name: 'Roll come-out' })
    expect(server.gateway.startRound.mock.calls[0]?.[0]).toBe(.75)
  })

  it('clears all draft wagers and restores the whole draft one change at a time with Undo', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    fireEvent.click(screen.getByRole('button', { name: 'Clear bets' }))
    expect((screen.getByRole('button', { name: 'Bet R0 on Pass Line' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.queryByLabelText(/^R[\d.]+ on Pass Line$/)).toBeNull()
    chooseChip(1)
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R1' }))
    fireEvent.click(screen.getByRole('button', { name: /^Field, add R1,/ }))
    fireEvent.click(screen.getByRole('button', { name: /^Any Seven, add R1,/ }))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R3.00')
    fireEvent.click(screen.getByRole('button', { name: 'Undo last chip' }))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R2.00')
    expect(screen.queryByLabelText('R1.00 on Any Seven')).toBeNull()
    expect(screen.getByLabelText('R1.00 on Field')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Undo last chip' }))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R1.00')
    expect(screen.queryByLabelText('R1.00 on Field')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Undo last chip' }))
    expect((screen.getByRole('button', { name: 'Bet R0 on Pass Line' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Undo last chip' }))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R10.00')
    expect((screen.getByRole('button', { name: 'Undo last chip' }) as HTMLButtonElement).disabled).toBe(true)
    expect(server.gateway.startRound).not.toHaveBeenCalled()
  })

  it('rejects an added chip beyond the table cap instead of clamping or submitting a different wager', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    chooseChip(50)
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R50' }))
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R50' }))
    expect(screen.getByRole('alert').textContent).toContain('Bet limit: R1.00–R100.00')
    expect(screen.getByLabelText('R60.00 on Pass Line')).toBeTruthy()
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R60.00')
    fireEvent.click(screen.getByRole('button', { name: 'Bet R60 on Pass Line' }))
    await screen.findByRole('button', { name: 'Roll come-out' })
    expect(server.gateway.startRound.mock.calls[0]?.[0]).toBe(60)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('starts a clean Undo history when editing a completed hand rather than undoing old-hand chips', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R5' }))
    fireEvent.click(screen.getByRole('button', { name: 'Bet R15 on Pass Line' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Roll come-out' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Roll point 6' }))
    await screen.findByRole('button', { name: 'Repeat R15' })
    fireEvent.click(screen.getByRole('button', { name: 'Edit bet' }))
    await screen.findByRole('button', { name: 'Bet R15 on Pass Line' })
    const undo = screen.getByRole('button', { name: 'Undo last chip' }) as HTMLButtonElement
    expect(undo.disabled).toBe(true)
    fireEvent.click(undo)
    expect(screen.getByLabelText('R15.00 on Pass Line')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R5' }))
    expect(undo.disabled).toBe(false)
    expect(screen.getByLabelText('R20.00 on Pass Line')).toBeTruthy()
    fireEvent.click(undo)
    expect(screen.getByLabelText('R15.00 on Pass Line')).toBeTruthy()
    expect(undo.disabled).toBe(true)
    expect(server.gateway.startRound).toHaveBeenCalledTimes(1)
  })

  it('blocks repeated bet clicks while the accepted bet response is pending', async () => {
    const server = fakeServer()
    const pending = deferred<CrapsRound>()
    server.gateway.startRound.mockImplementationOnce(() => pending.promise)
    render(<CrapsGame gateway={server.gateway} />)
    const bet = await readyToBet()
    fireEvent.click(bet)
    fireEvent.click(bet)
    expect(server.gateway.startRound).toHaveBeenCalledTimes(1)
    expect(bet.disabled).toBe(true)
    expect((screen.getByRole('button', { name: /^Pass Line, add R/ }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Chip options, selected R5' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.queryByRole('group', { name: 'Chip denominations' })).toBeNull()
    expect(sounds.roll).not.toHaveBeenCalled()
    await act(async () => pending.resolve(readyRound()))
    await waitFor(() => expect((screen.getByRole('button', { name: 'Roll come-out' }) as HTMLButtonElement).disabled).toBe(false))
    expect(server.gateway.roll).not.toHaveBeenCalled()
  })

  it('disables wager controls while watching another shooter', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} isYourTurn={false} activePlayerName="Maya" />)
    await waitFor(() => expect(server.gateway.getStatus).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(screen.getByText('Maya is the shooter')).toBeTruthy())
    fireEvent.click(screen.getByRole('button', { name: 'Bets' }))
    expect((screen.getByLabelText('Pass Line bet in Rand') as HTMLInputElement).disabled).toBe(true)
    const waiting = screen.getByRole('button', { name: 'Waiting' }) as HTMLButtonElement
    expect(waiting.disabled).toBe(true)
    fireEvent.click(waiting)
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.roll).not.toHaveBeenCalled()
    expect(sounds.roll).not.toHaveBeenCalled()
  })

  it('shows the established point and permits odds once, without changing the Pass Line stake', async () => {
    const server = fakeServer()
    restore(server, pointRound())
    render(<CrapsGame gateway={server.gateway} playerId={playerId} />)
    const addOdds = await screen.findByRole('button', { name: 'Add odds' }) as HTMLButtonElement
    await waitFor(() => expect(addOdds.disabled).toBe(false))
    expect(screen.getByLabelText('Point 6 is on').textContent).toContain('ON')
    fireEvent.click(addOdds)
    const odds = screen.getByRole('dialog', { name: 'Pass Line odds' })
    expect(odds.textContent).toContain('Pays 6:5')
    fireEvent.change(screen.getByLabelText('Pass Line odds stake'), { target: { value: '1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Place odds' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Add odds' })).toBeNull())
    expect(server.gateway.placeOdds).toHaveBeenCalledTimes(1)
    expect(server.gateway.placeOdds.mock.calls[0]?.slice(0, 2)).toEqual([roundId, 1])
    expect(server.state.stake).toBe(10)
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R11.00')
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.roll).not.toHaveBeenCalled()
  })

  it('does not offer odds on the come-out or if the gateway does not support them', async () => {
    const server = fakeServer()
    restore(server, readyRound())
    const first = render(<CrapsGame gateway={server.gateway} playerId={playerId} />)
    await screen.findByRole('button', { name: 'Roll come-out' })
    expect(screen.queryByRole('button', { name: 'Add odds' })).toBeNull()
    first.unmount()
    server.state = pointRound()
    const { placeOdds: _odds, ...legacy } = server.gateway
    render(<CrapsGame gateway={legacy} playerId={playerId} />)
    await screen.findByRole('button', { name: 'Roll point 6' })
    expect(screen.queryByRole('button', { name: 'Add odds' })).toBeNull()
    expect(server.gateway.placeOdds).not.toHaveBeenCalled()
  })

  it('plays button and throw sounds from interactions, then a single result cue after settlement', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    expect(sounds.click).not.toHaveBeenCalled()
    expect(sounds.roll).not.toHaveBeenCalled()
    expect(sounds.result).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Chip options, selected R5' }).querySelector('img')!)
    expect(sounds.click).toHaveBeenCalledTimes(1)
    const chipButton = screen.getByRole('button', { name: 'Select R2 chip' })
    fireEvent.click(chipButton.querySelector('img')!)
    expect(sounds.click).toHaveBeenCalledTimes(2)
    fireEvent.click(screen.getByRole('button', { name: 'Increase chip value' }).querySelector('path')!)
    expect(sounds.click).toHaveBeenCalledTimes(3)
    fireEvent.click(screen.getByRole('button', { name: 'Decrease chip value' }).querySelector('path')!)
    expect(sounds.click).toHaveBeenCalledTimes(4)
    const beforeUndo = sounds.click.mock.calls.length
    fireEvent.click(screen.getByRole('button', { name: 'Undo last chip' }))
    expect(sounds.click).toHaveBeenCalledTimes(beforeUndo)
    fireEvent.click(screen.getByRole('button', { name: 'Bet R10 on Pass Line' }))
    const comeOut = await screen.findByRole('button', { name: 'Roll come-out' })
    expect(sounds.roll).not.toHaveBeenCalled()
    fireEvent.click(comeOut)
    await screen.findByRole('button', { name: 'Roll point 6' })
    expect(sounds.roll).toHaveBeenCalledTimes(1)
    expect(sounds.result).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Roll point 6' }))
    await screen.findByRole('button', { name: 'Repeat R10' })
    expect(sounds.roll).toHaveBeenCalledTimes(2)
    await waitFor(() => expect(sounds.result).toHaveBeenCalledExactlyOnceWith(true))
    fireEvent.click(screen.getByRole('button', { name: 'Bets' }))
    expect(sounds.result).toHaveBeenCalledTimes(1)
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R20.00')
  })

  it('restores a resolved hand without replaying a throw or result and exposes a persistent mute control', async () => {
    const server = fakeServer()
    restore(server, advance(pointRound(), { first: 2, second: 4, total: 6, result: 'point-hit', isTerminal: true, totalReturn: 20 }))
    const view = render(<CrapsGame gateway={server.gateway} playerId={playerId} />)
    await screen.findByRole('button', { name: 'Repeat R10' })
    expect(sounds.roll).not.toHaveBeenCalled()
    expect(sounds.result).not.toHaveBeenCalled()
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.roll).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Mute Craps sounds' }))
    expect(sounds.setMuted).toHaveBeenLastCalledWith(true)
    expect(screen.getByRole('button', { name: 'Unmute Craps sounds' }).getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(screen.getByRole('button', { name: 'Unmute Craps sounds' }))
    expect(sounds.setMuted).toHaveBeenLastCalledWith(false)
    view.unmount()
    expect(sounds.dispose).toHaveBeenCalledTimes(1)
  })

  it('locks an uncertain hand and uses Check again only to read the authoritative hand', async () => {
    const server = fakeServer()
    restore(server, readyRound())
    render(<CrapsGame gateway={server.gateway} playerId={playerId} />)
    const roll = await screen.findByRole('button', { name: 'Roll come-out' }) as HTMLButtonElement
    await waitFor(() => expect(roll.disabled).toBe(false))
    server.gateway.roll.mockRejectedValueOnce(new TypeError('Response lost'))
    server.gateway.getRound.mockRejectedValueOnce(new TypeError('Still offline'))
    fireEvent.click(roll)
    const retry = await screen.findByRole('button', { name: 'Check again' })
    expect(roll.disabled).toBe(true)
    fireEvent.click(roll)
    expect(server.gateway.roll).toHaveBeenCalledTimes(1)
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    server.state = pointRound()
    fireEvent.click(retry)
    await waitFor(() => expect((screen.getByRole('button', { name: 'Roll point 6' }) as HTMLButtonElement).disabled).toBe(false))
    expect(server.gateway.getRound).toHaveBeenCalledTimes(3)
    expect(server.gateway.roll).toHaveBeenCalledTimes(1)
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.placeOdds).not.toHaveBeenCalled()
  })

  it('edits independent draft stakes and removes one side bet without changing another or sending a wager', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    fireEvent.click(screen.getByRole('button', { name: /^Field, add R5,/ }))
    fireEvent.click(screen.getByRole('button', { name: /^Any Seven, add R5,/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Edit amounts' }))
    const pass = screen.getByRole('spinbutton', { name: 'Pass Line bet in Rand' })
    const seven = screen.getByRole('spinbutton', { name: 'Any Seven bet in Rand' }) as HTMLInputElement
    fireEvent.change(pass, { target: { value: '7' } })
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Field bet in Rand' }), { target: { value: '13' } })
    fireEvent.change(seven, { target: { value: '' } })
    expect(seven.value).toBe('')
    expect(seven.getAttribute('aria-invalid')).toBe('true')
    expect((screen.getByRole('button', { name: /^Bet R/ }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.change(seven, { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Remove Field bet 1' }))
    expect((screen.getByRole('spinbutton', { name: 'Any Seven bet in Rand' }) as HTMLInputElement).value).toBe('2')
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Done' }))
    fireEvent.click(screen.getByRole('button', { name: 'Undo last chip' }))
    expect(screen.getByLabelText('R13.00 on Field')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Edit amounts' }))
    fireEvent.click(screen.getByRole('button', { name: 'Remove Field bet 1' }))
    fireEvent.click(screen.getByRole('button', { name: 'Done' }))
    fireEvent.click(screen.getByRole('button', { name: 'Bet R9 total, including Pass Line R7' }))
    await screen.findByRole('button', { name: 'Roll come-out' })
    expect(server.gateway.startRound.mock.calls[0]?.[0]).toBe(7)
    expect(server.gateway.startRound.mock.calls[0]?.[2]).toEqual([{ kind: 'any-seven', stake: 2 }])
    expect(screen.getByText('Bets are locked for this hand. Roll the come-out dice.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Edit amounts' })).toBeNull()
  })

  it.each(['', '0', '-1', '101', '1.5'])('explains invalid Pass Line amount %j without submitting or changing it', async value => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    fireEvent.click(screen.getByRole('button', { name: 'Edit amounts' }))
    const input = screen.getByRole('spinbutton', { name: 'Pass Line bet in Rand' }) as HTMLInputElement
    fireEvent.change(input, { target: { value } })
    expect(input.value).toBe(value)
    expect(input.getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByText(/^Choose a Pass Line bet:/)).toBeTruthy()
    expect((screen.getByRole('button', { name: /^Bet R/ }) as HTMLButtonElement).disabled).toBe(true)
    expect(server.gateway.startRound).not.toHaveBeenCalled()
  })

  it('offers exact legal endpoints even when there is no matching chip artwork', async () => {
    const server = fakeServer({ ...status, minimumStake: 7.5, maximumStake: 12.5, stakeIncrement: 2.5 })
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    fireEvent.click(screen.getByRole('button', { name: 'Clear bets' }))
    chooseChip(7.5)
    expect(screen.getByRole('button', { name: 'Chip options, selected R7.5' }).querySelector('.ff-craps-custom-chip')?.textContent).toBe('R7.5')
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R7.5' }))
    fireEvent.click(screen.getByRole('button', { name: 'Bet R7.5 on Pass Line' }))
    await screen.findByRole('button', { name: 'Roll come-out' })
    expect(server.gateway.startRound.mock.calls[0]?.[0]).toBe(7.5)
  })

  it('does not compare an already accepted draft with the wallet after the debit', async () => {
    const server = fakeServer({ ...status, mode: 'paid-pass-line', walletBalanceCents: 1500 })
    server.gateway.getStatus.mockResolvedValueOnce({ ...status, mode: 'paid-pass-line', walletBalanceCents: 1500 })
      .mockResolvedValue({ ...status, mode: 'paid-pass-line', walletBalanceCents: 500 })
    server.gateway.startRound.mockResolvedValueOnce({ ...readyRound(), walletBalanceCents: 500 })
    render(<CrapsGame gateway={server.gateway} playerId={playerId} requirePaid />)
    fireEvent.click(await readyToBet())
    const roll = await screen.findByRole('button', { name: 'Roll come-out' }) as HTMLButtonElement
    await waitFor(() => expect(roll.disabled).toBe(false))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('Balance R5.00')
    expect(screen.queryByText('Insufficient balance for these bets.')).toBeNull()
    expect(server.gateway.startRound).toHaveBeenCalledTimes(1)
  })

  it('explains an unaffordable repeat while still allowing the completed hand to be edited', async () => {
    const server = fakeServer({ ...status, mode: 'paid-pass-line', walletBalanceCents: 500 })
    server.gateway.getActiveRound.mockResolvedValueOnce({
      ...advance(pointRound(), { first: 1, second: 6, total: 7, result: 'seven-out', isTerminal: true, totalReturn: 0 }), walletBalanceCents: 500,
    })
    render(<CrapsGame gateway={server.gateway} playerId={playerId} requirePaid />)
    await screen.findByRole('button', { name: 'Repeat R10' })
    expect((screen.getByRole('button', { name: 'Repeat R10' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText('These bets cannot be repeated. Choose Edit bet to change the amounts.')).toBeTruthy()
    expect(screen.queryByText('Insufficient balance for these bets.')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Edit bet' }))
    fireEvent.click(screen.getByRole('button', { name: 'Edit amounts' }))
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Pass Line bet in Rand' }), { target: { value: '5' } })
    expect((screen.getByRole('button', { name: 'Bet R5 on Pass Line' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('preserves duplicate restored side bets when adding a chip to the edited draft', async () => {
    const server = fakeServer()
    const finished = advance(pointRound(), { first: 2, second: 4, total: 6, result: 'point-hit', isTerminal: true, totalReturn: 20 })
    restore(server, { ...finished, extraBets: [
      { kind: 'field', stake: 5, resolved: true, won: false, totalReturn: 0 },
      { kind: 'field', stake: 10, resolved: true, won: false, totalReturn: 0 },
      { kind: 'any-seven', stake: 2, resolved: true, won: false, totalReturn: 0 },
    ] })
    render(<CrapsGame gateway={server.gateway} playerId={playerId} />)
    await screen.findByRole('button', { name: 'Edit bet' })
    fireEvent.click(screen.getByRole('button', { name: 'Edit bet' }))
    fireEvent.click(screen.getByRole('button', { name: /^Field, add R5,/ }))
    expect(screen.getByLabelText('R20.00 on Field')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Bet R32 total, including Pass Line R10' }))
    await screen.findByRole('button', { name: 'Roll come-out' })
    expect(server.gateway.startRound.mock.calls[0]?.[2]).toEqual([
      { kind: 'field', stake: 10 }, { kind: 'field', stake: 10 }, { kind: 'any-seven', stake: 2 },
    ])
  })
})

async function readyToBet(): Promise<HTMLButtonElement> {
  const button = await screen.findByRole('button', { name: /^Bet R/ }) as HTMLButtonElement
  await waitFor(() => expect(button.disabled).toBe(false))
  return button
}

function chooseChip(value: number) {
  fireEvent.click(screen.getByRole('button', { name: /^Chip options, selected R/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Select R' + Number(value.toFixed(2)) + ' chip' }))
}

function restore(server: ReturnType<typeof fakeServer>, round: CrapsRound) {
  server.state = round
  sessionStorage.setItem(storageKey, round.roundId)
}

function readyRound(): CrapsRound {
  return { roundId, stake: 10, phase: 'come-out', point: null, rolls: [], lastOutcome: null, extraBets: [] }
}

function pointRound(): CrapsRound {
  return advance(readyRound(), { first: 3, second: 3, total: 6, result: 'point-established', isTerminal: false, totalReturn: null })
}

function advance(round: CrapsRound, outcome: CrapsOutcome): CrapsRound {
  return {
    ...round,
    phase: outcome.isTerminal ? 'resolved' : 'point',
    point: outcome.result === 'point-established' ? outcome.total : round.point,
    rolls: [...round.rolls, { rollNumber: round.rolls.length + 1, first: outcome.first, second: outcome.second, total: outcome.total, result: outcome.result }],
    lastOutcome: outcome,
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}

function fakeServer(tableStatus: CrapsStatus = status) {
  const server: { state: CrapsRound; gateway: Mocked<Required<Omit<CrapsGateway, 'setWager'>> & Pick<CrapsGateway, 'setWager'>> } = {
    state: readyRound(), gateway: {} as Mocked<Required<Omit<CrapsGateway, 'setWager'>> & Pick<CrapsGateway, 'setWager'>>,
  }
  server.gateway = {
    getStatus: vi.fn(async () => ({ ...tableStatus })),
    getActiveRound: vi.fn(async () => null),
    getRound: vi.fn(async () => server.state),
    startRound: vi.fn(async (stake: number, _signal?: AbortSignal, extras?: readonly CrapsExtraBetRequest[]) => {
      server.state = { ...readyRound(), stake, extraBets: (extras ?? []).map(bet => ({ ...bet, resolved: false, won: false, totalReturn: null })) }
      return server.state
    }),
    roll: vi.fn(async () => {
      server.state = advance(server.state, server.state.phase === 'come-out'
        ? { first: 3, second: 3, total: 6, result: 'point-established', isTerminal: false, totalReturn: null }
        : { first: 2, second: 4, total: 6, result: 'point-hit', isTerminal: true, totalReturn: server.state.stake * 2 })
      return server.state
    }),
    placeOdds: vi.fn(async (_id: string, stake: number) => {
      server.state = { ...server.state, extraBets: [...server.state.extraBets ?? [], { kind: 'odds', stake, resolved: false, won: false, totalReturn: null }] }
      return server.state
    }),
  }
  return server
}
